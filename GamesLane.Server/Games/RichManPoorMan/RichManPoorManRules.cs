using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.RichManPoorMan;

/// <summary>
/// What a game of rich man, poor man is waiting for.
/// </summary>
public enum RichManPoorManPhase
{
    /// <summary>After a new deal (from the second game on, or in the first with <see cref="RichManPoorManOptions.FirstGame"/>
    /// set to exchange), the pairs of players exchange cards.</summary>
    Exchanging,

    /// <summary>The rounds are played until every active player has emptied their hand.</summary>
    Playing,

    /// <summary>Every active player has emptied their hand; the ranking is complete and the next game can start.</summary>
    GameOver,
}

/// <summary>
/// Cards thrown together, all of one number: who threw them, and the cards.
/// </summary>
public record RichManPoorManThrow(int Player, IReadOnlyList<Card> Cards)
{
    /// <summary>The number thrown (2 to 14, the Ace).</summary>
    public int Rank => Cards[0].Rank;

    /// <summary>How many cards were thrown.</summary>
    public int Count => Cards.Count;
}

/// <summary>
/// The fixed rules of rich man, poor man. Suits do not matter, only the numbers: 2 is the lowest, then 3 to 10, J (11),
/// Q (12), K (13), and A (14) is the highest. The deck has one set of 2 to A per player, so every player is dealt 13
/// cards and each number appears once per player. The sets take the suits ♠, ♥, ♦, ♣ in turn.
/// </summary>
public static class RichManPoorManRules
{
    /// <summary>The fewest players a game needs.</summary>
    public const int MinPlayers = 4;

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
        [4] = [2, 1],
        [5] = [2, 1],
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
    /// The suits of the sets of 2 to A, in turn: the fifth set is ♠ again, and so on.
    /// </summary>
    public static IReadOnlyList<Suit> Suits => SuitOrder;

    private static readonly Suit[] SuitOrder = [Suit.Spades, Suit.Hearts, Suit.Diamonds, Suit.Clubs];

    /// <summary>
    /// The deck for <paramref name="players"/> players, unshuffled: one set of 2 to A per player, set by set.
    /// </summary>
    public static Card[] Deck(int players)
    {
        CheckPlayers(players);
        return [.. Enumerable.Range(0, players).SelectMany(set =>
            Enumerable.Range(Two, CardsPerPlayer).Select(rank => new Card(rank, SuitOrder[set % SuitOrder.Length])))];
    }

    /// <summary>
    /// Cards in the order a hand shows them: from low to high, equal numbers together (by suit, ♠ ♥ ♦ ♣).
    /// </summary>
    public static IEnumerable<Card> Sorted(IEnumerable<Card> cards) =>
        cards.OrderBy(c => c.Rank).ThenBy(c => Array.IndexOf(SuitOrder, c.Suit));

    /// <summary>
    /// Throws unless rich man, poor man can be played by <paramref name="players"/> players.
    /// </summary>
    public static void CheckPlayers(int players)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, MaxPlayers);
    }
}

/// <summary>
/// The options of rich man, poor man.
/// </summary>
public static class RichManPoorManOptions
{
    /// <summary>The key of the option for how the first game starts.</summary>
    public const string FirstGame = "firstGame";

    /// <summary>The first game is played without an exchange.</summary>
    public const string NoExchange = "NoExchange";

    /// <summary>The first game starts with an exchange, by the seat order.</summary>
    public const string Exchange = "Exchange";

    /// <summary>
    /// All options, in the order they are shown.
    /// </summary>
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new GameOption(
            FirstGame,
            "First game",
            [
                new GameOptionChoice(NoExchange, "No exchange", "The first game is played without an exchange; the player with the most 2s starts."),
                new GameOptionChoice(Exchange, "Exchange by current standings",
                    "The seats are the places: seat 1 is the rich man and the last seat the poor man. The pairs exchange cards before the first game, and the poorest player starts."),
            ],
            NoExchange),
    ];

    /// <summary>
    /// Whether the first game starts with an exchange, in the chosen options (as kept by the session).
    /// </summary>
    public static bool ExchangeFirst(IReadOnlyDictionary<string, string> options) =>
        options.GetValueOrDefault(FirstGame) == Exchange;
}
