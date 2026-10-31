namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// The phases of a heist.
/// </summary>
public enum TheGangPhase
{
    /// <summary>Players take chips; the stage ends when everyone holds a chip and is ready.</summary>
    Betting,

    /// <summary>
    /// Before the betting, every player does the step of a specialist card in play: Coordinator (pick a card to pass
    /// on), then Con Artist (confirm they have seen their cards).
    /// </summary>
    Preparing,

    /// <summary>Hands are revealed one at a time, from the lowest red chip to the highest.</summary>
    Reveal,

    /// <summary>All hands are revealed; the heist succeeded or failed.</summary>
    HeistOver,
}

/// <summary>
/// The Gang rules. <c>newDeck</c> supplies a shuffled deck for each heist. With <c>n</c> private cards each (2, or 3 with
/// Security Cameras), player <c>i</c> gets the deck's cards <c>n·i</c> to <c>n·i+n-1</c>; the five shared cards follow.
/// In Advanced mode, the first heist gets the chosen number of challenge cards; after a successful heist, one challenge
/// card is added, and after a failed one, one specialist card. Cards stay in play for the rest of the game; each card is
/// drawn at most once. Cards that cancel each other may be in play together (the cancelled card has no effect) unless
/// the rules avoid clashes.
/// </summary>
public partial class TheGangGame : ITheGangGame
{
    /// <summary>
    /// The fewest players.
    /// </summary>
    public const int MinPlayers = 3;

    /// <summary>
    /// The most players.
    /// </summary>
    public const int MaxPlayers = 8;

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
    private readonly Func<int, int, bool> _sameHolder;
    private readonly int?[,] _chips;
    private readonly bool[] _ready;
    private readonly HashSet<string> _usedCards = [];
    private Card[][] _holeCards = [];
    private Card[] _community = [];
    private int[] _revealOrder = [];
    private TheGangCard[] _cards = [];
    private IReadOnlyList<Card> _deck = [];
    private int _nextCard;
    private Card?[] _passing = [];
    private bool[] _prepared = [];
    private string[] _newCards = [];
    private string?[] _announcements = [];

