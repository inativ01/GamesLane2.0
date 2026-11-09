namespace GamesLane.Server.Games.FinalCard;

/// <summary>
/// When a Final Card game ends.
/// </summary>
public enum FinalCardEndMode
{
    /// <summary>One hand: the first player to play all their cards wins.</summary>
    OneGame,

    /// <summary>Hand after hand: the winner of each hand scores the cards left in the other hands, up to a target.</summary>
    Score,

    /// <summary>
    /// One long hand: a player who plays all their cards is dealt one card fewer than last time (7, then 6, … then 1), and
    /// finishes once they play their last single card. The game goes on until one player is left.
    /// </summary>
    Pyramid,
}

/// <summary>
/// Which draw cards can answer a draw card, passing on the cards to draw (all of them) to the next player.
/// </summary>
public enum FinalCardStacking
{
    /// <summary>None: a Draw Two or Wild Draw Four makes the next player draw and miss their turn.</summary>
    None,

    /// <summary>A Draw Two on a Draw Two.</summary>
    TwoOnTwo,

    /// <summary>A Draw Two on a Draw Two, and a Wild Draw Four on a Wild Draw Four.</summary>
    SameCard,

    /// <summary>As <see cref="SameCard"/>, and a Wild Draw Four on a Draw Two too.</summary>
    FourOnTwo,
}

/// <summary>
/// When a Wild Draw Four can be played (when not answering a stack of draw cards, where it only needs the stacking rule).
/// </summary>
public enum FinalCardWildDrawFour
{
    /// <summary>At any time.</summary>
    Always,

    /// <summary>Only when the player holds no card of the color to play (the game checks it).</summary>
    NoColor,

    /// <summary>
    /// At any time, but the next player can challenge it instead of drawing: if its player held a card of the color that
    /// was to play, they draw the cards instead; otherwise the challenger draws 2 more and misses their turn.
    /// </summary>
    Challenge,
}

/// <summary>
/// What a Final Card game is waiting for.
/// </summary>
public enum FinalCardPhase
{
    /// <summary>The player to play plays a card, or draws.</summary>
    Playing,

    /// <summary>A hand of a game to a score target has ended; the next hand can start.</summary>
    HandOver,

    /// <summary>The game has ended.</summary>
    GameOver,
}

/// <summary>
/// Something that happened in the last action, for showing it.
/// </summary>
public enum FinalCardEventKind
{
    /// <summary>The first card of the discard pile was turned up.</summary>
    TurnedUp,

    /// <summary>The player played <see cref="FinalCardEvent.Card"/> (a wild card with the chosen <see cref="FinalCardEvent.Color"/>).</summary>
    Played,

    /// <summary>The player drew <see cref="FinalCardEvent.Count"/> cards.</summary>
    Drew,

    /// <summary>The player misses their turn.</summary>
    Skipped,

    /// <summary>The player kept the card they drew.</summary>
    Passed,

    /// <summary>The player called Final Card.</summary>
    CalledFinalCard,

    /// <summary>The player was caught by <see cref="FinalCardEvent.By"/> without calling Final Card and drew <see cref="FinalCardEvent.Count"/> cards.</summary>
    Caught,

    /// <summary>The player challenged the Wild Draw Four of <see cref="FinalCardEvent.By"/>, who held a card of the color: they draw the cards.</summary>
    ChallengeWon,

    /// <summary>The player challenged the Wild Draw Four of <see cref="FinalCardEvent.By"/>, who held no card of the color: the challenger draws 2 more.</summary>
    ChallengeLost,

    /// <summary>Pyramid: the player played all their cards and was dealt <see cref="FinalCardEvent.Count"/> new ones.</summary>
    NewCards,

    /// <summary>
    /// The player played all their cards: they won the hand, whose cards left count <see cref="FinalCardEvent.Count"/> points; or in
    /// a pyramid, they finished at place <see cref="FinalCardEvent.Count"/>.
    /// </summary>
    Finished,
}

/// <summary>
/// Something that happened: the player it is about, what, and the card, count, chosen color or other player involved.
/// </summary>
public record FinalCardEvent(int Player, FinalCardEventKind Kind, FinalCardCard? Card = null, int Count = 0, FinalCardColor? Color = null, int? By = null);

/// <summary>
/// The end of a hand: the winner and the points of the cards left in the other hands.
/// </summary>
public record FinalCardHandResult(int Winner, int Points);

/// <summary>
/// The rules of a game, from its options: when it ends (and the score to reach), which draw cards can answer a draw card
/// to pass on the cards to draw, whether a player who draws keeps drawing until they get a card they can play, and when a
/// Wild Draw Four can be played.
/// </summary>
public record FinalCardRules(
    FinalCardEndMode End = FinalCardEndMode.Score,
    int Target = 500,
    FinalCardStacking Stacking = FinalCardStacking.None,
    bool DrawUntilPlayable = false,
    FinalCardWildDrawFour WildDrawFour = FinalCardWildDrawFour.NoColor)
{
    /// <summary>Whether <paramref name="card"/> can answer <paramref name="top"/>, a draw card, under the stacking rule.</summary>
    public bool CanStack(FinalCardCard card, FinalCardCard top) => (card.Value, top.Value) switch
    {
        (FinalCardValue.DrawTwo, FinalCardValue.DrawTwo) => Stacking != FinalCardStacking.None,
        (FinalCardValue.WildDrawFour, FinalCardValue.WildDrawFour) => Stacking is FinalCardStacking.SameCard or FinalCardStacking.FourOnTwo,
        (FinalCardValue.WildDrawFour, FinalCardValue.DrawTwo) => Stacking == FinalCardStacking.FourOnTwo,
        _ => false,
    };

    /// <summary>Whether any draw card can answer <paramref name="top"/> under the stacking rule.</summary>
    public bool CanBeStacked(FinalCardCard top) =>
        CanStack(new FinalCardCard(FinalCardColor.Red, FinalCardValue.DrawTwo), top)
        || CanStack(new FinalCardCard(FinalCardColor.Wild, FinalCardValue.WildDrawFour), top);

    /// <summary>The fewest players.</summary>
    public const int MinPlayers = 2;

    /// <summary>The most players.</summary>
    public const int MaxPlayers = 10;

    /// <summary>The cards dealt to each player (in a pyramid, the first time).</summary>
    public const int HandSize = 7;
}

