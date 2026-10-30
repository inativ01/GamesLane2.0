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
    /// Basic, or Advanced with challenge and specialist cards.
    /// </summary>
    TheGangMode Mode { get; }

    /// <summary>
    /// The challenge or specialist cards of the current heist (none in Basic mode).
    /// </summary>
    IReadOnlyList<TheGangCard> Cards { get; }

    /// <summary>
    /// The player who got new private cards in this heist (Motion Detector, Laser Tripwires), or null.
    /// </summary>
    int? RedrawnPlayer { get; }

    /// <summary>
    /// The stages the current heist skips (Quick Access: white; Hasty Getaway: orange).
    /// </summary>
    IReadOnlyList<int> SkippedStages { get; }

    /// <summary>
    /// The current stage's locked chips: whoever takes one first keeps it (Noise Sensors, Ventilation Shaft).
    /// </summary>
    IReadOnlyList<int> LockedChips { get; }

    /// <summary>
    /// Whether a stage's chips have been removed and must no longer be shown (Blackout: every stage before the current one).
    /// </summary>
    bool AreChipsDiscarded(int stage);

    /// <summary>
    /// What the current heist's specialist card shows everyone about a player's cards, e.g. "2 face cards" (Investor)
    /// or "17 total" (Math Whiz), or null.
    /// </summary>
    string? Announcement(int player);

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
    /// A player's private cards (two, or three with Security Cameras).
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
    /// A player's best hand from their private cards and the five shared cards.
    /// </summary>
    HandValue Hand(int player);

    /// <summary>
    /// Takes a chip of the current stage, from the middle or from another player. The player's previous chip goes back to
    /// the middle. A locked chip cannot be taken from another player, and a player holding one cannot swap it.
    /// </summary>
    bool TryTakeChip(int player, int chip, out string? error);

    /// <summary>
    /// Puts the player's chip of the current stage back in the middle, unless it is locked.
    /// </summary>
    bool TryReturnChip(int player, out string? error);

    /// <summary>
    /// Marks the player ready (or not). When every player holds a chip and is ready, the stage ends.
    /// </summary>
    bool TrySetReady(int player, bool ready, out string? error);

    /// <summary>
    /// Whether a player has done the specialist card's step before the betting (<see cref="TheGangPhase.Preparing"/>).
    /// </summary>
    bool IsPrepared(int player);

    /// <summary>
    /// Coordinator: the card a player has picked to pass on, or null.
    /// </summary>
    Card? PassingCard(int player);

    /// <summary>
    /// Coordinator: picks the card to pass to the next player (it can be changed until everyone has picked). When
    /// everyone has picked, all cards move at once and the betting starts.
    /// </summary>
    bool TryPassCard(int player, int cardIndex, out string? error);

    /// <summary>
    /// Con Artist: confirms the player has seen their cards. When everyone has, all private cards are shuffled and dealt
    /// again, and the betting starts.
    /// </summary>
    bool TryConfirmSeen(int player, out string? error);

    /// <summary>
    /// Reveals the next hand, if <paramref name="revealed"/> hands have been revealed so far; otherwise nothing changes.
    /// </summary>
    bool TryRevealNext(int revealed, out string? error);

    /// <summary>
    /// Deals the heist after <paramref name="heist"/>, once its hands are revealed; if it was already dealt, nothing changes.
    /// </summary>
    bool TryNextHeist(int heist, out string? error);
}
