namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// The phases of a heist.
/// </summary>
public enum TheGangPhase
{
    /// <summary>Players take chips; the stage ends when everyone holds a chip and is ready.</summary>
    Betting,

    /// <summary>Hands are revealed one at a time, from the lowest red chip to the highest.</summary>
    Reveal,

    /// <summary>All hands are revealed; the heist succeeded or failed.</summary>
    HeistOver,
}

/// <summary>
/// The Gang rules. <c>newDeck</c> supplies a shuffled deck for each heist. With <c>n</c> private cards each (2, or 3 with
/// Security Cameras), player <c>i</c> gets the deck's cards <c>n·i</c> to <c>n·i+n-1</c>; the five shared cards follow.
/// In Advanced mode, the first heist gets the chosen number of challenge cards; each later heist gets one challenge card
/// after a successful heist, or one specialist card after a failed one. A card is used at most once per game, and
/// clashing cards are never drawn for the same heist.
/// </summary>
public class TheGangGame : ITheGangGame
{
    /// <summary>
    /// The fewest players.
    /// </summary>
    public const int MinPlayers = 3;

    /// <summary>
    /// The most players.
    /// </summary>
    public const int MaxPlayers = 6;

    /// <summary>
    /// The number of betting stages.
    /// </summary>
    public const int StageCount = 4;

    /// <summary>
    /// Vaults needed to win, or alarms that lose.
    /// </summary>
    public const int HeistsToFinish = 3;

    private readonly Func<IReadOnlyList<Card>> _newDeck;
    private readonly TheGangRules _rules;
    private readonly Random _random;
    private readonly int?[,] _chips;
    private readonly bool[] _ready;
    private readonly HashSet<string> _usedCards = [];
    private Card[][] _holeCards = [];
    private Card[] _community = [];
    private int[] _revealOrder = [];
    private TheGangCard[] _cards = [];

