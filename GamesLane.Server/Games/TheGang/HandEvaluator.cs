namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// Poker hand categories, from weakest to strongest.
/// </summary>
public enum HandCategory
{
    HighCard,
    OnePair,
    TwoPair,
    ThreeOfAKind,
    Straight,
    Flush,
    FullHouse,
    FourOfAKind,
    StraightFlush,
}

/// <summary>
/// The value of a poker hand: its category, then the ranks that break ties, most important first.
/// Hands compare by category, then by those ranks. Equal values are equally strong.
/// </summary>
public sealed record HandValue(HandCategory Category, IReadOnlyList<int> Ranks) : IComparable<HandValue>
{
    /// <summary>
    /// Compares by category, then rank by rank.
    /// </summary>
    public int CompareTo(HandValue? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byCategory = Category.CompareTo(other.Category);
        if (byCategory != 0)
        {
            return byCategory;
        }

        for (var i = 0; i < Math.Min(Ranks.Count, other.Ranks.Count); i++)
        {
            var byRank = Ranks[i].CompareTo(other.Ranks[i]);
            if (byRank != 0)
            {
                return byRank;
            }
        }

        return 0;
    }

    /// <summary>
    /// Whether this hand is weaker than <paramref name="other"/>.
    /// </summary>
    public bool IsWeakerThan(HandValue other) => CompareTo(other) < 0;

    /// <summary>
    /// Value equality compares the ranks one by one.
    /// </summary>
    public bool Equals(HandValue? other) => other is not null && CompareTo(other) == 0;

    /// <summary>
    /// Matches <see cref="Equals(HandValue?)"/>.
    /// </summary>
    public override int GetHashCode() => Ranks.Aggregate((int)Category, HashCode.Combine);

    /// <summary>
    /// A description such as "Two pair, kings and sevens" or "Straight, nine high". The other cards (kickers) that
    /// decide between hands of the same kind are not named.
    /// </summary>
    public string Name => Category switch
    {
        HandCategory.HighCard => $"High card, {Singular(Ranks[0])}",
        HandCategory.OnePair => $"Pair of {Plural(Ranks[0])}",
        HandCategory.TwoPair => $"Two pair, {Plural(Ranks[0])} and {Plural(Ranks[1])}",
        HandCategory.ThreeOfAKind => $"Three {Plural(Ranks[0])}",
        HandCategory.Straight => $"Straight, {Singular(Ranks[0])} high",
        HandCategory.Flush => $"Flush, {Singular(Ranks[0])} high",
        HandCategory.FullHouse => $"Full house, {Plural(Ranks[0])} over {Plural(Ranks[1])}",
        HandCategory.FourOfAKind => $"Four {Plural(Ranks[0])}",
        _ => Ranks[0] == Card.Ace ? "Royal flush" : $"Straight flush, {Singular(Ranks[0])} high",
    };

    private static readonly string[] RankNames =
        ["", "", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "jack", "queen", "king", "ace"];

    /// <summary>
    /// "two", "king", "ace", ...
    /// </summary>
    internal static string Singular(int rank) => RankNames[rank];

    /// <summary>
    /// "twos", "sixes", "kings", ...
    /// </summary>
    internal static string Plural(int rank) => rank == 6 ? "sixes" : RankNames[rank] + "s";

    /// <summary>
    /// The hand ranking as named in the game: "High card" to "Straight flush", or "Royal flush".
    /// </summary>
    public string Ranking => HandRankings.Of(this);
}

/// <summary>
/// The hand rankings players can name (Getaway Driver, Fingerprint Scan): the categories, plus the royal flush.
/// </summary>
public static class HandRankings
{
    /// <summary>
    /// "High card", "Pair", ... "Straight flush", "Royal flush", from weakest to strongest.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        ["High card", "Pair", "Two pair", "Three of a kind", "Straight", "Flush", "Full house", "Four of a kind", "Straight flush", "Royal flush"];

    /// <summary>
    /// The ranking of a hand.
    /// </summary>
    public static string Of(HandValue hand) =>
        hand.Category == HandCategory.StraightFlush && hand.Ranks[0] == Card.Ace ? All[^1] : All[(int)hand.Category];

