using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// The games that can be played.
/// </summary>
public enum GameType
{
    TicTacToe,
    RockPaperScissors,
    TheGang,
}

/// <summary>
/// Which screen a session shows.
/// </summary>
public enum SessionPhase
{
    /// <summary>The session lobby: users take seats. No game has started yet, or the game is paused.</summary>
    Lobby,

    /// <summary>The game screen: a game is in progress or has just ended.</summary>
    Playing,
}

/// <summary>
/// How a user appears to other players: their display name, and an icon that is either a built-in emoji or the
/// address of an uploaded picture (starting with <c>/</c>).
/// </summary>
public record PlayerProfile(string Name, string Icon);

/// <summary>
/// A taken seat, as everyone sees it. <see cref="PlayerId"/> is a random ID for the user within this session,
/// so pages can recognise their own seats without revealing user IDs.
/// </summary>
public record SeatInfo(string PlayerId, string Name, string Icon, bool IsConnected);

/// <summary>
/// Public session state, safe to send to everyone in the session. <see cref="Seats"/> has one entry per seat (null when empty).
/// <see cref="GameNumber"/> is 0 until the first game starts; <see cref="Game"/> is the game-specific state, or null before then.
/// </summary>
public record SessionState(
    string SessionId,
    GameType GameType,
    SessionPhase Phase,
    int MinSeats,
    int MaxSeats,
    IReadOnlyList<SeatInfo?> Seats,
    int GameNumber,
    bool IsGameOver,
    bool CanResume,
    object? Game);

/// <summary>
/// Returned when a page enters a session: the public state, plus the caller's player ID if they are logged in, and
/// their private game information (e.g. their cards) if they hold seats in a game that has any.
/// </summary>
public record SessionEntry(SessionState State, string? PlayerId, object? Private);

/// <summary>
/// Private game information to send to one connection only.
/// </summary>
public record PrivateUpdate(string ConnectionId, object State);

/// <summary>
/// One session of a game: its seats, and the game played by the seated users.
/// Users are identified by a user key; a user may hold several seats. Each game type sets its number of seats.
/// When fewer players remain than the game needs, an unfinished game pauses and the session returns to the lobby,
/// where it can be resumed (by whoever then holds the seats) or replaced by a new game.
/// All public members are thread-safe; derived classes must lock <see cref="Sync"/> when accessing state.
/// </summary>
public abstract class GameSession : IGameSession
{
    private const int MaxSeatLimit = 12;

    /// <summary>
    /// Lock that protects all session and game state.
    /// </summary>
    protected readonly object Sync = new();

    private readonly Seat?[] _seats;
    private readonly Dictionary<string, string> _connections = [];
    private readonly Dictionary<string, DateTimeOffset> _disconnectedSince = [];
    private readonly Dictionary<string, string> _playerIds = [];
    private int[] _gameSeats = [];
    private DateTimeOffset? _emptySince;

