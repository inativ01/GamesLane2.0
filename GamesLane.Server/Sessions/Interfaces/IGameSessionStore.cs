namespace GamesLane.Server.Sessions;

/// <summary>
/// Store of active games, looked up by game code (case-insensitive).
/// </summary>
public interface IGameSessionStore
{
    IGameSession Create(GameType type);

    IGameSession? Get(string gameId);

    IEnumerable<IGameSession> All { get; }
}
