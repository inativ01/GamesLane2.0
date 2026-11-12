using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Tricks;

/// <summary>
/// A trick-taking game for 4 players, numbered from 0 in turn order (play goes to the left, clockwise), with the 52-card
/// deck, 13 cards each, hand after hand. In a trick, each player in turn plays a card, following the suit led if they
/// can; the highest trump, or else the highest card of the suit led, wins the trick, and its winner leads the next one.
/// </summary>
public interface ITrickTakingGame
{
    /// <summary>The number of players.</summary>
    int Players { get; }

    /// <summary>The hand being played, from 1.</summary>
    int HandNumber { get; }

    /// <summary>The player to play a card (while <see cref="IsPlaying"/>).</summary>
    int Turn { get; }

    /// <summary>Whether the players are playing cards to tricks (not, e.g., while passing or bidding, or between hands).</summary>
    bool IsPlaying { get; }

    /// <summary>The cards played to the trick in progress, in order (empty between tricks).</summary>
    IReadOnlyList<TrickCard> Trick { get; }

    /// <summary>The last trick completed in this hand, or null.</summary>
    CompletedTrick? LastTrick { get; }

    /// <summary>The tricks each player has won in this hand.</summary>
    IReadOnlyList<int> TricksWon { get; }

    /// <summary>The player who claimed the remaining tricks of this hand, or null.</summary>
    int? Claimer { get; }

    /// <summary>The cards the claimer held when they claimed (empty without a claim).</summary>
    IReadOnlyList<Card> ClaimedCards { get; }

    /// <summary>The number of actions so far, so a page can tell a new one.</summary>
    int Actions { get; }

    /// <summary>Whether the game has ended.</summary>
    bool IsOver { get; }

    /// <summary>The players whose action the game needs now: the player to play, or those who have not yet done what all must do.</summary>
    IReadOnlyList<int> AwaitedPlayers { get; }

    /// <summary>A player's cards, in hand order (see <see cref="TrickCards.Sorted"/>).</summary>
    IReadOnlyList<Card> Hand(int player);

    /// <summary>The cards a player won in tricks in this hand.</summary>
    IReadOnlyList<Card> Won(int player);

    /// <summary>The cards <paramref name="player"/> may play now: none unless it is their turn to play a card.</summary>
    IReadOnlyList<Card> Playable(int player);

    /// <summary><paramref name="player"/>, whose turn it is, plays <paramref name="card"/> to the trick.</summary>
    bool TryPlay(int player, Card card, out string? error);

    /// <summary>
    /// Whether <paramref name="player"/> may claim the remaining tricks now: it is their turn to lead, and from their
    /// own cards and those already played, their cards win every remaining trick whatever anyone plays.
    /// </summary>
    bool CanClaim(int player);

    /// <summary>
    /// <paramref name="player"/> claims the remaining tricks (see <see cref="CanClaim"/>): they take every card still in
    /// a hand, and the hand is scored as if those tricks were played.
    /// </summary>
    bool TryClaim(int player, out string? error);

    /// <summary>Deals the hand after hand <paramref name="hand"/>. If it has already been dealt, nothing changes.</summary>
    bool TryNextHand(int hand, out string? error);
}
