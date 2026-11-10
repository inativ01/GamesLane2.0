namespace GamesLane.Server.Games.FinalCard;

/// <summary>
/// A card's color. Wild cards have the color <see cref="Wild"/> until played; the player then chooses the color to play.
/// </summary>
public enum FinalCardColor
{
    Red,
    Yellow,
    Green,
    Blue,
    Wild,
}

/// <summary>
/// What a card shows: a number (0 to 9), an action (Stop, Change Direction, +2) or a card without a color (Change Color,
/// Change Color +4). Israeli Style adds Plus and TAKI in each color, and SuperTAKI (a TAKI without a color), Crown, +3 and
/// +3 Breaker.
/// </summary>
public enum FinalCardValue
{
    Zero,
    One,
    Two,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Skip,
    Reverse,
    DrawTwo,
    Wild,
    WildDrawFour,
    Plus,
    Taki,
    Crown,
    DrawThree,
    BreakThree,
}

/// <summary>
/// A card of Final Card. Its short text is its color's letter (R, Y, G, B) and its value (0 to 9, S for Stop, R for Change
/// Direction, +2, + for Plus, T for TAKI), e.g. "R7", "GS", "B+2", "Y+", "RT"; or for a card without a color "W" (Change
/// Color), "W+4" (Change Color +4), "WT" (SuperTAKI), "WC" (Crown), "W+3" (+3) and "W+3X" (+3 Breaker).
/// </summary>
public sealed record FinalCardCard(FinalCardColor Color, FinalCardValue Value)
{
    private const string ColorLetters = "RYGB";

    private static readonly (FinalCardValue Value, string Text)[] Colorless =
    [
        (FinalCardValue.Wild, "W"),
        (FinalCardValue.WildDrawFour, "W+4"),
        (FinalCardValue.Taki, "WT"),
        (FinalCardValue.Crown, "WC"),
        (FinalCardValue.DrawThree, "W+3"),
        (FinalCardValue.BreakThree, "W+3X"),
    ];

    /// <summary>
    /// Whether this card has no color (Change Color, Change Color +4, SuperTAKI, Crown, +3 or +3 Breaker).
    /// </summary>
    public bool IsWild => Color == FinalCardColor.Wild;

    /// <summary>
    /// Whether this card is a TAKI or a SuperTAKI.
    /// </summary>
    public bool IsTaki => Value == FinalCardValue.Taki;

    /// <summary>
    /// What the card counts for the winner of a hand: a number its value, an action 20, a card without a color 50.
    /// </summary>
    public int Points => Value switch
    {
        <= FinalCardValue.Nine => (int)Value,
        _ when !IsWild => 20,
        _ => 50,
    };

    /// <summary>
    /// The cards the next player draws: 2 for Draw Two, 4 for Wild Draw Four, otherwise 0.
    /// </summary>
    public int DrawCount => Value switch
    {
        FinalCardValue.DrawTwo => 2,
        FinalCardValue.WildDrawFour => 4,
        _ => 0,
    };

    private static readonly FinalCardColor[] Colors = [FinalCardColor.Red, FinalCardColor.Yellow, FinalCardColor.Green, FinalCardColor.Blue];

    /// <summary>
    /// The 108 cards of a deck: in each color one 0, two of each number 1 to 9 and two of each action; then four Change
    /// Color and four Change Color +4.
    /// </summary>
    public static IReadOnlyList<FinalCardCard> Deck() =>
    [
        .. Colors.SelectMany(color =>
            Enumerable.Range((int)FinalCardValue.Zero, (int)FinalCardValue.DrawTwo + 1)
                .SelectMany(value => Enumerable.Repeat(new FinalCardCard(color, (FinalCardValue)value), value == 0 ? 1 : 2))),
        .. Enumerable.Repeat(new FinalCardCard(FinalCardColor.Wild, FinalCardValue.Wild), 4),
        .. Enumerable.Repeat(new FinalCardCard(FinalCardColor.Wild, FinalCardValue.WildDrawFour), 4),
    ];

    /// <summary>
    /// The 116 cards of an Israeli Style (Super TAKI) deck: in each color two of each number 1 and 3 to 9 (no 0, and the
    /// +2 cards instead of the 2s), and two each of Stop, Change Direction, +2, Plus and TAKI; then four Change Color, and
    /// two each of SuperTAKI, Crown, +3 and +3 Breaker.
    /// </summary>
    public static IReadOnlyList<FinalCardCard> IsraeliDeck() =>
    [
        .. Colors.SelectMany(color =>
            new[]
            {
                FinalCardValue.One, FinalCardValue.Three, FinalCardValue.Four, FinalCardValue.Five, FinalCardValue.Six,
                FinalCardValue.Seven, FinalCardValue.Eight, FinalCardValue.Nine, FinalCardValue.Skip, FinalCardValue.Reverse,
                FinalCardValue.DrawTwo, FinalCardValue.Plus, FinalCardValue.Taki,
            }.SelectMany(value => Enumerable.Repeat(new FinalCardCard(color, value), 2))),
        .. Enumerable.Repeat(new FinalCardCard(FinalCardColor.Wild, FinalCardValue.Wild), 4),
        .. new[] { FinalCardValue.Taki, FinalCardValue.Crown, FinalCardValue.DrawThree, FinalCardValue.BreakThree }
            .SelectMany(value => Enumerable.Repeat(new FinalCardCard(FinalCardColor.Wild, value), 2)),
    ];

    /// <summary>
    /// Reads a card's short text (see <see cref="FinalCardCard"/>). Returns false for anything else.
    /// </summary>
    public static bool TryParse(string? text, out FinalCardCard card)
    {
        card = new FinalCardCard(FinalCardColor.Wild, FinalCardValue.Wild);
        if (Colorless.FirstOrDefault(c => c.Text == text) is { Text: not null } colorless)
        {
            card = new FinalCardCard(FinalCardColor.Wild, colorless.Value);
            return true;
        }

        if (text is null or { Length: < 2 })
        {
            return false;
        }

        var color = ColorLetters.IndexOf(text[0]);
        FinalCardValue? value = text[1..] switch
        {
            "S" => FinalCardValue.Skip,
            "R" => FinalCardValue.Reverse,
            "+2" => FinalCardValue.DrawTwo,
            "+" => FinalCardValue.Plus,
            "T" => FinalCardValue.Taki,
            [var digit and >= '0' and <= '9'] => (FinalCardValue)(digit - '0'),
            _ => null,
        };
        if (color < 0 || value is null)
        {
            return false;
        }

        card = new FinalCardCard((FinalCardColor)color, value.Value);
        return true;
    }

    /// <summary>
    /// The card's short text, e.g. "R7" or "W+4".
    /// </summary>
    public override string ToString() => IsWild
        ? Colorless.First(c => c.Value == Value).Text
        : $"{ColorLetters[(int)Color]}{Value switch
        {
            FinalCardValue.Skip => "S",
            FinalCardValue.Reverse => "R",
            FinalCardValue.DrawTwo => "+2",
            FinalCardValue.Plus => "+",
            FinalCardValue.Taki => "T",
            _ => ((int)Value).ToString(),
        }}";
}
