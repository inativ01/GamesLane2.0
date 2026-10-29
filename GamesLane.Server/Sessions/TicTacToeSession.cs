using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TicTacToe;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Public tic-tac-toe state. Seat 0 plays X, seat 1 plays O.
/// </summary>
public record TicTacToeState(
    IReadOnlyList<Mark> Board,
    Mark CurrentTurn,
    Mark Winner,
    bool IsDraw);

/// <summary>
/// A tic-tac-toe session for two seats. Seat 0 plays X, seat 1 plays O.
/// </summary>
public class TicTacToeSession(string id) : GameSession(id), ITicTacToeSession
{
    private TicTacToeGame _game = new();

    /// <inheritdoc />
    public override GameType Type => GameType.TicTacToe;

    /// <inheritdoc />
    protected override int MinSeats => 2;

    /// <inheritdoc />
    protected override int MaxSeats => 2;

    /// <inheritdoc />
    protected override bool IsGameOver => _game.IsOver;

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
            error = moveError ?? (moved ? null : "That move is not allowed.");
            return moved;
        }
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) => _game = new TicTacToeGame();

    /// <inheritdoc />
    protected override object GetGameState() =>
        new TicTacToeState(_game.Board.ToArray(), _game.CurrentTurn, _game.Winner, _game.IsDraw);

    /// <summary>
    /// The mark played by <paramref name="seat"/>: X for seat 0, O for seat 1.
    /// </summary>
    private static Mark MarkFor(int seat) => seat == 0 ? Mark.X : Mark.O;
}
