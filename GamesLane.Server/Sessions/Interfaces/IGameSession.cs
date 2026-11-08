using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// One session of a game: its seats, and the game played by the seated users.
/// Users are identified by a user key (their user ID when logged in). A user may hold several seats.
/// </summary>
public interface IGameSession
{
    /// <summary>
    /// The session code, e.g. <c>"K7M2QX"</c>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The game played in this session.
    /// </summary>
    GameType Type { get; }

    /// <summary>
    /// Registers a connection that opened the session page. If <paramref name="profile"/> is given (the user is logged in),
    /// updates the name and icon on the user's seats and returns the user's player ID. A logged-in user without a seat who
    /// opens or reconnects to a private game in progress is offered its absent seats if it has any.
    /// </summary>
    string? Enter(string userKey, PlayerProfile? profile, string connectionId);

    /// <summary>
    /// Whether <paramref name="userKey"/> is offered an absent seat of the game in progress (see <see cref="Enter"/>).
    /// </summary>
    bool HasSeatOffer(string userKey);

    /// <summary>
    /// <paramref name="userKey"/> declines their seat offer and watches the game, until they open it or reconnect again.
    /// Returns true if they had an offer.
    /// </summary>
    bool DeclineSeatOffer(string userKey);

    /// <summary>
    /// A player skips the turn of <paramref name="seat"/>, an absent seat the game waits for, in a game that allows it.
    /// </summary>
    bool TrySkipPlayer(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Forgets a closed connection. Returns true if this changes the public state: it leaves a seated user with no
    /// connection, or it was an observer's connection.
    /// </summary>
    bool Disconnect(string connectionId, DateTimeOffset now);

    /// <summary>
    /// Gives everything <paramref name="fromUserKey"/> holds in the session (their seats, and any request to join) to
    /// <paramref name="toUserKey"/>, shown as <paramref name="profile"/>, when the same person logs in or out.
    /// Returns true if anything changed.
    /// </summary>
    bool TransferSeats(string fromUserKey, string toUserKey, PlayerProfile profile, DateTimeOffset now);

    /// <summary>
    /// Marks users disconnected for <see cref="GameSession.ReconnectWindow"/> or more as Away. Away users keep their
    /// seats only during a game; otherwise their seats are freed. Returns true if anything changed.
    /// </summary>
    bool UpdateAwaySeats(DateTimeOffset now);

    /// <summary>
    /// Whether nobody has had the session open for at least <paramref name="idleTime"/>. The time is measured from the
    /// first call that finds nobody connected, so call this regularly.
    /// </summary>
    bool IsIdle(DateTimeOffset now, TimeSpan idleTime);

    /// <summary>
    /// Whether a game has ever been started in this session.
    /// </summary>
    bool HasStarted { get; }

    /// <summary>
    /// Whether this is a public game: joined only from the main screen, at random, with no game code to share.
    /// </summary>
    bool IsPublic { get; }

    /// <summary>
    /// Whether any seat is taken.
    /// </summary>
    bool HasSeatedPlayers { get; }

    /// <summary>
    /// Whether a random player can join this session from the main screen: a public game in the lobby, with a player
    /// and a free seat.
    /// </summary>
    bool IsOpenToJoin { get; }

    /// <summary>
    /// Makes this a public game. Call before anyone takes a seat; the first to take one is the host, who alone chooses
    /// the options and starts the game.
    /// </summary>
    void MakePublic();

    /// <summary>
    /// Whether <paramref name="userKey"/> holds a seat.
    /// </summary>
    bool IsSeated(string userKey);

    /// <summary>
    /// Whether a game is in progress where <paramref name="userKey"/> holds a seat but is not connected.
    /// </summary>
    bool IsWaitingFor(string userKey);

    /// <summary>
    /// The session's state as JSON, to be loaded again with <see cref="Restore"/>. Open connections are not saved.
    /// </summary>
    string Save();

    /// <summary>
    /// Loads a state saved at <paramref name="savedAt"/> into this new session; nobody is connected yet.
    /// </summary>
    void Restore(string data, DateTimeOffset savedAt);

    /// <summary>
    /// Takes a seat, showing the user's name and icon on it: in the lobby, an empty seat; during a game, for a user
    /// offered a seat only, an absent seat of the game (empty, or its holder is not connected), continuing that player's
    /// game.
    /// </summary>
    bool TryTakeSeat(string userKey, PlayerProfile profile, string connectionId, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Takes the next available seat: in the lobby, the first empty one; during a game, the first absent seat of the game.
    /// </summary>
    bool TryTakeNextSeat(string userKey, PlayerProfile profile, string connectionId, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Gives up one of the user's seats. During a game, the seat is then absent.
    /// </summary>
    bool TryLeaveSeat(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Frees all the user's seats. Returns true if they held any.
    /// </summary>
    bool LeaveAllSeats(string userKey);

    /// <summary>
    /// Starts a new game for everyone seated, replacing game <paramref name="gameNumber"/> (0 if there was none).
    /// If that game has already been replaced, nothing changes.
    /// </summary>
    bool TryNewGame(string userKey, int gameNumber, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Changes the game's options, for the next new game. Anyone in the session, only in the lobby. Options left out
    /// get their default; unknown options and values are refused.
    /// </summary>
    bool TrySetOptions(string userKey, IReadOnlyDictionary<string, string>? options, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Uses saved preferences as the options, e.g. the creator's when the session is created. Values that are no
    /// longer valid get their default.
    /// </summary>
    void UseSavedOptions(IReadOnlyDictionary<string, string>? options);

    /// <summary>
    /// Ends the game in progress and takes everyone back to the session lobby. The seats then move
    /// up, other users can join, and a new game can be started.
    /// </summary>
    bool TryEndGame(string userKey, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// After a game has ended, takes everyone back to the session lobby.
    /// </summary>
    bool TryReturnToLobby(string userKey, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Public session state, safe to send to everyone in the session (players and spectators).
    /// </summary>
    SessionState GetPublicState();

    /// <summary>
    /// The private game information for <paramref name="userKey"/> (e.g. the cards of their seats), or null if they hold
    /// no seat in the current game or the game has no private information.
    /// </summary>
    object? GetPrivateState(string userKey);

    /// <summary>
    /// The private game information for every open connection whose user has any.
    /// </summary>
    IReadOnlyList<PrivateUpdate> GetPrivateUpdates();

    /// <summary>
    /// Adds a chat message from <paramref name="userKey"/>, shown with <paramref name="profile"/>. Anyone with the session
    /// open can chat, seated or watching. Only the latest <see cref="GameSession.ChatHistoryLength"/> messages are kept.
    /// </summary>
    bool TrySendChat(string userKey, PlayerProfile profile, string? text, DateTimeOffset now,
        [NotNullWhen(true)] out ChatMessage? message, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// The session's latest chat messages, oldest first.
    /// </summary>
    IReadOnlyList<ChatMessage> GetChat();
}
