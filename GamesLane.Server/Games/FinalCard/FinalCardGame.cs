namespace GamesLane.Server.Games.FinalCard;

/// <summary>
/// Final Card rules for one game, in US or Israeli Style. <c>shuffle</c> shuffles the deck before each deal, which gives player 0
/// the first cards, player 1 the next ones, and so on, then turns up the next card (for tests); it also shuffles the discard
/// pile into a new draw pile when the draw pile runs out. Hand 1 is started by player 0, each later hand by the next player.
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
    private readonly int[] _penaltyCards;
    private readonly List<int> _ranking = [];
    private readonly List<FinalCardEvent> _events = [];

    // Whether the turn passed while a player still had penalty cards to draw: the player to play then waits for them.
    private bool _turnHeld;

    // The player of the Wild Draw Four that the player to play can challenge (or null), and whether they held a card of
    // the color that was to play.
    private int? _challengeable;
    private bool _heldTheColor;

    // The card to follow: the top card of the discard pile, but the card under a +3 or +3 Breaker.
    private FinalCardCard _leading = null!;

    // Israeli Style: the color of the TAKI the player to play is running (or null), and the player of the +3 nobody has
    // answered yet (or null).
    private FinalCardColor? _takiColor;
    private int? _threeBy;

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
        _handSizes = [.. Enumerable.Repeat(Rules.CardsDealt, players)];
        _calledFinalCard = new bool[players];
        _penaltyCards = new int[players];
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
    public FinalCardColor? TakiColor => _takiColor;

    /// <inheritdoc />
    public int? ThreeBy => _threeBy;

    /// <inheritdoc />
    public bool AnyCard => Rules.IsIsraeli && _leading.Value == FinalCardValue.Crown;

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
    public int PenaltyCards(int player) => _penaltyCards[player];

    /// <inheritdoc />
    public bool IsWaitingForPenalty => Phase == FinalCardPhase.Playing && _turnHeld && _penaltyCards.Any(n => n > 0);

    /// <inheritdoc />
    public bool CanChallenge => Phase == FinalCardPhase.Playing && _challengeable is not null;

    /// <inheritdoc />
    public bool CanBeCaught(int player) =>
        Phase == FinalCardPhase.Playing && _hands[player].Count == 1 && !_calledFinalCard[player] && _penaltyCards[player] == 0
        && !(player == Turn && _takiColor is not null);

    /// <inheritdoc />
    public IReadOnlyList<FinalCardCard> Playable(int player)
    {
        if (Phase != FinalCardPhase.Playing || _penaltyCards[player] > 0)
        {
            return [];
        }

        if (player != Turn || IsWaitingForPenalty)
        {
            return _threeBy is { } by && by != player ? [.. _hands[player].Where(c => c.Value == FinalCardValue.BreakThree).Distinct()] : [];
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
        var breaks = _threeBy is { } by && by != player && card.Value == FinalCardValue.BreakThree;
        if (!(breaks ? TryCheckBreaker(player, out error) : TryCheckTurn(player, out error)))
        {
            return false;
        }

        var hand = _hands[player];
        var index = hand.IndexOf(card);
        var choosesColor = ChoosesColor(card);
        error = index < 0 ? "You do not have that card."
            : DrawnCard is { } drawn && card != drawn ? "You can play only the card you drew, or keep it."
            : choosesColor && color is null or FinalCardColor.Wild ? "Choose the color to play."
            : null;
        if (error is not null || !CanPlay(player, card, out error))
        {
            return false;
        }

        var answersStack = PendingDraw > 0;
        var heldTheColor = hand.Any(c => c.Color == Color);
        var inTaki = _takiColor is { } run && (card.Color == run || card.Value == FinalCardValue.Taki);
        BeginAction();
        hand.RemoveAt(index);
        _discard.Add(card);
        if (card.Value is not (FinalCardValue.DrawThree or FinalCardValue.BreakThree))
        {
            _leading = card;
        }

        Color = choosesColor ? color!.Value : card.IsWild ? Color : card.Color;
        DrawnCard = null;
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Played, card, Color: choosesColor ? Color : null));
        if (hand.Count != 1)
        {
            _calledFinalCard[player] = false;
        }

        if (breaks)
        {
            // The +3 Breaker answers the +3: its player draws the 3 cards instead, and the player to play plays on.
            _events.Add(new FinalCardEvent(_threeBy!.Value, FinalCardEventKind.Penalty, Count: Owe(_threeBy.Value, 3)));
            _threeBy = null;
        }
        else if (inTaki || (Rules.IsIsraeli && card.IsTaki))
        {
            // A TAKI (or SuperTAKI, in the color to play) starts a run of its color; the run goes on until the player
            // closes it, or has no card left to play in it.
            _takiColor = Color;
            if (hand.Count == 0 || Playable(player).Count == 0)
            {
                CloseTaki(player);
            }
        }
        else
        {
            _takiColor = null;
            Apply(player, card, answersStack, heldTheColor);
        }

        PassTurn();
        if (hand.Count == 0 && !(Rules.IsIsraeli && TopCard.Value == FinalCardValue.Plus && Turn == player))
        {
            EmptiedHand(player);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryCloseTaki(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        if (_takiColor is null)
        {
            error = "There is no TAKI to close.";
            return false;
        }

        BeginAction();
        CloseTaki(player);
        PassTurn();
        return true;
    }

    /// <inheritdoc />
    public bool TryDraw(int player, out string? error)
    {
        CheckPlayer(player);
        if (Phase == FinalCardPhase.Playing && _penaltyCards[player] > 0)
        {
            Actions++;
            _events.Clear();
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.Drew, Count: Give(player, _penaltyCards[player])));
            _penaltyCards[player] = 0;
            _turnHeld &= _penaltyCards.Any(n => n > 0);
            error = null;
            return true;
        }

        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        error = DrawnCard is not null ? "You have already drawn. Play the card you drew, or keep it."
            : _takiColor is not null ? "Close the TAKI first."
            : null;
        if (error is not null)
        {
            return false;
        }

        BeginAction();
        if (_threeBy is { } by)
        {
            // Nobody breaks the +3: every other player draws 3 (the others as penalty cards), and the player to play plays on.
            _threeBy = null;
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.Drew, Count: Give(player, 3)));
            foreach (var other in Enumerable.Range(0, Players).Where(p => p != by && p != player && !_ranking.Contains(p)))
            {
                _events.Add(new FinalCardEvent(other, FinalCardEventKind.Penalty, Count: Owe(other, 3)));
            }

            PassTurn();
            return true;
        }

        if (PendingDraw > 0)
        {
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.Drew, Count: Give(player, PendingDraw)));
            PendingDraw = 0;
            Turn = Step(player, 1);
            PassTurn();
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
        if (!Rules.IsIsraeli && last is not null && CanPlay(player, last, out _))
        {
            DrawnCard = last;
        }
        else
        {
            Turn = Step(player, 1);
            PassTurn();
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
        PassTurn();
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
            error = "There is no Change Color +4 to challenge.";
            return false;
        }

        var count = PendingDraw;
        var won = _heldTheColor;
        BeginAction();
        PendingDraw = 0;
        if (won)
        {
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.ChallengeWon, Count: Owe(offender, count), By: offender));
        }
        else
        {
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.ChallengeLost, Count: Owe(player, count + 2), By: offender));
            _events.Add(new FinalCardEvent(player, FinalCardEventKind.Skipped));
            Turn = Step(player, 1);
            PassTurn();
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryCallFinalCard(int player, out string? error)
    {
        CheckPlayer(player);
        var protects = Phase == FinalCardPhase.Playing && !_calledFinalCard[player] && _penaltyCards[player] == 0
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
            _events.Add(new FinalCardEvent(target, FinalCardEventKind.Caught, Count: Owe(target, Rules.CaughtPenalty), By: player));
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
    /// Shuffles a full deck of the style, deals each player their cards and turns up the first card (a card without a
    /// color goes back under the draw pile and the next one is turned up). A Stop turned up skips the hand's starter, a
    /// Change Direction makes the player before them start in the other direction, and a +2 makes the starter draw 2 (as
    /// penalty cards) and miss their turn.
    /// </summary>
    private void Deal()
    {
        var deck = Rules.Deck().ToArray();
        _shuffle(deck);
        _drawPile.Clear();
        _drawPile.AddRange(deck);
        _discard.Clear();
        BeginAction();
        DrawnCard = null;
        PendingDraw = 0;
        _takiColor = null;
        _threeBy = null;
        Direction = 1;
        Phase = FinalCardPhase.Playing;
        for (var p = 0; p < Players; p++)
        {
            _hands[p].Clear();
            _calledFinalCard[p] = false;
            _penaltyCards[p] = 0;
            Give(p, _handSizes[p]);
        }

        var first = DrawOne()!;
        while (first.IsWild)
        {
            _drawPile.Add(first);
            first = DrawOne()!;
        }

        _discard.Add(first);
        _leading = first;
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

        PassTurn();
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
    /// only a draw card the stacking rule allows on the top card (in Israeli Style, a +2 or a Crown); with the rule, a
    /// Change Color +4 only without a card of the color to play; any other card without a color; otherwise a card of the
    /// color to play or of the leading card's number or symbol. In Israeli Style, a +3 nobody has answered yet can only be
    /// answered with a +3 Breaker (by any other player) or a Crown (by the player to play); during a TAKI, only a card of
    /// its color, a SuperTAKI, a Change Color, a Crown or a +3; and after a Crown, any card.
    /// </summary>
    private bool CanPlay(int player, FinalCardCard card, out string? error)
    {
        error = Rules.IsIsraeli && _threeBy is { } by
            ? (card.Value == FinalCardValue.BreakThree && player != by) || (card.Value == FinalCardValue.Crown && player == Turn) ? null
                : "Answer the +3 with a +3 Breaker or a Crown, or draw 3 cards."
            : Rules.IsIsraeli && PendingDraw > 0
                ? card.Value is FinalCardValue.DrawTwo or FinalCardValue.Crown ? null : "Answer with a +2 or a Crown, or draw the cards."
            : PendingDraw > 0
                ? Rules.CanStack(card, TopCard) ? null : "Answer with a draw card you can stack, or draw the cards."
            : _takiColor is { } run
                ? card.Color == run || (card.IsWild && card.Value != FinalCardValue.BreakThree) ? null
                    : "Play a card of the TAKI's color, or close the TAKI."
            : card.Value == FinalCardValue.WildDrawFour && Rules.WildDrawFour == FinalCardWildDrawFour.NoColor
                && _hands[player].Any(c => c.Color == Color)
                ? "You can play a Change Color +4 only when you have no card of the color to play."
            : AnyCard || card.IsWild || card.Color == Color || (!_leading.IsWild && card.Value == _leading.Value) ? null
            : "That card does not match the color or the symbol on the pile.";
        return error is null;
    }

    /// <summary>
    /// Whether <paramref name="card"/> needs a color chosen: a Change Color, a Change Color +4, or a SuperTAKI after a Crown
    /// (otherwise a SuperTAKI takes the color to play).
    /// </summary>
    private bool ChoosesColor(FinalCardCard card) =>
        card.Value is FinalCardValue.Wild or FinalCardValue.WildDrawFour || (card.IsTaki && card.IsWild && AnyCard && _takiColor is null);

    /// <summary>
    /// The action of <paramref name="card"/>, just played by <paramref name="player"/> (or that closed their TAKI), and who
    /// plays next. A Stop skips the next player; a Change Direction reverses the direction of play (with two players, in US
    /// Style it works like a Stop, in Israeli Style it changes nothing else); a +2 or Change Color +4 makes the next player
    /// draw (or answer it), or with the challenge rule challenge it. In Israeli Style: after a Plus or a Crown the player
    /// plays again (a Crown also cancels the cards to draw); a +3 waits for an answer from the next player (or a +3
    /// Breaker from anyone); a +3 Breaker played on another card makes its player draw 3.
    /// </summary>
    private void Apply(int player, FinalCardCard card, bool answersStack, bool heldTheColor)
    {
        var next = Step(player, 1);
        switch (card.Value)
        {
            case FinalCardValue.Skip:
                _events.Add(new FinalCardEvent(next, FinalCardEventKind.Skipped));
                Turn = Step(player, 2);
                break;
            case FinalCardValue.Reverse when ActiveCount == 2 && !Rules.IsIsraeli:
                // With two players, a Change Direction works like a Stop.
                Direction = -Direction;
                _events.Add(new FinalCardEvent(next, FinalCardEventKind.Skipped));
                Turn = player;
                break;
            case FinalCardValue.Reverse:
                Direction = -Direction;
                Turn = Step(player, 1);
                break;
            case FinalCardValue.DrawTwo or FinalCardValue.WildDrawFour when Rules.CanBeStacked(card) || answersStack
                || (card.Value == FinalCardValue.WildDrawFour && Rules.WildDrawFour == FinalCardWildDrawFour.Challenge && _hands[player].Count > 0):
                // The next player draws them all, unless they answer with a draw card or challenge a Change Color +4 (not
                // one played on a stack, nor a player's last card).
                PendingDraw += card.DrawCount;
                if (card.Value == FinalCardValue.WildDrawFour && Rules.WildDrawFour == FinalCardWildDrawFour.Challenge
                    && !answersStack && _hands[player].Count > 0)
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
            case FinalCardValue.Plus:
                Turn = player;
                break;
            case FinalCardValue.Crown:
                PendingDraw = 0;
                _threeBy = null;
                Turn = player;
                break;
            case FinalCardValue.DrawThree:
                _threeBy = player;
                Turn = next;
                break;
            case FinalCardValue.BreakThree:
                _events.Add(new FinalCardEvent(player, FinalCardEventKind.Penalty, Count: Owe(player, 3)));
                Turn = next;
                break;
            default:
                Turn = next;
                break;
        }
    }

    /// <summary>
    /// <paramref name="player"/>'s TAKI is closed: the action of its last card is carried out (a TAKI or SuperTAKI played on
    /// its own has none).
    /// </summary>
    private void CloseTaki(int player)
    {
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.ClosedTaki));
        _takiColor = null;
        Apply(player, TopCard, answersStack: false, heldTheColor: false);
    }

    /// <summary>
    /// <paramref name="player"/> has played their last card. In a pyramid, they are dealt one card fewer than last time,
    /// or after their single card, they finish (with one player left, the game is over). Otherwise they win the hand: the
    /// next player first draws any cards stacked for them and every player their penalty cards, then the winner scores the
    /// cards left in the other hands.
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

        for (var p = 0; p < Players; p++)
        {
            if (_penaltyCards[p] > 0)
            {
                _events.Add(new FinalCardEvent(p, FinalCardEventKind.Drew, Count: Give(p, _penaltyCards[p])));
                _penaltyCards[p] = 0;
            }
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
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Penalty, Count: Owe(player, count)));
        _events.Add(new FinalCardEvent(player, FinalCardEventKind.Skipped));
    }

    /// <summary>
    /// <paramref name="player"/> has <paramref name="count"/> more penalty cards to draw. Returns <paramref name="count"/>.
    /// </summary>
    private int Owe(int player, int count)
    {
        _penaltyCards[player] += count;
        return count;
    }

    /// <summary>
    /// The turn has passed: while a player still has penalty cards to draw, the player to play waits for them.
    /// </summary>
    private void PassTurn() => _turnHeld = _penaltyCards.Any(n => n > 0);

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
    /// Checks that <paramref name="player"/> can answer a +3 with a +3 Breaker, in or out of turn.
    /// </summary>
    private bool TryCheckBreaker(int player, out string? error)
    {
        CheckPlayer(player);
        error = Phase == FinalCardPhase.GameOver ? "The game is over."
            : Phase == FinalCardPhase.HandOver ? "This hand is over."
            : _penaltyCards[player] > 0 ? "Draw your penalty cards first."
            : null;
        return error is null;
    }

    /// <summary>
    /// Checks that the game is waiting for <paramref name="player"/> to play.
    /// </summary>
    private bool TryCheckTurn(int player, out string? error)
    {
        CheckPlayer(player);
        error = Phase == FinalCardPhase.GameOver ? "The game is over."
            : Phase == FinalCardPhase.HandOver ? "This hand is over."
            : _penaltyCards[player] > 0 ? "Draw your penalty cards first."
            : player != Turn ? "It is not your turn."
            : IsWaitingForPenalty ? "Wait until the penalty cards are drawn."
            : null;
        return error is null;
    }
}
