using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TicTacToe;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Public tic-tac-toe state: the current round's board, and the wins of each seat (in seat order) and draws so far.
/// Seat 0 plays X, seat 1 plays O.
/// </summary>
public record TicTacToeState(
    int Round,
    IReadOnlyList<Mark> Board,
    Mark CurrentTurn,
    Mark Winner,
    bool IsDraw,
    IReadOnlyList<int> Scores,
    int Draws);

/// <summary>
/// A tic-tac-toe match for two seats that continues round after round, keeping score. It never ends by itself.
/// Seat 0 plays X, seat 1 plays O. X starts the odd rounds and O the even ones.
/// </summary>
public class TicTacToeSession(string id) : GameSession(id), ITicTacToeSession
{
    private TicTacToeGame _game = new();
    private int _round = 1;
    private int[] _scores = new int[2];
    private int _draws;

    /// <inheritdoc />
    public override GameType Type => GameType.TicTacToe;

    /// <inheritdoc />
    protected override int MinSeats => 2;

    /// <inheritdoc />
    protected override int MaxSeats => 2;

    /// <inheritdoc />
    protected override bool IsGameOver => false;

    /// <inheritdoc />
    public bool TryMove(string userKey, int seat, int cell, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            var moved = _game.TryMove(MarkFor(seat), cell, out var moveError);
            if (moved && _game.Winner != Mark.None)
            {
                _scores[_game.Winner == Mark.X ? 0 : 1]++;
            }
            else if (moved && _game.IsDraw)
            {
                _draws++;
            }

            error = moveError ?? (moved ? null : "That move is not allowed.");
            return moved;
        }
    }

    /// <inheritdoc />
    public bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!IsInGame(userKey, out error))
            {
                return false;
            }

            if (round != _round)
            {
                return true;
            }

            if (!_game.IsOver)
            {
                error = "This round is not finished yet.";
                return false;
            }

            _round++;
            _game = new TicTacToeGame(FirstTurnOf(_round));
            return true;
        }
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats)
    {
        _round = 1;
        _scores = new int[2];
        _draws = 0;
        _game = new TicTacToeGame(FirstTurnOf(_round));
    }

    /// <inheritdoc />
    protected override object GetGameState() => new TicTacToeState(
        _round, _game.Board.ToArray(), _game.CurrentTurn, _game.Winner, _game.IsDraw, _scores.ToArray(), _draws);

    /// <summary>
    /// The mark played by <paramref name="seat"/>: X for seat 0, O for seat 1.
    /// </summary>
    private static Mark MarkFor(int seat) => seat == 0 ? Mark.X : Mark.O;

    /// <summary>
    /// The mark that moves first in <paramref name="round"/>: X in odd rounds, O in even rounds.
    /// </summary>
    private static Mark FirstTurnOf(int round) => round % 2 == 1 ? Mark.X : Mark.O;
}
