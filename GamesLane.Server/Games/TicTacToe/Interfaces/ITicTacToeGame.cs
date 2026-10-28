namespace GamesLane.Server.Games.TicTacToe;

/// <summary>
/// Tic-tac-toe rules. Cells are numbered 0-8, left to right, top to bottom. X always moves first.
/// </summary>
public interface ITicTacToeGame
{
    IReadOnlyList<Mark> Board { get; }

    Mark CurrentTurn { get; }

    Mark Winner { get; }

    bool IsDraw { get; }

    bool IsOver { get; }

    bool TryMove(Mark mark, int cell, out string? error);
}