/// <summary>
/// The options of Final Card.
/// </summary>
public static class FinalCardOptions
{
    /// <summary>The key of the option for when the game ends.</summary>
    public const string End = "end";

    /// <summary>The key of the score target option (with <see cref="FinalCardEndMode.Score"/>).</summary>
    public const string Target = "target";

    /// <summary>The key of the stacking option.</summary>
    public const string Stacking = "stacking";

    /// <summary>The key of the drawing option.</summary>
    public const string Draw = "draw";

    /// <summary>The key of the Wild Draw Four option.</summary>
    public const string WildDrawFour = "wildDrawFour";

    /// <summary>
    /// All options, in the order they are shown.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            End,
            "Game end",
            [
                new GameOptionChoice(nameof(FinalCardEndMode.OneGame), "One game", "The first player to play all their cards wins."),
                new GameOptionChoice(nameof(FinalCardEndMode.Score), "Score target",
                    "The winner of each hand scores the cards left in the other hands. The first to reach the target wins."),
                new GameOptionChoice(nameof(FinalCardEndMode.Pyramid), "Pyramid",
                    "Played all your cards? Take one card fewer than last time: 7, 6, 5 … down to 1. The first to play their last single card wins; play goes on until everyone is ranked."),
            ],
            nameof(FinalCardEndMode.Score)),
        new GameOption(
            Target,
            "Score target",
            [.. new[] { 100, 200, 300, 500 }.Select(n => new GameOptionChoice(n.ToString(), $"{n} points"))],
            "500",
            ShownWhen: new GameOptionCondition(End, nameof(FinalCardEndMode.Score))),
        new GameOption(
            Stacking,
            "Stacking",
            [
                new GameOptionChoice(nameof(FinalCardStacking.None), "No stacking",
                    "A Draw Two or Wild Draw Four makes the next player draw and miss their turn."),
                new GameOptionChoice(nameof(FinalCardStacking.TwoOnTwo), "+2 on +2",
                    "Answer a Draw Two with a Draw Two: the next player draws them all."),
                new GameOptionChoice(nameof(FinalCardStacking.SameCard), "+2 on +2 and +4 on +4",
                    "Answer a Draw Two with a Draw Two, or a Wild Draw Four with a Wild Draw Four: the next player draws them all."),
                new GameOptionChoice(nameof(FinalCardStacking.FourOnTwo), "+2 on +2, +4 on +4 and +4 on +2",
                    "As above, and a Wild Draw Four can also answer a Draw Two: the next player draws them all."),
            ],
            nameof(FinalCardStacking.None)),
        new GameOption(
            WildDrawFour,
            "Wild Draw Four",
            [
                new GameOptionChoice(nameof(FinalCardWildDrawFour.Always), "Always allowed", "A Wild Draw Four can be played at any time."),
                new GameOptionChoice(nameof(FinalCardWildDrawFour.NoColor), "Only without the color",
                    "A Wild Draw Four can be played only when you have no card of the color to play."),
                new GameOptionChoice(nameof(FinalCardWildDrawFour.Challenge), "Can be challenged",
                    "A Wild Draw Four can always be played, but the next player can challenge it: if its player had a card of the color to play, they draw the cards instead; if not, the challenger draws 2 more."),
            ],
            nameof(FinalCardWildDrawFour.NoColor)),
        new GameOption(
            Draw,
            "Drawing",
            [
                new GameOptionChoice("One", "Draw one card", "Draw one card; play it if you can, or keep it."),
                new GameOptionChoice("UntilPlayable", "Draw until you can play", "Keep drawing until you get a card you can play."),
            ],
            "One"),
    ];

    /// <summary>
    /// The rules for the chosen options (as kept by the session, with every option present and valid).
    /// </summary>
    public static FinalCardRules RulesFrom(IReadOnlyDictionary<string, string> options) => new(
        Enum.TryParse<FinalCardEndMode>(options.GetValueOrDefault(End), out var end) ? end : FinalCardEndMode.Score,
        int.TryParse(options.GetValueOrDefault(Target), out var target) ? target : 500,
        Enum.TryParse<FinalCardStacking>(options.GetValueOrDefault(Stacking), out var stacking) ? stacking : FinalCardStacking.None,
        options.GetValueOrDefault(Draw) == "UntilPlayable",
        Enum.TryParse<FinalCardWildDrawFour>(options.GetValueOrDefault(WildDrawFour), out var wild) ? wild : FinalCardWildDrawFour.NoColor);
}
