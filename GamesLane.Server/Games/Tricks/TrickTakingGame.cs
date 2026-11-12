using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Tricks;

/// <summary>
/// The play of a trick-taking game (see <see cref="ITrickTakingGame"/>); each game adds its own rules: what comes before
/// the play (e.g. passing cards or bidding, then <see cref="StartPlay"/>), the trump suit, which cards may not be played
/// (<see cref="RuleError"/>), and the scoring once the 13 tricks are played (<see cref="OnHandPlayed"/>). <c>shuffle</c>
/// shuffles the deck before each deal, which gives player 0 the first 13 cards, player 1 the next 13, and so on (for tests).
/// </summary>
public abstract class TrickTakingGame : ITrickTakingGame
{
    /// <summary>The number of players.</summary>
    public const int PlayerCount = 4;

    /// <summary>The cards dealt to each player.</summary>
    public const int HandSize = 13;

    private Action<Card[]> _shuffle;
    private readonly List<Card>[] _hands = [.. Enumerable.Range(0, PlayerCount).Select(_ => new List<Card>())];
    private readonly List<Card>[] _won = [.. Enumerable.Range(0, PlayerCount).Select(_ => new List<Card>())];
    private readonly List<TrickCard> _trick = [];
    private readonly int[] _tricksWon = new int[PlayerCount];
    private int? _claimer;
    private Card[] _claimed = [];

    /// <summary>
    /// Creates the game; the derived game's constructor then sets up the first hand (see <see cref="StartHand"/>).
    /// </summary>
    protected TrickTakingGame(Action<Card[]>? shuffle) => _shuffle = shuffle ?? Random.Shared.Shuffle;

    /// <summary>
    /// Gives a game loaded from a save its shuffle again (see the class).
    /// </summary>
    internal void Reattach(Action<Card[]>? shuffle) => _shuffle = shuffle ?? Random.Shared.Shuffle;

    /// <inheritdoc />
    public int Players => PlayerCount;

    /// <inheritdoc />
    public int HandNumber { get; private set; }

    /// <inheritdoc />
    public int Turn { get; protected set; }

