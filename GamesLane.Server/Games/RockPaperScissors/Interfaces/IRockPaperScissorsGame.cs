namespace GamesLane.Server.Games.RockPaperScissors;

/// <summary>
/// Rock-paper-scissors rules for two players (seats 0 and 1), played over any number of rounds.
/// </summary>
public interface IRockPaperScissorsGame
{
    int Round { get; }

    IReadOnlyList<Choice> Picks { get; }

    IReadOnlyList<int> Scores { get; }

    int Draws { get; }

    bool IsRoundComplete { get; }

    /// <summary>
    /// Seat of the current round's winner, or null if the round is not complete or was a draw.
    /// </summary>
    int? RoundWinner { get; }

    bool TryPick(int seat, Choice choice, out string? error);

    /// <summary>
    /// Starts the round after <paramref name="round"/>. If that round has already been started, nothing changes.
    /// </summary>
    bool TryNextRound(int round, out string? error);
}
