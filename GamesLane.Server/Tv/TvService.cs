using GamesLane.Server.Hubs;
using GamesLane.Server.Notifications;
using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Tv;

/// <summary>
/// What the TV shows: its pairing code (valid until <paramref name="CodeExpiresAt"/>), the names of the users paired with
/// it, and the user whose games it shows (see <see cref="TvViewer"/>), or null.
/// </summary>
public record TvStatus(string Code, DateTimeOffset CodeExpiresAt, IReadOnlyList<string> PairedWith, TvViewer? Viewer);

/// <summary>
/// The user whose games a TV shows, as the TV sees them: their name and icon, their language (a code from i18n.js), and
/// the session they have open and its game, or null when they have none open.
/// </summary>
public record TvViewer(string Name, string Icon, string Language, string? SessionId, GameType? GameType);

/// <summary>
/// A TV paired with the user, and what it is for them now.
/// </summary>
public record PairedTv(string TvId, string Name, TvAvailability Status);

/// <summary>
/// The TV apps (see the developer guide): pairing TVs with saved users through the code a TV shows, and Watch on
/// TV, which shows the games a user opens on one of their TVs, as an observer. Every change is sent to the TV as
/// <see cref="TvUpdated"/>; the user's pages whose Watch on TV ends get <see cref="WatchOnTvEnded"/>.
/// </summary>
public class TvService(
    TvLinks links,
    ITvPairingRepository pairings,
    IGameSessionStore store,
    IHubContext<GameHub> hub,
    NotificationTexts texts,
    TimeProvider timeProvider)
{
    /// <summary>
    /// The message a TV's connections receive with its new <see cref="TvStatus"/>.
    /// </summary>
    public const string TvUpdated = "TvUpdated";

    /// <summary>
    /// The message a user's pages receive when their games no longer show on the TV.
    /// </summary>
    public const string WatchOnTvEnded = "WatchOnTvEnded";

    /// <summary>
    /// A connection of the TV opened, or the TV asks for its status again (e.g. when its code expires).
    /// </summary>
    public Task<TvStatus> ConnectAsync(string tvId, string name, string connectionId) =>
        StatusAsync(links.ConnectTv(tvId, name, connectionId));

    /// <summary>
    /// Pairs the open TV showing <paramref name="code"/> with the user. Returns the TV's name, or null if no TV shows it.
    /// </summary>
    public async Task<string?> PairAsync(Guid userId, string? code)
    {
        if (links.RedeemCode(code) is not { } tv)
        {
            return null;
        }

        await pairings.SaveAsync(userId, tv.TvId, tv.Name, timeProvider.GetUtcNow());
        await SendStatusAsync([tv.TvId]);
        return tv.Name;
    }

    /// <summary>
    /// The TVs paired with the user, and what each is for them now.
    /// </summary>
    public async Task<IReadOnlyList<PairedTv>> ListAsync(Guid userId) =>
        [.. (await pairings.GetForUserAsync(userId)).Select(p => new PairedTv(p.TvId, p.TvName, links.AvailabilityFor(p.TvId, userId)))];

    /// <summary>
    /// Unpairs the TV from the user, which stops showing their games on it.
    /// </summary>
    public async Task UnpairAsync(Guid userId, string tvId)
    {
        var change = links.Unlink(userId, tvId);
        await pairings.DeleteAsync(userId, tvId);
        await ApplyAsync(change with { TvIds = [.. change.TvIds.Append(tvId).Distinct()] });
    }

    /// <summary>
    /// Shows the user's games on their paired TV <paramref name="tvId"/>, in <paramref name="language"/>, starting with
    /// the session <paramref name="sessionId"/> (null on the site's other pages), for as long as their connection
    /// <paramref name="connectionId"/> or a later one is open. Returns an error message, or null.
    /// </summary>
    public async Task<string?> WatchAsync(Guid userId, PlayerProfile profile, string? language, string? tvId, string? sessionId,
        string connectionId)
    {
        if (tvId is null || !await pairings.TouchAsync(userId, tvId, timeProvider.GetUtcNow()))
        {
            return "This TV is not paired with your account.";
        }

        var session = string.IsNullOrEmpty(sessionId) ? null : await store.GetAsync(sessionId);
        var link = new TvLink(userId, profile.Name, profile.Icon, texts.Language(language), session?.Id, session?.Type);
        if (!links.TryLink(tvId, link, connectionId, out var change, out var error))
        {
            return error;
        }

        await ApplyAsync(change);
        return null;
    }

    /// <summary>
    /// Stops showing the user's games on a TV.
    /// </summary>
    public Task StopAsync(Guid userId) => ApplyAsync(links.Unlink(userId));

    /// <summary>
    /// Forgets a closed connection; a link whose TV or user does not come back ends later (see <see cref="ExpireAsync"/>).
    /// </summary>
    public void Disconnected(string connectionId) => links.Disconnect(connectionId, timeProvider.GetUtcNow());

    /// <summary>
    /// Ends the links whose TV or user has been gone too long (see <see cref="TvLinks.Expire"/>).
    /// </summary>
    public Task ExpireAsync() => ApplyAsync(links.Expire(timeProvider.GetUtcNow()));

    private async Task ApplyAsync(TvChange change)
    {
        if (change.EndedConnections.Count > 0)
        {
            await hub.Clients.Clients(change.EndedConnections).SendAsync(WatchOnTvEnded);
        }

        await SendStatusAsync(change.TvIds);
    }

    private async Task SendStatusAsync(IEnumerable<string> tvIds)
    {
        foreach (var tvId in tvIds)
        {
            if (links.Find(tvId) is { } tv)
            {
                await hub.Clients.Clients(tv.Connections).SendAsync(TvUpdated, await StatusAsync(tv));
            }
        }
    }

    private async Task<TvStatus> StatusAsync(TvSnapshot tv) =>
        new(tv.Code, tv.CodeExpiresAt, await pairings.GetUserNamesAsync(tv.TvId),
            tv.Link is { } link ? new TvViewer(link.Name, link.Icon, link.Language, link.SessionId, link.GameType) : null);
}
