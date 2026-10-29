namespace GamesLane.Server.Games.RockPaperScissors;

/// <summary>
/// Rock-paper-scissors rules for two players (seats 0 and 1), played over any number of rounds.
/// </summary>
public interface IRockPaperScissorsGame
{
    /// <summary>
    /// The current round, starting at 1.
    /// </summary>
    int Round { get; }

    /// <summary>
    /// Each seat's pick in the current round, or <see cref="Choice.None"/> if it has not picked yet.
    /// </summary>
    IReadOnlyList<Choice> Picks { get; }

    /// <summary>
    /// The number of rounds each seat has won.
    /// </summary>
    IReadOnlyList<int> Scores { get; }

    /// <summary>
    /// The number of drawn rounds.
    /// </summary>
    int Draws { get; }

    /// <summary>
    /// Whether both seats have picked in the current round.
    /// </summary>
    bool IsRoundComplete { get; }

    /// <summary>
    /// Seat of the current round's winner, or null if the round is not complete or was a draw.
    /// </summary>
    int? RoundWinner { get; }

    /// <summary>
    /// Picks for <paramref name="seat"/> in the current round. Fails if the choice is invalid, the seat has already
    /// picked, or the round is over. The round is decided when both seats have picked.
    /// </summary>
    bool TryPick(int seat, Choice choice, out string? error);

    /// <summary>
    /// Starts the round after <paramref name="round"/>. If that round has already been started, nothing changes.
    /// </summary>
    bool TryNextRound(int round, out string? error);
}
