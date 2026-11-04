namespace GamesLane.Server.Sessions;

/// <summary>
/// The sessions, looked up by session code (case-insensitive). Sessions being played are kept in memory and saved in
/// the database after every change; a session nobody has open is removed from memory, and loaded again when someone
/// opens it.
/// </summary>
public interface IGameSessionStore
{
    /// <summary>
    /// Creates a session of the given game with a new code, unique in memory and in the database, and saves it.
    /// </summary>
    Task<IGameSession> CreateAsync(GameType type);

    /// <summary>
    /// The session with the given code: from memory, or else loaded from the database. Null if there is none, or if
    /// it was saved by a version of the code that cannot load it.
    /// </summary>
    Task<IGameSession?> GetAsync(string sessionId);

    /// <summary>
    /// Saves the session's current state in the database. Errors are logged, not thrown.
    /// </summary>
    Task SaveAsync(IGameSession session);

    /// <summary>
    /// Removes the session from memory: it is saved first, or deleted if no seat was ever taken in it.
    /// </summary>
    Task UnloadAsync(IGameSession session);

    /// <summary>
    /// The sessions in memory.
    /// </summary>
    IEnumerable<IGameSession> All { get; }
}
