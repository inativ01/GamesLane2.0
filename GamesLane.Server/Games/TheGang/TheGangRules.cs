namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// The rules a game of The Gang is played with, from its options: the mode, how many challenge cards the first heist
/// gets, which challenge and specialist cards (keys from <see cref="TheGangCards"/>) can be drawn, whether cards that
/// cancel each other are kept apart, and whether the deck has two jokers. Cards are only drawn in
/// <see cref="TheGangMode.Advanced"/> mode; jokers can be used in both modes. Two options only change the pages: whether
/// each player sees the ranking of their own hand next to their cards, and whether the challenge and specialist cards on
/// the table show what they do.
/// </summary>
public record TheGangRules(
    TheGangMode Mode,
    int FirstHeistChallenges,
    IReadOnlyList<string> Challenges,
    IReadOnlyList<string> Specialists,
    bool AvoidClashes = false,
    bool Jokers = false,
    bool ShowHandRanking = false,
    bool ShowCardTexts = false)
{
    /// <summary>
    /// The basic game, without cards.
    /// </summary>
    public static TheGangRules Basic { get; } = new(TheGangMode.Basic, 0, [], []);

    /// <summary>
    /// The rules for the chosen options (as kept by the session, with every option present and valid).
    /// </summary>
    public static TheGangRules FromOptions(IReadOnlyDictionary<string, string> options)
    {
        var option = (string key) => TheGangOptions.All.First(o => o.Key == key);
        return new TheGangRules(
            TheGangOptions.ModeOf(options),
            int.TryParse(options.GetValueOrDefault(TheGangOptions.FirstChallenges), out var count) ? count : 0,
            GameOptions.TurnedOn(option(TheGangOptions.Challenges), options),
            GameOptions.TurnedOn(option(TheGangOptions.Specialists), options),
            options.GetValueOrDefault(TheGangOptions.AvoidClashes) == TheGangOptions.Yes,
            options.GetValueOrDefault(TheGangOptions.Jokers) == TheGangOptions.Yes,
            options.GetValueOrDefault(TheGangOptions.ShowHandRanking) == TheGangOptions.Yes,
            options.GetValueOrDefault(TheGangOptions.ShowCardTexts) == TheGangOptions.Yes);
    }
}
