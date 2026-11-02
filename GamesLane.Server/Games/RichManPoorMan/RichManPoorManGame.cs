namespace GamesLane.Server.Games.RichManPoorMan;

/// <summary>
/// Rich man, poor man, game after game. <c>shuffle</c> shuffles the deck before each deal, which gives player 0 the
/// first 13 cards, player 1 the next 13, and so on (for tests); <c>random</c> breaks a tie between identical hands when
/// choosing who starts the first game.
/// </summary>
public class RichManPoorManGame : IRichManPoorManGame
{
    private readonly Action<int[]> _shuffle;
    private readonly Random _random;
    private readonly List<int>[] _hands;
    private readonly int[] _order;
    private readonly int[] _place;
    private readonly bool[] _passed;
    private readonly HashSet<int> _passedSinceThrow = [];
    private readonly List<int> _ranking = [];
    private readonly int[]?[] _gifts;
    private readonly int[]?[] _received;

    /// <summary>
    /// Deals the first game, in the players' own order. Its first round is started by the player with the most 2s.
    /// </summary>
    public RichManPoorManGame(int players, Action<int[]>? shuffle = null, Random? random = null)
    {
        RichManPoorManRules.CheckPlayers(players);
        PlayerCount = players;
        _random = random ?? Random.Shared;
        _shuffle = shuffle ?? _random.Shuffle;
        _hands = [.. Enumerable.Range(0, players).Select(_ => new List<int>())];
        _order = [.. Enumerable.Range(0, players)];
        _place = [.. _order];
        _passed = new bool[players];
        _gifts = new int[]?[players];
        _received = new int[]?[players];
        Deal();
        Phase = RichManPoorManPhase.Playing;
        Turn = FirstStarter();
    }

    /// <inheritdoc />
    public int PlayerCount { get; }

    /// <inheritdoc />
    public int GameNumber { get; private set; } = 1;

