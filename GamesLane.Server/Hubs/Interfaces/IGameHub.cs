using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Methods clients can call on the game hub.
/// </summary>
public interface IGameHub
{
    /// <summary>
    /// Opens a game page: subscribes to updates and, if the caller already holds a seat, reconnects them to it.
    /// </summary>
    Task<GameEntry> EnterGame(string gameId);

    string CreateGame(GameType gameType);

    Task<object> JoinAsSpectator(string gameId);

    Task<PlayerInfo> JoinAsPlayer(string gameId, string playerName);

    Task MakeMove(string gameId, int cell);

    Task PlayAgain(string gameId, int gameNumber);

    Task Pick(string gameId, Choice choice);

    Task NextRound(string gameId, int round);

    Task Leave(string gameId);
}
