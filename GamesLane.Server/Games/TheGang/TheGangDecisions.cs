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
/// What a specialist card shows everyone about a player's pocket cards when the betting starts: the card's key
/// (Investor or Math Whiz) and the fact, e.g. "2 face cards" or "17 total".
/// </summary>
public record TheGangFact(string Card, string Text);

/// <summary>
/// The first reason a revealed hand is weaker than the hand before it: its ranking is lower (<see cref="Ranking"/>), or
/// with the same ranking, the first of its five cards that is lower (<see cref="Cards"/>: positions 0 to 4 in
/// <see cref="HandValue.FiveRanks"/>, with all the cards of that value in a row, e.g. both cards of a lower pair).
/// With Muscle, a hand of the same ranking as Muscle's hand before it is weaker because of its ranking.
/// </summary>
public record TheGangWeakness(bool Ranking, IReadOnlyList<int> Cards);

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