    /// <summary>
    /// Creates an empty session in the lobby. Throws if the game's seat counts are invalid.
    /// </summary>
    protected GameSession(string id)
    {
        Id = id;
        if (MinSeats < 1 || MinSeats > MaxSeats || MaxSeats > MaxSeatLimit)
        {
            throw new InvalidOperationException($"A game needs between 1 and {MaxSeatLimit} seats.");
        }

        _seats = new Seat?[MaxSeats];
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public abstract GameType Type { get; }

    /// <summary>
    /// The number of seated players needed to start a game.
    /// </summary>
    protected abstract int MinSeats { get; }

    /// <summary>
    /// The number of seats (at most 12).
    /// </summary>
    protected abstract int MaxSeats { get; }

    /// <summary>
    /// Whether an unfinished game continues when players leave, as long as at least <see cref="MinSeats"/> of its players remain.
    /// The game must then handle the empty seats itself.
    /// </summary>
    protected virtual bool ContinuesWithFewerPlayers => false;

    /// <summary>
    /// Whether users can take a seat while a game is being played.
    /// </summary>
    protected virtual bool AllowsSeatingDuringGame => false;

    /// <summary>
    /// Whether the current game has ended.
    /// </summary>
    protected abstract bool IsGameOver { get; }

    /// <summary>
    /// Replaces the current game with a new one for the given seats.
    /// </summary>
    protected abstract void StartNewGame(IReadOnlyList<int> seats);

    /// <summary>
    /// The game-specific public state, sent as <see cref="SessionState.Game"/>. Called while holding <see cref="Sync"/>.
    /// </summary>
    protected abstract object GetGameState();

    /// <summary>
    /// The private information for a user holding <paramref name="seats"/> (seats of the current game), or null if the
    /// game has none. Sent only to that user's connections. Called while holding <see cref="Sync"/>.
    /// </summary>
    protected virtual object? GetPrivateGameState(IReadOnlyList<int> seats) => null;

    /// <summary>
    /// Seats playing the current game, in seat order. A game numbers its players in this order.
    /// </summary>
    protected IReadOnlyList<int> GameSeats => _gameSeats;

    /// <summary>
    /// Whether the session shows the session lobby or the game screen.
    /// </summary>
    private SessionPhase Phase { get; set; }

    /// <summary>
    /// The number of games started in this session; 0 before the first.
    /// </summary>
    private int GameNumber { get; set; }

    /// <summary>
    /// Whether a game has started and not ended (it may be paused).
    /// </summary>
    private bool HasUnfinishedGame => GameNumber > 0 && !IsGameOver;

    /// <summary>
    /// Registers a connection that opened the session page. If <paramref name="profile"/> is given (the user is logged in),
    /// updates the name and icon on the user's seats and returns the user's player ID.
    /// </summary>
    public string? Enter(string userKey, PlayerProfile? profile, string connectionId)
    {
        lock (Sync)
        {
            _connections[connectionId] = userKey;
            _disconnectedSince.Remove(userKey);
            if (profile is null)
            {
                return null;
            }

            foreach (var seat in _seats.Where(s => s?.UserKey == userKey))
            {
                seat!.Profile = profile;
            }

            return PlayerIdFor(userKey);
        }
    }

    /// <summary>
    /// Forgets a closed connection. Returns true if this leaves a seated user with no connection.
    /// </summary>
    public bool Disconnect(string connectionId, DateTimeOffset now)
    {
        lock (Sync)
        {
            if (!_connections.Remove(connectionId, out var userKey) || IsConnected(userKey) || !HoldsSeat(userKey))
            {
                return false;
            }

            _disconnectedSince[userKey] = now;
            return true;
        }
    }

    /// <summary>
    /// Frees the seats of users who have been disconnected since <paramref name="cutoff"/> or earlier. Returns true if any seat was freed.
    /// </summary>
    public bool ReleaseDisconnectedSeats(DateTimeOffset cutoff)
    {
        lock (Sync)
        {
            var expired = _disconnectedSince.Where(d => d.Value <= cutoff).Select(d => d.Key).ToList();
            var freed = 0;
            foreach (var userKey in expired)
            {
                freed += FreeSeats(userKey);
            }

            return freed > 0;
        }
    }

    /// <summary>
    /// Whether nobody has had the session open, and no seat has been taken, for at least <paramref name="gracePeriod"/>.
    /// The time is measured from the first call that finds the session empty, so call this regularly.
    /// </summary>
    public bool IsAbandoned(DateTimeOffset now, TimeSpan gracePeriod)
    {
        lock (Sync)
        {
            if (_connections.Count > 0 || _seats.Any(s => s is not null))
            {
                _emptySince = null;
                return false;
            }

            _emptySince ??= now;
            return now - _emptySince >= gracePeriod;
        }
    }

    /// <summary>
    /// Takes an empty seat, or the seat of a user who is disconnected. Only in the lobby, unless the game allows seating during a game.
    /// </summary>
    public bool TryTakeSeat(string userKey, PlayerProfile profile, string connectionId, int seat, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (seat < 0 || seat >= MaxSeats)
            {
                error = "There is no such seat.";
                return false;
            }

            if (Phase == SessionPhase.Playing && !AllowsSeatingDuringGame)
            {
                error = "The game has already started. You can watch it.";
                return false;
            }

            var current = _seats[seat];
            if (current?.UserKey == userKey)
            {
                error = "You are already in this seat.";
                return false;
            }

            if (current is not null && IsConnected(current.UserKey))
            {
                error = "That seat is taken.";
                return false;
            }

            _seats[seat] = new Seat(userKey, profile);
            _connections[connectionId] = userKey;
            _disconnectedSince.Remove(userKey);
            if (current is not null && !HoldsSeat(current.UserKey))
            {
                _disconnectedSince.Remove(current.UserKey);
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// Gives up one of the user's seats. An unfinished game pauses if too few of its players remain.
    /// </summary>
    public bool TryLeaveSeat(string userKey, int seat, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (seat < 0 || seat >= MaxSeats || _seats[seat]?.UserKey != userKey)
            {
                error = "That is not your seat.";
                return false;
            }

            _seats[seat] = null;
            if (!HoldsSeat(userKey))
            {
                _disconnectedSince.Remove(userKey);
            }

            AfterSeatsChanged();
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Frees all the user's seats. Returns true if they held any.
    /// </summary>
    public bool LeaveAllSeats(string userKey)
    {
        lock (Sync)
        {
            return FreeSeats(userKey) > 0;
        }
    }

    /// <summary>
    /// Starts a new game for everyone seated, replacing game <paramref name="gameNumber"/> (0 if there was none).
    /// If that game has already been replaced (for example, two players pressed Start), nothing changes.
    /// </summary>
    public bool TryNewGame(string userKey, int gameNumber, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryRequireSeated(userKey, out error) || gameNumber != GameNumber)
            {
                return error is null;
            }

            if (Phase == SessionPhase.Playing && !IsGameOver)
            {
                error = "The game is not over yet.";
                return false;
            }

            var seats = OccupiedSeats().ToArray();
            if (seats.Length < MinSeats)
            {
                error = $"At least {MinSeats} players must be seated.";
                return false;
            }

            _gameSeats = seats;
            GameNumber++;
            StartNewGame(seats);
            Phase = SessionPhase.Playing;
            return true;
        }
    }

    /// <summary>
    /// Continues the paused game. Whoever now holds each of its seats plays that seat.
    /// </summary>
    public bool TryResume(string userKey, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryRequireSeated(userKey, out error) || Phase == SessionPhase.Playing)
            {
                return error is null;
            }

            if (!HasUnfinishedGame)
            {
                error = "There is no game to resume.";
                return false;
            }

            if (!GameSeatsReady())
            {
                error = "Every player's seat must be taken to resume.";
                return false;
            }

            Phase = SessionPhase.Playing;
            return true;
        }
    }

