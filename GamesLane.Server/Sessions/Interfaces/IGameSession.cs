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
    /// updates the name and icon on the user's seats and returns the user's player ID.
    /// </summary>
    string? Enter(string userKey, PlayerProfile? profile, string connectionId);

    /// <summary>
    /// Forgets a closed connection. Returns true if this leaves a seated user with no connection.
    /// </summary>
    bool Disconnect(string connectionId, DateTimeOffset now);

    /// <summary>
    /// Frees the seats of users who have been disconnected since <paramref name="cutoff"/> or earlier. Returns true if any seat was freed.
    /// </summary>
    bool ReleaseDisconnectedSeats(DateTimeOffset cutoff);

    /// <summary>
    /// Whether nobody has had the session open, and no seat has been taken, for at least <paramref name="gracePeriod"/>.
    /// The time is measured from the first call that finds the session empty, so call this regularly.
    /// </summary>
    bool IsAbandoned(DateTimeOffset now, TimeSpan gracePeriod);

    /// <summary>
    /// Takes an empty seat, or the seat of a user who is disconnected, showing the user's name and icon on it.
    /// </summary>
    bool TryTakeSeat(string userKey, PlayerProfile profile, string connectionId, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Takes the next available seat: the first empty one, or if none is empty, the first seat of a disconnected user.
    /// </summary>
    bool TryTakeNextSeat(string userKey, PlayerProfile profile, string connectionId, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Gives up one of the user's seats. An unfinished game pauses if too few of its players remain.
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
    /// Changes the game's options, for the next new game. Only seated players, and only in the lobby. Options left out
    /// get their default; unknown options and values are refused.
    /// </summary>
    bool TrySetOptions(string userKey, IReadOnlyDictionary<string, string>? options, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Uses saved preferences as the options, e.g. the creator's when the session is created. Values that are no
    /// longer valid get their default.
    /// </summary>
    void UseSavedOptions(IReadOnlyDictionary<string, string>? options);

    /// <summary>
    /// Continues the paused game. Whoever now holds each of its seats plays that seat.
    /// </summary>
    bool TryResume(string userKey, [NotNullWhen(false)] out string? error);

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
}
