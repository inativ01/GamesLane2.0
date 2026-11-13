using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games;

namespace GamesLane.Server.Sessions;

/// <summary>
/// The games that can be played.
/// </summary>
public enum GameType
{
    TicTacToe,
    TheGang,
    Backgammon,
    RichManPoorMan,
    Wazoo,
    FinalCard,
    Yaniv,
    Hearts,
}

/// <summary>
/// A session's status.
/// </summary>
public enum SessionPhase
{
    /// <summary>The session lobby: users take seats and choose options. No game is in progress.</summary>
    Lobby,

    /// <summary>The game screen: a game is in progress (see <see cref="SessionState.WaitingSeats"/> for the absent
    /// players it may be waiting for).</summary>
    Playing,

    /// <summary>The game screen: the game has ended by itself and shows its result.</summary>
    Over,
}

/// <summary>
/// The state of a taken seat. A seat whose holder is not connected (<see cref="Reconnecting"/> or <see cref="Away"/>),
/// or an empty seat, is absent.
/// </summary>
public enum SeatStatus
{
    /// <summary>The holder is connected.</summary>
    Present,

    /// <summary>The holder lost their connection less than <see cref="GameSession.ReconnectWindow"/> ago.</summary>
    Reconnecting,

    /// <summary>The holder has been disconnected for <see cref="GameSession.ReconnectWindow"/> or more. During a game,
    /// the seat is still theirs until a newcomer takes it.</summary>
    Away,
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
public record SeatInfo(string PlayerId, string Name, string Icon, SeatStatus Status);

/// <summary>
/// Public session state, safe to send to everyone in the session. <see cref="Seats"/> has one entry per seat (null when empty);
/// <see cref="MaxSeats"/> is the number of them a game can use with the chosen options.
/// <see cref="GameNumber"/> is 0 until the first game starts; <see cref="Game"/> is the game-specific state, or null before then.
/// <see cref="Options"/> are the chosen values of the game's options (<see cref="OptionList"/>), used by the next new game.
/// <see cref="AbsentSeats"/> are, during a game, its seats that are empty or whose holder is not connected, in order (a
/// newcomer offered a seat can take one). <see cref="WaitingSeats"/> are the absent seats the game is waiting for, once
/// every present player has done what they can (empty otherwise); <see cref="CanSkipWaiting"/> tells whether the game
/// can skip them (otherwise the players can only end the game).
/// <see cref="IsPublic"/> tells a public game; <see cref="HostPlayerId"/> is its creator's player ID (who alone chooses
/// the options and starts it), and <see cref="Notice"/> a message for everyone, e.g. that a player left the game.
/// <see cref="ObserverCount"/> is the number of open connections to the session of users who hold no seat.
/// </summary>
public record SessionState(
    string SessionId,
    GameType GameType,
    SessionPhase Phase,
    int MinSeats,
    int MaxSeats,
    IReadOnlyList<SeatInfo?> Seats,
    int GameNumber,
    object? Game,
    IReadOnlyList<GameOption> OptionList,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlyList<int> AbsentSeats,
    IReadOnlyList<int> WaitingSeats,
    bool CanSkipWaiting,
    bool IsPublic,
    string? HostPlayerId,
    string? Notice,
    int ObserverCount);

/// <summary>
/// A game in progress where the user holds a seat but is not connected (Reconnecting or Away), so the other players are
/// waiting for them.
/// </summary>
public record WaitingGame(string SessionId, GameType GameType, bool IsPublic);

/// <summary>
/// Returned when a page enters a session: the public state, plus the caller's player ID if they are logged in, and
/// their private game information (e.g. their cards) if they hold seats in a game that has any, the session's latest
/// chat messages, oldest first, and whether the caller is offered an absent seat of the game in progress
/// (<see cref="SeatOffer"/>; see <see cref="GameSession.HasSeatOffer"/>).
/// </summary>
public record SessionEntry(SessionState State, string? PlayerId, object? Private, IReadOnlyList<ChatMessage> Chat, bool SeatOffer);

/// <summary>
/// A chat message, as everyone in the session sees it. <see cref="Number"/> counts the session's messages from 1;
/// <see cref="PlayerId"/> is the sender's player ID in the session, so a page recognises its own user's messages.
/// </summary>
public record ChatMessage(int Number, string PlayerId, string Name, string Icon, string Text, DateTimeOffset Time);

/// <summary>
/// Private game information to send to one connection only.
/// </summary>
public record PrivateUpdate(string ConnectionId, object State);

/// <summary>
/// One session of a game: its seats, and the game played by the seated users.
/// Users are identified by a user key; a user may hold several seats. Each game type sets its number of seats.
/// Users do not choose a seat: they take the next available one. In the lobby, the taken seats are always the first
/// ones: when a seat is freed, the seats after it move up.
/// A user who disconnects keeps their seats for <see cref="ReconnectWindow"/>; then they are Away. In the lobby, and once
/// a game is over, an Away user's seats are freed. During a game, they keep them until they come back or a newcomer
/// takes them: a user without a seat who opens the game, or reconnects to it, while it has absent seats is offered them
/// (see <see cref="HasSeatOffer"/>); players, and watchers who are not offered a seat, cannot take them. A
/// disconnected player's seat is absent at once; the game goes on until it
/// needs them (see <see cref="AwaitedSeats"/>), and then the players can skip them where the game allows it
/// (<see cref="TrySkipPlayer"/>), or end the game. Any seated player can end a game.
/// A public game (see <see cref="MakePublic"/>) is joined only from the main screen, and seats nobody else: its creator
/// (the host; when their seat is freed, the next seated player) alone chooses the options and starts it from the lobby,
/// it starts by itself when a player joins and fills its seats, and a player who leaves it during a game ends the game
/// (unless <see cref="ContinuesWithAbsentPlayers"/>).
/// All public members are thread-safe; derived classes must lock <see cref="Sync"/> when accessing state.
/// </summary>
public abstract class GameSession : IGameSession
{
    /// <summary>
    /// How long a disconnected user's seats count as present: after that, they are Away.
    /// </summary>
    public static readonly TimeSpan ReconnectWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The longest chat message, in characters.
    /// </summary>
    public const int MaxChatLength = 300;

