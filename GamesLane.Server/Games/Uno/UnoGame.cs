namespace GamesLane.Server.Games.Uno;

/// <summary>
/// Uno rules for one game. <c>shuffle</c> shuffles the deck before each deal, which gives player 0 the first cards, player
/// 1 the next ones, and so on, then turns up the next card (for tests); it also shuffles the discard pile into a new draw
/// pile when the draw pile runs out. Hand 1 is started by player 0, each later hand by the next player.
/// </summary>
public class UnoGame : IUnoGame
{
    private Action<UnoCard[]> _shuffle;
    private readonly List<UnoCard>[] _hands;
    private readonly List<UnoCard> _drawPile = [];
    private readonly List<UnoCard> _discard = [];
    private readonly int[] _scores;
    private readonly int[] _handSizes;
    private readonly bool[] _saidUno;
    private readonly List<int> _ranking = [];
    private readonly List<UnoEvent> _events = [];

    /// <summary>
    /// Deals the first hand for <paramref name="players"/> players (2 to 10).
    /// </summary>
    public UnoGame(int players, UnoRules? rules = null, Action<UnoCard[]>? shuffle = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, UnoRules.MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, UnoRules.MaxPlayers);
        Rules = rules ?? new UnoRules();
        _shuffle = shuffle ?? Random.Shared.Shuffle;
        _hands = [.. Enumerable.Range(0, players).Select(_ => new List<UnoCard>())];
        _scores = new int[players];
        _handSizes = [.. Enumerable.Repeat(UnoRules.HandSize, players)];
        _saidUno = new bool[players];
        Deal();
    }

    /// <summary>
    /// Gives a game loaded from a save its shuffle again (see the constructor).
    /// </summary>
    internal void Reattach(Action<UnoCard[]>? shuffle) => _shuffle = shuffle ?? Random.Shared.Shuffle;

    /// <inheritdoc />
    public UnoRules Rules { get; }

    /// <inheritdoc />
    public int Players => _hands.Length;

    /// <inheritdoc />
    public int HandNumber { get; private set; } = 1;

    /// <inheritdoc />
    public UnoPhase Phase { get; private set; }

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public int Direction { get; private set; } = 1;

    /// <inheritdoc />
    public UnoCard TopCard => _discard[^1];

    /// <inheritdoc />
    public UnoColor Color { get; private set; }

    /// <inheritdoc />
    public int DrawPileCount => _drawPile.Count;

    /// <inheritdoc />
    public int PendingDraw { get; private set; }

    /// <inheritdoc />
    public UnoCard? DrawnCard { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Scores => _scores;

    /// <inheritdoc />
    public int? UnoPending { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Ranking => _ranking;

    /// <inheritdoc />
    public IReadOnlyList<UnoEvent> Events => _events;

    /// <inheritdoc />
    public int Actions { get; private set; }

    /// <inheritdoc />
    public UnoHandResult? LastResult { get; private set; }

    /// <inheritdoc />
    public bool IsOver => Phase == UnoPhase.GameOver;

    /// <inheritdoc />
    public int? Winner => !IsOver ? null : Rules.End == UnoEndMode.Pyramid ? _ranking[0] : LastResult?.Winner;

    /// <inheritdoc />
    public IReadOnlyList<UnoCard> Hand(int player) => _hands[player];

    /// <inheritdoc />
    public int HandSize(int player) => _handSizes[player];

    /// <inheritdoc />
    public bool SaidUno(int player) => _saidUno[player];

    /// <inheritdoc />
    public IReadOnlyList<UnoCard> Playable(int player)
    {
        if (Phase != UnoPhase.Playing || player != Turn)
        {
            return [];
        }

        if (DrawnCard is { } drawn)
        {
            return CanPlay(player, drawn, out _) ? [drawn] : [];
        }

        return [.. _hands[player].Where(c => CanPlay(player, c, out _)).Distinct()];
    }

    /// <inheritdoc />
    public bool TryPlay(int player, UnoCard card, UnoColor? color, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        var hand = _hands[player];
        var index = hand.IndexOf(card);
        error = index < 0 ? "You do not have that card."
            : DrawnCard is { } drawn && card != drawn ? "You can play only the card you drew, or keep it."
            : card.IsWild && color is null or UnoColor.Wild ? "Choose a colour for the wild card."
            : null;
        if (error is not null || !CanPlay(player, card, out error))
        {
            return false;
        }

        BeginAction();
        hand.RemoveAt(index);
        _discard.Add(card);
        Color = card.IsWild ? color!.Value : card.Color;
        DrawnCard = null;
        _events.Add(new UnoEvent(player, UnoEventKind.Played, card, Color: card.IsWild ? Color : null));
        if (hand.Count == 1 && !_saidUno[player])
        {
            UnoPending = player;
        }
        else if (hand.Count != 1)
        {
            _saidUno[player] = false;
        }

        var twoPlaying = ActiveCount == 2;
        var next = Step(player, 1);
        switch (card.Value)
        {
            case UnoValue.Skip:
                _events.Add(new UnoEvent(next, UnoEventKind.Skipped));
                Turn = Step(player, 2);
                break;
            case UnoValue.Reverse when twoPlaying:
                // With two players, a Reverse works like a Skip.
                Direction = -Direction;
                _events.Add(new UnoEvent(next, UnoEventKind.Skipped));
                Turn = player;
                break;
            case UnoValue.Reverse:
                Direction = -Direction;
                Turn = Step(player, 1);
                break;
            case UnoValue.DrawTwo or UnoValue.WildDrawFour when Rules.Stacking:
                PendingDraw += card.DrawCount;
                Turn = next;
                break;
            case UnoValue.DrawTwo or UnoValue.WildDrawFour:
                DrawAndMissTurn(next, card.DrawCount);
                Turn = Step(player, 2);
                break;
            default:
                Turn = next;
                break;
        }

        if (hand.Count == 0)
        {
            EmptiedHand(player);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryDraw(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        if (DrawnCard is not null)
        {
            error = "You have already drawn. Play the card you drew, or keep it.";
            return false;
        }

        BeginAction();
        if (PendingDraw > 0)
        {
            _events.Add(new UnoEvent(player, UnoEventKind.Drew, Count: Give(player, PendingDraw)));
            PendingDraw = 0;
            Turn = Step(player, 1);
            return true;
        }

        var count = 0;
        UnoCard? last;
        do
        {
            last = DrawOne();
            if (last is null)
            {
                break;
            }

            _hands[player].Add(last);
            count++;
        }
        while (Rules.DrawUntilPlayable && !CanPlay(player, last, out _));

        _saidUno[player] = false;
        _events.Add(new UnoEvent(player, UnoEventKind.Drew, Count: count));
        if (last is not null && CanPlay(player, last, out _))
        {
            DrawnCard = last;
        }
        else
        {
            Turn = Step(player, 1);
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

        if (DrawnCard is null)
        {
            error = "Draw a card first.";
            return false;
        }

        BeginAction();
        DrawnCard = null;
        _events.Add(new UnoEvent(player, UnoEventKind.Passed));
        Turn = Step(player, 1);
        return true;
    }

    /// <inheritdoc />
    public bool TryCallUno(int player, out string? error)
    {
        CheckPlayer(player);
        var calls = UnoPending == player
            || (Turn == player && !_saidUno[player] && _hands[player].Count == 2 && Playable(player).Count > 0);
        error = Phase != UnoPhase.Playing ? "This hand is over."
            : calls ? null
            : _saidUno[player] ? "You have already called UNO."
            : "Call UNO when you are about to have one card left.";
        if (error is not null)
        {
            return false;
        }

        UnoPending = null;
        _saidUno[player] = true;
        Actions++;
        _events.Add(new UnoEvent(player, UnoEventKind.CalledUno));
        return true;
    }

    /// <inheritdoc />
    public bool TryCatch(int player, int target, out string? error)
    {
        CheckPlayer(player);
        error = target < 0 || target >= Players ? "There is no such player."
            : target == player ? "You cannot catch yourself."
            : UnoPending != target ? "That player cannot be caught now."
            : null;
        if (error is not null)
        {
            return false;
        }

        UnoPending = null;
        Actions++;
        _events.Add(new UnoEvent(target, UnoEventKind.Caught, Count: Give(target, 2), By: player));
        return true;
    }

    /// <inheritdoc />
    public bool TryNextHand(int hand, out string? error)
    {
        error = hand < HandNumber ? null
            : hand > HandNumber ? "There is no such hand."
            : Phase != UnoPhase.HandOver ? "This hand is not finished yet."
            : null;
        if (error is not null || hand < HandNumber)
        {
            return error is null;
        }

        HandNumber++;
        Deal();
        return true;
    }

    /// <summary>
    /// Shuffles a full deck, deals each player their cards and turns up the first card (a wild card goes back under the
    /// draw pile and the next one is turned up). A Skip turned up skips the hand's starter, a Reverse makes the player
    /// before them start in the other direction, and a Draw Two makes the starter draw 2 and miss their turn.
    /// </summary>
    private void Deal()
    {
        var deck = UnoCard.Deck().ToArray();
        _shuffle(deck);
        _drawPile.Clear();
        _drawPile.AddRange(deck);
        _discard.Clear();
        BeginAction();
        DrawnCard = null;
        PendingDraw = 0;
        Direction = 1;
        Phase = UnoPhase.Playing;
        for (var p = 0; p < Players; p++)
        {
            _hands[p].Clear();
            _saidUno[p] = false;
            Give(p, _handSizes[p]);
        }

        var first = DrawOne()!;
        while (first.IsWild)
        {
            _drawPile.Add(first);
            first = DrawOne()!;
        }

        _discard.Add(first);
        Color = first.Color;
        var starter = (HandNumber - 1) % Players;
        _events.Add(new UnoEvent(starter, UnoEventKind.TurnedUp, first));
        switch (first.Value)
        {
            case UnoValue.Skip:
                _events.Add(new UnoEvent(starter, UnoEventKind.Skipped));
                Turn = Step(starter, 1);
                break;
            case UnoValue.Reverse:
                Direction = -1;
                Turn = Step(starter, 1);
                break;
            case UnoValue.DrawTwo:
                DrawAndMissTurn(starter, 2);
                Turn = Step(starter, 1);
                break;
            default:
                Turn = starter;
                break;
        }
    }

    /// <summary>
    /// Starts a new action: clears the last action's events, and ends the chance to catch a player who did not call UNO.
    /// </summary>
    private void BeginAction()
    {
        _events.Clear();
        UnoPending = null;
        Actions++;
    }

    /// <summary>
    /// Whether <paramref name="player"/> may play <paramref name="card"/> on the pile. With cards to draw from a stack,
    /// only the same draw card; a Wild Draw Four only without a card of the colour to play; any other wild card; otherwise
    /// a card of the colour to play or of the top card's number or symbol.
    /// </summary>
    private bool CanPlay(int player, UnoCard card, out string? error)
    {
        error = PendingDraw > 0
            ? card.Value == TopCard.Value ? null : "Answer with the same draw card, or draw the cards."
            : card.Value == UnoValue.WildDrawFour && _hands[player].Any(c => c.Color == Color)
                ? "You can play a Wild Draw Four only when you have no card of the colour to play."
            : card.IsWild || card.Color == Color || (!TopCard.IsWild && card.Value == TopCard.Value) ? null
            : "That card does not match the colour or the symbol on the pile.";
        return error is null;
    }

    /// <summary>
    /// <paramref name="player"/> has played their last card. In a pyramid, they are dealt one card fewer than last time,
    /// or after their single card, they finish (with one player left, the game is over). Otherwise they win the hand: the
    /// next player first draws any cards stacked for them, then the winner scores the cards left in the other hands.
    /// </summary>
    private void EmptiedHand(int player)
    {
        if (Rules.End == UnoEndMode.Pyramid)
        {
            if (_handSizes[player] > 1)
            {
                _handSizes[player]--;
                _events.Add(new UnoEvent(player, UnoEventKind.NewCards, Count: Give(player, _handSizes[player])));
                return;
            }

            _ranking.Add(player);
            _events.Add(new UnoEvent(player, UnoEventKind.Finished, Count: _ranking.Count));
            if (ActiveCount == 1)
            {
                _ranking.Add(Enumerable.Range(0, Players).Single(p => !_ranking.Contains(p)));
                Phase = UnoPhase.GameOver;
                Turn = _ranking[0];
            }
            else if (Turn == player)
            {
                Turn = Step(player, 1);
            }

            return;
        }

        if (PendingDraw > 0)
        {
            _events.Add(new UnoEvent(Turn, UnoEventKind.Drew, Count: Give(Turn, PendingDraw)));
            PendingDraw = 0;
        }

        var points = _hands.Sum(hand => hand.Sum(c => c.Points));
        LastResult = new UnoHandResult(player, points);
        _events.Add(new UnoEvent(player, UnoEventKind.Finished, Count: points));
        if (Rules.End == UnoEndMode.Score)
        {
            _scores[player] += points;
        }

        Turn = player;
        Phase = Rules.End == UnoEndMode.OneGame || _scores[player] >= Rules.Target ? UnoPhase.GameOver : UnoPhase.HandOver;
    }

    private void DrawAndMissTurn(int player, int count)
    {
        _events.Add(new UnoEvent(player, UnoEventKind.Drew, Count: Give(player, count)));
        _events.Add(new UnoEvent(player, UnoEventKind.Skipped));
    }

    /// <summary>
    /// <paramref name="player"/> draws up to <paramref name="count"/> cards (fewer if there are no more). Returns how many.
    /// </summary>
    private int Give(int player, int count)
    {
        var given = 0;
        while (given < count && DrawOne() is { } card)
        {
            _hands[player].Add(card);
            given++;
        }

        _saidUno[player] = false;
        return given;
    }

    /// <summary>
    /// The top card of the draw pile, or null if there is none. When the draw pile is empty, the discard pile but its top
    /// card is shuffled into a new draw pile.
    /// </summary>
    private UnoCard? DrawOne()
    {
        if (_drawPile.Count == 0 && _discard.Count > 1)
        {
            var rest = _discard.GetRange(0, _discard.Count - 1).ToArray();
            _discard.RemoveRange(0, rest.Length);
            _shuffle(rest);
            _drawPile.AddRange(rest);
        }

        if (_drawPile.Count == 0)
        {
            return null;
        }

        var card = _drawPile[0];
        _drawPile.RemoveAt(0);
        return card;
    }

    /// <summary>
    /// The player <paramref name="steps"/> places after <paramref name="from"/> in the direction of play, passing over
    /// players who have finished a pyramid.
    /// </summary>
    private int Step(int from, int steps)
    {
        var player = from;
        for (var moved = 0; moved < steps;)
        {
            player = (player + Direction + Players) % Players;
            if (!_ranking.Contains(player))
            {
                moved++;
            }
        }

        return player;
    }

    private int ActiveCount => Players - _ranking.Count;

    private void CheckPlayer(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, Players);
    }

    /// <summary>
    /// Checks that the game is waiting for <paramref name="player"/> to play.
    /// </summary>
    private bool TryCheckTurn(int player, out string? error)
    {
        CheckPlayer(player);
        error = Phase == UnoPhase.GameOver ? "The game is over."
            : Phase == UnoPhase.HandOver ? "This hand is over."
            : player != Turn ? "It is not your turn."
            : null;
        return error is null;
    }
}
