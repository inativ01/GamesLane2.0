namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// The Gang: a cooperative game of Texas Hold'em hands. Players (numbered from 0) try to rank their hands, from weakest
/// to strongest, by taking numbered chips over four betting stages, without telling each other their cards.
/// Three successful heists win the game; three failed heists lose it.
/// </summary>
public interface ITheGangGame
{
    /// <summary>
    /// The number of players (3 to 6). Chips are numbered 1 to this number.
    /// </summary>
    int PlayerCount { get; }

    /// <summary>
    /// The current heist, starting at 1.
    /// </summary>
    int Heist { get; }

    /// <summary>
    /// The betting stage (0 to 3: white, yellow, orange, red). After the betting, it stays at 3.
    /// </summary>
    int Stage { get; }

    /// <summary>
    /// Betting, revealing hands, or the heist's result.
    /// </summary>
    TheGangPhase Phase { get; }

    /// <summary>
    /// Successful heists.
    /// </summary>
    int Vaults { get; }

    /// <summary>
    /// Failed heists.
    /// </summary>
    int Alarms { get; }

    /// <summary>
    /// Whether the game has been won or lost.
    /// </summary>
    bool IsOver { get; }

    /// <summary>
    /// The shared cards everyone can see: none, then 3, 4 and 5 as the stages go on.
    /// </summary>
    IReadOnlyList<Card> VisibleCommunityCards { get; }

    /// <summary>
    /// A player's two private cards.
    /// </summary>
    IReadOnlyList<Card> HoleCards(int player);

    /// <summary>
    /// The chip a player holds in a stage, or null.
    /// </summary>
    int? Chip(int stage, int player);

    /// <summary>
    /// The current stage's chips that nobody holds.
    /// </summary>
    IReadOnlyList<int> MiddleChips { get; }

    /// <summary>
    /// Whether a player is ready to end the current stage.
    /// </summary>
    bool IsReady(int player);

    /// <summary>
    /// The players in the order their hands are revealed (by red chip). Empty while betting.
    /// </summary>
    IReadOnlyList<int> RevealOrder { get; }

    /// <summary>
    /// How many hands have been revealed.
    /// </summary>
    int Revealed { get; }

    /// <summary>
    /// For each revealed hand (in reveal order), whether it is at least as strong as the one revealed before it.
    /// The heist succeeds if all of them are.
    /// </summary>
    IReadOnlyList<bool> RevealedInOrder { get; }

    /// <summary>
    /// A player's best hand from their two cards and the five shared cards.
    /// </summary>
    HandValue Hand(int player);

    /// <summary>
    /// Takes a chip of the current stage, from the middle or from another player. The player's previous chip goes back to the middle.
    /// </summary>
    bool TryTakeChip(int player, int chip, out string? error);

    /// <summary>
    /// Puts the player's chip of the current stage back in the middle.
    /// </summary>
    bool TryReturnChip(int player, out string? error);

    /// <summary>
    /// Marks the player ready (or not). When every player holds a chip and is ready, the stage ends.
    /// </summary>
    bool TrySetReady(int player, bool ready, out string? error);

    /// <summary>
    /// Reveals the next hand, if <paramref name="revealed"/> hands have been revealed so far; otherwise nothing changes.
    /// </summary>
    bool TryRevealNext(int revealed, out string? error);

    /// <summary>
    /// Deals the heist after <paramref name="heist"/>, once its hands are revealed; if it was already dealt, nothing changes.
    /// </summary>
    bool TryNextHeist(int heist, out string? error);
}
