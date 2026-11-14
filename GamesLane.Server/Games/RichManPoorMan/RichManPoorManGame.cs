using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.RichManPoorMan;

/// <summary>
/// Rich man, poor man, game after game. <c>shuffle</c> shuffles the deck before each deal, which gives #1 the first 13
/// cards, #2 the next 13, and so on (for tests); <c>random</c> breaks a tie between identical hands when choosing who
/// starts the first game.
/// </summary>
public class RichManPoorManGame : IRichManPoorManGame
{
    private Action<Card[]> _shuffle;
    private Random _random;
    private readonly Dictionary<int, PlayerState> _players = [];
    private readonly List<int> _order = [];
    private readonly HashSet<int> _passedSinceThrow = [];
    private readonly List<int> _ranking = [];
    private readonly bool _exchangeFirst;

    /// <summary>
    /// Deals the first game, with the players at the places in <paramref name="order"/>. Its first round is started by
    /// the player with the most 2s; with <paramref name="exchangeFirst"/>, the pairs exchange cards first, as in later
    /// games, and the poorest player starts.
    /// </summary>
    public RichManPoorManGame(
        IReadOnlyList<int> order, Action<Card[]>? shuffle = null, Random? random = null, bool exchangeFirst = false)
    {
        RichManPoorManRules.CheckPlayers(order.Count);
        if (order.Distinct().Count() != order.Count)
        {
            throw new ArgumentException("Each player can play only once.", nameof(order));
        }

        _random = random ?? Random.Shared;
        _shuffle = shuffle ?? _random.Shuffle;
        _exchangeFirst = exchangeFirst;
        SetPlayers(order);
        if (exchangeFirst)
        {
            StartExchange();
            return;
        }

        Deal();
        Phase = RichManPoorManPhase.Playing;
        Turn = FirstStarter();
    }

    /// <summary>
    /// Gives a game loaded from a save its shuffle and random draws again (see the constructor).
    /// </summary>
    internal void Reattach(Action<Card[]>? shuffle, Random? random)
    {
        _random = random ?? Random.Shared;
        _shuffle = shuffle ?? _random.Shuffle;
    }

    /// <inheritdoc />
    public int GameNumber { get; private set; } = 1;

