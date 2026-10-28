using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TicTacToe;

namespace GamesLane.Server.Sessions;

public record TicTacToePlayer(string Name, Mark Mark, bool IsConnected);

/// <summary>
/// Public tic-tac-toe state. Players are listed in seat order.
/// </summary>
public record TicTacToeState(
    string GameId,
    GameType GameType,
    GameStatus Status,
    int GameNumber,
    IReadOnlyList<Mark> Board,
    Mark CurrentTurn,
    Mark Winner,
    bool IsDraw,
    IReadOnlyList<TicTacToePlayer> Players);

/// <summary>
/// A tic-tac-toe game. Seat 0 plays X, seat 1 plays O. After a game ends, the same players can play again.
/// </summary>
public class TicTacToeSession(string id) : GameSession(id)
{
    private TicTacToeGame _game = new();
    private int _gameNumber = 1;

    public override GameType Type => GameType.TicTacToe;

    public static Mark MarkFor(int seat) => seat == 0 ? Mark.X : Mark.O;

    public bool TryMove(string connectionId, int cell, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayer(connectionId, out var player, out error))
            {
                return false;
            }

            if (GetStatus() != GameStatus.InProgress)
            {
                error = "The game is not in progress.";
                return false;
            }

            return _game.TryMove(MarkFor(player.Seat), cell, out error);
        }
    }

    /// <summary>
    /// Starts a new game after game <paramref name="gameNumber"/> has ended. If that game has already been
    /// replaced (for example, both players pressed "Play again"), nothing changes.
    /// </summary>
    public bool TryPlayAgain(string connectionId, int gameNumber, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayer(connectionId, out _, out error))
            {
                return false;
            }

            if (gameNumber != _gameNumber)
            {
                return true;
            }

            if (!_game.IsOver)
            {
                error = "The game is not over yet.";
                return false;
            }

            _game = new TicTacToeGame();
            _gameNumber++;
            return true;
        }
    }

    public TicTacToeState GetState()
    {
        lock (Sync)
        {
            return new TicTacToeState(
                Id,
                Type,
                GetStatus(),
                _gameNumber,
                _game.Board.ToArray(),
                _game.CurrentTurn,
                _game.Winner,
                _game.IsDraw,
                Players.Select(p => new TicTacToePlayer(p.Name, MarkFor(p.Seat), p.IsConnected)).ToArray());
        }
    }

    public override object GetPublicState() => GetState();

    private GameStatus GetStatus() =>
        _game.IsOver ? GameStatus.Finished
        : Players.Count < MaxPlayers ? GameStatus.WaitingForPlayers
        : GameStatus.InProgress;
}