    /// <summary>
    /// How many of the latest chat messages a session keeps.
    /// </summary>
    public const int ChatHistoryLength = 100;

    private const int MaxSeatLimit = 12;

    /// <summary>
    /// Lock that protects all session and game state.
    /// </summary>
    protected readonly object Sync = new();

    private readonly Seat?[] _seats;
    [NotSaved]
    private readonly Dictionary<string, string> _connections = [];
    private readonly Dictionary<string, DateTimeOffset> _disconnectedSince = [];
    private readonly HashSet<string> _away = [];
    private readonly Dictionary<string, string> _playerIds = [];
    private int[] _gameSeats = [];
    private bool _onGameScreen;
    private bool _isPublic;
    private string? _host;
    private string? _notice;
    [NotSaved]
    private DateTimeOffset? _idleSince;
    private IReadOnlyDictionary<string, string> _options;
    private readonly List<ChatMessage> _chat = [];
    private int _chatCount;
    // During a game: the users offered an absent seat when they last opened it or reconnected to it.
    private readonly HashSet<string> _seatOffers = [];

    /// <summary>
    /// Creates an empty session in the lobby, with the game's default options. Throws if the game's seat counts are invalid.
    /// </summary>
    protected GameSession(string id)
    {
        Id = id;
        if (MinSeats < 1 || MinSeats > MaxSeats || MaxSeats > MaxSeatLimit)
        {
            throw new InvalidOperationException($"A game needs between 1 and {MaxSeatLimit} seats.");
        }

        _seats = new Seat?[MaxSeats];
        _options = GameOptions.Defaults(Type);
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
    /// The number of seats a game can use with <paramref name="options"/> (at most <see cref="MaxSeats"/>), for a game
    /// whose number of players depends on its options (e.g. a style for fewer players). All of them by default.
    /// </summary>
    protected virtual int SeatLimitFor(IReadOnlyDictionary<string, string> options) => MaxSeats;

    /// <summary>
    /// Whether a public game goes on when one of its players gives up their seat. Otherwise it ends, back in the lobby.
    /// </summary>
    protected virtual bool ContinuesWithAbsentPlayers => false;

    /// <summary>
    /// During a game, why the absent game seat <paramref name="seat"/> cannot be taken now, or null if it can (the
    /// newcomer continues that player's game); <paramref name="seat"/> is -1 when no seat is absent. By default, it can.
    /// </summary>
    protected virtual string? SeatingDuringGameError(int seat) => null;

    /// <summary>
    /// The game seats whose action the game needs now: the seat to play, or the seats that have not yet done what all
    /// must do (e.g. pick, or give cards). Once all of them are absent, the game waits for them (see
    /// <see cref="SessionState.WaitingSeats"/>). Called while holding <see cref="Sync"/>, during a game. None by default.
    /// </summary>
    protected virtual IEnumerable<int> AwaitedSeats => [];

    /// <summary>
    /// Whether the game can skip the absent seats it waits for (see <see cref="TrySkipAwaited"/>). Otherwise the players
    /// can only end the game.
    /// </summary>
    protected virtual bool CanSkipAwaited => false;

    /// <summary>
    /// Skips the turn of <paramref name="seat"/>, an absent seat the game waits for, when <see cref="CanSkipAwaited"/>.
    /// Called while holding <see cref="Sync"/>.
    /// </summary>
    protected virtual bool TrySkipAwaited(int seat, [NotNullWhen(false)] out string? error)
    {
        error = "This player cannot be skipped. You can end the game.";
        return false;
    }

    /// <summary>
    /// Called after a seat is taken or freed, or a holder disconnects, becomes Away or comes back, while holding
    /// <see cref="Sync"/>.
    /// </summary>
    protected virtual void OnSeatsChanged()
    {
    }

    /// <summary>
    /// Called by <see cref="TransferSeats"/>, while holding <see cref="Sync"/>, for a game that keeps more about its
    /// users than their seats (e.g. requests to join). Returns true if anything changed.
    /// </summary>
    protected virtual bool OnUserReplaced(string fromUserKey, string toUserKey, PlayerProfile profile) => false;

    /// <summary>
    /// Called after the session was loaded from a save (see <see cref="Restore"/>), while holding <see cref="Sync"/>:
    /// a session gives its game again what is not saved, such as its dice or deck.
    /// </summary>
    protected virtual void OnRestored()
    {
    }

    /// <summary>
    /// The session's state as JSON, to be loaded again with <see cref="Restore"/> (see <see cref="SessionSnapshot"/>).
    /// Open connections are not saved.
    /// </summary>
    public string Save()
    {
        lock (Sync)
        {
            return SessionSnapshot.Save(this);
        }
    }

    /// <summary>
    /// Loads a state saved at <paramref name="savedAt"/> into this new session. Nobody is connected yet: users who were
    /// connected then count as disconnected since then.
    /// </summary>
    public void Restore(string data, DateTimeOffset savedAt)
    {
        lock (Sync)
        {
            SessionSnapshot.Restore(this, data);
            foreach (var userKey in _seats.OfType<Seat>().Select(s => s.UserKey).Distinct())
            {
                _disconnectedSince.TryAdd(userKey, savedAt);
            }

            OnRestored();
        }
    }

    /// <summary>
    /// Whether a game has ever been started in this session.
    /// </summary>
    public bool HasStarted
    {
        get
        {
            lock (Sync)
            {
                return GameNumber > 0;
            }
        }
    }

    /// <inheritdoc />
    public int GamesStarted
    {
        get
        {
            lock (Sync)
            {
                return GameNumber;
            }
        }
    }

    /// <inheritdoc />
    public bool HasFinishedGame
    {
        get
        {
            lock (Sync)
            {
                return Phase == SessionPhase.Over;
            }
        }
    }

    /// <summary>
    /// Whether this is a public game (see <see cref="MakePublic"/>).
    /// </summary>
    public bool IsPublic
    {
        get
        {
            lock (Sync)
            {
                return _isPublic;
            }
        }
    }

    /// <summary>
    /// Whether any seat is taken.
    /// </summary>
    public bool HasSeatedPlayers
    {
        get
        {
            lock (Sync)
            {
                return _seats.Any(s => s is not null);
            }
        }
    }

    /// <summary>
    /// Whether a random player can join this session from the main screen: a public game in the lobby, with a player
    /// and a free seat.
    /// </summary>
    public bool IsOpenToJoin
    {
        get
        {
            lock (Sync)
            {
                return _isPublic && !_onGameScreen && OccupiedSeats().Count() < SeatLimit && _seats.Any(s => s is not null);
            }
        }
    }

    /// <summary>
    /// Makes this a public game. Call before anyone takes a seat; the first to take one is the host.
    /// </summary>
    public void MakePublic()
    {
        lock (Sync)
        {
            _isPublic = true;
        }
    }

    /// <summary>
    /// Whether <paramref name="userKey"/> holds a seat.
    /// </summary>
    public bool IsSeated(string userKey)
    {
        lock (Sync)
        {
            return HoldsSeat(userKey);
        }
    }

    /// <summary>
    /// Whether a game is in progress where <paramref name="userKey"/> holds a seat but is not connected.
    /// </summary>
    public bool IsWaitingFor(string userKey)
    {
        lock (Sync)
        {
            return HasUnfinishedGame && _gameSeats.Any(s => _seats[s]?.UserKey == userKey) && !IsConnected(userKey);
        }
    }

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
    /// The chosen value of every option of the game (see <see cref="GameOptions"/>). Read it in <see cref="StartNewGame"/>.
    /// </summary>
    protected IReadOnlyDictionary<string, string> Options => _options;

    /// <summary>
    /// The user holding <paramref name="seat"/>, or null if it is empty. Call while holding <see cref="Sync"/>.
    /// </summary>
    protected string? HolderOf(int seat) => _seats[seat]?.UserKey;

    /// <summary>
    /// Whether <paramref name="seat"/> is absent: empty, or its holder is not connected (Reconnecting or Away). Call while
    /// holding <see cref="Sync"/>.
    /// </summary>
    protected bool IsAbsent(int seat) => _seats[seat] is not { } taken || !IsConnected(taken.UserKey);

    /// <summary>
    /// Whether <paramref name="seat"/> is empty, or its holder has been disconnected for <see cref="ReconnectWindow"/> or
    /// more (Away). Call while holding <see cref="Sync"/>.
    /// </summary>
    protected bool IsAwayOrVacant(int seat) => _seats[seat] is not { } taken || _away.Contains(taken.UserKey);

    /// <summary>
    /// The session's status, from the screen it shows and whether the game is over.
    /// </summary>
    private SessionPhase Phase =>
        !_onGameScreen ? SessionPhase.Lobby
        : IsGameOver ? SessionPhase.Over
        : SessionPhase.Playing;

    /// <summary>
    /// The number of games started in this session; 0 before the first.
    /// </summary>
    private int GameNumber { get; set; }

    /// <summary>
    /// Whether a game is in progress (Playing).
    /// </summary>
    private bool HasUnfinishedGame => _onGameScreen && !IsGameOver;

    /// <summary>
    /// Registers a connection that opened the session page. If <paramref name="profile"/> is given (the user is logged in),
    /// updates the name and icon on the user's seats and returns the user's player ID. A logged-in user opening a private
    /// session for the first time while it is in the lobby takes the next empty seat if there is one; later, they keep
    /// watching until they take a seat. A logged-in user without a seat who opens or reconnects to a private game in
    /// progress is offered its absent seats if it has any (see <see cref="HasSeatOffer"/>); otherwise they watch it.
    /// </summary>
    public string? Enter(string userKey, PlayerProfile? profile, string connectionId)
    {
        lock (Sync)
        {
            _connections[connectionId] = userKey;
            MarkConnected(userKey);
            if (profile is null)
            {
                return null;
            }

            if (!_playerIds.ContainsKey(userKey) && !_isPublic && !_onGameScreen)
            {
                TryTakeNextSeat(userKey, profile, connectionId, out _);
            }

            foreach (var seat in _seats.Where(s => s?.UserKey == userKey))
            {
                seat!.Profile = profile;
            }

            if (HasUnfinishedGame && !_isPublic && !HoldsSeat(userKey) && OfferedSeats().Any())
            {
                _seatOffers.Add(userKey);
            }
            else
            {
                _seatOffers.Remove(userKey);
            }

            return PlayerIdFor(userKey);
        }
    }

    /// <summary>
    /// Whether <paramref name="userKey"/> is offered an absent seat of the game in progress: they last opened it, or
    /// reconnected to it, without a seat while it had absent seats, and have neither taken one nor chosen to watch (see
    /// <see cref="DeclineSeatOffer"/>).
    /// </summary>
    public bool HasSeatOffer(string userKey)
    {
        lock (Sync)
        {
            return HasUnfinishedGame && _seatOffers.Contains(userKey);
        }
    }

    /// <summary>
    /// <paramref name="userKey"/> declines their seat offer and watches the game, until they open it or reconnect again.
    /// Returns true if they had an offer.
    /// </summary>
    public bool DeclineSeatOffer(string userKey)
    {
        lock (Sync)
        {
            return _seatOffers.Remove(userKey);
        }
    }

    /// <summary>
    /// A player (<paramref name="userKey"/>) skips the turn of <paramref name="seat"/>, an absent seat the game waits for
    /// (see <see cref="SessionState.WaitingSeats"/>), in a game that allows it. If the game no longer waits for that seat
    /// (e.g. its player came back), nothing changes.
    /// </summary>
    public bool TrySkipPlayer(string userKey, int seat, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!IsInGame(userKey, out error) || !HasUnfinishedGame)
            {
                error ??= "The game is not in progress.";
                return false;
            }

            if (!WaitingSeats().Contains(seat))
            {
                return true;
            }

            if (!CanSkipAwaited)
            {
                error = "This player cannot be skipped. You can end the game.";
                return false;
            }

            return TrySkipAwaited(seat, out error);
        }
    }

