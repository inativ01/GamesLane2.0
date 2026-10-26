using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TicTacToe;

namespace GamesLane.Server.Sessions;

public enum GameStatus
{
    WaitingForPlayers,
    InProgress,
    Finished,
}

public record Player(string Id, string Name, string ConnectionId, Mark Mark);

public record PlayerSummary(string Name, Mark Mark);

public record PlayerInfo(string PlayerId, Mark Mark);

/// <summary>
/// Public game state, safe to send to everyone in the game (players and spectators).
/// </summary>
public record GameState(
    string GameId,
    GameStatus Status,
    IReadOnlyList<Mark> Board,
    Mark CurrentTurn,
    Mark Winner,
    bool IsDraw,
    IReadOnlyList<PlayerSummary> Players);

/// <summary>
/// A single tic-tac-toe game. Players are identified by their SignalR connection, so a client can only act for itself.
/// All members are thread-safe.
/// </summary>
public class GameSession(string id)
{
    public const int MaxPlayers = 2;

    private readonly object _sync = new();
    private readonly List<Player> _players = [];
    private readonly TicTacToeGame _game = new();

    public string Id { get; } = id;

    public bool TryAddPlayer(
        string name,
        string connectionId,
        [NotNullWhen(true)] out Player? player,
        [NotNullWhen(false)] out string? error)
    {
        lock (_sync)
        {
            player = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Player name is required.";
                return false;
            }

            if (_players.Any(p => p.ConnectionId == connectionId))
            {
                error = "You have already joined this game.";
                return false;
            }

            if (_players.Count >= MaxPlayers)
            {
                error = "The game is full.";
                return false;
            }

            var mark = _players.Count == 0 ? Mark.X : Mark.O;
            player = new Player(Guid.NewGuid().ToString("N"), name.Trim(), connectionId, mark);
            _players.Add(player);
            error = null;
            return true;
        }
    }

    public bool TryMove(string connectionId, int cell, [NotNullWhen(false)] out string? error)
    {
        lock (_sync)
        {
            var player = _players.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player is null)
            {
                error = "Only players in this game can make moves.";
                return false;
            }

            if (GetStatus() != GameStatus.InProgress)
            {
                error = "The game is not in progress.";
                return false;
            }

            return _game.TryMove(player.Mark, cell, out error);
        }
    }

    public GameState GetState()
    {
        lock (_sync)
        {
            return new GameState(
                Id,
                GetStatus(),
                _game.Board.ToArray(),
                _game.CurrentTurn,
                _game.Winner,
                _game.IsDraw,
                _players.Select(p => new PlayerSummary(p.Name, p.Mark)).ToArray());
        }
    }

    private GameStatus GetStatus() =>
        _game.IsOver ? GameStatus.Finished
        : _players.Count < MaxPlayers ? GameStatus.WaitingForPlayers
        : GameStatus.InProgress;
}
