namespace GamesLane.Server.Games.Uno;

/// <summary>
/// A card's colour. Wild cards have the colour <see cref="Wild"/> until played; the player then chooses the colour to play.
/// </summary>
public enum UnoColor
{
    Red,
    Yellow,
    Green,
    Blue,
    Wild,
}

/// <summary>
/// What a card shows: a number (0 to 9), an action (Skip, Reverse, Draw Two) or a wild card (Wild, Wild Draw Four).
/// </summary>
public enum UnoValue
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
}

/// <summary>
/// An Uno card. Its short text is its colour's letter (R, Y, G, B) and its value (0 to 9, S for Skip, R for Reverse, +2
/// for Draw Two), e.g. "R7", "GS", "B+2"; or "W" for Wild and "W+4" for Wild Draw Four.
/// </summary>
public sealed record UnoCard(UnoColor Color, UnoValue Value)
{
    private const string ColorLetters = "RYGB";

    /// <summary>
    /// Whether this is a Wild or Wild Draw Four card.
    /// </summary>
    public bool IsWild => Color == UnoColor.Wild;

    /// <summary>
    /// What the card counts for the winner of a hand: a number its value, an action 20, a wild card 50.
    /// </summary>
    public int Points => Value switch
    {
        <= UnoValue.Nine => (int)Value,
        UnoValue.Skip or UnoValue.Reverse or UnoValue.DrawTwo => 20,
        _ => 50,
    };

    /// <summary>
    /// The cards the next player draws: 2 for Draw Two, 4 for Wild Draw Four, otherwise 0.
    /// </summary>
    public int DrawCount => Value switch
    {
        UnoValue.DrawTwo => 2,
        UnoValue.WildDrawFour => 4,
        _ => 0,
    };

    /// <summary>
    /// The 108 cards of a deck: in each colour one 0, two of each number 1 to 9 and two of each action; then four Wild and
    /// four Wild Draw Four.
    /// </summary>
    public static IReadOnlyList<UnoCard> Deck() =>
    [
        .. new[] { UnoColor.Red, UnoColor.Yellow, UnoColor.Green, UnoColor.Blue }.SelectMany(color =>
            Enumerable.Range((int)UnoValue.Zero, (int)UnoValue.DrawTwo + 1)
                .SelectMany(value => Enumerable.Repeat(new UnoCard(color, (UnoValue)value), value == 0 ? 1 : 2))),
        .. Enumerable.Repeat(new UnoCard(UnoColor.Wild, UnoValue.Wild), 4),
        .. Enumerable.Repeat(new UnoCard(UnoColor.Wild, UnoValue.WildDrawFour), 4),
    ];

    /// <summary>
    /// Reads a card's short text (see <see cref="UnoCard"/>). Returns false for anything else.
    /// </summary>
    public static bool TryParse(string? text, out UnoCard card)
    {
        card = new UnoCard(UnoColor.Wild, UnoValue.Wild);
        switch (text)
        {
            case "W":
                return true;
            case "W+4":
                card = new UnoCard(UnoColor.Wild, UnoValue.WildDrawFour);
                return true;
            case null or { Length: < 2 }:
                return false;
        }

        var color = ColorLetters.IndexOf(text[0]);
        UnoValue? value = text[1..] switch
        {
            "S" => UnoValue.Skip,
            "R" => UnoValue.Reverse,
            "+2" => UnoValue.DrawTwo,
            [var digit and >= '0' and <= '9'] => (UnoValue)(digit - '0'),
            _ => null,
        };
        if (color < 0 || value is null)
        {
            return false;
        }

        card = new UnoCard((UnoColor)color, value.Value);
        return true;
    }

    /// <summary>
    /// The card's short text, e.g. "R7" or "W+4".
    /// </summary>
    public override string ToString() => IsWild
        ? Value == UnoValue.WildDrawFour ? "W+4" : "W"
        : $"{ColorLetters[(int)Color]}{Value switch
        {
            UnoValue.Skip => "S",
            UnoValue.Reverse => "R",
            UnoValue.DrawTwo => "+2",
            _ => ((int)Value).ToString(),
        }}";
}
