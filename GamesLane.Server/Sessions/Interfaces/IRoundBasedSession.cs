using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A match that continues round after round, keeping score (rock-paper-scissors, tic-tac-toe).
/// </summary>
public interface IRoundBasedSession : IGameSession
{
    /// <summary>
    /// Starts the round after <paramref name="round"/>. If it has already started, nothing changes.
    /// </summary>
    bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error);
}
