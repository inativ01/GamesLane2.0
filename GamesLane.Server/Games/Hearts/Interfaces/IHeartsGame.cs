using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.Tricks;

namespace GamesLane.Server.Games.Hearts;

/// <summary>
/// Hearts for 4 players, hand after hand. Before each hand's play, every player passes 3 cards (to the left, the right,
/// across, or in every fourth hand not at all). The holder of the 2♣ leads it. Each heart taken scores 1 point and the
/// queen of spades 13; a player who takes all 26 shoots the moon, and every other player scores 26 instead. Once a
/// player reaches 100 points, the lowest score wins.
/// </summary>
public interface IHeartsGame : ITrickTakingGame
{
    /// <summary>What the game is waiting for.</summary>
    HeartsPhase Phase { get; }

    /// <summary>Where the cards are passed in this hand.</summary>
    HeartsPass Pass { get; }

    /// <summary>Whether a heart has been played in this hand, so hearts can be led.</summary>
    bool HeartsBroken { get; }

    /// <summary>Each player's score.</summary>
    IReadOnlyList<int> Scores { get; }

    /// <summary>How the last hand ended, or null during the first.</summary>
    HeartsHandResult? LastResult { get; }

    /// <summary>The players with the lowest score once the game is over (more than one on a tie), otherwise empty.</summary>
    IReadOnlyList<int> Winners { get; }

    /// <summary>The points a player has taken in this hand so far.</summary>
    int HandPoints(int player);

    /// <summary>The cards a player chose to pass in this hand, or null while they have not.</summary>
    IReadOnlyList<Card>? Passed(int player);

    /// <summary>The cards a player received in this hand's pass (empty before the pass, and with Hold).</summary>
    IReadOnlyList<Card> Received(int player);

    /// <summary>
    /// <paramref name="player"/> passes <paramref name="cards"/> (3 of their cards). Once every player has, each gets the
    /// cards passed to them, and the holder of the 2♣ leads.
    /// </summary>
    bool TryPass(int player, IReadOnlyList<Card> cards, out string? error);
}
