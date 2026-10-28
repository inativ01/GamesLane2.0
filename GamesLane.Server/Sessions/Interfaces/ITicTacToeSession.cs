using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A tic-tac-toe game. Seat 0 plays X, seat 1 plays O. After a game ends, the same players can play again.
/// </summary>
public interface ITicTacToeSession : IGameSession
{
    bool TryMove(string connectionId, int cell, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Starts a new game after game <paramref name="gameNumber"/> has ended. If that game has already been replaced, nothing changes.
    /// </summary>
    bool TryPlayAgain(string connectionId, int gameNumber, [NotNullWhen(false)] out string? error);

    TicTacToeState GetState();
}