    /// <summary>
    /// Forgets a closed connection. Returns true if this changes the public state: it leaves a seated user with no
    /// connection, or it was an observer's connection.
    /// </summary>
    public bool Disconnect(string connectionId, DateTimeOffset now)
    {
        lock (Sync)
        {
            if (!_connections.Remove(connectionId, out var userKey))
            {
                return false;
            }

            if (!HoldsSeat(userKey))
            {
                return true;
            }

            if (IsConnected(userKey))
            {
                return false;
            }

            _disconnectedSince[userKey] = now;
            OnSeatsChanged();
            return true;
        }
    }

    /// <summary>
    /// Gives everything <paramref name="fromUserKey"/> holds in the session (their seats, player ID, and whether they are
    /// reconnecting or Away) to <paramref name="toUserKey"/>, shown as <paramref name="profile"/>, when the same person
    /// logs in or out. If <paramref name="toUserKey"/> is not connected, they count as disconnected since
    /// <paramref name="fromUserKey"/> was, or since <paramref name="now"/>. Returns true if anything changed.
    /// </summary>
    public bool TransferSeats(string fromUserKey, string toUserKey, PlayerProfile profile, DateTimeOffset now)
    {
        lock (Sync)
        {
            if (fromUserKey == toUserKey)
            {
                return false;
            }

            var moved = false;
            for (var i = 0; i < _seats.Length; i++)
            {
                if (_seats[i]?.UserKey == fromUserKey)
                {
                    _seats[i] = new Seat(toUserKey, profile);
                    moved = true;
                }
            }

            var changed = OnUserReplaced(fromUserKey, toUserKey, profile);
            if (!moved && !changed)
            {
                return false;
            }

            if (!_playerIds.ContainsKey(toUserKey) && _playerIds.Remove(fromUserKey, out var playerId))
            {
                _playerIds[toUserKey] = playerId;
            }

            if (moved && _host == fromUserKey)
            {
                _host = toUserKey;
            }

            if (_seatOffers.Remove(fromUserKey))
            {
                _seatOffers.Add(toUserKey);
            }

            var wasDisconnected = _disconnectedSince.Remove(fromUserKey, out var since);
            var wasAway = _away.Remove(fromUserKey);
            if (moved && !IsConnected(toUserKey))
            {
                _disconnectedSince.TryAdd(toUserKey, wasDisconnected ? since : now);
                if (wasAway)
                {
                    _away.Add(toUserKey);
                }
            }

            if (moved)
            {
                OnSeatsChanged();
            }

            return true;
        }
    }

