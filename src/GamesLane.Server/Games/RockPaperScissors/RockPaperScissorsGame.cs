namespace GamesLane.Server.Games.RockPaperScissors;

public enum Choice
{
    None,
    Rock,
    Paper,
    Scissors,
}

/// <summary>
/// Rock-paper-scissors rules for two players (seats 0 and 1), played over any number of rounds.
/// Each player picks once per round. The round is decided when both have picked.
/// </summary>
public class RockPaperScissorsGame
{
    public const int PlayerCount = 2;

    private readonly Choice[] _picks = new Choice[PlayerCount];
    private readonly int[] _scores = new int[PlayerCount];

    public int Round { get; private set; } = 1;

    public IReadOnlyList<Choice> Picks => _picks;

    public IReadOnlyList<int> Scores => _scores;

    public int Draws { get; private set; }

    public bool IsRoundComplete => _picks.All(p => p != Choice.None);

    /// <summary>
    /// Seat of the current round's winner, or null if the round is not complete or was a draw.
    /// </summary>
    public int? RoundWinner { get; private set; }

    public static bool Beats(Choice a, Choice b) =>
        (a, b) is (Choice.Rock, Choice.Scissors) or (Choice.Paper, Choice.Rock) or (Choice.Scissors, Choice.Paper);

    public bool TryPick(int seat, Choice choice, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seat);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, PlayerCount);

        if (choice == Choice.None || !Enum.IsDefined(choice))
        {
            error = "Pick rock, paper or scissors.";
            return false;
        }

        if (IsRoundComplete)
        {
            error = "This round is over. Start the next round.";
            return false;
        }

        if (_picks[seat] != Choice.None)
        {
            error = "You have already picked this round.";
            return false;
        }

        _picks[seat] = choice;
        if (IsRoundComplete)
        {
            DecideRound();
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Starts the round after <paramref name="round"/>. If that round has already been started
    /// (for example, both players pressed "Next round"), nothing changes.
    /// </summary>
    public bool TryNextRound(int round, out string? error)
    {
        error = null;
        if (round != Round)
        {
            return true;
        }

        if (!IsRoundComplete)
        {
            error = "This round is not finished yet.";
            return false;
        }

        Array.Clear(_picks);
        RoundWinner = null;
        Round++;
        return true;
    }

    private void DecideRound()
    {
        if (Beats(_picks[0], _picks[1]))
        {
            RoundWinner = 0;
        }
        else if (Beats(_picks[1], _picks[0]))
        {
            RoundWinner = 1;
        }

        if (RoundWinner is { } winner)
        {
            _scores[winner]++;
        }
        else
        {
            Draws++;
        }
    }
}
