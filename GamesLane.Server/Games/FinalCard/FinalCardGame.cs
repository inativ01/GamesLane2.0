namespace GamesLane.Server.Games.FinalCard;

/// <summary>
/// Final Card rules for one game. <c>shuffle</c> shuffles the deck before each deal, which gives player 0 the first cards, player
/// 1 the next ones, and so on, then turns up the next card (for tests); it also shuffles the discard pile into a new draw
/// pile when the draw pile runs out. Hand 1 is started by player 0, each later hand by the next player.
/// </summary>
public class FinalCardGame : IFinalCardGame
{
    private Action<FinalCardCard[]> _shuffle;
    private readonly List<FinalCardCard>[] _hands;
    private readonly List<FinalCardCard> _drawPile = [];
    private readonly List<FinalCardCard> _discard = [];
    private readonly int[] _scores;
    private readonly int[] _handSizes;
    private readonly bool[] _calledFinalCard;
    private readonly List<int> _ranking = [];
    private readonly List<FinalCardEvent> _events = [];

    // The player of the Wild Draw Four that the player to play can challenge (or null), and whether they held a card of
    // the colour that was to play.
    private int? _challengeable;
    private bool _heldTheColor;

    /// <summary>
    /// Deals the first hand for <paramref name="players"/> players (2 to 10).
    /// </summary>
    public FinalCardGame(int players, FinalCardRules? rules = null, Action<FinalCardCard[]>? shuffle = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, FinalCardRules.MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, FinalCardRules.MaxPlayers);
        Rules = rules ?? new FinalCardRules();
        _shuffle = shuffle ?? Random.Shared.Shuffle;
        _hands = [.. Enumerable.Range(0, players).Select(_ => new List<FinalCardCard>())];
        _scores = new int[players];
        _handSizes = [.. Enumerable.Repeat(FinalCardRules.HandSize, players)];
        _calledFinalCard = new bool[players];
        Deal();
    }

    /// <summary>
    /// Gives a game loaded from a save its shuffle again (see the constructor).
    /// </summary>
    internal void Reattach(Action<FinalCardCard[]>? shuffle) => _shuffle = shuffle ?? Random.Shared.Shuffle;

    /// <inheritdoc />
    public FinalCardRules Rules { get; }

    /// <inheritdoc />
    public int Players => _hands.Length;

    /// <inheritdoc />
    public int HandNumber { get; private set; } = 1;

    /// <inheritdoc />
    public FinalCardPhase Phase { get; private set; }

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public int Direction { get; private set; } = 1;

    /// <inheritdoc />
    public FinalCardCard TopCard => _discard[^1];

    /// <inheritdoc />
    public FinalCardColor Color { get; private set; }

    /// <inheritdoc />
    public int DrawPileCount => _drawPile.Count;

    /// <inheritdoc />
    public int PendingDraw { get; private set; }

    /// <inheritdoc />
    public FinalCardCard? DrawnCard { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Scores => _scores;

    /// <inheritdoc />
    public IReadOnlyList<int> Ranking => _ranking;

    /// <inheritdoc />
    public IReadOnlyList<FinalCardEvent> Events => _events;

    /// <inheritdoc />
    public int Actions { get; private set; }

    /// <inheritdoc />
    public FinalCardHandResult? LastResult { get; private set; }

    /// <inheritdoc />
    public bool IsOver => Phase == FinalCardPhase.GameOver;

    /// <inheritdoc />
    public int? Winner => !IsOver ? null : Rules.End == FinalCardEndMode.Pyramid ? _ranking[0] : LastResult?.Winner;

    /// <inheritdoc />
    public IReadOnlyList<FinalCardCard> Hand(int player) => _hands[player];

    /// <inheritdoc />
    public int HandSize(int player) => _handSizes[player];

    /// <inheritdoc />
    public bool CalledFinalCard(int player) => _calledFinalCard[player];

    /// <inheritdoc />
    public bool CanChallenge => Phase == FinalCardPhase.Playing && _challengeable is not null;

    /// <inheritdoc />
    public bool CanBeCaught(int player) => Phase == FinalCardPhase.Playing && _hands[player].Count == 1 && !_calledFinalCard[player];

    /// <inheritdoc />
    public IReadOnlyList<FinalCardCard> Playable(int player)
    {
        if (Phase != FinalCardPhase.Playing || player != Turn)
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
    public bool TryPlay(int player, FinalCardCard card, FinalCardColor? color, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        var hand = _hands[player];
        var index = hand.IndexOf(card);
        error = index < 0 ? "You do not have that card."
            : DrawnCard is { } drawn && card != drawn ? "You can play only the card you drew, or keep it."
            : card.IsWild && color is null or FinalCardColor.Wild ? "Choose a colour for the wild card."
            : null;
        if (error is not null || !CanPlay(player, card, out error))
        {
            return false;
        }

        var answersStack = PendingDraw > 0;
        var heldTheColor = hand.Any(c => c.Color == Color);
        BeginAction();
        hand.RemoveAt(index);
        _discard.Add(card);
        Color = card.IsWild ? color!.Value : card.Color;
        DrawnCard = null;
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Played, card, Color: card.IsWild ? Color : null));
        if (hand.Count != 1)
        {
            _calledFinalCard[player] = false;
        }

        var twoPlaying = ActiveCount == 2;
        var next = Step(player, 1);
        switch (card.Value)
        {
            case FinalCardValue.Skip:
                _events.Add(new FinalCardEvent(next, FinalCardEventKind.Skipped));
                Turn = Step(player, 2);
                break;
            case FinalCardValue.Reverse when twoPlaying:
                // With two players, a Reverse works like a Skip.
                Direction = -Direction;
                _events.Add(new FinalCardEvent(next, FinalCardEventKind.Skipped));
                Turn = player;
                break;
            case FinalCardValue.Reverse:
                Direction = -Direction;
                Turn = Step(player, 1);
                break;
            case FinalCardValue.DrawTwo or FinalCardValue.WildDrawFour when Rules.CanBeStacked(card) || answersStack
                || (card.Value == FinalCardValue.WildDrawFour && Rules.WildDrawFour == FinalCardWildDrawFour.Challenge && hand.Count > 0):
                // The next player draws them all, unless they answer with a draw card or challenge a Wild Draw Four (not
                // one played on a stack, nor a player's last card).
                PendingDraw += card.DrawCount;
                if (card.Value == FinalCardValue.WildDrawFour && Rules.WildDrawFour == FinalCardWildDrawFour.Challenge
                    && !answersStack && hand.Count > 0)
                {
                    _challengeable = player;
                    _heldTheColor = heldTheColor;
                }

                Turn = next;
                break;
            case FinalCardValue.DrawTwo or FinalCardValue.WildDrawFour:
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
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.Drew, Count: Give(player, PendingDraw)));
            PendingDraw = 0;
            Turn = Step(player, 1);
            return true;
        }

        var count = 0;
        FinalCardCard? last;
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

        _calledFinalCard[player] = false;
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Drew, Count: count));
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
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Passed));
        Turn = Step(player, 1);
        return true;
    }

    /// <inheritdoc />
    public bool TryChallenge(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        if (_challengeable is not { } offender)
        {
            error = "There is no Wild Draw Four to challenge.";
            return false;
        }

        var count = PendingDraw;
        var won = _heldTheColor;
        BeginAction();
        PendingDraw = 0;
        if (won)
        {
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.ChallengeWon, Count: Give(offender, count), By: offender));
        }
        else
        {
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.ChallengeLost, Count: Give(player, count + 2), By: offender));
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.Skipped));
            Turn = Step(player, 1);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryCallFinalCard(int player, out string? error)
    {
        CheckPlayer(player);
        var protects = Phase == FinalCardPhase.Playing && !_calledFinalCard[player]
            && (_hands[player].Count == 1 || (Turn == player && _hands[player].Count == 2 && Playable(player).Count > 0));
        var caught = Enumerable.Range(0, Players).Where(p => p != player && CanBeCaught(p)).ToList();
        error = Phase != FinalCardPhase.Playing ? "This hand is over."
            : !protects && caught.Count == 0 ? "Nobody to catch."
            : null;
        if (error is not null)
        {
            return false;
        }

        Actions++;
        if (protects)
        {
            _calledFinalCard[player] = true;
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.CalledFinalCard));
        }

        foreach (var target in caught)
        {
            _events.Add(new FinalCardEvent(target, FinalCardEventKind.Caught, Count: Give(target, 2), By: player));
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryNextHand(int hand, out string? error)
    {
        error = hand < HandNumber ? null
            : hand > HandNumber ? "There is no such hand."
            : Phase != FinalCardPhase.HandOver ? "This hand is not finished yet."
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
        var deck = FinalCardCard.Deck().ToArray();
        _shuffle(deck);
        _drawPile.Clear();
        _drawPile.AddRange(deck);
        _discard.Clear();
        BeginAction();
        DrawnCard = null;
        PendingDraw = 0;
        Direction = 1;
        Phase = FinalCardPhase.Playing;
        for (var p = 0; p < Players; p++)
        {
            _hands[p].Clear();
            _calledFinalCard[p] = false;
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
        _events.Add(new FinalCardEvent(starter, FinalCardEventKind.TurnedUp, first));
        switch (first.Value)
        {
            case FinalCardValue.Skip:
                _events.Add(new FinalCardEvent(starter, FinalCardEventKind.Skipped));
                Turn = Step(starter, 1);
                break;
            case FinalCardValue.Reverse:
                Direction = -1;
                Turn = Step(starter, 1);
                break;
            case FinalCardValue.DrawTwo:
                DrawAndMissTurn(starter, 2);
                Turn = Step(starter, 1);
                break;
            default:
                Turn = starter;
                break;
        }
    }

    /// <summary>
    /// Starts a new action: clears the last action's events, and ends the chance to challenge a Wild Draw Four.
    /// </summary>
    private void BeginAction()
    {
        _events.Clear();
        _challengeable = null;
        Actions++;
    }

    /// <summary>
    /// Whether <paramref name="player"/> may play <paramref name="card"/> on the pile. With cards to draw from a stack,
    /// only a draw card the stacking rule allows on the top card; with the rule, a Wild Draw Four only without a card of
    /// the colour to play; any other wild card; otherwise a card of the colour to play or of the top card's number or symbol.
    /// </summary>
    private bool CanPlay(int player, FinalCardCard card, out string? error)
    {
        error = PendingDraw > 0
            ? Rules.CanStack(card, TopCard) ? null : "Answer with a draw card you can stack, or draw the cards."
            : card.Value == FinalCardValue.WildDrawFour && Rules.WildDrawFour == FinalCardWildDrawFour.NoColor
                && _hands[player].Any(c => c.Color == Color)
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
        if (Rules.End == FinalCardEndMode.Pyramid)
        {
            if (_handSizes[player] > 1)
            {
                _handSizes[player]--;
                _events.Add(new FinalCardEvent(player, FinalCardEventKind.NewCards, Count: Give(player, _handSizes[player])));
                return;
            }

            _ranking.Add(player);
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.Finished, Count: _ranking.Count));
            if (ActiveCount == 1)
            {
                _ranking.Add(Enumerable.Range(0, Players).Single(p => !_ranking.Contains(p)));
                Phase = FinalCardPhase.GameOver;
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
            _events.Add(new FinalCardEvent(Turn, FinalCardEventKind.Drew, Count: Give(Turn, PendingDraw)));
            PendingDraw = 0;
        }

        var points = _hands.Sum(hand => hand.Sum(c => c.Points));
        LastResult = new FinalCardHandResult(player, points);
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Finished, Count: points));
        if (Rules.End == FinalCardEndMode.Score)
        {
            _scores[player] += points;
        }

        Turn = player;
        Phase = Rules.End == FinalCardEndMode.OneGame || _scores[player] >= Rules.Target ? FinalCardPhase.GameOver : FinalCardPhase.HandOver;
    }

    private void DrawAndMissTurn(int player, int count)
    {
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Drew, Count: Give(player, count)));
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Skipped));
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

        _calledFinalCard[player] = false;
        return given;
    }

    /// <summary>
    /// The top card of the draw pile, or null if there is none. When the draw pile is empty, the discard pile but its top
    /// card is shuffled into a new draw pile.
    /// </summary>
    private FinalCardCard? DrawOne()
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
        error = Phase == FinalCardPhase.GameOver ? "The game is over."
            : Phase == FinalCardPhase.HandOver ? "This hand is over."
            : player != Turn ? "It is not your turn."
            : null;
        return error is null;
    }
}
