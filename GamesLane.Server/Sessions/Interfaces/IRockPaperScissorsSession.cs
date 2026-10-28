using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.RockPaperScissors;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A rock-paper-scissors match that continues round after round, keeping score.
/// </summary>
public interface IRockPaperScissorsSession : IGameSession
{
    bool TryPick(string connectionId, Choice choice, [NotNullWhen(false)] out string? error);

    bool TryNextRound(string connectionId, int round, [NotNullWhen(false)] out string? error);

    RockPaperScissorsState GetState();
}
