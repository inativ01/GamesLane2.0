namespace GamesLane.Server.Games.Dominoes;

/// <summary>
/// The kind of dominoes game.
/// </summary>
public enum DominoesStyle
{
    /// <summary>A line of double-six tiles; a player who cannot play passes.</summary>
    Block,

    /// <summary>A line of double-six tiles; a player who cannot play draws from the boneyard until they can.</summary>
    Draw,

    /// <summary>As <see cref="Draw"/>, and a play that makes the open ends add up to a multiple of 5 scores them.</summary>
    AllFives,

    /// <summary>Double-twelve tiles: a train for each player and the Mexican train, from an engine double, round after round.</summary>
    MexicanTrain,
}

/// <summary>
/// What a dominoes game is waiting for.
/// </summary>
public enum DominoesPhase
{
    /// <summary>The player whose turn it is plays, draws or passes.</summary>
    Playing,

    /// <summary>A round has ended; the next one can be dealt.</summary>
    RoundOver,

    /// <summary>The game has ended.</summary>
    GameOver,
}

/// <summary>
/// What a player did on their turn.
/// </summary>
public enum DominoesActionKind
{
    Played,
    Drew,
    Passed,
    Skipped,
}

/// <summary>
/// The last action: who, what, the tile played (or null), where (see <see cref="IDominoesGame.TryPlay"/>), and the points
/// it scored (All Fives).
/// </summary>
public record DominoesAction(int Player, DominoesActionKind Kind, Tile? Tile = null, int Target = 0, int Points = 0);

/// <summary>
/// The end of round <see cref="Round"/>: the player who played their last tile (null when the round was blocked: nobody
/// could play), each player's tiles left and their pips, the points each side gained, and each side's score after the
/// round. (In All Fives, a game that reaches its target during a round ends without a result for that round.)
/// </summary>
public record DominoesRoundResult(int Round, int? Domino, bool Blocked, Tile[][] Hands, int[] Pips, int[] Points, int[] Scores);

/// <summary>
/// The rules of a game, from its options: the style, whether 4 players play in 2 teams of partners across (not in
/// Mexican Train), the score that ends the game (not in Mexican Train), and the rounds of Mexican Train.
/// </summary>
public record DominoesRules(DominoesStyle Style = DominoesStyle.Draw, bool Teams = false, int Target = 100, int TrainRounds = 13)
{
    /// <summary>The fewest players.</summary>
    public const int MinPlayers = 2;

    /// <summary>The most players of the line styles.</summary>
    public const int LineMaxPlayers = 4;

    /// <summary>The most players of Mexican Train.</summary>
    public const int TrainMaxPlayers = 8;

    /// <summary>Whether the tiles are laid in one line (every style but Mexican Train).</summary>
    public bool IsLine => Style != DominoesStyle.MexicanTrain;

    /// <summary>Whether a player who cannot play draws from the boneyard.</summary>
    public bool Draws => Style != DominoesStyle.Block;

    /// <summary>The highest double of the set: 6, or 12 in Mexican Train.</summary>
    public int Highest => IsLine ? 6 : 12;

    /// <summary>The most players of this style.</summary>
    public int MaxPlayers => IsLine ? LineMaxPlayers : TrainMaxPlayers;

    /// <summary>The tiles dealt to each of <paramref name="players"/> players.</summary>
    public int HandSize(int players) => Style switch
    {
        DominoesStyle.MexicanTrain => players <= 4 ? 15 : players <= 6 ? 12 : 10,
        DominoesStyle.Block => 7,
        _ => players == 2 ? 7 : 5,
    };

    /// <summary>The engine double of Mexican Train round <paramref name="round"/> (from 1): 12, then 11, and so on.</summary>
    public int EngineFor(int round) => Highest - (round - 1);
}

/// <summary>
/// The options of dominoes.
/// </summary>
public static class DominoesOptions
{
    /// <summary>The key of the style option.</summary>
    public const string Style = "style";

    /// <summary>The key of the teams option (line styles).</summary>
    public const string Teams = "teams";

    /// <summary>The key of the score target option (line styles).</summary>
    public const string Target = "target";

    /// <summary>The key of the Mexican Train rounds option.</summary>
    public const string Rounds = "rounds";

    private static readonly GameOptionCondition WhenLine =
        new(Style, nameof(DominoesStyle.Block), nameof(DominoesStyle.Draw), nameof(DominoesStyle.AllFives));

    /// <summary>
    /// All options, in the order they are shown.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            Style,
            "Style",
            [
                new GameOptionChoice(nameof(DominoesStyle.Draw), "Draw",
                    "Match an end of the line. Can't play? Draw from the boneyard until you can. 2–4 players."),
                new GameOptionChoice(nameof(DominoesStyle.Block), "Block", "Match an end of the line. Can't play? Pass. 2–4 players."),
                new GameOptionChoice(nameof(DominoesStyle.AllFives), "All Fives",
                    "As Draw, and when the open ends add up to 5, 10, 15 … you score them. 2–4 players."),
                new GameOptionChoice(nameof(DominoesStyle.MexicanTrain), "Mexican Train",
                    "Double-twelve tiles: build your own train or the Mexican train from the engine. The lowest total wins. 2–8 players."),
            ],
            nameof(DominoesStyle.Draw)),
        new GameOption(
            Teams,
            "Teams",
            [
                new GameOptionChoice("No", "Every player for themselves"),
                new GameOptionChoice("Yes", "Partners", "With 4 players: two teams, partners sitting across."),
            ],
            "No",
            ShownWhen: WhenLine),
        new GameOption(
            Target,
            "Score target",
            [.. new[] { 100, 150, 250 }.Select(n => new GameOptionChoice(n.ToString(), $"{n} points"))],
            "100",
            ShownWhen: WhenLine),
        new GameOption(
            Rounds,
            "Rounds",
            [
                new GameOptionChoice("13", "13 rounds", "The engine goes from double 12 down to double 0."),
                new GameOptionChoice("7", "7 rounds", "The engine goes from double 12 down to double 6."),
            ],
            "13",
            ShownWhen: new GameOptionCondition(Style, nameof(DominoesStyle.MexicanTrain))),
    ];

    /// <summary>
    /// The rules for the chosen options (as kept by the session, with every option present and valid).
    /// </summary>
    public static DominoesRules RulesFrom(IReadOnlyDictionary<string, string> options) => new(
        Enum.TryParse<DominoesStyle>(options.GetValueOrDefault(Style), out var style) ? style : DominoesStyle.Draw,
        options.GetValueOrDefault(Teams) == "Yes",
        int.TryParse(options.GetValueOrDefault(Target), out var target) ? target : 100,
        int.TryParse(options.GetValueOrDefault(Rounds), out var rounds) ? rounds : 13);
}