    /// <summary>
    /// Marks users disconnected for <see cref="ReconnectWindow"/> or more as Away. Away users keep their seats only during
    /// a game; otherwise their seats are freed. Returns true if anything changed.
    /// </summary>
    public bool UpdateAwaySeats(DateTimeOffset now)
    {
        lock (Sync)
        {
            var newlyAway = _disconnectedSince
                .Where(d => now - d.Value >= ReconnectWindow && !_away.Contains(d.Key))
                .Select(d => d.Key)
                .ToList();
            _away.UnionWith(newlyAway);
            var freed = FreeAwaySeats();
            if (newlyAway.Count > 0 && freed == 0)
            {
                OnSeatsChanged();
            }

            return newlyAway.Count > 0 || freed > 0;
        }
    }

    /// <summary>
    /// Whether nobody has had the session open for at least <paramref name="idleTime"/>. The time is measured from the
    /// first call that finds nobody connected, so call this regularly.
    /// </summary>
    public bool IsIdle(DateTimeOffset now, TimeSpan idleTime)
    {
        lock (Sync)
        {
            if (_connections.Count > 0)
            {
                _idleSince = null;
                return false;
            }

            _idleSince ??= now;
            return now - _idleSince >= idleTime;
        }
    }

    /// <summary>
    /// Takes a seat. In the lobby, an empty seat. During a game, only a user offered a seat (see <see cref="HasSeatOffer"/>)
    /// can take an absent seat of the game (empty, or its holder is not connected, who then loses it), unless
    /// the game refuses it (see <see cref="SeatingDuringGameError"/>); the newcomer continues that player's game. Not once
    /// a game is over.
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

