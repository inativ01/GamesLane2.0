namespace GamesLane.Server.Games.Spades;

/// <summary>
/// What a Spades game is waiting for.
/// </summary>
public enum SpadesPhase
{
    /// <summary>The players bid, one by one.</summary>
    Bidding,

    /// <summary>The players play the 13 tricks.</summary>
    Playing,

    /// <summary>A hand has ended; the next one can be dealt.</summary>
    HandOver,

    /// <summary>The game has ended.</summary>
    GameOver,
}

/// <summary>
/// A player's bid: the tricks they expect to take, 0 for Nil (none), and whether a Nil was bid blind (before looking at
/// the cards).
/// </summary>
public record SpadesBid(int Tricks, bool Blind = false)
{
    /// <summary>Whether the bid is Nil (or Blind Nil).</summary>
    public bool IsNil => Tricks == 0;
}

/// <summary>
/// A team at the end of a hand: its bid (without Nil bids), the tricks counting for it (without those of its Nil bidders),
/// whether it made its bid, the points it gained (with its Nil bids and any bag penalty), the bags it took in the hand,
/// whether 10 bags cost it 100 points, and its score and bags after the hand.
/// </summary>
public record SpadesTeamResult(int Team, int Bid, int Tricks, bool Made, int Points, int NewBags, bool BagPenalty, int Score, int Bags);

/// <summary>
/// A Nil bid at the end of a hand: who bid it, whether blind, and whether they took no trick.
/// </summary>
public record SpadesNilResult(int Player, bool Blind, bool Made);

/// <summary>
/// The end of a hand: each team's result, and the Nil bids.
/// </summary>
public record SpadesHandResult(SpadesTeamResult[] Teams, SpadesNilResult[] Nils);

/// <summary>
/// The rules of a game, from its options: whether a player may bid Nil, and Blind Nil.
/// </summary>
public record SpadesRules(bool Nil = true, bool BlindNil = false)
{
    /// <summary>The score that wins the game, at the end of a hand.</summary>
    public const int Target = 500;

    /// <summary>The score at which a team loses the game, at the end of a hand.</summary>
    public const int LosingScore = -200;

    /// <summary>The points for each trick bid, won if the bid is made and lost if it is not.</summary>
    public const int PointsPerTrick = 10;

    /// <summary>The points of a Nil bid, won or lost.</summary>
    public const int NilPoints = 100;

    /// <summary>The points of a Blind Nil bid, won or lost.</summary>
    public const int BlindNilPoints = 200;

    /// <summary>The bags that cost a team <see cref="BagPenalty"/> points.</summary>
    public const int BagLimit = 10;

    /// <summary>The points 10 bags cost.</summary>
    public const int BagPenalty = 100;

    /// <summary>The team of a player: 0 for players 0 and 2, 1 for players 1 and 3.</summary>
    public static int TeamOf(int player) => player % 2;
}

/// <summary>
/// The options of Spades.
/// </summary>
public static class SpadesOptions
{
    /// <summary>The key of the Nil option.</summary>
    public const string Nil = "nil";

    /// <summary>The key of the Blind Nil option (with Nil).</summary>
    public const string BlindNil = "blindNil";

    private const string Yes = "Yes";
    private const string No = "No";

    /// <summary>
    /// All options, in the order they are shown.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            Nil,
            "Nil",
            [
                new GameOptionChoice(Yes, "Nil allowed", "Bid to take no trick: 100 points if you take none, otherwise 100 off."),
                new GameOptionChoice(No, "No Nil", "Every player bids at least 1 trick."),
            ],
            Yes),
        new GameOption(
            BlindNil,
            "Blind Nil",
            [
                new GameOptionChoice(No, "No Blind Nil", "Every player looks at their cards before bidding."),
                new GameOptionChoice(Yes, "Blind Nil allowed",
                    "Bid Nil before looking at your cards: 200 points if you take no trick, otherwise 200 off."),
            ],
            No,
            ShownWhen: new GameOptionCondition(Nil, Yes)),
    ];

    /// <summary>
    /// The rules for the chosen options (as kept by the session, with every option present and valid).
    /// </summary>
    public static SpadesRules RulesFrom(IReadOnlyDictionary<string, string> options)
    {
        var nil = options.GetValueOrDefault(Nil) != No;
        return new SpadesRules(nil, nil && options.GetValueOrDefault(BlindNil) == Yes);
    }
}
