using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A tic-tac-toe match for two seats that continues round after round, keeping score. Seat 0 plays X, seat 1 plays O;
/// the starting player alternates.
/// </summary>
public interface ITicTacToeSession : IRoundBasedSession
{
    /// <summary>
    /// Places the mark of <paramref name="seat"/>, which <paramref name="userKey"/> must hold.
    /// </summary>
    bool TryMove(string userKey, int seat, int cell, [NotNullWhen(false)] out string? error);
}
