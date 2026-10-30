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
    /// All options, in the order they are shown.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            Mode,
            "Mode",
            [
                new GameOptionChoice(nameof(TheGangMode.Basic), "Basic", "The standard rules."),
                new GameOptionChoice(nameof(TheGangMode.Advanced), "Advanced", "With challenge cards. Coming soon: plays like Basic for now."),
            ],
            nameof(TheGangMode.Basic)),
    ];

    /// <summary>
    /// The mode in <paramref name="options"/>, Basic if it is missing or unknown.
    /// </summary>
    public static TheGangMode ModeOf(IReadOnlyDictionary<string, string> options) =>
        Enum.TryParse<TheGangMode>(options.GetValueOrDefault(Mode), out var mode) && Enum.IsDefined(mode) ? mode : TheGangMode.Basic;
}
