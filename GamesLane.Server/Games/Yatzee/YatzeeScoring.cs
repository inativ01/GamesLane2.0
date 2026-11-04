namespace GamesLane.Server.Games.Yatzee;

/// <summary>
/// The 13 boxes of the scorecard, in the order they are listed: the upper section (Ones to Sixes), then the lower section.
/// </summary>
public enum YatzeeCategory
{
    Ones,
    Twos,
    Threes,
    Fours,
    Fives,
    Sixes,
    ThreeOfAKind,
    FourOfAKind,
    FullHouse,
    SmallStraight,
    LargeStraight,
    Yatzee,
    Chance,
}

/// <summary>
/// What the dice score in each box, with the classic rules: the upper boxes count the dice of their number; three and
/// four of a kind and Chance count all the dice; a full house scores 25, a small straight (four in a row) 30, a large
/// straight (five in a row) 40, and a Yatzee (five of a kind) 50.
/// A Yatzee rolled once the Yatzee box is filled is a joker: it must go in the upper box of its number if that box is
/// open; otherwise in any open lower box, where a full house and the straights score in full; otherwise in any open
/// upper box, for 0.
/// </summary>
public static class YatzeeScoring
{
    /// <summary>The number of boxes on a scorecard.</summary>
    public const int Categories = 13;

    /// <summary>The points of a full house.</summary>
    public const int FullHousePoints = 25;

    /// <summary>The points of a small straight.</summary>
    public const int SmallStraightPoints = 30;

    /// <summary>The points of a large straight.</summary>
    public const int LargeStraightPoints = 40;

    /// <summary>The points of a Yatzee.</summary>
    public const int YatzeePoints = 50;

    /// <summary>
    /// Whether <paramref name="category"/> is in the upper section (Ones to Sixes).
    /// </summary>
    public static bool IsUpper(YatzeeCategory category) => category <= YatzeeCategory.Sixes;

    /// <summary>
    /// Whether all five dice show the same number.
    /// </summary>
    public static bool IsYatzee(IReadOnlyList<int> dice) => dice.Count > 0 && dice.All(d => d == dice[0]);

    /// <summary>
    /// What <paramref name="dice"/> score in <paramref name="category"/>, without the joker rule.
    /// </summary>
    public static int Score(YatzeeCategory category, IReadOnlyList<int> dice)
    {
        var counts = Enumerable.Range(1, 6).Select(n => dice.Count(d => d == n)).ToArray();
        var sum = dice.Sum();
        bool Run(int from, int length) => Enumerable.Range(from, length).All(n => counts[n - 1] > 0);
        return category switch
        {
            <= YatzeeCategory.Sixes => counts[(int)category] * ((int)category + 1),
            YatzeeCategory.ThreeOfAKind => counts.Max() >= 3 ? sum : 0,
            YatzeeCategory.FourOfAKind => counts.Max() >= 4 ? sum : 0,
            YatzeeCategory.FullHouse => counts.Contains(3) && counts.Contains(2) ? FullHousePoints : 0,
            YatzeeCategory.SmallStraight => Run(1, 4) || Run(2, 4) || Run(3, 4) ? SmallStraightPoints : 0,
            YatzeeCategory.LargeStraight => Run(1, 5) || Run(2, 5) ? LargeStraightPoints : 0,
            YatzeeCategory.Yatzee => IsYatzee(dice) ? YatzeePoints : 0,
            _ => sum,
        };
    }

    /// <summary>
    /// For each box (in <see cref="YatzeeCategory"/> order), what <paramref name="dice"/> would score there on
    /// <paramref name="card"/>, or null if the box is filled or the joker rule does not allow it.
    /// </summary>
    public static int?[] Options(YatzeeScorecard card, IReadOnlyList<int> dice)
    {
        var options = new int?[Categories];
        var joker = IsJoker(card, dice);
        var upper = joker ? (YatzeeCategory)(dice[0] - 1) : default;
        var lowerOpen = Enum.GetValues<YatzeeCategory>().Any(c => !IsUpper(c) && card[c] is null);
        foreach (var category in Enum.GetValues<YatzeeCategory>())
        {
            if (card[category] is not null)
            {
                continue;
            }

            if (!joker)
            {
                options[(int)category] = Score(category, dice);
            }
            else if (card[upper] is null)
            {
                options[(int)category] = category == upper ? Score(category, dice) : null;
            }
            else if (lowerOpen)
            {
                options[(int)category] = IsUpper(category) ? null : JokerScore(category, dice);
            }
            else
            {
                options[(int)category] = 0;
            }
        }

        return options;
    }

    /// <summary>
    /// Why <paramref name="dice"/> cannot be scored in <paramref name="category"/> on <paramref name="card"/>, or null if
    /// they can.
    /// </summary>
    public static string? ScoreError(YatzeeScorecard card, IReadOnlyList<int> dice, YatzeeCategory category)
    {
        if (!Enum.IsDefined(category))
        {
            return "There is no such box.";
        }

        if (card[category] is not null)
        {
            return "That box is already filled.";
        }

        if (Options(card, dice)[(int)category] is not null)
        {
            return null;
        }

        return card[(YatzeeCategory)(dice[0] - 1)] is null
            ? "This Yatzee must be scored in the upper box of its number."
            : "This Yatzee must be scored in an open lower box.";
    }

    /// <summary>
    /// Whether <paramref name="dice"/> are a Yatzee rolled once the Yatzee box is filled (with 50 or 0).
    /// </summary>
    public static bool IsJoker(YatzeeScorecard card, IReadOnlyList<int> dice) =>
        IsYatzee(dice) && card[YatzeeCategory.Yatzee] is not null;

    /// <summary>
    /// What a joker scores in a lower box: a full house and the straights in full, the others as usual.
    /// </summary>
    private static int JokerScore(YatzeeCategory category, IReadOnlyList<int> dice) => category switch
    {
        YatzeeCategory.FullHouse => FullHousePoints,
        YatzeeCategory.SmallStraight => SmallStraightPoints,
        YatzeeCategory.LargeStraight => LargeStraightPoints,
        _ => Score(category, dice),
    };
}
