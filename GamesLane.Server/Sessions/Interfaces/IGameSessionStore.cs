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
    /// <paramref name="prepare"/>, if given, sets the session up before anyone else can find it.
    /// </summary>
    Task<IGameSession> CreateAsync(GameType type, Action<IGameSession>? prepare = null);

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
    /// Removes the session from memory: it is saved first if it changed since it was last saved or loaded, or deleted
    /// if its game never started, or if it is a public game with nobody seated.
    /// </summary>
    Task UnloadAsync(IGameSession session);

    /// <summary>
    /// The sessions in memory.
    /// </summary>
    IEnumerable<IGameSession> All { get; }

    /// <summary>
    /// When the session was last saved (after its last change), or <see cref="DateTimeOffset.MinValue"/> if not since it
    /// was loaded.
    /// </summary>
    DateTimeOffset LastActivity(IGameSession session);
}
