using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Tricks;

/// <summary>
/// A card played to a trick, and who played it.
/// </summary>
public record TrickCard(int Player, Card Card);

/// <summary>
/// A trick once every player has played to it: its cards in the order they were played (the first one led), and the
/// player who won it.
/// </summary>
public record CompletedTrick(TrickCard[] Cards, int Winner);

/// <summary>
/// The cards of the trick-taking games: the 52-card deck (the ace high), the order of a hand, and who wins a trick.
/// </summary>
public static class TrickCards
{
    /// <summary>The suits in the order a hand is sorted: clubs, diamonds, spades, hearts.</summary>
    private static readonly Suit[] HandOrder = [Suit.Clubs, Suit.Diamonds, Suit.Spades, Suit.Hearts];

    /// <summary>
    /// The 52 cards: 2 to A in each suit.
    /// </summary>
    public static Card[] Deck() =>
    [
        .. HandOrder.SelectMany(suit => Enumerable.Range(Card.Two, Card.Ace - Card.Two + 1).Select(rank => new Card(rank, suit))),
    ];

    /// <summary>
    /// The cards in hand order: by suit (clubs, diamonds, spades, hearts), each from 2 to A.
    /// </summary>
    public static IEnumerable<Card> Sorted(IEnumerable<Card> cards) =>
        cards.OrderBy(c => Array.IndexOf(HandOrder, c.Suit)).ThenBy(c => c.Rank);

    /// <summary>
    /// Who wins a complete trick: the highest <paramref name="trump"/> played, if any; otherwise the highest card of the
    /// suit led (the first card's).
    /// </summary>
    public static int Winner(IReadOnlyList<TrickCard> trick, Suit? trump)
    {
        var led = trick[0].Card.Suit;
        var suit = trump is { } t && trick.Any(c => c.Card.Suit == t) ? t : led;
        return trick.Where(c => c.Card.Suit == suit).MaxBy(c => c.Card.Rank)!.Player;
    }
}
