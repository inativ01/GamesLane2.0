namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// The cards where the group decides together. Any voter proposes (a player, a value, or both); everyone sees it; once
/// every voter agrees, the card takes effect. Changing the proposal clears the agreement. Each user votes once: agreeing
/// for one seat agrees for all of that user's seats. These specialist cards are optional: the group may use any of them,
/// in any order, at any time during the betting, each at most once per heist; unused ones do not hold up the heist. The
/// scans (Retina Scan, Fingerprint Scan) are decided by everyone except the strongest hand's player, just before that
/// hand is revealed; they are skipped if the hands are already out of order, since the heist fails anyway.
/// </summary>
public partial class TheGangGame
{
    private Dictionary<string, Decision> _decisions = [];
    private List<TheGangResult> _results = [];
    private (int From, Card Card, int To)? _shown;
    private int? _muscle;
    private bool _scanFailed;

    /// <inheritdoc />
    public IReadOnlyList<TheGangDecisionInfo> Decisions => [.. _decisions.Values.Select(d => new TheGangDecisionInfo(
        d.Card.Key, d.Card.Choice, d.Player, d.Value, Voters(d), [.. d.Agreed.Order()], IsOpen(d), d.Done, IsSkipped(d)))];

    /// <inheritdoc />
    public IReadOnlyList<TheGangResult> Results => _results;

    /// <inheritdoc />
    public int? PendingPlayer { get; private set; }

    /// <inheritdoc />
    public TheGangStep PendingStep { get; private set; }

    /// <inheritdoc />
    public (int From, int To)? ShownBetween => _shown is { } s ? (s.From, s.To) : null;

    /// <inheritdoc />
    public Card? ShownCardFor(int player) => _shown is { } s && s.To == player ? s.Card : null;

    /// <inheritdoc />
    public bool? Succeeded => Phase == TheGangPhase.HeistOver ? HeistSucceeds : null;

    /// <inheritdoc />
    public bool TryPropose(int player, string card, int? target, string? value, out string? error)
    {
        if (!TryGetOpenDecision(player, card, out var decision, out error))
        {
            return false;
        }

        // A proposal may be partial (e.g. Mastermind's player before its value); agreeing needs a complete one.
        var choice = decision.Card.Choice;
        var needsPlayer = choice is TheGangChoice.Player or TheGangChoice.PlayerAndRank;
        var needsValue = choice is not TheGangChoice.Player;
        var valueError = choice == TheGangChoice.Ranking ? "Choose a hand ranking." : "Choose a card value.";
        error = target is not null && !needsPlayer ? "This card does not choose a player."
            : value is not null && !needsValue ? "This card does not choose a value."
            : target is { } t && (t < 0 || t >= PlayerCount) ? "Choose a player."
            : value is not null && !TheGangValues.IsValid(choice, value) ? valueError
            : target is null && value is null ? (needsPlayer ? "Choose a player." : valueError)
            : null;
        if (error is not null)
        {
            return false;
        }

        (decision.Player, decision.Value) = (target, value);
        decision.Agreed.Clear();
        decision.Proposed = (!needsPlayer || target is not null) && (!needsValue || value is not null);
        return true;
    }

