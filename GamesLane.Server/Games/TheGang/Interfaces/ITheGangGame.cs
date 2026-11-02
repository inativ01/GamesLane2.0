namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// The Gang: a cooperative game of Texas Hold'em hands. Players (numbered from 0) try to rank their hands, from weakest
/// to strongest, by taking numbered chips over four betting stages, without telling each other their cards.
/// Three successful heists win the game; three failed heists lose it.
/// </summary>
public interface ITheGangGame
{
    /// <summary>
    /// The number of players (3 to 8). Chips are numbered 1 to this number.
    /// </summary>
    int PlayerCount { get; }

    /// <summary>
    /// Basic, or Advanced with challenge and specialist cards.
    /// </summary>
    TheGangMode Mode { get; }

    /// <summary>
    /// The challenge and specialist cards in play, in the order they were added (none in Basic mode). Cards stay in play
    /// for the rest of the game.
    /// </summary>
    IReadOnlyList<TheGangCard> Cards { get; }

    /// <summary>
    /// The keys of the cards added for the current heist.
    /// </summary>
    IReadOnlyList<string> NewCards { get; }

    /// <summary>
    /// The step every player must do before the betting: <see cref="TheGangGame.JokersStep"/> (swapping the jokers they
    /// were dealt), or a specialist card's (Coordinator or Con Artist); or null.
    /// </summary>
    string? PreparingCard { get; }

    /// <summary>
    /// The player who got new pocket cards in this heist (Motion Detector, Laser Tripwires), or null. Their white chip
    /// no longer says anything about their hand.
    /// </summary>
    int? RedrawnPlayer { get; }

    /// <summary>
    /// Whether <see cref="RedrawnPlayer"/> has confirmed they got new pocket cards. The stage cannot end until they have.
    /// </summary>
    bool IsRedrawConfirmed { get; }

    /// <summary>
    /// Motion Detector, Laser Tripwires: <see cref="RedrawnPlayer"/> confirms they got new pocket cards.
    /// </summary>
    bool TryConfirmRedraw(int player, out string? error);

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
    /// What the specialist cards in play show everyone about a player's pocket cards when the betting starts, e.g.
    /// "2 face cards" (Investor) or "17 total" (Math Whiz), or null.
    /// </summary>
    string? Announcement(int player);

    /// <summary>
    /// The same as <see cref="Announcement"/>, one fact per card (Investor first, then Math Whiz). Empty if there are none.
    /// </summary>
    IReadOnlyList<TheGangFact> Facts(int player);

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
    /// The shared slots everyone can see: none, then 3, 4 and 5 as the stages go on. A slot holds one card, or with
    /// jokers, the 2 or 3 cards that replaced a joker, of which each hand uses one.
    /// </summary>
    IReadOnlyList<IReadOnlyList<Card>> VisibleCommunity { get; }

    /// <summary>
    /// All the cards in <see cref="VisibleCommunity"/>.
    /// </summary>
    IReadOnlyList<Card> VisibleCommunityCards { get; }

    /// <summary>
    /// A player's pocket cards (two, or three with Security Cameras). A joker is kept in place until the player has
    /// swapped it.
    /// </summary>
    IReadOnlyList<Card> HoleCards(int player);

    /// <summary>
    /// With jokers: the two cards a player is offered for a joker in their hand, or null if they have none to swap.
    /// </summary>
    IReadOnlyList<Card>? JokerOffer(int player);

    /// <summary>
    /// With jokers: how many jokers a player has swapped in this heist (everyone sees this, not the cards).
    /// </summary>
    int JokerSwaps(int player);

    /// <summary>
    /// With jokers: the player picks card <paramref name="index"/> (0 or 1) of <see cref="JokerOffer"/>. A normal card
    /// replaces the joker in their hand and the other is discarded; an offered joker is swapped for two more cards.
    /// </summary>
    bool TryChooseJokerCard(int player, int index, out string? error);

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
    /// For the revealed hand at <paramref name="index"/> (in reveal order), the first reason it is weaker than the hand
    /// before it, or null if it is not.
    /// </summary>
    TheGangWeakness? Weakness(int index);

    /// <summary>
    /// A player's best hand from their pocket cards and the five shared slots (one card from each).
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
    /// Marks the player ready (or not), together with the other players of the same user (a user is ready once).
    /// Players can only be ready once every player holds a chip. When everyone is ready, the stage ends.
    /// </summary>
    bool TrySetReady(int player, bool ready, out string? error);

    /// <summary>
    /// Whether a player has done the specialist card's step (<see cref="TheGangPhase.Preparing"/>).
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
    /// Con Artist: confirms the player, and the other players of the same user, have seen their cards. When everyone
    /// has, all pocket cards are shuffled and dealt again, and the betting starts.
    /// </summary>
    bool TryConfirmSeen(int player, out string? error);

    /// <summary>
    /// The group decisions of the heist's cards (Informant, Getaway Driver, Mastermind, Hacker, Jack, Muscle, and the scans).
    /// </summary>
    IReadOnlyList<TheGangDecisionInfo> Decisions { get; }

    /// <summary>
    /// What the cards showed everyone so far (Getaway Driver, Mastermind, the scans' guesses).
    /// </summary>
    IReadOnlyList<TheGangResult> Results { get; }

    /// <summary>
    /// The player who must take a private step (show or discard a card) before the stage can end, or null.
    /// </summary>
    int? PendingPlayer { get; }

    /// <summary>
    /// The step <see cref="PendingPlayer"/> must take.
    /// </summary>
    TheGangStep PendingStep { get; }

    /// <summary>
    /// Informant: who showed a card to whom, or null.
    /// </summary>
    (int From, int To)? ShownBetween { get; }

    /// <summary>
    /// Informant: the card shown to <paramref name="player"/>, or null.
    /// </summary>
    Card? ShownCardFor(int player);

    /// <summary>
    /// Once the heist is over, whether it succeeded (hands in order and no wrong scan guess); null before.
    /// </summary>
    bool? Succeeded { get; }

    /// <summary>
    /// Proposes a choice for a card's group decision: a player (<paramref name="target"/>) and/or a value (a card value
    /// such as "9" or a hand ranking such as "Pair"). Clears everyone's agreement.
    /// </summary>
    bool TryPropose(int player, string card, int? target, string? value, out string? error);

    /// <summary>
    /// Agrees (or no longer agrees) to the proposal, for all the voting players of the same user. Once every voter
    /// agrees, the card takes effect.
    /// </summary>
    bool TryAgree(int player, string card, bool agree, out string? error);

    /// <summary>
    /// Informant: the chosen player secretly shows their card number <paramref name="cardIndex"/> to player <paramref name="to"/>.
    /// </summary>
    bool TryShowCard(int player, int cardIndex, int to, out string? error);

    /// <summary>
    /// Hacker, Jack: the chosen player discards their card number <paramref name="cardIndex"/>.
    /// </summary>
    bool TryDiscard(int player, int cardIndex, out string? error);

    /// <summary>
    /// Reveals the next hand, if <paramref name="revealed"/> hands have been revealed so far; otherwise nothing changes.
    /// Before the strongest hand, the scans' guesses must be agreed.
    /// </summary>
    bool TryRevealNext(int revealed, out string? error);

    /// <summary>
    /// Deals the heist after <paramref name="heist"/>, once its hands are revealed; if it was already dealt, nothing changes.
    /// </summary>
    bool TryNextHeist(int heist, out string? error);
}
