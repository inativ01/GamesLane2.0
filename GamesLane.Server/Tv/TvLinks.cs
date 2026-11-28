using System.Security.Cryptography;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tv;

/// <summary>
/// What a paired TV is for a user: not open (<see cref="Offline"/>), free to show their games (<see cref="Available"/>),
/// showing another user's games (<see cref="InUse"/>), or showing theirs (<see cref="Watching"/>).
/// </summary>
public enum TvAvailability
{
    Offline,
    Available,
    InUse,
    Watching,
}

/// <summary>
/// The user whose games a TV shows (Watch on TV), in their language, and the session they have open, or null on the
/// site's other pages.
/// </summary>
public record TvLink(Guid UserId, string Name, string Icon, string Language, string? SessionId, GameType? GameType);

/// <summary>
/// A TV that is open, as <see cref="TvLinks"/> knows it: its name, its connections, its pairing code and when the code
/// expires, and the user whose games it shows, if any.
/// </summary>
public record TvSnapshot(string TvId, string Name, IReadOnlyList<string> Connections, string Code, DateTimeOffset CodeExpiresAt, TvLink? Link);

/// <summary>
/// The result of a change to the links: the TVs to send their new status to, and the users' connections whose Watch on
/// TV has ended.
/// </summary>
public record TvChange(IReadOnlyList<string> TvIds, IReadOnlyList<string> EndedConnections)
{
    /// <summary>
    /// Nothing changed.
    /// </summary>
    public static readonly TvChange None = new([], []);
}

/// <summary>
/// The TVs that are open (a TV app connected to the hub), their pairing codes, and the links between users and
/// TVs (Watch on TV), kept in memory. A TV shows the games of one user at a time, and a user's games show on one TV at a
/// time. A link lasts while the TV and one of the user's pages are connected; it ends once either has been gone for
/// <see cref="ReconnectWindow"/> (so it survives the user moving from page to page), or when the user stops it. Each
/// pairing code is valid for <see cref="CodeLifetime"/>, and only once. All members are thread-safe.
/// </summary>
public class TvLinks
{
    /// <summary>
    /// How long a TV's pairing code is valid.
    /// </summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a link lasts without a connection of the TV or of the user.
    /// </summary>
    public static readonly TimeSpan ReconnectWindow = GameSession.ReconnectWindow;

    /// <summary>
    /// The number of characters in a pairing code.
    /// </summary>
    public const int CodeLength = 6;

    // Letters and digits that cannot be mistaken for one another (no I, O, 0 or 1).
    private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, Screen> _tvs = [];
    private readonly Dictionary<string, string> _codes = [];
    private readonly Dictionary<Guid, string> _userTvs = [];
    // Each connection's TV: the TV's own connections, and the connections of the user it shows the games of.
    private readonly Dictionary<string, string> _connections = [];

