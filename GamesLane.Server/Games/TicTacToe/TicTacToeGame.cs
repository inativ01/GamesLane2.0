namespace GamesLane.Server.Games.TicTacToe;

/// <summary>
/// A tic-tac-toe mark, or <see cref="None"/> for an empty cell.
/// </summary>
public enum Mark
{
    None,
    X,
    O,
}

/// <summary>
/// Tic-tac-toe rules for one board. Cells are numbered 0-8, left to right, top to bottom.
/// <paramref name="firstTurn"/> (X unless given) moves first.
/// </summary>
public class TicTacToeGame(Mark firstTurn = Mark.X) : ITicTacToeGame
{
    private const int CellCount = 9;

    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8],
        [0, 3, 6], [1, 4, 7], [2, 5, 8],
        [0, 4, 8], [2, 4, 6],
    ];

    private readonly Mark[] _board = new Mark[CellCount];

    /// <inheritdoc />
    public IReadOnlyList<Mark> Board => _board;

    /// <inheritdoc />
    public Mark CurrentTurn { get; private set; } = firstTurn == Mark.None
        ? throw new ArgumentException("The first turn must be X or O.", nameof(firstTurn))
        : firstTurn;

    /// <inheritdoc />
    public Mark Winner { get; private set; } = Mark.None;

    /// <inheritdoc />
    public bool IsDraw { get; private set; }

    /// <inheritdoc />
    public bool IsOver => Winner != Mark.None || IsDraw;

    /// <inheritdoc />
    public bool TryMove(Mark mark, int cell, out string? error)
    {
        if (IsOver)
        {
            error = "The game is over.";
            return false;
        }

        if (mark != CurrentTurn)
        {
            error = "It is not your turn.";
            return false;
        }

        if (cell < 0 || cell >= CellCount)
        {
            error = $"Cell must be between 0 and {CellCount - 1}.";
            return false;
        }

        if (_board[cell] != Mark.None)
        {
            error = "That cell is already taken.";
            return false;
        }

        _board[cell] = mark;

        if (Lines.Any(line => line.All(i => _board[i] == mark)))
        {
            Winner = mark;
        }
        else if (_board.All(c => c != Mark.None))
        {
            IsDraw = true;
        }
        else
        {
            CurrentTurn = mark == Mark.X ? Mark.O : Mark.X;
        }

        error = null;
        return true;
    }
}