            if (_seats[seat]?.UserKey == userKey)
            {
                error = "You are already in this seat.";
                return false;
            }

            error = _isPublic && _onGameScreen ? "A public game cannot be joined once it has started."
                : Phase == SessionPhase.Over ? "The game is over. Wait for the next game to take a seat."
                : !HasUnfinishedGame ? (seat >= SeatLimit ? "All seats are taken." : _seats[seat] is null ? null : "That seat is taken.")
                : !_gameSeats.Contains(seat) ? "The game has already started. You can watch it."
                : !IsAbsent(seat) ? "That seat is taken."
                : !_seatOffers.Contains(userKey) ? "Only a user opening the game without a seat can take a disconnected player's seat."
                : SeatingDuringGameError(seat);
            if (error is not null)
            {
                return false;
            }

            var previous = _seats[seat]?.UserKey;
            _seats[seat] = new Seat(userKey, profile);
            PlayerIdFor(userKey);
            _seatOffers.Remove(userKey);
            if (previous is not null && !HoldsSeat(previous))
            {
                _disconnectedSince.Remove(previous);
                _away.Remove(previous);
            }

            _connections[connectionId] = userKey;
            MarkConnected(userKey);
            if (_isPublic)
            {
                _host ??= userKey;
                if (OccupiedSeats().Count() >= SeatLimit)
                {
                    TryStartGame(out _);
                }
            }