    /// <summary>
    /// Links kept with <paramref name="timeProvider"/>'s time.
    /// </summary>
    public TvLinks(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    private sealed class Screen(string name)
    {
        public string Name { get; set; } = name;
        public HashSet<string> Connections { get; } = [];
        public DateTimeOffset? OfflineSince { get; set; }
        public string? Code { get; set; }
        public DateTimeOffset CodeExpiresAt { get; set; }
        public Viewer? Viewer { get; set; }
        public bool IsOpen => Connections.Count > 0;
    }

    private sealed class Viewer(TvLink link)
    {
        public TvLink Link { get; set; } = link;
        public HashSet<string> Connections { get; } = [];
        public DateTimeOffset? DisconnectedSince { get; set; }
    }

    /// <summary>
    /// The pairing code in the form users type it: upper case, without spaces or dashes.
    /// </summary>
    public static string NormalizeCode(string? code) =>
        new((code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// A connection of the TV <paramref name="tvId"/>, named <paramref name="name"/>, opened (or the TV asking for its
    /// status again). Returns the TV with a pairing code that is still valid.
    /// </summary>
    public TvSnapshot ConnectTv(string tvId, string name, string connectionId)
    {
        lock (_sync)
        {
            if (!_tvs.TryGetValue(tvId, out var tv))
            {
                tv = new Screen(name);
                _tvs[tvId] = tv;
            }

            tv.Name = name;
            tv.Connections.Add(connectionId);
            tv.OfflineSince = null;
            _connections[connectionId] = tvId;
            return Snapshot(tvId, tv);
        }
    }

    /// <summary>
    /// The TV, with a pairing code that is still valid, or null if it is not open.
    /// </summary>
    public TvSnapshot? Find(string tvId)
    {
        lock (_sync)
        {
            return _tvs.TryGetValue(tvId, out var tv) && tv.IsOpen ? Snapshot(tvId, tv) : null;
        }
    }

    /// <summary>
    /// Uses up a pairing code: returns the ID and name of the open TV that shows it, or null if no TV shows it (it was
    /// mistyped, has expired or was already used). The TV gets a new code.
    /// </summary>
    public (string TvId, string Name)? RedeemCode(string? code)
    {
        var key = NormalizeCode(code);
        lock (_sync)
        {
            if (!_codes.TryGetValue(key, out var tvId) || _tvs[tvId] is not { IsOpen: true } tv
                || tv.CodeExpiresAt <= _timeProvider.GetUtcNow())
            {
                return null;
            }

            _codes.Remove(key);
            tv.Code = null;
            return (tvId, tv.Name);
        }
    }

    /// <summary>
    /// What the TV is for the user (see <see cref="TvAvailability"/>).
    /// </summary>
    public TvAvailability AvailabilityFor(string tvId, Guid userId)
    {
        lock (_sync)
        {
            return !_tvs.TryGetValue(tvId, out var tv) || !tv.IsOpen ? TvAvailability.Offline
                : tv.Viewer is null ? TvAvailability.Available
                : tv.Viewer.Link.UserId == userId ? TvAvailability.Watching
                : TvAvailability.InUse;
        }
    }

    /// <summary>
    /// Shows <paramref name="link"/>'s user's games on the TV, starting with <see cref="TvLink.SessionId"/>, for as long
    /// as the user's connection <paramref name="connectionId"/> (or another of theirs linked later) is open. Refused when
    /// the TV is not open, or shows another user's games. A TV the user linked before is released.
    /// </summary>
    public bool TryLink(string tvId, TvLink link, string connectionId, out TvChange change, out string? error)
    {
        lock (_sync)
        {
            change = TvChange.None;
            error = !_tvs.TryGetValue(tvId, out var tv) || !tv.IsOpen ? "This TV is not open. Open GamesLane on the TV first."
                : tv.Viewer is { } viewer && viewer.Link.UserId != link.UserId ? "This TV is showing another player's games."
                : null;
            if (error is not null)
            {
                return false;
            }

            var ended = new List<string>();
            var tvIds = new List<string> { tvId };
            if (_userTvs.TryGetValue(link.UserId, out var previous) && previous != tvId)
            {
                ended.AddRange(Release(previous).Where(c => c != connectionId));
                tvIds.Add(previous);
            }

            if (_connections.TryGetValue(connectionId, out var other) && other != tvId
                && _tvs.TryGetValue(other, out var otherTv) && otherTv.Viewer is { } old)
            {
                old.Connections.Remove(connectionId);
            }

            tv!.Viewer ??= new Viewer(link);
            tv.Viewer.Link = link;
            tv.Viewer.Connections.Add(connectionId);
            tv.Viewer.DisconnectedSince = null;
            _userTvs[link.UserId] = tvId;
            _connections[connectionId] = tvId;
            change = new TvChange(tvIds, ended);
            return true;
        }
    }

    /// <summary>
    /// Stops showing the user's games on the TV they linked (any TV when <paramref name="tvId"/> is null).
    /// </summary>
    public TvChange Unlink(Guid userId, string? tvId = null)
    {
        lock (_sync)
        {
            if (!_userTvs.TryGetValue(userId, out var linked) || (tvId is not null && tvId != linked))
            {
                return TvChange.None;
            }

            return new TvChange([linked], Release(linked));
        }
    }

    /// <summary>
    /// Forgets a closed connection, of a TV or of a user whose games a TV shows.
    /// </summary>
    public void Disconnect(string connectionId, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!_connections.Remove(connectionId, out var tvId) || !_tvs.TryGetValue(tvId, out var tv))
            {
                return;
            }

            if (tv.Connections.Remove(connectionId) && !tv.IsOpen)
            {
                tv.OfflineSince = now;
            }

            if (tv.Viewer is { } viewer && viewer.Connections.Remove(connectionId) && viewer.Connections.Count == 0)
            {
                viewer.DisconnectedSince = now;
            }
        }
    }

    /// <summary>
    /// Ends the links whose TV, or whose user, has had no connection for <see cref="ReconnectWindow"/>, and forgets the
    /// TVs closed that long.
    /// </summary>
    public TvChange Expire(DateTimeOffset now)
    {
        lock (_sync)
        {
            var tvIds = new List<string>();
            var ended = new List<string>();
            foreach (var (tvId, tv) in _tvs.ToList())
            {
                var tvGone = tv.OfflineSince is { } offline && now - offline >= ReconnectWindow;
                if (tv.Viewer is { } viewer && (tvGone || (viewer.DisconnectedSince is { } since && now - since >= ReconnectWindow)))
                {
                    ended.AddRange(Release(tvId));
                    tvIds.Add(tvId);
                }

                if (tvGone)
                {
                    if (tv.Code is not null)
                    {
                        _codes.Remove(tv.Code);
                    }

                    _tvs.Remove(tvId);
                }
            }

            return new TvChange(tvIds, ended);
        }
    }

    // Ends the TV's link; returns the user's connections that were linked.
    private List<string> Release(string tvId)
    {
        var tv = _tvs[tvId];
        if (tv.Viewer is not { } viewer)
        {
            return [];
        }

        tv.Viewer = null;
        _userTvs.Remove(viewer.Link.UserId);
        foreach (var connection in viewer.Connections)
        {
            _connections.Remove(connection);
        }

        return [.. viewer.Connections];
    }

    // The TV as it is now, with a new pairing code if it has none or it has expired.
    private TvSnapshot Snapshot(string tvId, Screen tv)
    {
        var now = _timeProvider.GetUtcNow();
        if (tv.Code is null || tv.CodeExpiresAt <= now)
        {
            if (tv.Code is not null)
            {
                _codes.Remove(tv.Code);
            }

            do
            {
                tv.Code = RandomNumberGenerator.GetString(CodeCharacters, CodeLength);
            }
            while (!_codes.TryAdd(tv.Code, tvId));
            tv.CodeExpiresAt = now + CodeLifetime;
        }

        return new TvSnapshot(tvId, tv.Name, [.. tv.Connections], tv.Code, tv.CodeExpiresAt, tv.Viewer?.Link);
    }
}