    /// <summary>
    /// Starts the first heist. <paramref name="rules"/> defaults to the basic game; <paramref name="random"/> draws the cards.
    /// <paramref name="sameHolder"/> tells whether two players are played by the same user (one screen), so a user who
    /// sees the strongest hand does not take part in the scans' guesses; by default, every player is a different user.
    /// </summary>
    public TheGangGame(
        int playerCount,
        Func<IReadOnlyList<Card>> newDeck,
        TheGangRules? rules = null,
        Random? random = null,
        Func<int, int, bool>? sameHolder = null)
    {
        _sameHolder = sameHolder ?? ((a, b) => a == b);
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
    public IReadOnlyList<string> NewCards => _newCards;

    /// <inheritdoc />
    public string? PreparingCard { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<TheGangCard> Cards => _cards;

    /// <inheritdoc />
    public int? RedrawnPlayer { get; private set; }

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
        Phase is TheGangPhase.Reveal or TheGangPhase.HeistOver ? _community : _community[..(Stage == 0 ? 0 : Stage + 2)];

    /// <inheritdoc />
    public bool IsPrepared(int player) => Phase == TheGangPhase.Preparing && _prepared[player];

    /// <inheritdoc />
    public Card? PassingCard(int player) => Phase == TheGangPhase.Preparing ? _passing[player] : null;

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
    public string? Announcement(int player) => _announcements.ElementAtOrDefault(player);

    /// <inheritdoc />
    public HandValue Hand(int player) => HandEvaluator.Best([.. _holeCards[player], .. _community]);

    /// <inheritdoc />
    public IReadOnlyList<bool> RevealedInOrder =>
        Enumerable.Range(0, Revealed)
            .Select(i => i == 0 || !IsWeaker(_revealOrder[i], _revealOrder[i - 1]))
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

        // Ready only once every player holds a chip (any chip change clears it); each user is ready once, for all of
        // their players.
        if (ready && MiddleChips.Count > 0)
        {
            error = "Wait until everyone has a chip.";
            return false;
        }

        foreach (var p in Enumerable.Range(0, PlayerCount).Where(p => _sameHolder(p, player)))
        {
            _ready[p] = ready;
        }

        if (_ready.All(r => r) && PendingStep == TheGangStep.None)
        {
            EndStage();
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryPassCard(int player, int cardIndex, out string? error)
    {
        if (!TryCheckPreparing(player, TheGangCards.Coordinator, out error))
        {
            return false;
        }

        if (cardIndex < 0 || cardIndex >= _holeCards[player].Length)
        {
            error = "Pick one of your cards.";
            return false;
        }

        _passing[player] = _holeCards[player][cardIndex];
        _prepared[player] = true;
        if (_prepared.All(p => p))
        {
            // Everyone passes their chosen card to the next player at the same time.
            var passed = _passing.Select(c => c!.Value).ToArray();
            for (var p = 0; p < PlayerCount; p++)
            {
                _holeCards[p] = [.. _holeCards[p].Where(c => c != passed[p]), passed[(p + PlayerCount - 1) % PlayerCount]];
            }

            FinishPreparing();
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryConfirmSeen(int player, out string? error)
    {
        if (!TryCheckPreparing(player, TheGangCards.ConArtist, out error))
        {
            return false;
        }

        // Each user confirms once, for all of their players.
        foreach (var p in Enumerable.Range(0, PlayerCount).Where(p => _sameHolder(p, player)))
        {
            _prepared[p] = true;
        }

        if (_prepared.All(p => p))
        {
            // All private cards are gathered, shuffled and dealt again, the same number to each player.
            var gathered = _holeCards.SelectMany(cards => cards).ToArray();
            _random.Shuffle(gathered);
            var perPlayer = _holeCards[0].Length;
            _holeCards = [.. Enumerable.Range(0, PlayerCount).Select(p => gathered.Skip(perPlayer * p).Take(perPlayer).ToArray())];
            FinishPreparing();
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

        if (AwaitsScan)
        {
            error = "First agree on the guess about the strongest hand.";
            return false;
        }

        Revealed++;
        if (Revealed == PlayerCount)
        {
            Phase = TheGangPhase.HeistOver;
            if (HeistSucceeds)
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

        var succeeded = HeistSucceeds;
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
    /// When the first 3 shared cards appear: with Motion Detector, if any of them is a J, Q or K, the holder of the
    /// 1-star white chip gets new private cards; with Laser Tripwires, if none is, the holder of the highest white chip.
    /// </summary>
    private void CheckAlarmsOnTheFlop()
    {
        var hasFace = _community.Take(3).Any(c => c.Rank is >= 11 and <= 13);
        var chip = Has(TheGangCards.MotionDetector) && hasFace ? 1
            : Has(TheGangCards.LaserTripwires) && !hasFace ? PlayerCount
            : (int?)null;
        var player = Enumerable.Range(0, PlayerCount).FirstOrDefault(p => chip is not null && _chips[0, p] == chip, -1);
        if (player < 0)
        {
            return;
        }

        _holeCards[player] = _deck.Skip(_nextCard).Take(_holeCards[player].Length).ToArray();
        _nextCard += _holeCards[player].Length;
        RedrawnPlayer = player;
    }

    /// <summary>
    /// Whether a chip of a stage is locked (Noise Sensors: the 1-star chips; Ventilation Shaft: the highest chips; both
    /// only in the white, yellow and orange stages).
    /// </summary>
    private bool IsLocked(int stage, int chip) =>
        stage < StageCount - 1 &&
        ((Has(TheGangCards.NoiseSensors) && chip == 1) || (Has(TheGangCards.VentilationShaft) && chip == PlayerCount));

    /// <summary>
    /// Adds up to <paramref name="count"/> random cards of a kind to the cards in play (only in Advanced mode): enabled,
    /// not used before in this game, and if the rules say so, not cancelling a card in play. Cards stay in play for the
    /// rest of the game.
    /// </summary>
    private void DrawCards(TheGangCardKind kind, int count)
    {
        var drawn = new List<TheGangCard>();
        var enabled = kind == TheGangCardKind.Challenge ? _rules.Challenges : _rules.Specialists;
        while (Mode == TheGangMode.Advanced && drawn.Count < count)
        {
            var candidates = enabled
                .Where(key => !_usedCards.Contains(key)
                    && !(_rules.AvoidClashes && _cards.Concat(drawn).Any(d => TheGangCards.Clash(d.Key, key))))
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

        _cards = [.. _cards, .. drawn];
        _newCards = [.. drawn.Select(c => c.Key)];
    }

    /// <summary>
    /// Checks that chips can be taken now, by an existing player.
    /// </summary>
    private bool TryCheckBetting(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        error = Phase switch
        {
            TheGangPhase.Betting => null,
            TheGangPhase.Preparing => "The betting starts once everyone has done the specialist card's step.",
            _ => "The betting is over for this heist.",
        };
        return error is null;
    }

    /// <summary>
    /// Checks that the heist is waiting for the step of the specialist card <paramref name="card"/>.
    /// </summary>
    private bool TryCheckPreparing(int player, string card, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        error = Phase == TheGangPhase.Preparing && PreparingCard == card ? null : "That is not possible now.";
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

            if (Stage == 1)
            {
                CheckAlarmsOnTheFlop();
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
        _deck = deck;
        _nextCard = (perPlayer * PlayerCount) + 5;
        RedrawnPlayer = null;
        Array.Clear(_chips);
        Array.Clear(_ready);
        _revealOrder = [];
        Revealed = 0;
        Stage = SkippedStages.Contains(0) ? 1 : 0;
        ResetDecisions();
        _announcements = [];
        if (Has(TheGangCards.Coordinator))
        {
            StartPreparing(TheGangCards.Coordinator);
        }
        else if (Has(TheGangCards.ConArtist))
        {
            StartPreparing(TheGangCards.ConArtist);
        }
        else
        {
            StartBetting();
        }
    }

    /// <summary>
    /// With Coordinator or Con Artist in play, every player does the card's step before the betting starts.
    /// </summary>
    private void StartPreparing(string card)
    {
        _prepared = new bool[PlayerCount];
        _passing = new Card?[PlayerCount];
        PreparingCard = card;
        Phase = TheGangPhase.Preparing;
    }

    /// <summary>
    /// Once every player has done the step: Con Artist's step follows Coordinator's; then the betting starts.
    /// </summary>
    private void FinishPreparing()
    {
        if (PreparingCard == TheGangCards.Coordinator && Has(TheGangCards.ConArtist))
        {
            StartPreparing(TheGangCards.ConArtist);
            return;
        }

        StartBetting();
    }

    /// <summary>
    /// Starts the betting. With Investor or Math Whiz in play, what they show about each player's private cards is
    /// announced now, after the steps before the betting.
    /// </summary>
    private void StartBetting()
    {
        _announcements = [.. _holeCards.Select(cards =>
        {
            var facts = new List<string>();
            if (Has(TheGangCards.Investor))
            {
                var faces = cards.Count(c => c.Rank is >= 11 and <= 13);
                facts.Add(faces == 1 ? "1 face card" : $"{faces} face cards");
            }

            if (Has(TheGangCards.MathWhiz))
            {
                facts.Add($"{cards.Sum(c => c.Rank == Card.Ace ? 11 : Math.Min(c.Rank, 10))} total");
            }

            return facts.Count == 0 ? null : string.Join(" · ", facts);
        })];
        PreparingCard = null;
        Phase = TheGangPhase.Betting;
    }
}
