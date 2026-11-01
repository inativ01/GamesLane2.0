namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// A card suit. <see cref="None"/> is only for the "Jack" specialist card, which has no suit and so never makes a flush;
/// <see cref="Joker"/> only for the jokers.
/// </summary>
public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
    None,
    Joker,
}

/// <summary>
/// A playing card. <see cref="Rank"/> is 2 to 14, where 11 is a jack, 12 a queen, 13 a king and 14 an ace; a joker
/// (<see cref="Joker"/>) has rank 0. Jokers never count in a hand: they are replaced by other cards.
/// </summary>
public readonly record struct Card(int Rank, Suit Suit)
{
    /// <summary>
    /// The lowest rank (a two).
    /// </summary>
    public const int Two = 2;

    /// <summary>
    /// The highest rank (an ace).
    /// </summary>
    public const int Ace = 14;

    /// <summary>
    /// The text of a joker.
    /// </summary>
    public const string JokerText = "JOKER";

    /// <summary>
    /// Short text such as <c>"10♥"</c> or <c>"A♠"</c>, or <see cref="JokerText"/>; also sent to the pages.
    /// </summary>
    public override string ToString() => IsJoker ? JokerText : $"{RankSymbol(Rank)}{SuitSymbol(Suit)}";

    /// <summary>
    /// The "Jack" specialist card: a jack without a suit.
    /// </summary>
    public static Card SuitlessJack { get; } = new(11, Suit.None);

    /// <summary>
    /// A joker (the deck has two when the game is played with jokers).
    /// </summary>
    public static Card Joker { get; } = new(0, Suit.Joker);

    /// <summary>
    /// Whether this card is a joker.
    /// </summary>
    public bool IsJoker => Suit == Suit.Joker;

    /// <summary>
    /// Parses short text such as <c>"10H"</c>, <c>"AS"</c> or <c>"7♦"</c>; <c>"JX"</c> or <c>"J★"</c> is the suitless jack,
    /// and <c>"*"</c> or <see cref="JokerText"/> a joker.
    /// </summary>
    public static Card Parse(string text)
    {
        if (text is "*" || text.Equals(JokerText, StringComparison.OrdinalIgnoreCase))
        {
            return Joker;
        }

        var rank = text[..^1].ToUpperInvariant() switch
        {
            "J" => 11,
            "Q" => 12,
            "K" => 13,
            "A" => Ace,
            var digits => int.Parse(digits),
        };
        var suit = char.ToUpperInvariant(text[^1]) switch
        {
            'C' or '♣' => Suit.Clubs,
            'D' or '♦' => Suit.Diamonds,
            'H' or '♥' => Suit.Hearts,
            'S' or '♠' => Suit.Spades,
            'X' or '★' => Suit.None,
            _ => throw new FormatException($"Unknown suit in '{text}'."),
        };
        if (rank is < Two or > Ace)
        {
            throw new FormatException($"Unknown rank in '{text}'.");
        }

        return new Card(rank, suit);
    }

    /// <summary>
    /// <c>"2"</c> to <c>"10"</c>, <c>"J"</c>, <c>"Q"</c>, <c>"K"</c> or <c>"A"</c>.
    /// </summary>
    private static string RankSymbol(int rank) => rank switch
    {
        11 => "J",
        12 => "Q",
        13 => "K",
        Ace => "A",
        _ => rank.ToString(),
    };

    /// <summary>
    /// ♣, ♦, ♥ or ♠, or ★ for no suit.
    /// </summary>
    private static char SuitSymbol(Suit suit) => suit switch
    {
        Suit.Clubs => '♣',
        Suit.Diamonds => '♦',
        Suit.Hearts => '♥',
        Suit.None => '★',
        _ => '♠',
    };
}

/// <summary>
/// The 52-card deck. When a game is played with jokers, the game adds two.
/// </summary>
public static class Deck
{
    /// <summary>
    /// All 52 cards, shuffled.
    /// </summary>
    public static IReadOnlyList<Card> Shuffled(Random random)
    {
        var cards = new[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades }
            .SelectMany(suit => Enumerable.Range(Card.Two, Card.Ace - Card.Two + 1).Select(rank => new Card(rank, suit)))
            .ToArray();
        random.Shuffle(cards);
        return cards;
    }
}