    /// <summary>
    /// Starts the first heist. <paramref name="rules"/> defaults to the basic game; <paramref name="random"/> draws the cards.
    /// </summary>
    public TheGangGame(int playerCount, Func<IReadOnlyList<Card>> newDeck, TheGangRules? rules = null, Random? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(playerCount, MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(playerCount, MaxPlayers);
        PlayerCount = playerCount;
        _rules = rules ?? TheGangRules.Basic;
        _random = random ?? Random.Shared;
        _newDeck = newDeck;
        _chips = new int?[StageCount, playerCount];
        _ready = new bool[playerCount];
        DrawCards(TheGangCardKind.Challenge, _rules.FirstHeistChallenges);
        Deal();
    }

    /// <inheritdoc />
    public int PlayerCount { get; }

    /// <inheritdoc />
    public TheGangMode Mode => _rules.Mode;

    /// <inheritdoc />
    public IReadOnlyList<TheGangCard> Cards => _cards;

    /// <inheritdoc />
    public IReadOnlyList<int> SkippedStages =>
        [.. new[] { Has(TheGangCards.QuickAccess) ? 0 : -1, Has(TheGangCards.HastyGetaway) ? 2 : -1 }.Where(s => s >= 0)];

    /// <inheritdoc />
    public IReadOnlyList<int> LockedChips =>
        Phase == TheGangPhase.Betting ? [.. Enumerable.Range(1, PlayerCount).Where(chip => IsLocked(Stage, chip))] : [];

    /// <inheritdoc />
    public int Heist { get; private set; } = 1;

    /// <inheritdoc />
    public int Stage { get; private set; }

    /// <inheritdoc />
    public TheGangPhase Phase { get; private set; }

    /// <inheritdoc />
    public int Vaults { get; private set; }

    /// <inheritdoc />
    public int Alarms { get; private set; }

    /// <inheritdoc />
    public bool IsOver => Vaults >= HeistsToFinish || Alarms >= HeistsToFinish;

    /// <inheritdoc />
    public IReadOnlyList<Card> VisibleCommunityCards =>
        Phase == TheGangPhase.Betting ? _community[..(Stage == 0 ? 0 : Stage + 2)] : _community;

    /// <inheritdoc />
    public IReadOnlyList<int> MiddleChips =>
        Enumerable.Range(1, PlayerCount).Where(chip => !Enumerable.Range(0, PlayerCount).Any(p => _chips[Stage, p] == chip)).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<int> RevealOrder => _revealOrder;

    /// <inheritdoc />
    public int Revealed { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<Card> HoleCards(int player) => _holeCards[player];

    /// <inheritdoc />
    public int? Chip(int stage, int player) => _chips[stage, player];

    /// <inheritdoc />
    public bool IsReady(int player) => _ready[player];

    /// <inheritdoc />
    public bool AreChipsDiscarded(int stage) => Has(TheGangCards.Blackout) && stage < Stage;

    /// <inheritdoc />
    public string? Announcement(int player)
    {
        var cards = _holeCards[player];
        if (Has(TheGangCards.Investor))
        {
            var faces = cards.Count(c => c.Rank is >= 11 and <= 13);
            return faces == 1 ? "1 face card" : $"{faces} face cards";
        }

        if (Has(TheGangCards.MathWhiz))
        {
            return $"{cards.Sum(c => c.Rank == Card.Ace ? 11 : Math.Min(c.Rank, 10))} total";
        }

        return null;
    }

    /// <inheritdoc />
    public HandValue Hand(int player) => HandEvaluator.Best([.. _holeCards[player], .. _community]);

    /// <inheritdoc />
    public IReadOnlyList<bool> RevealedInOrder =>
        Enumerable.Range(0, Revealed)
            .Select(i => i == 0 || !Hand(_revealOrder[i]).IsWeakerThan(Hand(_revealOrder[i - 1])))
            .ToArray();

    /// <inheritdoc />
    public bool TryTakeChip(int player, int chip, out string? error)
    {
        if (!TryCheckBetting(player, out error))
        {
            return false;
        }

        if (chip < 1 || chip > PlayerCount)
        {
            error = $"Chips are numbered 1 to {PlayerCount}.";
            return false;
        }

        if (_chips[Stage, player] == chip)
        {
            error = "You already have that chip.";
            return false;
        }

        if (_chips[Stage, player] is { } current && IsLocked(Stage, current))
        {
            error = LockedOwnChip;
            return false;
        }

        if (IsLocked(Stage, chip) && Enumerable.Range(0, PlayerCount).Any(p => _chips[Stage, p] == chip))
        {
            error = "That chip is locked: whoever took it first keeps it.";
            return false;
        }

        for (var other = 0; other < PlayerCount; other++)
        {
            if (_chips[Stage, other] == chip)
            {
                _chips[Stage, other] = null;
            }
        }

        _chips[Stage, player] = chip;
        Array.Clear(_ready);
        return true;
    }

    /// <inheritdoc />
    public bool TryReturnChip(int player, out string? error)
    {
        if (!TryCheckBetting(player, out error))
        {
            return false;
        }

        if (_chips[Stage, player] is not { } chip)
        {
            error = "You have no chip to return.";
            return false;
        }

        if (IsLocked(Stage, chip))
        {
            error = LockedOwnChip;
            return false;
        }

        _chips[Stage, player] = null;
        Array.Clear(_ready);
        return true;
    }

    /// <inheritdoc />
    public bool TrySetReady(int player, bool ready, out string? error)
    {
        if (!TryCheckBetting(player, out error))
        {
            return false;
        }

        if (ready && _chips[Stage, player] is null)
        {
            error = "Take a chip first.";
            return false;
        }

        _ready[player] = ready;
        if (_ready.All(r => r) && MiddleChips.Count == 0)
        {
            EndStage();
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryRevealNext(int revealed, out string? error)
    {
        error = null;
        if (revealed != Revealed)
        {
            return true;
        }

        if (Phase != TheGangPhase.Reveal)
        {
            error = "There is no hand to reveal now.";
            return false;
        }

        Revealed++;
        if (Revealed == PlayerCount)
        {
            Phase = TheGangPhase.HeistOver;
            if (RevealedInOrder.All(inOrder => inOrder))
            {
                Vaults++;
            }
            else
            {
                Alarms++;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryNextHeist(int heist, out string? error)
    {
        error = null;
        if (heist != Heist)
        {
            return true;
        }

        if (Phase != TheGangPhase.HeistOver)
        {
            error = "The heist is not over yet.";
            return false;
        }

        if (IsOver)
        {
            error = "The game is over.";
            return false;
        }

        var succeeded = RevealedInOrder.All(inOrder => inOrder);
        Heist++;
        DrawCards(succeeded ? TheGangCardKind.Challenge : TheGangCardKind.Specialist, 1);
        Deal();
        return true;
    }

    private const string LockedOwnChip = "Your chip is locked: you keep it for this stage.";

    /// <summary>
    /// Whether this heist has the card with this key.
    /// </summary>
    private bool Has(string key) => _cards.Any(c => c.Key == key);

    /// <summary>
    /// Whether a chip of a stage is locked (Noise Sensors: the 1-star chips; Ventilation Shaft: the highest chips; both
    /// only in the white, yellow and orange stages).
    /// </summary>
    private bool IsLocked(int stage, int chip) =>
        stage < StageCount - 1 &&
        ((Has(TheGangCards.NoiseSensors) && chip == 1) || (Has(TheGangCards.VentilationShaft) && chip == PlayerCount));

    /// <summary>
    /// Replaces the heist's cards with up to <paramref name="count"/> random cards of a kind (only in Advanced mode):
    /// enabled, not used before in this game, and not clashing with each other.
    /// </summary>
    private void DrawCards(TheGangCardKind kind, int count)
    {
        var drawn = new List<TheGangCard>();
        var enabled = kind == TheGangCardKind.Challenge ? _rules.Challenges : _rules.Specialists;
        while (Mode == TheGangMode.Advanced && drawn.Count < count)
        {
            var candidates = enabled
                .Where(key => !_usedCards.Contains(key) && !drawn.Any(d => TheGangCards.Clash(d.Key, key)))
                .Select(TheGangCards.Find)
                .OfType<TheGangCard>()
                .Where(card => card.Kind == kind)
                .ToArray();
            if (candidates.Length == 0)
            {
                break;
            }

            var card = candidates[_random.Next(candidates.Length)];
            drawn.Add(card);
            _usedCards.Add(card.Key);
        }

        _cards = [.. drawn];
    }

    /// <summary>
    /// Checks that chips can be taken now, by an existing player.
    /// </summary>
    private bool TryCheckBetting(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        error = Phase == TheGangPhase.Betting ? null : "The betting is over for this heist.";
        return error is null;
    }

    /// <summary>
    /// Moves to the next stage (showing more shared cards), or after the red stage, to revealing the hands.
    /// </summary>
    private void EndStage()
    {
        Array.Clear(_ready);
        if (Stage < StageCount - 1)
        {
            Stage++;
            if (SkippedStages.Contains(Stage))
            {
                Stage++;
            }

            return;
        }

        _revealOrder = Enumerable.Range(0, PlayerCount).OrderBy(p => _chips[Stage, p]).ToArray();
        Phase = TheGangPhase.Reveal;
    }

    /// <summary>
    /// Deals a new heist: two private cards each (three with Security Cameras), five shared cards, no chips taken.
    /// It starts at the first stage that is not skipped.
    /// </summary>
    private void Deal()
    {
        var deck = _newDeck();
        var perPlayer = Has(TheGangCards.SecurityCameras) ? 3 : 2;
        _holeCards = Enumerable.Range(0, PlayerCount).Select(p => deck.Skip(perPlayer * p).Take(perPlayer).ToArray()).ToArray();
        _community = deck.Skip(perPlayer * PlayerCount).Take(5).ToArray();
        Array.Clear(_chips);
        Array.Clear(_ready);
        _revealOrder = [];
        Revealed = 0;
        Stage = SkippedStages.Contains(0) ? 1 : 0;
        Phase = TheGangPhase.Betting;
    }
}