    /// <inheritdoc />
    public RichManPoorManPhase Phase { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Order => _order;

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<int>> Hands => _hands;

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public RichManPoorManThrow? CurrentThrow { get; private set; }

    /// <inheritdoc />
    public RichManPoorManThrow? LastThrow { get; private set; }

    /// <inheritdoc />
    public int Throws { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<bool> Passed => _passed;

    /// <inheritdoc />
    public int? LastRoundWinner { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Ranking => _ranking;

    /// <inheritdoc />
    public int? ExchangePartner(int player)
    {
        var place = PlaceOf(player);
        return ExchangeCountAt(place) > 0 ? _order[PlayerCount - 1 - place] : null;
    }

    /// <inheritdoc />
    public int ExchangeCount(int player) => ExchangeCountAt(PlaceOf(player));

    /// <inheritdoc />
    public IReadOnlyList<int>? RequiredGift(int player)
    {
        var count = ExchangeCount(player);
        var hand = _hands[player];
        return count > 0 && IsPoorer(player) ? hand.GetRange(hand.Count - count, count) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<int>? Gift(int player) => _gifts[PlayerIndex(player)];

    /// <inheritdoc />
    public IReadOnlyList<int>? Received(int player) => _received[PlayerIndex(player)];

    /// <inheritdoc />
    public bool TryThrow(int player, int rank, int count, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        var hand = _hands[player];
        error = rank < RichManPoorManRules.Two || rank > RichManPoorManRules.Ace || count < 1 ? "Choose the cards to throw."
            : hand.Count(c => c == rank) < count ? "You do not have those cards."
            : CurrentThrow is { } current && count != current.Count
                ? current.Count == 1 ? "Throw exactly 1 card." : $"Throw exactly {current.Count} cards."
            : CurrentThrow is { } beat && rank <= beat.Rank ? "Throw a higher number than the cards on the table."
            : null;
        if (error is not null)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            hand.Remove(rank);
        }

        Throws++;
        CurrentThrow = LastThrow = new RichManPoorManThrow(player, rank, count);
        _passed[player] = false;
        _passedSinceThrow.Clear();
        if (hand.Count == 0)
        {
            _ranking.Add(player);
        }

        if (_ranking.Count == PlayerCount)
        {
            LastRoundWinner = player;
            CurrentThrow = null;
            Phase = RichManPoorManPhase.GameOver;
        }
        else if (rank == RichManPoorManRules.Ace || NextWithCards(player) == player)
        {
            EndRound(player);
        }
        else
        {
            Turn = NextWithCards(player);
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

        if (CurrentThrow is not { } current)
        {
            error = "You start this round: throw cards of one number.";
            return false;
        }

        _passed[player] = true;
        _passedSinceThrow.Add(player);
        var waiting = Enumerable.Range(0, PlayerCount)
            .Where(p => p != current.Player && _hands[p].Count > 0 && !_passedSinceThrow.Contains(p));
        if (waiting.Any())
        {
            Turn = NextWithCards(player);
        }
        else
        {
            EndRound(current.Player);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryGive(int player, IReadOnlyList<int> cards, out string? error)
    {
        var count = ExchangeCount(player);
        var given = cards.Order().ToArray();
        var hand = _hands[player];
        error = Phase != RichManPoorManPhase.Exchanging ? "The cards are not being exchanged now."
            : count == 0 ? "You do not exchange cards in this game."
            : _gifts[player] is not null ? "You have already given your cards."
            : given.Length != count ? count == 1 ? "Give exactly 1 card." : $"Give exactly {count} cards."
            : given.GroupBy(c => c).Any(g => hand.Count(c => c == g.Key) < g.Count()) ? "You do not have those cards."
            : RequiredGift(player) is { } required && !required.SequenceEqual(given) ? "You must give your highest cards."
            : null;
        if (error is not null)
        {
            return false;
        }

        _gifts[player] = given;
        var givers = Enumerable.Range(0, PlayerCount).Where(p => ExchangeCount(p) > 0).ToArray();
        if (givers.All(p => _gifts[p] is not null))
        {
            Exchange(givers);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryNextGame(int game, out string? error)
    {
        error = null;
        if (game != GameNumber)
        {
            return true;
        }

        if (Phase != RichManPoorManPhase.GameOver)
        {
            error = "This game is not over yet.";
            return false;
        }

        _ranking.CopyTo(_order);
        for (var place = 0; place < PlayerCount; place++)
        {
            _place[_order[place]] = place;
        }

        GameNumber++;
        _ranking.Clear();
        Array.Clear(_gifts);
        Array.Clear(_received);
        LastThrow = null;
        LastRoundWinner = null;
        Deal();
        Phase = RichManPoorManPhase.Exchanging;
        Turn = _order[^1];
        return true;
    }

    /// <summary>
    /// Shuffles a new deck and deals 13 cards to every player, sorted from low to high.
    /// </summary>
    private void Deal()
    {
        var deck = RichManPoorManRules.Deck(PlayerCount);
        _shuffle(deck);
        for (var player = 0; player < PlayerCount; player++)
        {
            _hands[player].Clear();
            _hands[player].AddRange(deck.Skip(player * RichManPoorManRules.CardsPerPlayer).Take(RichManPoorManRules.CardsPerPlayer).Order());
        }

        CurrentThrow = null;
        Array.Clear(_passed);
        _passedSinceThrow.Clear();
    }

    /// <summary>
    /// The player with the most 2s; on a tie, the one of them with the most 3s, and so on. Between identical hands, one
    /// of them at random.
    /// </summary>
    private int FirstStarter()
    {
        var candidates = Enumerable.Range(0, PlayerCount).ToList();
        for (var rank = RichManPoorManRules.Two; rank <= RichManPoorManRules.Ace && candidates.Count > 1; rank++)
        {
            var most = candidates.Max(p => _hands[p].Count(c => c == rank));
            candidates.RemoveAll(p => _hands[p].Count(c => c == rank) < most);
        }

        return candidates[_random.Next(candidates.Count)];
    }

    /// <summary>
    /// Every pair has given: each player's gift goes to their partner, and the poorest player starts.
    /// </summary>
    private void Exchange(IEnumerable<int> givers)
    {
        foreach (var player in givers)
        {
            var partner = ExchangePartner(player)!.Value;
            foreach (var card in _gifts[player]!)
            {
                _hands[player].Remove(card);
            }

            _received[partner] = _gifts[player];
        }

        foreach (var player in givers)
        {
            _hands[player].AddRange(_received[player]!);
            _hands[player].Sort();
        }

        Array.Clear(_gifts);
        Phase = RichManPoorManPhase.Playing;
        Turn = _order[^1];
    }

    /// <summary>
    /// Ends the round won by <paramref name="winner"/>, who starts the next one, or if their hand is empty, the next
    /// player in turn who still has cards.
    /// </summary>
    private void EndRound(int winner)
    {
        LastRoundWinner = winner;
        CurrentThrow = null;
        Array.Clear(_passed);
        _passedSinceThrow.Clear();
        Turn = _hands[winner].Count > 0 ? winner : NextWithCards(winner);
    }

    /// <summary>
    /// The next player after <paramref name="player"/> in the direction of play (one place richer, and from #1 back to
    /// the last) who still has cards; <paramref name="player"/> itself if nobody else has.
    /// </summary>
    private int NextWithCards(int player)
    {
        var place = _place[player];
        for (var step = 1; step < PlayerCount; step++)
        {
            var next = _order[(place - step + PlayerCount) % PlayerCount];
            if (_hands[next].Count > 0)
            {
                return next;
            }
        }

        return player;
    }

    /// <summary>
    /// The number of cards the player at <paramref name="place"/> (0 for #1) exchanges in this game: none in the first game.
    /// </summary>
    private int ExchangeCountAt(int place)
    {
        var counts = RichManPoorManRules.ExchangeCounts(PlayerCount);
        var pair = Math.Min(place, PlayerCount - 1 - place);
        return GameNumber > 1 && pair < counts.Count ? counts[pair] : 0;
    }

    private bool IsPoorer(int player) => PlaceOf(player) >= PlayerCount - PlayerCount / 2;

    private int PlaceOf(int player) => _place[PlayerIndex(player)];

    private int PlayerIndex(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        return player;
    }

    /// <summary>
    /// Checks that the game is waiting for <paramref name="player"/> to throw or pass.
    /// </summary>
    private bool TryCheckTurn(int player, out string? error)
    {
        PlayerIndex(player);
        error = Phase == RichManPoorManPhase.Exchanging ? "Wait until the cards have been exchanged."
            : Phase == RichManPoorManPhase.GameOver ? "The game is over."
            : player != Turn ? "It is not your turn."
            : null;
        return error is null;
    }
}