    /// <inheritdoc />
    public bool IsPlaying { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<TrickCard> Trick => _trick;

    /// <inheritdoc />
    public CompletedTrick? LastTrick { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> TricksWon => _tricksWon;

    /// <inheritdoc />
    public int? Claimer => _claimer;

    /// <inheritdoc />
    public IReadOnlyList<Card> ClaimedCards => _claimed;

    /// <inheritdoc />
    public int Actions { get; protected set; }

    /// <inheritdoc />
    public abstract bool IsOver { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<int> AwaitedPlayers => IsPlaying ? [Turn] : [];

    /// <summary>The trump suit, or null for none.</summary>
    protected abstract Suit? Trump { get; }

    /// <summary>Whether this hand is over and the next one can be dealt.</summary>
    protected abstract bool IsHandOver { get; }

    /// <inheritdoc />
    public IReadOnlyList<Card> Hand(int player) => _hands[player];

    /// <inheritdoc />
    public IReadOnlyList<Card> Won(int player) => _won[player];

    /// <inheritdoc />
    public IReadOnlyList<Card> Playable(int player) =>
        IsPlaying && Turn == player ? [.. _hands[player].Where(c => PlayError(player, c) is null)] : [];

    /// <inheritdoc />
    public bool TryPlay(int player, Card card, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        error = IsOver ? "The game is over."
            : !IsPlaying ? "Wait until it is time to play a card."
            : player != Turn ? "It is not your turn."
            : PlayError(player, card);
        if (error is not null)
        {
            return false;
        }

        _hands[player].Remove(card);
        _trick.Add(new TrickCard(player, card));
        Actions++;
        OnCardPlayed(player, card);
        if (_trick.Count < PlayerCount)
        {
            Turn = (player + 1) % PlayerCount;
            return true;
        }

        var winner = TrickCards.Winner(_trick, Trump);
        LastTrick = new CompletedTrick([.. _trick], winner);
        _won[winner].AddRange(_trick.Select(c => c.Card));
        _tricksWon[winner]++;
        _trick.Clear();
        Turn = winner;
        if (_hands[winner].Count == 0)
        {
            IsPlaying = false;
            OnHandPlayed();
        }

        return true;
    }

    /// <inheritdoc />
    public bool CanClaim(int player) =>
        IsPlaying && Turn == player && _trick.Count == 0 && _hands[player].Count > 0 && WinsEveryTrick(player);

    /// <inheritdoc />
    public bool TryClaim(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        error = IsOver ? "The game is over."
            : !IsPlaying ? "Wait until it is time to play a card."
            : player != Turn ? "It is not your turn."
            : _trick.Count > 0 ? "You can claim only at the start of a trick."
            : !WinsEveryTrick(player) ? "Your cards do not win every remaining trick."
            : null;
        if (error is not null)
        {
            return false;
        }

        _claimer = player;
        _claimed = [.. _hands[player]];
        _tricksWon[player] += _hands[player].Count;
        for (var p = 0; p < PlayerCount; p++)
        {
            _won[player].AddRange(_hands[p]);
            _hands[p].Clear();
        }

        Actions++;
        IsPlaying = false;
        OnHandPlayed();
        return true;
    }

    /// <inheritdoc />
    public bool TryNextHand(int hand, out string? error)
    {
        error = hand < HandNumber ? null
            : hand > HandNumber ? "There is no such hand."
            : IsOver ? "The game is over."
            : !IsHandOver ? "This hand is not finished yet."
            : null;
        if (error is not null || hand < HandNumber)
        {
            return error is null;
        }

        StartHand();
        return true;
    }

    /// <summary>
    /// Why <paramref name="player"/> may not play <paramref name="card"/> by the game's own rules, or null if they may
    /// (only asked for a card they hold that follows suit when they can).
    /// </summary>
    protected virtual string? RuleError(int player, Card card) => null;

    /// <summary>Called after <paramref name="player"/> plays <paramref name="card"/>, before the trick is decided.</summary>
    protected virtual void OnCardPlayed(int player, Card card)
    {
    }

    /// <summary>Called once the 13 tricks are played: the game scores the hand and ends it, or the game.</summary>
    protected abstract void OnHandPlayed();

    /// <summary>Deals the next hand (with <see cref="Deal"/>) and sets it up.</summary>
    protected abstract void StartHand();

    /// <summary>
    /// Shuffles the deck and deals each player 13 cards, for hand <see cref="HandNumber"/> + 1. No trick is played until
    /// <see cref="StartPlay"/>.
    /// </summary>
    protected void Deal()
    {
        var deck = TrickCards.Deck();
        _shuffle(deck);
        for (var p = 0; p < PlayerCount; p++)
        {
            _hands[p].Clear();
            _hands[p].AddRange(TrickCards.Sorted(deck.Skip(p * HandSize).Take(HandSize)));
            _won[p].Clear();
            _tricksWon[p] = 0;
        }

        _trick.Clear();
        LastTrick = null;
        _claimer = null;
        _claimed = [];
        IsPlaying = false;
        HandNumber++;
        Actions++;
    }

    /// <summary>Starts the play of the hand: <paramref name="leader"/> leads the first trick.</summary>
    protected void StartPlay(int leader)
    {
        IsPlaying = true;
        Turn = leader;
        Actions++;
    }

    /// <summary>Takes <paramref name="cards"/> out of a player's hand (e.g. to pass them); they must hold them.</summary>
    protected void TakeFromHand(int player, IEnumerable<Card> cards)
    {
        foreach (var card in cards)
        {
            _hands[player].Remove(card);
        }
    }

    /// <summary>Adds <paramref name="cards"/> to a player's hand, which stays in hand order.</summary>
    protected void AddToHand(int player, IEnumerable<Card> cards)
    {
        var hand = TrickCards.Sorted(_hands[player].Concat(cards)).ToList();
        _hands[player].Clear();
        _hands[player].AddRange(hand);
    }

    /// <summary>
    /// Whether <paramref name="player"/>, leading, wins every remaining trick whatever anyone plays, judged only from
    /// what they can know: their own cards and the cards already played. Each of their cards must be higher than every
    /// card of its suit still out (neither in their hand nor played), and while any trump is out, they must hold only
    /// trumps.
    /// </summary>
    private bool WinsEveryTrick(int player)
    {
        var hand = _hands[player];
        var played = _won.SelectMany(w => w).Concat(_trick.Select(c => c.Card));
        var outstanding = TrickCards.Deck().Except(hand).Except(played).ToList();
        return hand.All(c => !outstanding.Any(o => o.Suit == c.Suit && o.Rank > c.Rank))
            && (Trump is not { } trump || hand.All(c => c.Suit == trump) || !outstanding.Any(o => o.Suit == trump));
    }

    /// <summary>
    /// Why <paramref name="player"/> may not play <paramref name="card"/> now: they do not hold it, they must follow the
    /// suit led, or a rule of the game (see <see cref="RuleError"/>); null if they may.
    /// </summary>
    private string? PlayError(int player, Card card)
    {
        var hand = _hands[player];
        return !hand.Contains(card) ? "You do not have that card."
            : _trick.Count > 0 && card.Suit != _trick[0].Card.Suit && hand.Any(c => c.Suit == _trick[0].Card.Suit) ? "You must follow suit."
            : RuleError(player, card);
    }
}

