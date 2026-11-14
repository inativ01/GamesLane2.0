using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Yaniv;

/// <summary>
/// Yaniv rules for one game. <c>shuffle</c> shuffles the deck before each deal, which gives the first player still in the
/// game the first 5 cards, the next one the next 5, and so on, turns up the next card to start the open pile, and leaves
/// the rest as the closed pile, drawn from the top (for tests); it also shuffles the open pile but its top card into a new
/// closed pile when the closed pile runs out, so a card is face up on the open pile after every shuffle.
/// Round 1 is started by player 0, each later round by the winner of the round before.
/// </summary>
public class YanivGame : IYanivGame
{
    private Action<Card[]> _shuffle;
    private readonly List<Card>[] _hands;
    private readonly List<Card> _closed = [];
    private readonly List<Card> _open = [];
    private readonly int[] _scores;
    private readonly bool[] _out;
    private readonly List<int> _lastTurns = [];

    /// <summary>
    /// Deals the first round for <paramref name="players"/> players (2 to 4).
    /// </summary>
    public YanivGame(int players, YanivRules? rules = null, Action<Card[]>? shuffle = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, YanivRules.MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, YanivRules.MaxPlayers);
        Rules = rules ?? new YanivRules();
        _shuffle = shuffle ?? Random.Shared.Shuffle;
        _hands = [.. Enumerable.Range(0, players).Select(_ => new List<Card>())];
        _scores = new int[players];
        _out = new bool[players];
        Deal();
    }

    /// <summary>
    /// Gives a game loaded from a save its shuffle again (see the constructor).
    /// </summary>
    internal void Reattach(Action<Card[]>? shuffle) => _shuffle = shuffle ?? Random.Shared.Shuffle;

    /// <inheritdoc />
    public YanivRules Rules { get; }

    /// <inheritdoc />
    public int Players => _hands.Length;

    /// <inheritdoc />
    public int RoundNumber { get; private set; } = 1;

    /// <inheritdoc />
    public YanivPhase Phase { get; private set; }

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public int? Caller { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> LastTurns => _lastTurns;

    /// <inheritdoc />
    public Card? OpenTop => _open.Count > 0 ? _open[^1] : null;

    /// <inheritdoc />
    public int OpenPileCount => _open.Count;

    /// <inheritdoc />
    public int ClosedPileCount => _closed.Count;

    /// <inheritdoc />
    public IReadOnlyList<int> Scores => _scores;

    /// <inheritdoc />
    public YanivTurn? LastTurn { get; private set; }

    /// <inheritdoc />
    public int Actions { get; private set; }

    /// <inheritdoc />
    public int Shuffles { get; private set; }

    /// <inheritdoc />
    public YanivRoundResult? LastResult { get; private set; }

    /// <inheritdoc />
    public bool IsOver => Phase == YanivPhase.GameOver;

    /// <inheritdoc />
    public int? Winner { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<Card> Hand(int player) => _hands[player];

    /// <inheritdoc />
    public int HandScore(int player) => YanivCards.Score(_hands[player]);

    /// <inheritdoc />
    public bool IsOut(int player) => _out[player];

    /// <inheritdoc />
    public bool CanCallYaniv(int player) =>
        Phase == YanivPhase.Playing && Turn == player && Caller is null && HandScore(player) <= YanivRules.YanivLimit;

    /// <inheritdoc />
    public bool TryDiscard(int player, IReadOnlyList<Card> cards, bool takeOpen, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        var hand = _hands[player];
        error = cards.Count == 0 ? "Choose the cards to discard."
            : !Holds(hand, cards) ? "You do not have those cards."
            : !YanivCards.TryArrange(cards, out _) ? YanivCards.InvalidDiscard
            : takeOpen && _open.Count == 0 ? "The open pile is empty. Take a card from the closed pile."
            : null;
        if (error is not null)
        {
            return false;
        }

        YanivCards.TryArrange(cards, out var ordered);
        foreach (var card in cards)
        {
            hand.Remove(card);
        }

        Card[] taken = [];
        if (takeOpen)
        {
            taken = [_open[^1]];
            _open.RemoveAt(_open.Count - 1);
        }
        else if (DrawClosed() is { } drawn)
        {
            taken = [drawn];
        }

        hand.AddRange(taken);
        _open.AddRange(ordered);
        Actions++;
        LastTurn = new YanivTurn(player, ordered, takeOpen, takeOpen ? taken : []);
        EndTurn(player);
        return true;
    }

    /// <inheritdoc />
    public bool TryCallYaniv(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        error = Caller is not null ? "Yaniv has already been called."
            : HandScore(player) > YanivRules.YanivLimit ? $"You can call Yaniv only with {YanivRules.YanivLimit} points or less."
            : null;
        if (error is not null)
        {
            return false;
        }

        Actions++;
        Caller = player;
        _lastTurns.Clear();
        for (var p = Next(player); p != player; p = Next(p))
        {
            _lastTurns.Add(p);
        }

        Turn = _lastTurns[0];
        return true;
    }

    /// <inheritdoc />
    public bool TrySkip(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        Actions++;
        LastTurn = new YanivTurn(player, [], false, [], Skipped: true);
        EndTurn(player);
        return true;
    }

    /// <inheritdoc />
    public bool TryPass(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        if (Caller is null)
        {
            error = "You can pass only after Yaniv is called.";
            return false;
        }

        Actions++;
        LastTurn = new YanivTurn(player, [], false, [], Passed: true);
        EndTurn(player);
        return true;
    }

    /// <inheritdoc />
    public bool TryNextRound(int round, out string? error)
    {
        error = round < RoundNumber ? null
            : round > RoundNumber ? "There is no such round."
            : Phase == YanivPhase.GameOver ? "The game is over."
            : Phase != YanivPhase.RoundOver ? "This round is not finished yet."
            : null;
        if (error is not null || round < RoundNumber)
        {
            return error is null;
        }

        RoundNumber++;
        Deal();
        return true;
    }

    /// <summary>
    /// Shuffles the full deck and deals each player still in the game their cards; the next card is turned up to start the
    /// open pile, and the rest is the closed pile.
    /// </summary>
    private void Deal()
    {
        var deck = YanivCards.Deck();
        _shuffle(deck);
        Shuffles++;
        _closed.Clear();
        _closed.AddRange(deck);
        _open.Clear();
        _lastTurns.Clear();
        Caller = null;
        LastTurn = null;
        Actions++;
        Phase = YanivPhase.Playing;
        for (var p = 0; p < Players; p++)
        {
            _hands[p].Clear();
            if (!_out[p])
            {
                _hands[p].AddRange(_closed.Take(YanivRules.HandSize));
                _closed.RemoveRange(0, YanivRules.HandSize);
            }
        }

        _open.Add(_closed[0]);
        _closed.RemoveAt(0);

        Turn = LastResult?.Winner ?? 0;
    }

    /// <summary>
    /// After <paramref name="player"/>'s turn: the next player plays; after a Yaniv call, the next player still to play
    /// their last turn, and once all have, the round ends.
    /// </summary>
    private void EndTurn(int player)
    {
        if (Caller is null)
        {
            Turn = Next(player);
            return;
        }

        _lastTurns.Remove(player);
        if (_lastTurns.Count > 0)
        {
            Turn = _lastTurns[0];
            return;
        }

        EndRound(Caller.Value);
    }

    /// <summary>
    /// The hands are revealed. The caller wins the round unless another hand is lower (a tie goes to the caller); then the
    /// caller is Assaf and gains 30 points more, and the lowest hand wins (the first after the caller, on a tie). The
    /// winner gains nothing, everyone else their hand score. With a score target, the bonus takes 50 points off a player
    /// who lands exactly on half the target or on the target, and players above the target are out.
    /// </summary>
    private void EndRound(int caller)
    {
        var others = new List<int>();
        for (var p = Next(caller); p != caller; p = Next(p))
        {
            others.Add(p);
        }

        var lowest = others.MinBy(HandScore);
        var assaf = HandScore(lowest) < HandScore(caller);
        var winner = assaf ? lowest : caller;
        var players = new List<YanivRoundPlayer>();
        foreach (var p in Enumerable.Range(0, Players).Where(p => !_out[p]))
        {
            var handScore = HandScore(p);
            var points = p == winner ? 0 : handScore + (p == caller ? YanivRules.AssafPenalty : 0);
            _scores[p] += points;
            var bonus = Rules.End == YanivEndMode.Score && Rules.Bonus && points > 0
                && (_scores[p] == Rules.Target / 2 || _scores[p] == Rules.Target);
            if (bonus)
            {
                _scores[p] -= YanivRules.BonusPoints;
            }

            _out[p] = Rules.End == YanivEndMode.Score && _scores[p] > Rules.Target;
            players.Add(new YanivRoundPlayer(p, [.. YanivCards.Sorted(_hands[p])], handScore, points, bonus, _scores[p], _out[p]));
        }

        Actions++;
        LastResult = new YanivRoundResult(caller, winner, assaf, [.. players]);
        Turn = winner;
        _lastTurns.Clear();
        var over = Rules.End == YanivEndMode.OneRound || _out.Count(o => !o) <= 1;
        Phase = over ? YanivPhase.GameOver : YanivPhase.RoundOver;
        Winner = over ? winner : null;
    }

    /// <summary>
    /// The top card of the closed pile, or null if there is none. When the closed pile is empty, the open pile but its
    /// top card is shuffled into a new closed pile.
    /// </summary>
    private Card? DrawClosed()
    {
        if (_closed.Count == 0 && _open.Count > 1)
        {
            var rest = _open.GetRange(0, _open.Count - 1).ToArray();
            _open.RemoveRange(0, rest.Length);
            _shuffle(rest);
            Shuffles++;
            _closed.AddRange(rest);
        }

        if (_closed.Count == 0)
        {
            return null;
        }

        var card = _closed[0];
        _closed.RemoveAt(0);
        return card;
    }

    /// <summary>
    /// The next player in turn order who is still in the game.
    /// </summary>
    private int Next(int player)
    {
        var next = player;
        do
        {
            next = (next + 1) % Players;
        }
        while (_out[next]);
        return next;
    }

    private static bool Holds(List<Card> hand, IReadOnlyList<Card> cards)
    {
        var left = hand.ToList();
        return cards.All(left.Remove);
    }

    /// <summary>
    /// Checks that the game is waiting for <paramref name="player"/> to play.
    /// </summary>
    private bool TryCheckTurn(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, Players);
        error = Phase == YanivPhase.GameOver ? "The game is over."
            : Phase == YanivPhase.RoundOver ? "This round is over."
            : player != Turn ? "It is not your turn."
            : null;
        return error is null;
    }
}