    /// <summary>
    /// The ranking of any number of cards: the best 5-card hand if there are at least 5, otherwise what the cards make
    /// so far (high card, pair, two pair, three or four of a kind).
    /// </summary>
    public static string OfCards(IReadOnlyList<Card> cards)
    {
        if (cards.Count >= 5)
        {
            return Of(HandEvaluator.Best(cards));
        }

        var shape = cards.GroupBy(c => c.Rank).Select(g => g.Count()).OrderByDescending(n => n).ToArray();
        var category = shape switch
        {
            [4, ..] => HandCategory.FourOfAKind,
            [3, ..] => HandCategory.ThreeOfAKind,
            [2, 2, ..] => HandCategory.TwoPair,
            [2, ..] => HandCategory.OnePair,
            _ => HandCategory.HighCard,
        };
        return All[(int)category];
    }
}

/// <summary>
/// Finds the best poker hand that can be made from any number of cards (e.g. 2 private cards and 5 shared cards).
/// </summary>
public static class HandEvaluator
{
    /// <summary>
    /// The best 5-card hand among <paramref name="cards"/> (at least 5).
    /// </summary>
    public static HandValue Best(IReadOnlyList<Card> cards)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cards.Count, 5);
        HandValue? best = null;
        foreach (var five in Combinations(cards, 5))
        {
            var value = Evaluate(five);
            if (best is null || value.CompareTo(best) > 0)
            {
                best = value;
            }
        }

        return best!;
    }

    /// <summary>
    /// The value of exactly 5 cards.
    /// </summary>
    public static HandValue Evaluate(IReadOnlyList<Card> five)
    {
        var ranks = five.Select(c => c.Rank).OrderByDescending(r => r).ToArray();
        var isFlush = five.All(c => c.Suit == five[0].Suit);
        var straightHigh = StraightHigh(ranks);

        if (isFlush && straightHigh is { } flushHigh)
        {
            return new HandValue(HandCategory.StraightFlush, [flushHigh]);
        }

        // Groups of equal ranks, largest group first, then highest rank first.
        var groups = ranks.GroupBy(r => r).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).ToArray();
        var byGroup = groups.Select(g => g.Key).ToArray();
        var shape = groups.Select(g => g.Count()).ToArray();

        return shape switch
        {
            // Five of a rank (the four jacks and the "Jack" specialist card) count as four of a kind with that kicker.
            [5] => new HandValue(HandCategory.FourOfAKind, [byGroup[0], byGroup[0]]),
            [4, ..] => new HandValue(HandCategory.FourOfAKind, byGroup),
            [3, 2] => new HandValue(HandCategory.FullHouse, byGroup),
            _ when isFlush => new HandValue(HandCategory.Flush, ranks),
            _ when straightHigh is { } high => new HandValue(HandCategory.Straight, [high]),
            [3, ..] => new HandValue(HandCategory.ThreeOfAKind, byGroup),
            [2, 2, ..] => new HandValue(HandCategory.TwoPair, byGroup),
            [2, ..] => new HandValue(HandCategory.OnePair, byGroup),
            _ => new HandValue(HandCategory.HighCard, ranks),
        };
    }

    /// <summary>
    /// The highest card of a straight made by 5 ranks sorted high to low, or null. A-2-3-4-5 counts, with five high.
    /// </summary>
    private static int? StraightHigh(int[] ranks)
    {
        if (ranks.Distinct().Count() != 5)
        {
            return null;
        }

        if (ranks[0] - ranks[4] == 4)
        {
            return ranks[0];
        }

        return ranks is [Card.Ace, 5, 4, 3, 2] ? 5 : null;
    }

    /// <summary>
    /// All ways to choose <paramref name="size"/> of the cards.
    /// </summary>
    private static IEnumerable<Card[]> Combinations(IReadOnlyList<Card> cards, int size)
    {
        var indexes = Enumerable.Range(0, size).ToArray();
        while (true)
        {
            yield return indexes.Select(i => cards[i]).ToArray();
            var position = size - 1;
            while (position >= 0 && indexes[position] == cards.Count - size + position)
            {
                position--;
            }

            if (position < 0)
            {
                yield break;
            }

            indexes[position]++;
            for (var i = position + 1; i < size; i++)
            {
                indexes[i] = indexes[i - 1] + 1;
            }
        }
    }
}
