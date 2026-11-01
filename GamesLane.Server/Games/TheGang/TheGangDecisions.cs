namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// A private step a chosen player must take before the stage can end: show a card (Informant) or discard one (Hacker, Jack).
/// </summary>
public enum TheGangStep
{
    None,
    ShowCard,
    Discard,
}

/// <summary>
/// A card's group decision: the proposed player and value, who votes and who has agreed, whether players can vote now,
/// whether it has been carried out, and for a scan, whether it was skipped because the heist fails anyway. Players are
/// game player numbers.
/// </summary>
public record TheGangDecisionInfo(
    string Card,
    TheGangChoice Choice,
    int? Player,
    string? Value,
    IReadOnlyList<int> Voters,
    IReadOnlyList<int> Agreed,
    bool IsOpen,
    bool IsDone,
    bool IsSkipped);

/// <summary>
/// What a card showed everyone about a player: Getaway Driver (hand ranking), Mastermind (how many of a value), and the
/// scans' guesses about the strongest hand, with <see cref="Passed"/> telling whether the guess was right (null for
/// other cards).
/// </summary>
public record TheGangResult(int Player, string Card, string Text, bool? Passed = null);

/// <summary>
/// The values players can name: card values ("2" to "10", "J", "Q", "K", "A") and hand rankings.
/// </summary>
public static class TheGangValues
{
    /// <summary>
    /// The card values, from 2 to ace.
    /// </summary>
    public static IReadOnlyList<string> Ranks { get; } = ["2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A"];

    /// <summary>
    /// The rank (2 to 14) of a card value, or null.
    /// </summary>
    public static int? RankOf(string? value) => value is null || !Ranks.Contains(value) ? null : Ranks.ToList().IndexOf(value) + Card.Two;

    /// <summary>
    /// Whether <paramref name="value"/> is one of the values <paramref name="choice"/> names.
    /// </summary>
    public static bool IsValid(TheGangChoice choice, string value) => choice switch
    {
        TheGangChoice.PlayerAndRank or TheGangChoice.Rank => RankOf(value) is not null,
        TheGangChoice.Ranking => HandRankings.All.Contains(value),
        _ => false,
    };
}