    /// <inheritdoc />
    public bool TryAgree(int player, string card, bool agree, out string? error)
    {
        if (!TryGetOpenDecision(player, card, out var decision, out error))
        {
            return false;
        }

        if (!decision.Proposed)
        {
            error = "Complete the proposal first.";
            return false;
        }

        var usersSeats = Voters(decision).Where(p => _sameHolder(p, player)).ToArray();
        if (!agree)
        {
            decision.Agreed.ExceptWith(usersSeats);
            return true;
        }

        decision.Agreed.UnionWith(usersSeats);
        if (Voters(decision).All(decision.Agreed.Contains))
        {
            Carry(decision);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryShowCard(int player, int cardIndex, int to, out string? error)
    {
        if (!TryCheckStep(player, TheGangStep.ShowCard, cardIndex, out error))
        {
            return false;
        }

        if (to < 0 || to >= PlayerCount || to == player)
        {
            error = "Choose another player to show the card to.";
            return false;
        }

        _shown = (player, _holeCards[player][cardIndex], to);
        FinishStep();
        return true;
    }

    /// <inheritdoc />
    public bool TryDiscard(int player, int cardIndex, out string? error)
    {
        if (!TryCheckStep(player, TheGangStep.Discard, cardIndex, out error))
        {
            return false;
        }

        _holeCards[player] = [.. _holeCards[player].Where((_, i) => i != cardIndex)];
        FinishStep();
        return true;
    }

    /// <summary>
    /// Whether the heist succeeds so far: every revealed hand is in order and no scan guess was wrong.
    /// </summary>
    private bool HeistSucceeds => RevealedInOrder.All(inOrder => inOrder) && !_scanFailed;

    /// <summary>
    /// Whether the scans' guesses must be agreed before the next (strongest) hand can be revealed. Not if the heist
    /// already fails because the hands are out of order (including the strongest hand, not yet revealed).
    /// </summary>
    private bool AwaitsScan => _decisions.Values.Any(d => IsScan(d) && IsOpen(d));

    /// <summary>
    /// Whether every hand, revealed or not, is at least as strong as the one before it in the reveal order.
    /// </summary>
    private bool AllHandsInOrder =>
        _revealOrder.Length > 0 && Enumerable.Range(1, _revealOrder.Length - 1).All(i => !IsWeaker(_revealOrder[i], _revealOrder[i - 1]));

    /// <summary>
    /// Whether a scan is not needed: the hands are revealed and already out of order, so the heist fails anyway.
    /// </summary>
    private bool IsSkipped(Decision decision) =>
        IsScan(decision) && !decision.Done && Phase is (TheGangPhase.Reveal or TheGangPhase.HeistOver) && !AllHandsInOrder;

    /// <summary>
    /// Whether <paramref name="weaker"/>'s hand is weaker than <paramref name="stronger"/>'s. With Muscle, the chosen
    /// player's hand beats every other hand of the same ranking.
    /// </summary>
    private bool IsWeaker(int weaker, int stronger)
    {
        var (a, b) = (Hand(weaker), Hand(stronger));
        if (_muscle is { } muscle && (weaker == muscle || stronger == muscle) && HandRankings.Of(a) == HandRankings.Of(b))
        {
            return stronger == muscle;
        }

        return a.IsWeakerThan(b);
    }

    /// <summary>
    /// Sets up the decisions of the heist's cards; called when a heist is dealt.
    /// </summary>
    private void ResetDecisions()
    {
        _decisions = _cards.Where(c => c.Choice != TheGangChoice.None).ToDictionary(c => c.Key, c => new Decision(c));
        _results = [];
        _shown = null;
        _muscle = null;
        _scanFailed = false;
        PendingPlayer = null;
        PendingStep = TheGangStep.None;
    }

    private static bool IsScan(Decision decision) => decision.Card.Kind == TheGangCardKind.Challenge;

    /// <summary>
    /// The players who decide: everyone, or for a scan, everyone except the players of the user who holds the strongest
    /// hand (they can see it on their screen). If that user plays every seat, only the strongest hand's player is left out.
    /// </summary>
    private IReadOnlyList<int> Voters(Decision decision)
    {
        var everyone = Enumerable.Range(0, PlayerCount).ToArray();
        if (!IsScan(decision) || _revealOrder.Length == 0)
        {
            return everyone;
        }

        var strongest = _revealOrder[^1];
        var others = everyone.Where(p => !_sameHolder(p, strongest)).ToArray();
        return others.Length > 0 ? others : [.. everyone.Where(p => p != strongest)];
    }

    /// <summary>
    /// Whether players can propose and agree now: specialist cards during the betting (unless a chosen player still has a
    /// step to take), scans just before the strongest hand is revealed.
    /// </summary>
    private bool IsOpen(Decision decision) => !decision.Done && (IsScan(decision)
        ? Phase == TheGangPhase.Reveal && Revealed == PlayerCount - 1 && AllHandsInOrder
        : Phase == TheGangPhase.Betting && PendingStep == TheGangStep.None);

    private bool TryGetOpenDecision(int player, string card, out Decision decision, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        decision = _decisions.GetValueOrDefault(card)!;
        error = decision is null || !IsOpen(decision) ? "That cannot be decided now."
            : !Voters(decision).Contains(player) ? "You do not take part in this decision."
            : null;
        return error is null;
    }

    /// <summary>
    /// Carries out an agreed decision.
    /// </summary>
    private void Carry(Decision decision)
    {
        decision.Done = true;
        // The chosen player, or for a scan the strongest hand's player.
        var player = decision.Player ?? (_revealOrder.Length > 0 ? _revealOrder[^1] : -1);
        switch (decision.Card.Key)
        {
            case TheGangCards.Informant:
                (PendingPlayer, PendingStep) = (player, TheGangStep.ShowCard);
                break;
            case TheGangCards.GetawayDriver:
                _results.Add(new(player, decision.Card.Key, HandRankings.OfCards([.. _holeCards[player], .. VisibleCommunityCards])));
                break;
            case TheGangCards.Mastermind:
                var count = _holeCards[player].Count(c => c.Rank == TheGangValues.RankOf(decision.Value));
                _results.Add(new(player, decision.Card.Key, count switch
                {
                    0 => $"no {decision.Value}s",
                    1 => $"one {decision.Value}",
                    _ => $"{count} {decision.Value}s",
                }));
                break;
            case TheGangCards.Hacker:
                _holeCards[player] = [.. _holeCards[player], _deck[_nextCard++]];
                (PendingPlayer, PendingStep) = (player, TheGangStep.Discard);
                break;
            case TheGangCards.Jack:
                _holeCards[player] = [.. _holeCards[player], Card.SuitlessJack];
                (PendingPlayer, PendingStep) = (player, TheGangStep.Discard);
                break;
            case TheGangCards.Muscle:
                _muscle = player;
                break;
            case TheGangCards.RetinaScan or TheGangCards.FingerprintScan:
                var right = decision.Card.Key == TheGangCards.RetinaScan
                    ? _holeCards[player].Any(c => c.Rank == TheGangValues.RankOf(decision.Value))
                    : HandRankings.Of(Hand(player)) == decision.Value;
                _scanFailed |= !right;
                _results.Add(new(player, decision.Card.Key, $"guessed {decision.Value}: {(right ? "right" : "wrong")}", right));
                break;
        }
    }

    private bool TryCheckStep(int player, TheGangStep step, int cardIndex, out string? error)
    {
        error = PendingPlayer != player || PendingStep != step ? "That is not possible now."
            : cardIndex < 0 || cardIndex >= _holeCards[player].Length ? "Pick one of your cards."
            : null;
        return error is null;
    }

    /// <summary>
    /// Ends the chosen player's step; the stage ends now if it was only waiting for this.
    /// </summary>
    private void FinishStep()
    {
        (PendingPlayer, PendingStep) = (null, TheGangStep.None);
        if (Phase == TheGangPhase.Betting && _ready.All(r => r) && MiddleChips.Count == 0)
        {
            EndStage();
        }
    }

    /// <summary>
    /// A card's group decision while it is being made.
    /// </summary>
    private sealed class Decision(TheGangCard card)
    {
        public TheGangCard Card { get; } = card;

        public int? Player { get; set; }

        public string? Value { get; set; }

        public bool Proposed { get; set; }

        public HashSet<int> Agreed { get; } = [];

        public bool Done { get; set; }
    }
}
