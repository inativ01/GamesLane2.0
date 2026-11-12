using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.Tricks;

namespace GamesLane.Server.Games.Hearts;

/// <summary>
/// Hearts rules for one game (see <see cref="IHeartsGame"/>). <c>shuffle</c> shuffles the deck before each deal (see
/// <see cref="TrickTakingGame"/>). On the first trick, nobody may play a heart or the queen of spades unless they hold
/// nothing else; hearts cannot be led until one has been played, unless the leader holds only hearts.
/// </summary>
public class HeartsGame : TrickTakingGame, IHeartsGame
{
    private readonly int[] _scores = new int[PlayerCount];
    private readonly Card[]?[] _passed = new Card[]?[PlayerCount];
    private readonly Card[][] _received = [.. Enumerable.Range(0, PlayerCount).Select(_ => Array.Empty<Card>())];
    private readonly List<int> _winners = [];

    /// <summary>
    /// Deals the first hand.
    /// </summary>
    public HeartsGame(Action<Card[]>? shuffle = null) : base(shuffle) => StartHand();

    /// <inheritdoc />
    public HeartsPhase Phase { get; private set; }

    /// <inheritdoc />
    public HeartsPass Pass { get; private set; }

    /// <inheritdoc />
    public bool HeartsBroken { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Scores => _scores;

    /// <inheritdoc />
    public HeartsHandResult? LastResult { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Winners => _winners;

    /// <inheritdoc />
    public override bool IsOver => Phase == HeartsPhase.GameOver;

    /// <inheritdoc />
    public override IReadOnlyList<int> AwaitedPlayers => Phase == HeartsPhase.Passing
        ? [.. Enumerable.Range(0, PlayerCount).Where(p => _passed[p] is null)]
        : base.AwaitedPlayers;

    /// <inheritdoc />
    protected override Suit? Trump => null;

    /// <inheritdoc />
    protected override bool IsHandOver => Phase == HeartsPhase.HandOver;

    /// <inheritdoc />
    public int HandPoints(int player) => Won(player).Sum(HeartsRules.Points);

    /// <inheritdoc />
    public IReadOnlyList<Card>? Passed(int player) => _passed[player];

    /// <inheritdoc />
    public IReadOnlyList<Card> Received(int player) => _received[player];

    /// <inheritdoc />
    public bool TryPass(int player, IReadOnlyList<Card> cards, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
        var hand = Hand(player);
        error = Phase == HeartsPhase.GameOver ? "The game is over."
            : Phase != HeartsPhase.Passing ? "The cards are not being passed now."
            : _passed[player] is not null ? "You have already passed your cards."
            : cards.Count != HeartsRules.PassCount || cards.Distinct().Count() != cards.Count ? $"Choose {HeartsRules.PassCount} cards to pass."
            : !cards.All(hand.Contains) ? "You do not have those cards."
            : null;
        if (error is not null)
        {
            return false;
        }

        _passed[player] = [.. cards];
        Actions++;
        if (_passed.All(p => p is not null))
        {
            ExchangeCards();
        }

        return true;
    }

    /// <inheritdoc />
    protected override string? RuleError(int player, Card card)
    {
        var hand = Hand(player);
        var firstTrick = TricksWon.Sum() == 0;
        if (Trick.Count == 0)
        {
            return firstTrick && card != HeartsRules.TwoOfClubs ? "Lead the 2♣."
                : card.Suit == Suit.Hearts && !HeartsBroken && hand.Any(c => c.Suit != Suit.Hearts) ? "Hearts are not broken yet."
                : null;
        }

        return firstTrick && HeartsRules.Points(card) > 0 && hand.Any(c => HeartsRules.Points(c) == 0)
            ? "No points on the first trick: play another card."
            : null;
    }

    /// <inheritdoc />
    protected override void OnCardPlayed(int player, Card card) => HeartsBroken |= card.Suit == Suit.Hearts;

    /// <summary>
    /// Scores the hand: each player gains the points they took, unless one took all 26 (shot the moon): then every other
    /// player gains 26. Once a player has 100 or more, the game is over and the lowest score wins.
    /// </summary>
    protected override void OnHandPlayed()
    {
        var points = Enumerable.Range(0, PlayerCount).Select(HandPoints).ToArray();
        int? moon = Array.IndexOf(points, HeartsRules.AllPoints) is var shooter and >= 0 ? shooter : null;
        for (var p = 0; p < PlayerCount; p++)
        {
            _scores[p] += moon is null ? points[p] : p == moon ? 0 : HeartsRules.AllPoints;
        }

        LastResult = new HeartsHandResult(
            [.. Enumerable.Range(0, PlayerCount).Select(p => new HeartsHandPlayer(p, moon is null ? points[p] : p == moon ? 0 : HeartsRules.AllPoints, _scores[p]))],
            moon);
        Actions++;
        if (_scores.Max() >= HeartsRules.Target)
        {
            _winners.AddRange(Enumerable.Range(0, PlayerCount).Where(p => _scores[p] == _scores.Min()));
            Phase = HeartsPhase.GameOver;
            return;
        }

        Phase = HeartsPhase.HandOver;
    }

    /// <inheritdoc />
    protected override void StartHand()
    {
        Deal();
        HeartsBroken = false;
        Array.Fill(_passed, null);
        Array.Fill(_received, []);
        Pass = HeartsRules.PassFor(HandNumber);
        Phase = Pass == HeartsPass.Hold ? HeartsPhase.Playing : HeartsPhase.Passing;
        if (Pass == HeartsPass.Hold)
        {
            StartPlay(HolderOfTwoOfClubs());
        }
    }

    /// <summary>Each player gets the cards passed to them, and the holder of the 2♣ leads.</summary>
    private void ExchangeCards()
    {
        for (var p = 0; p < PlayerCount; p++)
        {
            TakeFromHand(p, _passed[p]!);
        }

        for (var p = 0; p < PlayerCount; p++)
        {
            var target = HeartsRules.PassTarget(p, Pass);
            _received[target] = [.. TrickCards.Sorted(_passed[p]!)];
            AddToHand(target, _passed[p]!);
        }

        Phase = HeartsPhase.Playing;
        StartPlay(HolderOfTwoOfClubs());
    }

    private int HolderOfTwoOfClubs() => Enumerable.Range(0, PlayerCount).First(p => Hand(p).Contains(HeartsRules.TwoOfClubs));
}
