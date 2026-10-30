namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// Basic rules, or advanced rules with challenge cards.
/// </summary>
public enum TheGangMode
{
    Basic,
    Advanced,
}

/// <summary>
/// The options of The Gang.
/// </summary>
public static class TheGangOptions
{
    /// <summary>
    /// The key of the <see cref="TheGangMode"/> option.
    /// </summary>
    public const string Mode = "mode";

    /// <summary>
    /// The key of the option for how many challenge cards the first heist gets (0 to 3).
    /// </summary>
    public const string FirstChallenges = "firstChallenges";

    /// <summary>
    /// The key of the set of challenge cards that can be drawn.
    /// </summary>
    public const string Challenges = "challenges";

    /// <summary>
    /// The key of the set of specialist cards that can be drawn.
    /// </summary>
    public const string Specialists = "specialists";

    /// <summary>
    /// The key of the option that keeps cards which cancel each other out of the same heist (off by default).
    /// </summary>
    public const string AvoidClashes = "avoidClashes";

    /// <summary>
    /// The values of yes/no options.
    /// </summary>
    public const string Yes = "Yes";

    /// <summary>
    /// The values of yes/no options.
    /// </summary>
    public const string No = "No";

    private static readonly GameOptionCondition WhenAdvanced = new(Mode, nameof(TheGangMode.Advanced));

    /// <summary>
    /// All options, in the order they are shown. The card options only apply in Advanced mode.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            Mode,
            "Mode",
            [
                new GameOptionChoice(nameof(TheGangMode.Basic), "Basic", "The standard rules."),
                new GameOptionChoice(nameof(TheGangMode.Advanced), "Advanced",
                    "With challenge and specialist cards: after a successful heist, the next one gets a challenge card; after a failed one, a specialist card."),
            ],
            nameof(TheGangMode.Basic)),
        new GameOption(
            FirstChallenges,
            "Challenge cards on the first heist",
            [.. Enumerable.Range(0, 4).Select(n => new GameOptionChoice(n.ToString(), n.ToString()))],
            "1",
            ShownWhen: WhenAdvanced),
        new GameOption(Challenges, "Challenge cards", CardChoices(TheGangCards.Challenges), "", GameOptionKind.Set, WhenAdvanced),
        new GameOption(Specialists, "Specialist cards", CardChoices(TheGangCards.Specialists), "", GameOptionKind.Set, WhenAdvanced),
        new GameOption(
            AvoidClashes,
            "Cards that cancel each other",
            [
                new GameOptionChoice(No, "Allow", "As in the original game. E.g. with Quick Access, Motion Detector and Laser Tripwires have no effect."),
                new GameOptionChoice(Yes, "Avoid", "Never draw them for the same heist."),
            ],
            No,
            ShownWhen: WhenAdvanced),
    ];

    private static GameOptionChoice[] CardChoices(IEnumerable<TheGangCard> cards) =>
        [.. cards.Select(c => new GameOptionChoice(c.Key, c.Name, c.Text))];

    /// <summary>
    /// The mode in <paramref name="options"/>, Basic if it is missing or unknown.
    /// </summary>
    public static TheGangMode ModeOf(IReadOnlyDictionary<string, string> options) =>
        Enum.TryParse<TheGangMode>(options.GetValueOrDefault(Mode), out var mode) && Enum.IsDefined(mode) ? mode : TheGangMode.Basic;
}
