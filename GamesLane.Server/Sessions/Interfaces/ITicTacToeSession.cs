using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A tic-tac-toe session for two seats. Seat 0 plays X, seat 1 plays O.
/// </summary>
public interface ITicTacToeSession : IGameSession
{
    /// <summary>
    /// Places the mark of <paramref name="seat"/>, which <paramref name="userKey"/> must hold.
    /// </summary>
    bool TryMove(string userKey, int seat, int cell, [NotNullWhen(false)] out string? error);
}
