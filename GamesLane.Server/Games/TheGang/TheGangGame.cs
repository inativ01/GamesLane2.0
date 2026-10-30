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
/// The Gang rules, without challenge or specialist cards (Advanced mode plays like Basic for now). <c>newDeck</c> supplies
/// a shuffled deck for each heist. Player <c>i</c> gets the deck's cards <c>2i</c> and <c>2i+1</c>; the five shared cards follow.
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
    private readonly int?[,] _chips;
    private readonly bool[] _ready;
    private Card[][] _holeCards = [];
    private Card[] _community = [];
    private int[] _revealOrder = [];

    /// <summary>
    /// Starts the first heist.
    /// </summary>
    public TheGangGame(int playerCount, Func<IReadOnlyList<Card>> newDeck, TheGangMode mode = TheGangMode.Basic)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(playerCount, MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(playerCount, MaxPlayers);
        PlayerCount = playerCount;
        Mode = mode;
        _newDeck = newDeck;
        _chips = new int?[StageCount, playerCount];
        _ready = new bool[playerCount];
        Deal();
    }

    /// <inheritdoc />
    public int PlayerCount { get; }

    /// <inheritdoc />
    public TheGangMode Mode { get; }

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

        if (_chips[Stage, player] is null)
        {
            error = "You have no chip to return.";
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

        Heist++;
        Deal();
        return true;
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
            return;
        }

        _revealOrder = Enumerable.Range(0, PlayerCount).OrderBy(p => _chips[Stage, p]).ToArray();
        Phase = TheGangPhase.Reveal;
    }

    /// <summary>
    /// Deals a new heist: two private cards each, five shared cards, no chips taken.
    /// </summary>
    private void Deal()
    {
        var deck = _newDeck();
        _holeCards = Enumerable.Range(0, PlayerCount).Select(p => new[] { deck[2 * p], deck[(2 * p) + 1] }).ToArray();
        _community = deck.Skip(2 * PlayerCount).Take(5).ToArray();
        Array.Clear(_chips);
        Array.Clear(_ready);
        _revealOrder = [];
        Revealed = 0;
        Stage = 0;
        Phase = TheGangPhase.Betting;
    }
}
