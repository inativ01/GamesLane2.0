using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Yaniv;

/// <summary>
/// When a Yaniv game ends.
/// </summary>
public enum YanivEndMode
{
    /// <summary>One round: its winner wins the game.</summary>
    OneRound,

    /// <summary>Round after round: a player whose points go above the target is out; the last player left wins.</summary>
    Score,
}

/// <summary>
/// What a Yaniv game is waiting for.
/// </summary>
public enum YanivPhase
{
    /// <summary>The player to play discards and takes a card, or calls Yaniv.</summary>
    Playing,

    /// <summary>A round of a game to a score target has ended; the next round can start.</summary>
    RoundOver,

    /// <summary>The game has ended.</summary>
    GameOver,
}

/// <summary>
/// A player's turn: the cards they discarded (a straight from lowest to highest), and whether they took the open pile's
/// top card (<see cref="Taken"/>) or a card from the closed pile. <see cref="Skipped"/> when an absent player's turn was
/// skipped; <see cref="Passed"/> when the player passed their last turn after a Yaniv call.
/// </summary>
public record YanivTurn(int Player, Card[] Cards, bool TookOpen, Card[] Taken, bool Skipped = false, bool Passed = false);

/// <summary>
/// A player at the end of a round: their cards, their hand score, the points they gained (with the Assaf penalty), whether
/// the bonus took 50 points off, their points after the round, and whether they are out.
/// </summary>
public record YanivRoundPlayer(int Player, Card[] Cards, int HandScore, int Points, bool Bonus, int Score, bool IsOut);

/// <summary>
/// The end of a round: who called Yaniv, who won the round (the caller, or the lowest hand if the caller was Assaf), and
/// each player who played it.
/// </summary>
public record YanivRoundResult(int Caller, int Winner, bool Assaf, YanivRoundPlayer[] Players);

/// <summary>
/// The rules of a game, from its options: when it ends, the points that put a player out (with <see cref="YanivEndMode.Score"/>),
/// and whether landing exactly on half the target or on the target takes 50 points off.
/// </summary>
public record YanivRules(YanivEndMode End = YanivEndMode.Score, int Target = 200, bool Bonus = true)
{
    /// <summary>The fewest players.</summary>
    public const int MinPlayers = 2;

    /// <summary>The most players.</summary>
    public const int MaxPlayers = 4;

    /// <summary>The cards dealt to each player.</summary>
    public const int HandSize = 5;

    /// <summary>The highest hand score with which a player may call Yaniv.</summary>
    public const int YanivLimit = 7;

    /// <summary>The points added to the hand score of a caller who is Assaf.</summary>
    public const int AssafPenalty = 30;

    /// <summary>The points taken off by the bonus.</summary>
    public const int BonusPoints = 50;
}

/// <summary>
/// The options of Yaniv.
/// </summary>
public static class YanivOptions
{
    /// <summary>The key of the option for when the game ends.</summary>
    public const string End = "end";

    /// <summary>The key of the score target option (with <see cref="YanivEndMode.Score"/>).</summary>
    public const string Target = "target";

    /// <summary>The key of the bonus option (with <see cref="YanivEndMode.Score"/>).</summary>
    public const string Bonus = "bonus";

    /// <summary>
    /// All options, in the order they are shown.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            End,
            "Game end",
            [
                new GameOptionChoice(nameof(YanivEndMode.OneRound), "One round", "The winner of the round wins the game."),
                new GameOptionChoice(nameof(YanivEndMode.Score), "Score target",
                    "Round after round, each player gains their hand score. Above the target, a player is out; the last player left wins."),
            ],
            nameof(YanivEndMode.Score)),
        new GameOption(
            Target,
            "Score target",
            [.. new[] { 100, 150, 200, 300 }.Select(n => new GameOptionChoice(n.ToString(), $"{n} points"))],
            "200",
            ShownWhen: new GameOptionCondition(End, nameof(YanivEndMode.Score))),
        new GameOption(
            Bonus,
            "Bonus",
            [
                new GameOptionChoice("Yes", "Bonus",
                    "Landing exactly on half the target or on the target takes 50 points off."),
                new GameOptionChoice("No", "No bonus", "Points only go up."),
            ],
            "Yes",
            ShownWhen: new GameOptionCondition(End, nameof(YanivEndMode.Score))),
    ];

    /// <summary>
    /// The rules for the chosen options (as kept by the session, with every option present and valid).
    /// </summary>
    public static YanivRules RulesFrom(IReadOnlyDictionary<string, string> options) => new(
        Enum.TryParse<YanivEndMode>(options.GetValueOrDefault(End), out var end) ? end : YanivEndMode.Score,
        int.TryParse(options.GetValueOrDefault(Target), out var target) ? target : 200,
        options.GetValueOrDefault(Bonus) != "No");
}