    /// <inheritdoc />
    public RichManPoorManPhase Phase { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Order => _order;

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public RichManPoorManThrow? CurrentThrow { get; private set; }

    /// <inheritdoc />
    public RichManPoorManThrow? LastThrow { get; private set; }

    /// <inheritdoc />
    public int Throws { get; private set; }

    /// <inheritdoc />
    public int Shuffles { get; private set; }

    /// <inheritdoc />
    public int? LastRoundWinner { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Ranking => _ranking;

    /// <inheritdoc />
    public IReadOnlyList<int> NextOrder => [.. _ranking, .. _order.Where(p => !_ranking.Contains(p))];

    /// <inheritdoc />
    public IReadOnlyList<Card> Hand(int player) => Player(player).Hand;

    /// <inheritdoc />
    public bool HasPassed(int player) => Player(player).Passed;

    /// <inheritdoc />
    public IReadOnlyList<Card>? RoundThrow(int player) => Player(player).RoundThrow;

    /// <inheritdoc />
    public bool IsActive(int player) => Player(player).Active;

    /// <inheritdoc />
    public int? ExchangePartner(int player)
    {
        var place = PlaceOf(player);
        return ExchangeCountAt(place) > 0 ? _order[_order.Count - 1 - place] : null;
    }

    /// <inheritdoc />
    public int ExchangeCount(int player) => ExchangeCountAt(PlaceOf(player));

    /// <inheritdoc />
    public IReadOnlyList<Card>? RequiredGift(int player)
    {
        var count = ExchangeCount(player);
        var hand = Player(player).Hand;
        return Phase == RichManPoorManPhase.Exchanging && count > 0 && IsPoorer(player)
            ? hand.GetRange(hand.Count - count, count)
            : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<Card>? Gift(int player) => Player(player).Gift;

    /// <inheritdoc />
    public IReadOnlyList<Card>? Received(int player) => Player(player).Received;

    /// <inheritdoc />
    public bool TryThrow(int player, IReadOnlyList<Card> cards, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        var hand = Player(player).Hand;
        error = cards.Count == 0 ? "Choose the cards to throw."
            : cards.Any(c => c.Rank != cards[0].Rank) ? "Throw cards of one number only."
            : !Holds(hand, cards) ? "You do not have those cards."
            : CurrentThrow is { } current && cards.Count != current.Count
                ? current.Count == 1 ? "Throw exactly 1 card." : $"Throw exactly {current.Count} cards."
            : CurrentThrow is { } beat && cards[0].Rank <= beat.Rank ? "Throw a higher number than the cards on the table."
            : null;
        if (error is not null)
        {
            return false;
        }

        foreach (var card in cards)
        {
            hand.Remove(card);
        }

        if (CurrentThrow is null)
        {
            ClearCalls();
        }

        Throws++;
        var thrown = RichManPoorManRules.Sorted(cards).ToArray();
        CurrentThrow = LastThrow = new RichManPoorManThrow(player, thrown);
        Player(player).Passed = false;
        Player(player).RoundThrow = thrown;
        _passedSinceThrow.Clear();
        if (hand.Count == 0)
        {
            _ranking.Add(player);
        }

        if (EndIfDone())
        {
            LastRoundWinner = player;
        }
        else if (cards[0].Rank == RichManPoorManRules.Ace)
        {
            EndRound(player);
        }
        else
        {
            PassTurn(player);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryPass(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        if (CurrentThrow is null)
        {
            error = "You start this round: throw cards of one number.";
            return false;
        }

        Player(player).Passed = true;
        _passedSinceThrow.Add(player);
        PassTurn(player);
        return true;
    }

    /// <inheritdoc />
    public bool TrySkip(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        if (CurrentThrow is null)
        {
            Turn = NextWithCards(player);
        }
        else
        {
            Player(player).Passed = true;
            _passedSinceThrow.Add(player);
            PassTurn(player);
        }

        EndIfDone();
        return true;
    }

    /// <inheritdoc />
    public bool TryGive(int player, IReadOnlyList<Card> cards, out string? error)
    {
        var count = ExchangeCount(player);
        var given = RichManPoorManRules.Sorted(cards).ToArray();
        var state = Player(player);
        error = Phase != RichManPoorManPhase.Exchanging ? "The cards are not being exchanged now."
            : count == 0 ? "You do not exchange cards in this game."
            : state.Gift is not null ? "You have already given your cards."
            : given.Length != count ? count == 1 ? "Give exactly 1 card." : $"Give exactly {count} cards."
            : !Holds(state.Hand, given) ? "You do not have those cards."
            : RequiredGift(player) is { } required && !required.Select(c => c.Rank).SequenceEqual(given.Select(c => c.Rank))
                ? "You must give your highest cards."
            : null;
        if (error is not null)
        {
            return false;
        }

        state.Gift = given;
        var givers = _order.Where(p => ExchangeCount(p) > 0).ToArray();
        if (givers.All(p => _players[p].Gift is not null))
        {
            Exchange(givers);
        }

        return true;
    }

    /// <inheritdoc />
    public void SetActive(int player, bool active) => Player(player).Active = active;

    /// <inheritdoc />
    public bool TryNextGame(int game, IReadOnlyList<int> order, out string? error)
    {
        error = null;
        if (game != GameNumber)
        {
            return true;
        }

        error = Phase != RichManPoorManPhase.GameOver ? "This game is not over yet."
            : order.Count < RichManPoorManRules.MinPlayers ? $"At least {RichManPoorManRules.MinPlayers} players are needed for the exchange."
            : order.Count > RichManPoorManRules.MaxPlayers ? $"At most {RichManPoorManRules.MaxPlayers} players can play."
            : order.Distinct().Count() != order.Count ? "Each player can play only once."
            : null;
        if (error is not null)
        {
            return false;
        }

        GameNumber++;
        _ranking.Clear();
        LastThrow = null;
        LastRoundWinner = null;
        SetPlayers(order);
        StartExchange();
        return true;
    }

    /// <inheritdoc />
    public bool TryAddPlayer(int player, int place, out string? error)
    {
        error = Phase != RichManPoorManPhase.Exchanging ? "Players can join only during the exchange."
            : _players.ContainsKey(player) ? "That player is already playing."
            : _order.Count >= RichManPoorManRules.MaxPlayers ? "The table is full."
            : place < 0 || place > _order.Count ? "There is no such place."
            : null;
        if (error is not null)
        {
            return false;
        }

        SetPlayers([.. _order.Take(place), player, .. _order.Skip(place)]);
        StartExchange();
        return true;
    }

    /// <inheritdoc />
    public bool TryRemovePlayer(int player, out string? error)
    {
        error = Phase != RichManPoorManPhase.Exchanging ? "Players can be dropped only during the exchange."
            : !_players.ContainsKey(player) ? "That player is not playing."
            : _order.Count <= RichManPoorManRules.MinPlayers ? $"At least {RichManPoorManRules.MinPlayers} players are needed for the exchange."
            : null;
        if (error is not null)
        {
            return false;
        }

        SetPlayers([.. _order.Where(p => p != player)]);
        StartExchange();
        return true;
    }

    /// <summary>
    /// Makes <paramref name="order"/> the players, at those places, all active and with no cards yet.
    /// </summary>
    private void SetPlayers(IReadOnlyList<int> order)
    {
        _order.Clear();
        _order.AddRange(order);
        _players.Clear();
        foreach (var player in order)
        {
            _players[player] = new PlayerState();
        }
    }

    /// <summary>
    /// Deals the cards for the current players and waits for every pair to give; gifts already given are cancelled.
    /// </summary>
    private void StartExchange()
    {
        Deal();
        Phase = RichManPoorManPhase.Exchanging;
        Turn = _order[^1];
    }

    /// <summary>
    /// Shuffles a new deck (one set of 2 to A per player) and deals 13 cards to every player, sorted from low to high.
    /// </summary>
    private void Deal()
    {
        var deck = RichManPoorManRules.Deck(_order.Count);
        _shuffle(deck);
        Shuffles++;
        for (var place = 0; place < _order.Count; place++)
        {
            var state = _players[_order[place]];
            state.Hand.Clear();
            state.Hand.AddRange(RichManPoorManRules.Sorted(
                deck.Skip(place * RichManPoorManRules.CardsPerPlayer).Take(RichManPoorManRules.CardsPerPlayer)));
            state.Passed = false;
            state.RoundThrow = null;
            state.Gift = null;
            state.Received = null;
        }

        CurrentThrow = null;
        _passedSinceThrow.Clear();
    }

    /// <summary>
    /// The player with the most 2s; on a tie, the one of them with the most 3s, and so on. Between identical hands, one
    /// of them at random.
    /// </summary>
    private int FirstStarter()
    {
        var candidates = _order.ToList();
        for (var rank = RichManPoorManRules.Two; rank <= RichManPoorManRules.Ace && candidates.Count > 1; rank++)
        {
            var most = candidates.Max(p => _players[p].Hand.Count(c => c.Rank == rank));
            candidates.RemoveAll(p => _players[p].Hand.Count(c => c.Rank == rank) < most);
        }

        return candidates[_random.Next(candidates.Count)];
    }

    /// <summary>
    /// Every pair has given: all the gifts go to the partners at once, and the poorest player starts.
    /// </summary>
    private void Exchange(IReadOnlyList<int> givers)
    {
        foreach (var player in givers)
        {
            var state = _players[player];
            foreach (var card in state.Gift!)
            {
                state.Hand.Remove(card);
            }

            _players[ExchangePartner(player)!.Value].Received = state.Gift;
        }

        foreach (var player in givers)
        {
            var state = _players[player];
            var hand = RichManPoorManRules.Sorted([.. state.Hand, .. state.Received!]).ToList();
            state.Hand.Clear();
            state.Hand.AddRange(hand);
            state.Gift = null;
        }

        Phase = RichManPoorManPhase.Playing;
        Turn = _order[^1];
    }

    /// <summary>
    /// After <paramref name="player"/> has thrown or passed: the turn goes to the next player in turn who has cards. The
    /// round ends once everyone else with cards has passed since the last throw.
    /// </summary>
    private void PassTurn(int player)
    {
        var next = player;
        while (true)
        {
            var thrower = CurrentThrow!.Player;
            if (!_order.Any(p => p != thrower && _players[p].Hand.Count > 0 && !_passedSinceThrow.Contains(p)))
            {
                EndRound(thrower);
                return;
            }

            next = NextWithCards(next);
            if (next == thrower || _passedSinceThrow.Contains(next))
            {
                continue;
            }

            Turn = next;
            return;
        }
    }

    /// <summary>
    /// Ends the round won by <paramref name="winner"/>, who starts the next one; if their hand is empty, the next player
    /// in turn who still has cards starts it. The round's calls stay until the next round's first throw.
    /// </summary>
    private void EndRound(int winner)
    {
        LastRoundWinner = winner;
        CurrentThrow = null;
        _passedSinceThrow.Clear();
        Turn = _players[winner].Hand.Count > 0 ? winner : NextWithCards(winner);
    }

    /// <summary>
    /// Forgets every player's call (pass or throw) in the round that ended.
    /// </summary>
    private void ClearCalls()
    {
        foreach (var state in _players.Values)
        {
            state.Passed = false;
            state.RoundThrow = null;
        }
    }

    /// <summary>
    /// Ends the game once only one player has cards left, or once only inactive players have (while someone is active).
    /// The last player with cards takes the last place if they are active; inactive players who still have cards are
    /// dropped from the game (see <see cref="IRichManPoorManGame.Order"/>). Returns whether it ended.
    /// </summary>
    private bool EndIfDone()
    {
        var holding = _order.Where(p => _players[p].Hand.Count > 0).ToArray();
        var onlyInactiveHold = holding.All(p => !_players[p].Active) && _players.Values.Any(s => s.Active);
        if (holding.Length > 1 && !onlyInactiveHold)
        {
            return false;
        }

        foreach (var player in holding)
        {
            if (_players[player].Active)
            {
                _ranking.Add(player);
            }
            else
            {
                _order.Remove(player);
                _players.Remove(player);
            }
        }

        CurrentThrow = null;
        Turn = _order.Count > 0 ? _order[0] : Turn;
        Phase = RichManPoorManPhase.GameOver;
        return true;
    }

    /// <summary>
    /// The next player after <paramref name="player"/> in the direction of play (one place richer, and from #1 back to
    /// the last) who still has cards; <paramref name="player"/> itself if nobody else has.
    /// </summary>
    private int NextWithCards(int player) => NextWhere(player, s => s.Hand.Count > 0) ?? player;

    private int? NextWhere(int player, Func<PlayerState, bool> condition)
    {
        var place = PlaceOf(player);
        var count = _order.Count;
        for (var step = 1; step < count; step++)
        {
            var next = _order[(place - step + count) % count];
            if (condition(_players[next]))
            {
                return next;
            }
        }

        return null;
    }

    /// <summary>
    /// The number of cards the player at <paramref name="place"/> (0 for #1) exchanges in this game: none in the first game,
    /// unless it starts with an exchange.
    /// </summary>
    private int ExchangeCountAt(int place)
    {
        if (GameNumber == 1 && !_exchangeFirst)
        {
            return 0;
        }

        var counts = RichManPoorManRules.ExchangeCounts(_order.Count);
        var pair = Math.Min(place, _order.Count - 1 - place);
        return pair < counts.Count ? counts[pair] : 0;
    }

    private bool IsPoorer(int player) => PlaceOf(player) >= _order.Count - _order.Count / 2;

    private int PlaceOf(int player)
    {
        Player(player);
        return _order.IndexOf(player);
    }

    private PlayerState Player(int player) =>
        _players.TryGetValue(player, out var state)
            ? state
            : throw new ArgumentOutOfRangeException(nameof(player), player, "Not a player in this game.");

    /// <summary>
    /// Whether <paramref name="hand"/> holds all of <paramref name="cards"/> (a card may appear more than once).
    /// </summary>
    private static bool Holds(List<Card> hand, IEnumerable<Card> cards) =>
        cards.GroupBy(c => c).All(g => hand.Count(c => c == g.Key) >= g.Count());

    /// <summary>
    /// Checks that the game is waiting for <paramref name="player"/> to throw or pass.
    /// </summary>
    private bool TryCheckTurn(int player, out string? error)
    {
        Player(player);
        error = Phase == RichManPoorManPhase.Exchanging ? "Wait until the cards have been exchanged."
            : Phase == RichManPoorManPhase.GameOver ? "The game is over."
            : player != Turn ? "It is not your turn."
            : null;
        return error is null;
    }

    /// <summary>
    /// A player's cards and state in the current game.
    /// </summary>
    private sealed class PlayerState
    {
        /// <summary>The cards in hand, from low to high.</summary>
        public List<Card> Hand { get; } = [];

        /// <summary>Whether the player's last action in this round was to pass.</summary>
        public bool Passed { get; set; }

        /// <summary>The cards the player last threw in this round, or null.</summary>
        public Card[]? RoundThrow { get; set; }

        /// <summary>Whether the player is connected.</summary>
        public bool Active { get; set; } = true;

        /// <summary>The cards given in this exchange, until all are exchanged.</summary>
        public Card[]? Gift { get; set; }

        /// <summary>The cards received in this game's exchange.</summary>
        public Card[]? Received { get; set; }
    }
}
