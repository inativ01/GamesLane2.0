using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.RockPaperScissors;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A rock-paper-scissors match for two seats that continues round after round, keeping score.
/// </summary>
public interface IRockPaperScissorsSession : IGameSession
{
    /// <summary>
    /// Picks for <paramref name="seat"/>, which <paramref name="userKey"/> must hold.
    /// </summary>
    bool TryPick(string userKey, int seat, Choice choice, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Starts the round after <paramref name="round"/>. If it has already started, nothing changes.
    /// </summary>
    bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error);
}
