namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// Playing with jokers (the deck has two). A joker dealt to the table is replaced by two cards side by side (three if one
/// of them is the other joker); each hand uses one of them. A joker in a player's hand is swapped: the player is offered
/// two cards from the deck and keeps one, discarding the other. If one of them is a joker, the player either keeps the
/// other card or swaps that joker for two more cards. With two jokers in hand, they are swapped one after the other. This
/// happens before the betting (the betting waits for everyone), and also for cards drawn later (Motion Detector, Laser
/// Tripwires, Hacker), when the stage cannot end until the player has chosen. Everyone sees that a player swapped a joker,
/// but not which cards.
/// </summary>
public partial class TheGangGame
{
    /// <summary>
    /// <see cref="PreparingCard"/> while players swap the jokers they were dealt, before the betting.
    /// </summary>
    public const string JokersStep = "jokers";

    private Card[]?[] _jokerOffers = [];
    private int[] _jokerSwaps = [];

    /// <inheritdoc />
    public IReadOnlyList<Card>? JokerOffer(int player) => _jokerOffers.ElementAtOrDefault(player);

    /// <inheritdoc />
    public int JokerSwaps(int player) => _jokerSwaps.ElementAtOrDefault(player);

    /// <inheritdoc />
    public bool TryChooseJokerCard(int player, int index, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        if (_jokerOffers[player] is not { } offer)
        {
            error = "You have no joker to swap.";
            return false;
        }

        if (index is < 0 or > 1)
        {
            error = "Pick one of the two cards.";
            return false;
        }

        error = null;
        if (offer[index].IsJoker)
        {
            // Swap the offered joker for two more cards; the other offered card is discarded.
            _jokerOffers[player] = [DrawCard(), DrawCard()];
            return true;
        }

        var cards = _holeCards[player].ToList();
        cards[cards.FindIndex(c => c.IsJoker)] = offer[index];
        _holeCards[player] = [.. cards];
        _jokerSwaps[player]++;
        _jokerOffers[player] = null;
        OfferJoker(player);

        if (Phase == TheGangPhase.Preparing && PreparingCard == JokersStep && _jokerOffers.All(o => o is null))
        {
            FinishPreparing();
        }
        else if (Phase == TheGangPhase.Betting && CanEndStage)
        {
            EndStage();
        }

        return true;
    }

    /// <summary>
    /// Whether any player still has to choose a card for a joker.
    /// </summary>
    private bool AwaitsJokers => _jokerOffers.Any(o => o is not null);

    /// <summary>
    /// The deck for a heist: with jokers in the rules, two jokers are added at random places, unless the deck already has
    /// them (as in tests).
    /// </summary>
    private IReadOnlyList<Card> WithJokers(IReadOnlyList<Card> deck)
    {
        if (!_rules.Jokers || deck.Any(c => c.IsJoker))
        {
            return deck;
        }

        var cards = deck.ToList();
        cards.Insert(_random.Next(cards.Count + 1), Card.Joker);
        cards.Insert(_random.Next(cards.Count + 1), Card.Joker);
        return cards;
    }

    /// <summary>
    /// The next card of the deck.
    /// </summary>
    private Card DrawCard() => _deck[_nextCard++];

    /// <summary>
    /// If the player holds a joker and is not choosing for one yet, offers them two cards for it.
    /// </summary>
    private void OfferJoker(int player)
    {
        if (_jokerOffers[player] is null && _holeCards[player].Any(c => c.IsJoker))
        {
            _jokerOffers[player] = [DrawCard(), DrawCard()];
        }
    }

    /// <summary>
    /// Replaces a joker on the table by two cards from the deck, and so on if one of them is a joker.
    /// </summary>
    private Card[] ReplaceJokers(Card[] slot)
    {
        while (slot.Any(c => c.IsJoker))
        {
            var joker = Array.FindIndex(slot, c => c.IsJoker);
            slot = [.. slot[..joker], DrawCard(), DrawCard(), .. slot[(joker + 1)..]];
        }

        return slot;
    }

    /// <summary>
    /// Sets up a new heist's jokers: the table's are replaced, and every player holding one is offered two cards.
    /// Returns true if any player must choose.
    /// </summary>
    private bool DealJokers()
    {
        _slots = [.. _slots.Select(ReplaceJokers)];
        _jokerOffers = new Card[]?[PlayerCount];
        _jokerSwaps = new int[PlayerCount];
        for (var player = 0; player < PlayerCount; player++)
        {
            OfferJoker(player);
        }

        return AwaitsJokers;
    }

    /// <summary>
    /// A player's private cards that count in a hand (a joker they have not swapped yet does not).
    /// </summary>
    private Card[] CountingCards(int player) => [.. _holeCards[player].Where(c => !c.IsJoker)];
}
