namespace GamesLane.Server.Games.TicTacToe;

/// <summary>
/// Tic-tac-toe rules for one board. Cells are numbered 0-8, left to right, top to bottom.
/// </summary>
public interface ITicTacToeGame
{
    /// <summary>
    /// The nine cells, each <see cref="Mark.None"/>, <see cref="Mark.X"/> or <see cref="Mark.O"/>.
    /// </summary>
    IReadOnlyList<Mark> Board { get; }

    /// <summary>
    /// The mark to move next. Once the game is over, the mark that made the last move.
    /// </summary>
    Mark CurrentTurn { get; }

    /// <summary>
    /// The winning mark, or <see cref="Mark.None"/> if there is no winner (yet).
    /// </summary>
    Mark Winner { get; }

    /// <summary>
    /// Whether the board is full with no winner.
    /// </summary>
    bool IsDraw { get; }

    /// <summary>
    /// Whether the game has been won or drawn.
    /// </summary>
    bool IsOver { get; }

    /// <summary>
    /// Places <paramref name="mark"/> in <paramref name="cell"/>. Fails if the game is over, it is not that mark's turn,
    /// or the cell does not exist or is taken.
    /// </summary>
    bool TryMove(Mark mark, int cell, out string? error);
}