    /// <summary>
    /// After a game has ended, takes everyone back to the session lobby.
    /// </summary>
    public bool TryReturnToLobby(string userKey, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryRequireSeated(userKey, out error) || Phase == SessionPhase.Lobby)
            {
                return error is null;
            }

            if (!IsGameOver)
            {
                error = "The game is not over yet.";
                return false;
            }

            Phase = SessionPhase.Lobby;
            return true;
        }
    }

    /// <summary>
    /// Public session state, safe to send to everyone in the session (players and spectators).
    /// </summary>
    public SessionState GetPublicState()
    {
        lock (Sync)
        {
            return new SessionState(
                Id,
                Type,
                Phase,
                MinSeats,
                MaxSeats,
                _seats.Select(s => s is null
                    ? null
                    : new SeatInfo(PlayerIdFor(s.UserKey), s.Profile.Name, s.Profile.Icon, IsConnected(s.UserKey))).ToArray(),
                GameNumber,
                GameNumber > 0 && IsGameOver,
                Phase == SessionPhase.Lobby && HasUnfinishedGame,
                GameNumber > 0 ? GetGameState() : null);
        }
    }

    /// <summary>
    /// The private game information for <paramref name="userKey"/> (e.g. the cards of their seats), or null if they hold
    /// no seat in the current game or the game has no private information.
    /// </summary>
    public object? GetPrivateState(string userKey)
    {
        lock (Sync)
        {
            return PrivateStateFor(userKey);
        }
    }

    /// <summary>
    /// The private game information for every open connection whose user has any.
    /// </summary>
    public IReadOnlyList<PrivateUpdate> GetPrivateUpdates()
    {
        lock (Sync)
        {
            var byUser = _connections.Values.Distinct().ToDictionary(userKey => userKey, PrivateStateFor);
            return _connections
                .Where(c => byUser[c.Value] is not null)
                .Select(c => new PrivateUpdate(c.Key, byUser[c.Value]!))
                .ToArray();
        }
    }

    /// <summary>
    /// Checks that a game is in progress and that <paramref name="userKey"/> holds <paramref name="seat"/> in it.
    /// </summary>
    protected bool TryGetPlayingSeat(string userKey, int seat, [NotNullWhen(false)] out string? error)
    {
        if (Phase != SessionPhase.Playing || !HasUnfinishedGame)
        {
            error = "The game is not in progress.";
            return false;
        }

        if (seat < 0 || seat >= MaxSeats || _seats[seat]?.UserKey != userKey || !_gameSeats.Contains(seat))
        {
            error = "Only players in this game can make moves.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Checks that <paramref name="userKey"/> holds a seat in the game being played.
    /// </summary>
    protected bool IsInGame(string userKey, [NotNullWhen(false)] out string? error)
    {
        var inGame = Phase == SessionPhase.Playing && _gameSeats.Any(s => _seats[s]?.UserKey == userKey);
        error = inGame ? null : "Only players in this game can do that.";
        return inGame;
    }

    /// <summary>
    /// The private game information for the seats <paramref name="userKey"/> holds in the current game, or null.
    /// </summary>
    private object? PrivateStateFor(string userKey)
    {
        if (GameNumber == 0)
        {
            return null;
        }

        var seats = _gameSeats.Where(s => _seats[s]?.UserKey == userKey).ToArray();
        return seats.Length == 0 ? null : GetPrivateGameState(seats);
    }

    /// <summary>
    /// Checks that <paramref name="userKey"/> holds at least one seat.
    /// </summary>
    private bool TryRequireSeated(string userKey, out string? error)
    {
        error = HoldsSeat(userKey) ? null : "Only seated players can do that.";
        return error is null;
    }

    /// <summary>
    /// Empties all the user's seats and returns how many were freed.
    /// </summary>
    private int FreeSeats(string userKey)
    {
        var freed = 0;
        for (var i = 0; i < _seats.Length; i++)
        {
            if (_seats[i]?.UserKey == userKey)
            {
                _seats[i] = null;
                freed++;
            }
        }

        _disconnectedSince.Remove(userKey);
        if (freed > 0)
        {
            AfterSeatsChanged();
        }

        return freed;
    }

    /// <summary>
    /// Goes back to the lobby when too few players remain: an unfinished game pauses, and after a game has ended,
    /// there are not enough players for a new one.
    /// </summary>
    private void AfterSeatsChanged()
    {
        if (Phase != SessionPhase.Playing)
        {
            return;
        }

        var enough = IsGameOver ? OccupiedSeats().Count() >= MinSeats : GameSeatsReady();
        if (!enough)
        {
            Phase = SessionPhase.Lobby;
        }
    }

    /// <summary>
    /// Whether enough of the current game's seats are taken for it to be played.
    /// </summary>
    private bool GameSeatsReady()
    {
        var taken = _gameSeats.Count(s => _seats[s] is not null);
        return taken == _gameSeats.Length || (ContinuesWithFewerPlayers && taken >= MinSeats);
    }

    /// <summary>
    /// The numbers of the taken seats, in order.
    /// </summary>
    private IEnumerable<int> OccupiedSeats() => Enumerable.Range(0, MaxSeats).Where(i => _seats[i] is not null);

    /// <summary>
    /// Whether <paramref name="userKey"/> holds any seat.
    /// </summary>
    private bool HoldsSeat(string userKey) => _seats.Any(s => s?.UserKey == userKey);

    /// <summary>
    /// Whether <paramref name="userKey"/> has at least one open connection to this session.
    /// </summary>
    private bool IsConnected(string userKey) => _connections.ContainsValue(userKey);

    /// <summary>
    /// The user's random player ID in this session, created on first use.
    /// </summary>
    private string PlayerIdFor(string userKey)
    {
        if (!_playerIds.TryGetValue(userKey, out var playerId))
        {
            playerId = Guid.NewGuid().ToString("N");
            _playerIds[userKey] = playerId;
        }

        return playerId;
    }

    /// <summary>
    /// A taken seat: who holds it, and the name and icon shown on it.
    /// </summary>
    private sealed class Seat(string userKey, PlayerProfile profile)
    {
        /// <summary>
        /// The user holding the seat.
        /// </summary>
        public string UserKey { get; } = userKey;

        /// <summary>
        /// The user's display name and icon, updated when they change them and re-enter the session.
        /// </summary>
        public PlayerProfile Profile { get; set; } = profile;
    }
}
