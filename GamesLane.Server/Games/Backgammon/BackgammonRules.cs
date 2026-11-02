namespace GamesLane.Server.Games.Backgammon;

/// <summary>
/// What a backgammon match is waiting for.
/// </summary>
public enum BackgammonPhase
{
    /// <summary>Each player rolls one die; the higher die moves first (both roll again on a tie).</summary>
    Opening,

    /// <summary>The player to move rolls the dice, or first offers to double.</summary>
    Rolling,

    /// <summary>The player to move offered to double; the other player takes or drops.</summary>
    Doubled,

    /// <summary>The player to move moves checkers with the dice, then ends the turn.</summary>
    Moving,

    /// <summary>A game of the match has ended; the next one can start.</summary>
    GameOver,

    /// <summary>A player has reached the match length.</summary>
    MatchOver,
}

/// <summary>
/// How a game was won: a single game, a gammon (the loser bore off no checker), a backgammon (and still had a checker on
/// the bar or in the winner's home board), or by the other player dropping a double.
/// </summary>
public enum BackgammonResultKind
{
    Single,
    Gammon,
    Backgammon,
    Dropped,
}

/// <summary>
/// The end of a game: the winner (0 white, 1 black), the points won (the kind's 1, 2 or 3 times the cube) and how.
/// </summary>
public record BackgammonResult(int Winner, int Points, BackgammonResultKind Kind);

/// <summary>
/// A finished turn, for showing the last move: who moved, the dice, and the checkers moved (empty if none could be).
/// <see cref="Forced"/> is true when there was only one way to play, so the game played it.
/// </summary>
public record BackgammonTurn(int Player, IReadOnlyList<int> Dice, IReadOnlyList<BackgammonStep> Steps, bool Forced);

/// <summary>
/// The rules of a match, from its options: the points needed to win, and whether the doubling cube is used (with the
/// Crawford rule: no doubling in the game right after a player first comes within one point of winning).
/// </summary>
public record BackgammonRules(int MatchLength = 5, bool Cube = true);

/// <summary>
/// The options of backgammon.
/// </summary>
public static class BackgammonOptions
{
    /// <summary>
    /// The key of the match length option (1, 3, 5, 7 or 9 points).
    /// </summary>
    public const string MatchLength = "matchLength";

    /// <summary>
    /// The key of the doubling cube option.
    /// </summary>
    public const string Cube = "cube";

    /// <summary>
    /// All options, in the order they are shown.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            MatchLength,
            "Match length",
            [.. new[] { 1, 3, 5, 7, 9 }.Select(n => new GameOptionChoice(n.ToString(), n == 1 ? "1 point" : $"{n} points"))],
            "5"),
        new GameOption(
            Cube,
            "Doubling cube",
            [
                new GameOptionChoice("Yes", "With the cube",
                    "Before rolling, a player may offer to double the game's value; the other player takes it or gives up the game. Crawford rule included."),
                new GameOptionChoice("No", "Without the cube", "Every game is worth 1, 2 (gammon) or 3 (backgammon) points."),
            ],
            "Yes"),
    ];

    /// <summary>
    /// The rules for the chosen options (as kept by the session, with every option present and valid).
    /// </summary>
    public static BackgammonRules RulesFrom(IReadOnlyDictionary<string, string> options) => new(
        int.TryParse(options.GetValueOrDefault(MatchLength), out var length) ? length : 5,
        options.GetValueOrDefault(Cube) != "No");
}
