namespace GamesLane.Server.Sessions;

/// <summary>
/// Store of active sessions, looked up by session code (case-insensitive).
/// </summary>
public interface IGameSessionStore
{
    /// <summary>
    /// Creates a session of the given game with a new, unique code.
    /// </summary>
    IGameSession Create(GameType type);

    /// <summary>
    /// The session with the given code, or null if there is none.
    /// </summary>
    IGameSession? Get(string sessionId);

    /// <summary>
    /// All active sessions.
    /// </summary>
    IEnumerable<IGameSession> All { get; }
}
