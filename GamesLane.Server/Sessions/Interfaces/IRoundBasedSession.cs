using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A match that continues round after round, keeping score (tic-tac-toe, and backgammon, whose rounds are the games of
/// a match), game after game (rich man, poor man), or hand after hand (Final Card).
/// </summary>
public interface IRoundBasedSession : IGameSession
{
    /// <summary>
    /// Starts the round after <paramref name="round"/>. If it has already started, nothing changes.
    /// </summary>
    bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error);
}
