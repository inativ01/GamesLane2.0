using GamesLane.Server.Games.Tricks;

namespace GamesLane.Server.Games.Spades;

/// <summary>
/// Spades for 4 players in 2 teams of partners sitting across (players 0 and 2, 1 and 3), hand after hand. The deal
/// moves to the left each hand; starting on the dealer's left, each player bids the tricks they expect to take (or Nil
/// with the option, or Blind Nil before looking at their cards). Spades are trumps, and cannot be led until one has been
/// played. A team that takes at least its bid scores 10 a trick bid and 1 a trick over (a bag), and loses 100 for every 10
/// bags; otherwise it loses 10 a trick bid. The first team to 500 wins; a team at -200 loses.
/// </summary>
public interface ISpadesGame : ITrickTakingGame
{
    /// <summary>The rules of this game.</summary>
    SpadesRules Rules { get; }

    /// <summary>What the game is waiting for.</summary>
    SpadesPhase Phase { get; }

    /// <summary>The dealer of this hand.</summary>
    int Dealer { get; }

    /// <summary>The player to bid (while bidding).</summary>
    int Bidder { get; }

    /// <summary>Whether a spade has been played in this hand, so spades can be led.</summary>
    bool SpadesBroken { get; }

    /// <summary>Each team's score.</summary>
    IReadOnlyList<int> Scores { get; }

    /// <summary>Each team's bags (tricks over its bids) not yet penalized.</summary>
    IReadOnlyList<int> Bags { get; }

    /// <summary>How the last hand ended, or null during the first.</summary>
    SpadesHandResult? LastResult { get; }

    /// <summary>The winning team once the game is over, otherwise null.</summary>
    int? Winner { get; }

    /// <summary>A player's bid in this hand, or null before they bid.</summary>
    SpadesBid? BidOf(int player);

    /// <summary>
    /// Whether a player has looked at their cards in this hand. Without Blind Nil, every player has at once; with it, a
    /// player looks when they choose to, or bids.
    /// </summary>
    bool HasLooked(int player);

    /// <summary><paramref name="player"/> looks at their cards, so they can no longer bid Blind Nil.</summary>
    bool TryLook(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is to bid, bids <paramref name="tricks"/> (1 to 13, or 0 for Nil with the
    /// option); <paramref name="blind"/> for a Blind Nil, before looking at their cards. After the fourth bid, the player
    /// on the dealer's left leads.
    /// </summary>
    bool TryBid(int player, int tricks, bool blind, out string? error);
}
