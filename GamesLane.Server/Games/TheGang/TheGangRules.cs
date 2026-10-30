namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// The rules a game of The Gang is played with, from its options: the mode, how many challenge cards the first heist
/// gets, and which challenge and specialist cards (keys from <see cref="TheGangCards"/>) can be drawn. Cards are only
/// drawn in <see cref="TheGangMode.Advanced"/> mode.
/// </summary>
public record TheGangRules(
    TheGangMode Mode,
    int FirstHeistChallenges,
    IReadOnlyList<string> Challenges,
    IReadOnlyList<string> Specialists)
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
            GameOptions.TurnedOn(option(TheGangOptions.Specialists), options));
    }
}
