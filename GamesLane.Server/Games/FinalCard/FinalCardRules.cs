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

    /// <summary>Pyramid: the player played all their cards and was dealt <see cref="FinalCardEvent.Count"/> new ones.</summary>
    NewCards,

    /// <summary>
    /// The player played all their cards: they won the hand, whose cards left count <see cref="FinalCardEvent.Count"/> points; or in
    /// a pyramid, they finished at place <see cref="FinalCardEvent.Count"/>.
    /// </summary>
    Finished,
}

/// <summary>
/// Something that happened: the player it is about, what, and the card, count, chosen colour or other player involved.
/// </summary>
public record FinalCardEvent(int Player, FinalCardEventKind Kind, FinalCardCard? Card = null, int Count = 0, FinalCardColor? Color = null, int? By = null);

/// <summary>
/// The end of a hand: the winner and the points of the cards left in the other hands.
/// </summary>
public record FinalCardHandResult(int Winner, int Points);

/// <summary>
/// The rules of a game, from its options: when it ends (and the score to reach), whether a Draw Two can be answered with a
/// Draw Two (and a Wild Draw Four with a Wild Draw Four) to pass on the cards to draw, and whether a player who draws keeps
/// drawing until they get a card they can play.
/// </summary>
public record FinalCardRules(FinalCardEndMode End = FinalCardEndMode.Score, int Target = 500, bool Stacking = false, bool DrawUntilPlayable = false)
{
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
                new GameOptionChoice("No", "No stacking", "A Draw Two or Wild Draw Four makes the next player draw and miss their turn."),
                new GameOptionChoice("Yes", "Stacking",
                    "Answer a Draw Two with a Draw Two, or a Wild Draw Four with a Wild Draw Four: the next player draws them all."),
            ],
            "No"),
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
        options.GetValueOrDefault(Stacking) == "Yes",
        options.GetValueOrDefault(Draw) == "UntilPlayable");
}
