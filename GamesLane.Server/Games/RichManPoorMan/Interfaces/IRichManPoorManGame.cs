namespace GamesLane.Server.Games.RichManPoorMan;

/// <summary>
/// Rich man, poor man for 6 to 12 players, game after game. Players are numbered 0 to <see cref="PlayerCount"/> - 1 for
/// good; each game also gives them places from #1 (the rich man) to the last (the poor man), in <see cref="Order"/>.
/// The first game uses the players' own order; each later game re-seats them by the last game's ranking, deals again,
/// and the pairs exchange cards before playing.
/// Play goes in descending places, from the poor towards the rich, then from #1 straight back to the last.
/// A round: its starter throws any number N of cards of one number; in turn, each other player throws N cards of a
/// strictly higher number or passes. The round ends when everyone else has passed since the last throw, or at once when
/// Aces are thrown; the last thrower wins it and starts the next one (or, if their hand is empty, the next player in turn
/// who still has cards).
/// </summary>
public interface IRichManPoorManGame
{
    /// <summary>
    /// The number of players.
    /// </summary>
    int PlayerCount { get; }

    /// <summary>
    /// The current game, starting at 1.
    /// </summary>
    int GameNumber { get; }

    /// <summary>
    /// What the game is waiting for.
    /// </summary>
    RichManPoorManPhase Phase { get; }

    /// <summary>
    /// The players in this game's places, from #1 (the rich man) to the last (the poor man).
    /// </summary>
    IReadOnlyList<int> Order { get; }

    /// <summary>
    /// Each player's cards, from low to high.
    /// </summary>
    IReadOnlyList<IReadOnlyList<int>> Hands { get; }

    /// <summary>
    /// The player to throw or pass while <see cref="RichManPoorManPhase.Playing"/>.
    /// </summary>
    int Turn { get; }

    /// <summary>
    /// The throw to beat in this round, or null when the round's starter is to throw.
    /// </summary>
    RichManPoorManThrow? CurrentThrow { get; }

    /// <summary>
    /// The latest throw of this game, kept after its round ends; null before the first.
    /// </summary>
    RichManPoorManThrow? LastThrow { get; }

    /// <summary>
    /// How many throws have been made in all the games so far, so pages can tell a new throw.
    /// </summary>
    int Throws { get; }

    /// <summary>
    /// Whether each player's last action in this round was to pass.
    /// </summary>
    IReadOnlyList<bool> Passed { get; }

    /// <summary>
    /// The winner of the last finished round of this game, or null before then.
    /// </summary>
    int? LastRoundWinner { get; }

    /// <summary>
    /// The players who have emptied their hands in this game, in the order they did.
    /// </summary>
    IReadOnlyList<int> Ranking { get; }

    /// <summary>
    /// The player <paramref name="player"/> exchanges cards with in this game, or null if they do not exchange.
    /// </summary>
    int? ExchangePartner(int player);

    /// <summary>
    /// The number of cards <paramref name="player"/> gives (and receives) in this game's exchange; 0 if they do not exchange.
    /// </summary>
    int ExchangeCount(int player);

    /// <summary>
    /// The cards <paramref name="player"/> must give, if they are the poorer of their pair: their highest cards. Null for
    /// the richer player (who chooses) and for players who do not exchange.
    /// </summary>
    IReadOnlyList<int>? RequiredGift(int player);

    /// <summary>
    /// The cards <paramref name="player"/> has given in this exchange, while waiting for the others; null until given.
    /// </summary>
    IReadOnlyList<int>? Gift(int player);

    /// <summary>
    /// The cards <paramref name="player"/> received in this game's exchange, once it has happened; null otherwise.
    /// </summary>
    IReadOnlyList<int>? Received(int player);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, throws <paramref name="count"/> cards of number <paramref name="rank"/>.
    /// </summary>
    bool TryThrow(int player, int rank, int count, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, passes. The round's starter cannot pass.
    /// </summary>
    bool TryPass(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/> gives <paramref name="cards"/> to their exchange partner: the poorer player must give
    /// <see cref="RequiredGift"/>, the richer any cards of theirs, as many as <see cref="ExchangeCount"/>. Once every
    /// pair has given, the cards are exchanged and the poorest player starts.
    /// </summary>
    bool TryGive(int player, IReadOnlyList<int> cards, out string? error);

    /// <summary>
    /// Starts the game after game <paramref name="game"/>, once it is over. If it has already started, nothing changes.
    /// </summary>
    bool TryNextGame(int game, out string? error);
}