            OnSeatsChanged();
            return true;
        }
    }

    /// <summary>
    /// Takes the next available seat: in the lobby, the first empty one; during a game, the first absent seat of the game
    /// that can be taken.
    /// </summary>
    public bool TryTakeNextSeat(string userKey, PlayerProfile profile, string connectionId, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            var seat = HasUnfinishedGame
                ? AbsentGameSeats().Where(s => SeatingDuringGameError(s) is null).DefaultIfEmpty(-1).First()
                : Enumerable.Range(0, SeatLimit).Where(s => _seats[s] is null).DefaultIfEmpty(-1).First();
            if (seat < 0)
            {
                error = Phase == SessionPhase.Over ? "The game is over. Wait for the next game to take a seat."
                    : HasUnfinishedGame ? AbsentGameSeats().DefaultIfEmpty(-1).Select(SeatingDuringGameError)
                        .FirstOrDefault(e => e is not null) ?? "The game has already started. You can watch it."
                    : "All seats are taken.";
                return false;
            }

            return TryTakeSeat(userKey, profile, connectionId, seat, out error);
        }
    }

    /// <summary>
    /// Gives up one of the user's seats. During a game, the seat is then absent, and a user left without a seat watches
    /// the game.
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

            var leaver = LeaverOfPublicGame([seat]);
            VacateSeat(seat);
            if (leaver is not null)
            {
                EndGameAfterLeaving(leaver);
            }

            AfterSeatsChanged();
            OnSeatsChanged();
            CloseGapsInLobby();
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

            if (HasUnfinishedGame)
            {
                error = "The game is not over yet.";
                return false;
            }

            if (_isPublic && !_onGameScreen && userKey != _host)
            {
                error = "Only the game's creator can start the game.";
                return false;
            }

            return TryStartGame(out error);
        }
    }

    /// <summary>
    /// Starts a new game for everyone seated, if enough are.
    /// </summary>
    private bool TryStartGame([NotNullWhen(false)] out string? error)
    {
        MoveSeatsUp();
        var seats = OccupiedSeats().ToArray();
        if (seats.Length < MinSeats)
        {
            error = $"At least {MinSeats} players must be seated.";
            return false;
        }

        _gameSeats = seats;
        GameNumber++;
        _notice = null;
        StartNewGame(seats);
        _onGameScreen = true;
        _seatOffers.Clear();
        error = null;
        return true;
    }

    /// <summary>
    /// Changes the game's options, for the next new game. Anyone in the session, only in the lobby. Options left out
    /// get their default; unknown options and values are refused.
    /// </summary>
    public bool TrySetOptions(string userKey, IReadOnlyDictionary<string, string>? options, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (Phase != SessionPhase.Lobby)
            {
                error = "Options can only be changed in the lobby.";
                return false;
            }

            if (_isPublic && userKey != _host)
            {
                error = "Only the game's creator can change the options.";
                return false;
            }

            if (!GameOptions.TryValidate(Type, options, out var normalized, out error))
            {
                return false;
            }

            var limit = SeatLimitFor(normalized);
            if (OccupiedSeats().Count() > limit)
            {
                error = $"With these options, at most {limit} players can play.";
                return false;
            }

            _options = normalized;
            return true;
        }
    }

    /// <summary>
    /// Uses saved preferences as the options, e.g. the creator's when the session is created. Values that are no
    /// longer valid get their default.
    /// </summary>
    public void UseSavedOptions(IReadOnlyDictionary<string, string>? options)
    {
        lock (Sync)
        {
            _options = GameOptions.Normalize(Type, options);
        }
    }

    /// <summary>
    /// Ends the game in progress and takes everyone back to the session lobby. The seats then move
    /// up, other users can join, and a new game can be started.
    /// </summary>
    public bool TryEndGame(string userKey, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryRequireSeated(userKey, out var seatError))
            {
                error = seatError!;
                return false;
            }

            if (!HasUnfinishedGame)
            {
                error = "There is no game in progress to end.";
                return false;
            }

            EndGameInLobby();
            error = null;
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

            EndGameInLobby();
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
            var waiting = WaitingSeats();
            return new SessionState(
                Id,
                Type,
                Phase,
                MinSeats,
                SeatLimit,
                _seats.Select(s => s is null
                    ? null
                    : new SeatInfo(PlayerIdFor(s.UserKey), s.Profile.Name, s.Profile.Icon, StatusOf(s.UserKey))).ToArray(),
                GameNumber,
                GameNumber > 0 ? GetGameState() : null,
                GameOptions.For(Type),
                _options,
                HasUnfinishedGame ? [.. AbsentGameSeats()] : [],
                waiting,
                waiting.Count > 0 && CanSkipAwaited,
                _isPublic,
                _host is null ? null : PlayerIdFor(_host),
                _notice,
                _connections.Values.Count(userKey => !HoldsSeat(userKey)));
        }
    }

    /// <summary>
    /// Adds a chat message from <paramref name="userKey"/>, shown with <paramref name="profile"/>. Anyone with the session
    /// open can chat, seated or watching. Runs of spaces and line breaks become one space. Only the latest
    /// <see cref="ChatHistoryLength"/> messages are kept.
    /// </summary>
    public bool TrySendChat(string userKey, PlayerProfile profile, string? text, DateTimeOffset now,
        [NotNullWhen(true)] out ChatMessage? message, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            message = null;
            text = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length is 0 or > MaxChatLength)
            {
                error = $"Write a message of 1 to {MaxChatLength} characters.";
                return false;
            }

            if (!IsConnected(userKey))
            {
                error = "Open the game to chat.";
                return false;
            }

            message = new ChatMessage(++_chatCount, PlayerIdFor(userKey), profile.Name, profile.Icon, text, now);
            _chat.Add(message);
            if (_chat.Count > ChatHistoryLength)
            {
                _chat.RemoveRange(0, _chat.Count - ChatHistoryLength);
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// The session's latest chat messages, oldest first.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetChat()
    {
        lock (Sync)
        {
            return [.. _chat];
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
        if (!HasUnfinishedGame)
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
        var inGame = _onGameScreen && _gameSeats.Any(s => _seats[s]?.UserKey == userKey);
        error = inGame ? null : "Only players in this game can do that.";
        return inGame;
    }

    /// <summary>
    /// Whether the session shows the game screen (a game is being played, or has just ended), not the session lobby.
    /// </summary>
    protected bool IsPlaying => _onGameScreen;

    /// <summary>
    /// Changes which seats play the current game, for a game whose players change between its rounds.
    /// </summary>
    protected void SetGameSeats(IEnumerable<int> seats) => _gameSeats = [.. seats.Order()];

    /// <summary>
    /// Seats <paramref name="userKey"/> in the first empty seat, whatever the phase (for a game that seats players
    /// itself, e.g. once another player accepts them). Returns the seat, or null if every seat is taken.
    /// </summary>
    protected int? SeatUser(string userKey, PlayerProfile profile)
    {
        var seat = Array.FindIndex(_seats, s => s is null);
        if (seat < 0)
        {
            return null;
        }

        _seats[seat] = new Seat(userKey, profile);
        PlayerIdFor(userKey);
        MarkConnected(userKey);
        return seat;
    }

    /// <summary>
    /// Empties <paramref name="seat"/> without any other effect (for a game that removes players itself, e.g. players
    /// who left at a card exchange). Call while holding <see cref="Sync"/>.
    /// </summary>
    protected void VacateSeat(int seat)
    {
        if (_seats[seat] is not { } taken)
        {
            return;
        }

        _seats[seat] = null;
        if (!HoldsSeat(taken.UserKey))
        {
            _disconnectedSince.Remove(taken.UserKey);
            _away.Remove(taken.UserKey);
        }

        UpdateHost();
    }

    /// <summary>
    /// The user with the player ID <paramref name="playerId"/> in this session, or null.
    /// </summary>
    protected string? UserKeyOf(string playerId) => _playerIds.FirstOrDefault(p => p.Value == playerId).Key;

    /// <summary>
    /// Ends the current game and takes everyone back to the session lobby, where a new game can be started (for a game
    /// that cannot go on, e.g. with too few players).
    /// </summary>
    protected void EndGameInLobby()
    {
        _onGameScreen = false;
        _seatOffers.Clear();
        FreeAwaySeats();
        CloseGapsInLobby();
    }

    /// <summary>
    /// The private game information for the seats <paramref name="userKey"/> holds in the current game, or null.
    /// Nothing is sent in the lobby, where seats may have changed hands or moved.
    /// </summary>
    private object? PrivateStateFor(string userKey)
    {
        if (GameNumber == 0 || !_onGameScreen)
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
        var leaver = LeaverOfPublicGame(OccupiedSeats().Where(s => _seats[s]!.UserKey == userKey));
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
        _away.Remove(userKey);
        if (freed > 0)
        {
            UpdateHost();
            if (leaver is not null)
            {
                EndGameAfterLeaving(leaver);
            }

            AfterSeatsChanged();
            OnSeatsChanged();
            CloseGapsInLobby();
        }

        return freed;
    }

    /// <summary>
    /// The name on <paramref name="seats"/> if giving them up ends a public game in progress (one of them plays it, and the
    /// game does not go on without its players), otherwise null.
    /// </summary>
    private string? LeaverOfPublicGame(IEnumerable<int> seats) =>
        _isPublic && HasUnfinishedGame && !ContinuesWithAbsentPlayers
            ? seats.Where(s => _gameSeats.Contains(s)).Select(s => _seats[s]?.Profile.Name).FirstOrDefault()
            : null;

    /// <summary>
    /// A player left a public game in progress: everyone goes back to the lobby, and is told who left.
    /// </summary>
    private void EndGameAfterLeaving(string name)
    {
        _notice = $"{name} left the game.";
        EndGameInLobby();
    }

    /// <summary>
    /// In a public game, when the host no longer holds a seat, the first seated player becomes the host.
    /// </summary>
    private void UpdateHost()
    {
        if (_isPublic && (_host is null || !HoldsSeat(_host)))
        {
            _host = _seats.FirstOrDefault(s => s is not null)?.UserKey;
        }
    }

    /// <summary>
    /// Outside a game in progress (in the lobby, or once a game is over), frees the seats of Away users. Returns how
    /// many seats were freed.
    /// </summary>
    private int FreeAwaySeats() =>
        HasUnfinishedGame ? 0 : _away.ToList().Sum(FreeSeats);

    /// <summary>
    /// A user is connected again: no longer reconnecting or Away.
    /// </summary>
    private void MarkConnected(string userKey)
    {
        var wasDisconnected = _disconnectedSince.Remove(userKey);
        if (_away.Remove(userKey) || wasDisconnected)
        {
            OnSeatsChanged();
        }
    }

    /// <summary>
    /// The state of a seat held by <paramref name="userKey"/>.
    /// </summary>
    private SeatStatus StatusOf(string userKey) =>
        IsConnected(userKey) ? SeatStatus.Present
        : _away.Contains(userKey) ? SeatStatus.Away
        : SeatStatus.Reconnecting;

    /// <summary>
    /// In the lobby, moves the taken seats up so they are the first ones, in order.
    /// </summary>
    private void CloseGapsInLobby()
    {
        if (!_onGameScreen)
        {
            MoveSeatsUp();
        }
    }

    private void MoveSeatsUp()
    {
        var taken = _seats.Where(s => s is not null).ToArray();
        Array.Clear(_seats);
        taken.CopyTo(_seats, 0);
    }

    /// <summary>
    /// The current game's absent seats (empty, or held by a user who is not connected), in order.
    /// </summary>
    private IEnumerable<int> AbsentGameSeats() => _gameSeats.Where(IsAbsent);

    /// <summary>
    /// The absent seats of the game in progress a newcomer can take.
    /// </summary>
    private IEnumerable<int> OfferedSeats() => AbsentGameSeats().Where(s => SeatingDuringGameError(s) is null);

    /// <summary>
    /// The seats the game in progress waits for: its awaited seats (see <see cref="AwaitedSeats"/>), in order, once all of
    /// them are absent; otherwise none.
    /// </summary>
    private IReadOnlyList<int> WaitingSeats()
    {
        if (!HasUnfinishedGame)
        {
            return [];
        }

        var awaited = AwaitedSeats.Distinct().Order().ToArray();
        return awaited.Length > 0 && awaited.All(IsAbsent) ? awaited : [];
    }

    /// <summary>
    /// Once a game is over, goes back to the lobby when there are not enough players for a new one.
    /// </summary>
    private void AfterSeatsChanged()
    {
        if (Phase == SessionPhase.Over && OccupiedSeats().Count() < MinSeats)
        {
            EndGameInLobby();
        }
    }

    /// <summary>
    /// The numbers of the taken seats, in order.
    /// </summary>
    private IEnumerable<int> OccupiedSeats() => Enumerable.Range(0, MaxSeats).Where(i => _seats[i] is not null);

    /// <summary>
    /// The number of seats a game can use with the chosen options (see <see cref="SeatLimitFor"/>).
    /// </summary>
    private int SeatLimit => SeatLimitFor(_options);

    /// <summary>
    /// Whether <paramref name="userKey"/> holds any seat.
    /// </summary>
    private bool HoldsSeat(string userKey) => _seats.Any(s => s?.UserKey == userKey);

    /// <summary>
    /// Whether <paramref name="userKey"/> has at least one open connection to this session.
    /// </summary>
    protected bool IsConnected(string userKey) => _connections.ContainsValue(userKey);

    /// <summary>
    /// The user's random player ID in this session, created on first use.
    /// </summary>
    protected string PlayerIdFor(string userKey)
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
