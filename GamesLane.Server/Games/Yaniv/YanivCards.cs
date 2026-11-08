using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Yaniv;

/// <summary>
/// Yaniv's cards: the 52-card deck and two jokers, their values, and the cards that can be discarded together. The ace is
/// low: it counts 1 and only starts a straight (A-2-3).
/// </summary>
public static class YanivCards
{
    /// <summary>The error for cards that cannot be discarded together.</summary>
    public const string InvalidDiscard =
        "Discard one card, 2 to 4 cards of the same value, or 3 or more cards in a row of one suit.";

    /// <summary>
    /// The 54 cards: 2 to A in each suit, and two jokers.
    /// </summary>
    public static Card[] Deck() =>
    [
        .. new[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades }
            .SelectMany(suit => Enumerable.Range(Card.Two, Card.Ace - Card.Two + 1).Select(rank => new Card(rank, suit))),
        Card.Joker,
        Card.Joker,
    ];

    /// <summary>
    /// A card's value: a joker 0, an ace 1, 2 to 10 their number, J, Q and K 10.
    /// </summary>
    public static int Value(Card card) => card.IsJoker ? 0 : Math.Min(Position(card), 10);

    /// <summary>
    /// The sum of the cards' values.
    /// </summary>
    public static int Score(IEnumerable<Card> cards) => cards.Sum(Value);

    /// <summary>
    /// A card's place in a straight: an ace 1, 2 to 10 their number, J 11, Q 12, K 13 (a joker 0).
    /// </summary>
    public static int Position(Card card) => card.IsJoker ? 0 : card.Rank == Card.Ace ? 1 : card.Rank;

    /// <summary>
    /// Whether <paramref name="cards"/> can be discarded together, and the order they go on the open pile: one card; a
    /// series of 2 to 4 cards of the same value (jokers stand for any card), jokers first, then the others in the order
    /// given; or a straight of 3 or more cards of one suit in a row (jokers stand for any missing card), from lowest to
    /// highest. Jokers left over in a straight extend it downwards, or upwards once it reaches the ace. So a joker is on
    /// top only when it must follow the highest card of a straight from the ace (e.g. A-2-joker), or every card is a joker.
    /// </summary>
    public static bool TryArrange(IReadOnlyList<Card> cards, out Card[] ordered)
    {
        var others = cards.Where(c => !c.IsJoker).ToList();
        var jokers = cards.Count - others.Count;
        ordered = [.. cards.Where(c => c.IsJoker), .. others];
        if (cards.Count == 1 || (cards.Count <= 4 && others.Select(c => c.Rank).Distinct().Count() <= 1))
        {
            return true;
        }

        if (cards.Count < 3 || others.Count == 0 || others.Select(c => c.Suit).Distinct().Count() != 1)
        {
            return false;
        }

        var positions = others.Select(Position).Order().ToList();
        if (positions.Distinct().Count() != positions.Count)
        {
            return false;
        }

        var low = positions[0];
        var high = positions[^1];
        var spare = jokers - (high - low + 1 - positions.Count);
        if (spare < 0)
        {
            return false;
        }

        var down = Math.Min(spare, low - 1);
        low -= down;
        high += spare - down;
        if (high > 13)
        {
            return false;
        }

        ordered = [.. Enumerable.Range(low, high - low + 1)
            .Select(p => positions.Contains(p) ? others.First(c => Position(c) == p) : Card.Joker)];
        return true;
    }

    /// <summary>
    /// The cards in hand order: jokers first, then by place (ace low) and suit.
    /// </summary>
    public static IEnumerable<Card> Sorted(IEnumerable<Card> cards) => cards.OrderBy(Position).ThenBy(c => c.Suit);
}
