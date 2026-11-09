namespace GamesLane.Server.Games.FinalCard;

/// <summary>
/// Final Card rules for one game of 2 to 10 players, numbered from 0 in turn order.
/// </summary>
public interface IFinalCardGame
{
    /// <summary>The rules of this game.</summary>
    FinalCardRules Rules { get; }

    /// <summary>The number of players.</summary>
    int Players { get; }

    /// <summary>The hand being played, from 1 (a pyramid is one long hand).</summary>
    int HandNumber { get; }

    /// <summary>What the game is waiting for.</summary>
    FinalCardPhase Phase { get; }

    /// <summary>The player to play (after a hand, its winner).</summary>
    int Turn { get; }

    /// <summary>1 while play goes up the player numbers, -1 after a Reverse.</summary>
    int Direction { get; }

    /// <summary>The top card of the discard pile.</summary>
    FinalCardCard TopCard { get; }

    /// <summary>The colour to play: the top card's, or the one chosen for a wild card.</summary>
    FinalCardColor Color { get; }

    /// <summary>The cards left in the draw pile.</summary>
    int DrawPileCount { get; }

    /// <summary>With stacking: the cards the player to play must draw, unless they pass them on; otherwise 0.</summary>
    int PendingDraw { get; }

    /// <summary>The card the player to play has just drawn and may play, or null if they have not drawn.</summary>
    FinalCardCard? DrawnCard { get; }

    /// <summary>A player's cards.</summary>
    IReadOnlyList<FinalCardCard> Hand(int player);

    /// <summary>The cards <paramref name="player"/> can play now (none unless it is their turn).</summary>
    IReadOnlyList<FinalCardCard> Playable(int player);

    /// <summary>Each player's points, in a game to a score target.</summary>
    IReadOnlyList<int> Scores { get; }

    /// <summary>Pyramid: the number of cards a player was last dealt (7 at first).</summary>
    int HandSize(int player);

    /// <summary>Whether a player has called Final Card for their last card, which protects them while they hold it.</summary>
    bool CalledFinalCard(int player);

    /// <summary>Whether a player holds one card without having called Final Card, so another player can catch them.</summary>
    bool CanBeCaught(int player);

    /// <summary>Pyramid: the players who have finished, in order; at the end, everyone.</summary>
    IReadOnlyList<int> Ranking { get; }

    /// <summary>What happened in the last action.</summary>
    IReadOnlyList<FinalCardEvent> Events { get; }

    /// <summary>The number of actions so far, so a page can tell a new one.</summary>
    int Actions { get; }

    /// <summary>How the last hand ended, or null during the first.</summary>
    FinalCardHandResult? LastResult { get; }

    /// <summary>Whether the game has ended.</summary>
    bool IsOver { get; }

    /// <summary>The winner once the game is over, otherwise null.</summary>
    int? Winner { get; }

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, plays <paramref name="card"/>, choosing <paramref name="color"/> for a
    /// wild card.
    /// </summary>
    bool TryPlay(int player, FinalCardCard card, FinalCardColor? color, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, draws: the cards to draw from a stack, or else one card (or, with the
    /// option, cards until one can be played). If the card drawn can be played they may play it, otherwise the turn passes.
    /// </summary>
    bool TryDraw(int player, out string? error);

    /// <summary><paramref name="player"/> keeps the card they drew, which ends their turn.</summary>
    bool TryPass(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/> calls Final Card, at any time during a hand. It protects them if they hold one card, or two on
    /// their turn with one they can play, and are not protected yet; and every other player who can be caught (see
    /// <see cref="CanBeCaught"/>) draws 2 cards. Refused when it does neither.
    /// </summary>
    bool TryCallFinalCard(int player, out string? error);

    /// <summary>Deals the hand after hand <paramref name="hand"/>. If it has already been dealt, nothing changes.</summary>
    bool TryNextHand(int hand, out string? error);
}
