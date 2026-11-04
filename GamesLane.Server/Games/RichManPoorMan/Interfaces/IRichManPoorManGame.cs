using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.RichManPoorMan;

/// <summary>
/// Rich man, poor man for 6 to 12 players, game after game. A player is identified by a number of the caller's choice
/// (the session uses seat numbers); each game gives the players places from #1 (the rich man) to the last (the poor
/// man), in <see cref="Order"/>. The first game uses the order given; each later game re-seats them by the last game's
/// ranking, deals again, and the pairs exchange cards before playing.
/// Play goes in descending places, from the poor towards the rich, then from #1 straight back to the last.
/// A round: its starter throws any number N of cards of one number; in turn, each other player throws N cards of a
/// strictly higher number or passes. The round ends when everyone else has passed since the last throw, or at once when
/// Aces are thrown; the last thrower wins it and starts the next one (or, if their hand is empty, the next player in turn
/// who still has cards).
/// A player who is not active (who left) keeps their cards until the game ends and passes automatically; if they are to
/// start a round, the next active player in turn starts it instead. The game ends once every active player has emptied
/// their hand. Players who left are dropped at the next game; during the exchange, players can be added or dropped,
/// which deals the cards again for the new number of players.
/// </summary>
public interface IRichManPoorManGame
{
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
    /// <paramref name="player"/>'s cards, from low to high (see <see cref="RichManPoorManRules.Sorted"/>).
    /// </summary>
    IReadOnlyList<Card> Hand(int player);

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
    /// Whether <paramref name="player"/>'s last action in this round was to pass (not counting automatic passes).
    /// </summary>
    bool HasPassed(int player);

    /// <summary>
    /// The cards <paramref name="player"/> last threw in this round, or null if they have not thrown in it.
    /// </summary>
    IReadOnlyList<Card>? RoundThrow(int player);

    /// <summary>
    /// Whether <paramref name="player"/> is active, i.e. has not left the game (see <see cref="SetActive"/>).
    /// </summary>
    bool IsActive(int player);

    /// <summary>
    /// The winner of the last finished round of this game, or null before then.
    /// </summary>
    int? LastRoundWinner { get; }

    /// <summary>
    /// The players who have emptied their hands in this game, in the order they did.
    /// </summary>
    IReadOnlyList<int> Ranking { get; }

    /// <summary>
    /// The order for the next game: <see cref="Ranking"/>, then any players without a finishing place (who left before
    /// emptying their hand), in their current order.
    /// </summary>
    IReadOnlyList<int> NextOrder { get; }

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
    IReadOnlyList<Card>? RequiredGift(int player);

    /// <summary>
    /// The cards <paramref name="player"/> has given in this exchange, while waiting for the others; null until given.
    /// </summary>
    IReadOnlyList<Card>? Gift(int player);

    /// <summary>
    /// The cards <paramref name="player"/> received in this game's exchange, once it has happened; null otherwise.
    /// </summary>
    IReadOnlyList<Card>? Received(int player);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, throws <paramref name="cards"/>, all of one number.
    /// </summary>
    bool TryThrow(int player, IReadOnlyList<Card> cards, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, passes. The round's starter cannot pass.
    /// </summary>
    bool TryPass(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/> gives <paramref name="cards"/> to their exchange partner: the poorer player must give
    /// <see cref="RequiredGift"/>, the richer any cards of theirs, as many as <see cref="ExchangeCount"/>. Nobody gets
    /// any cards until every pair has given; then all the cards are exchanged at once and the poorest player starts.
    /// </summary>
    bool TryGive(int player, IReadOnlyList<Card> cards, out string? error);

    /// <summary>
    /// Marks <paramref name="player"/> as active (playing) or not (they left). An inactive player keeps their cards and
    /// passes automatically.
    /// </summary>
    void SetActive(int player, bool active);

    /// <summary>
    /// Starts the game after game <paramref name="game"/>, once it is over, with the players in <paramref name="order"/>
    /// (usually <see cref="NextOrder"/> without the players who left): deals again for them, and the pairs exchange
    /// cards. If that game has already started, nothing changes.
    /// </summary>
    bool TryNextGame(int game, IReadOnlyList<int> order, out string? error);

    /// <summary>
    /// During the exchange, adds <paramref name="player"/> at <paramref name="place"/> (0 for #1); the players from that
    /// place on move down one. The cards are dealt again for the new number of players, and every pair gives again.
    /// </summary>
    bool TryAddPlayer(int player, int place, out string? error);

    /// <summary>
    /// During the exchange, drops <paramref name="player"/>. The cards are dealt again for the new number of players, and
    /// every pair gives again. At least <see cref="RichManPoorManRules.MinPlayers"/> players must remain.
    /// </summary>
    bool TryRemovePlayer(int player, out string? error);
}
