namespace GamesLane.Server.Games.RichManPoorMan;

/// <summary>
/// What a game of rich man, poor man is waiting for.
/// </summary>
public enum RichManPoorManPhase
{
    /// <summary>After a new deal (from the second game on), the pairs of players exchange cards.</summary>
    Exchanging,

    /// <summary>The rounds are played until every player has emptied their hand.</summary>
    Playing,

    /// <summary>Every player has emptied their hand; the ranking is complete and the next game can start.</summary>
    GameOver,
}

/// <summary>
/// Cards thrown together: who threw them, their number (2 to 14, the Ace) and how many.
/// </summary>
public record RichManPoorManThrow(int Player, int Rank, int Count);

/// <summary>
/// The fixed rules of rich man, poor man. Suits do not matter, so a card is just its number: 2 is the lowest, then 3 to
/// 10, J (11), Q (12), K (13), and A (14) is the highest. The deck has one suit (2 to A) per player, so every player is
/// dealt 13 cards.
/// </summary>
public static class RichManPoorManRules
{
    /// <summary>The fewest players a game needs.</summary>
    public const int MinPlayers = 6;

    /// <summary>The most players a game can have.</summary>
    public const int MaxPlayers = 12;

    /// <summary>The cards each player is dealt.</summary>
    public const int CardsPerPlayer = 13;

    /// <summary>The lowest card.</summary>
    public const int Two = 2;

    /// <summary>The highest card: throwing Aces wins the round at once.</summary>
    public const int Ace = 14;

    // The cards exchanged by each pair, from #1 with the last player inwards; the players in the middle without a
    // partner here do not exchange.
    private static readonly Dictionary<int, int[]> Exchanges = new()
    {
        [6] = [3, 2, 1],
        [7] = [3, 2, 1],
        [8] = [4, 3, 2, 1],
        [9] = [4, 3, 2, 1],
        [10] = [4, 3, 2, 1],
        [11] = [4, 3, 2, 1, 1],
        [12] = [4, 3, 2, 1, 1],
    };

    /// <summary>
    /// The number of cards exchanged between player #(k + 1) and their partner from the other end of the ranking, for
    /// each pair k, starting with #1 and the last player. Players in the middle not covered do not exchange.
    /// </summary>
    public static IReadOnlyList<int> ExchangeCounts(int players)
    {
        CheckPlayers(players);
        return Exchanges[players];
    }

    /// <summary>
    /// The deck for <paramref name="players"/> players, unshuffled: every number from 2 to A once per player.
    /// </summary>
    public static int[] Deck(int players)
    {
        CheckPlayers(players);
        return [.. Enumerable.Range(Two, CardsPerPlayer).SelectMany(rank => Enumerable.Repeat(rank, players))];
    }

    /// <summary>
    /// Throws unless rich man, poor man can be played by <paramref name="players"/> players.
    /// </summary>
    public static void CheckPlayers(int players)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, MaxPlayers);
    }
}
