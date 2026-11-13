using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Yaniv;

/// <summary>
/// Yaniv rules for one game of 2 to 4 players, numbered from 0 in turn order. Players lower their hand score by
/// discarding cards and taking new ones, and call Yaniv with 7 or less.
/// </summary>
public interface IYanivGame
{
    /// <summary>The rules of this game.</summary>
    YanivRules Rules { get; }

    /// <summary>The number of players.</summary>
    int Players { get; }

    /// <summary>The round being played, from 1.</summary>
    int RoundNumber { get; }

    /// <summary>What the game is waiting for.</summary>
    YanivPhase Phase { get; }

    /// <summary>The player to play (after a round, its winner, who starts the next one).</summary>
    int Turn { get; }

    /// <summary>The player who called Yaniv in this round, or null.</summary>
    int? Caller { get; }

    /// <summary>After a Yaniv call, the players still to play their last turn, in order.</summary>
    IReadOnlyList<int> LastTurns { get; }

    /// <summary>The top card of the open pile, the only one that can be taken, or null while it is empty.</summary>
    Card? OpenTop { get; }

    /// <summary>The cards in the open pile.</summary>
    int OpenPileCount { get; }

    /// <summary>The cards in the closed pile.</summary>
    int ClosedPileCount { get; }

    /// <summary>A player's cards (none once they are out).</summary>
    IReadOnlyList<Card> Hand(int player);

    /// <summary>The sum of a player's card values.</summary>
    int HandScore(int player);

    /// <summary>Whether <paramref name="player"/> may call Yaniv now: on their turn, before anyone called it, with 7 or less.</summary>
    bool CanCallYaniv(int player);

    /// <summary>Each player's points.</summary>
    IReadOnlyList<int> Scores { get; }

    /// <summary>Whether a player's points went above the target, so they no longer play.</summary>
    bool IsOut(int player);

    /// <summary>The last turn played in this round, or null.</summary>
    YanivTurn? LastTurn { get; }

    /// <summary>The number of actions so far, so a page can tell a new one.</summary>
    int Actions { get; }

    /// <summary>
    /// The number of times cards have been shuffled in this game (each deal, and each time the open pile becomes a new
    /// closed pile), so a page can show each shuffle.
    /// </summary>
    int Shuffles { get; }

    /// <summary>How the last round ended, or null during the first.</summary>
    YanivRoundResult? LastResult { get; }

    /// <summary>Whether the game has ended.</summary>
    bool IsOver { get; }

    /// <summary>The winner once the game is over, otherwise null.</summary>
    int? Winner { get; }

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, discards <paramref name="cards"/> (one card, a series or a straight)
    /// and takes the open pile's top card (<paramref name="takeOpen"/>) or a card from the closed pile; the cards
    /// discarded then go on the open pile.
    /// </summary>
    bool TryDiscard(int player, IReadOnlyList<Card> cards, bool takeOpen, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, calls Yaniv with a hand score of 7 or less: every other player plays
    /// one last turn, then the hands are revealed.
    /// </summary>
    bool TryCallYaniv(int player, out string? error);

    /// <summary><paramref name="player"/>'s turn is skipped (they are absent): their hand stays as it is.</summary>
    bool TrySkip(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, passes their last turn after a Yaniv call: their hand stays as it is.
    /// Before a Yaniv call, a player cannot pass.
    /// </summary>
    bool TryPass(int player, out string? error);

    /// <summary>Deals the round after round <paramref name="round"/>. If it has already been dealt, nothing changes.</summary>
    bool TryNextRound(int round, out string? error);
}
