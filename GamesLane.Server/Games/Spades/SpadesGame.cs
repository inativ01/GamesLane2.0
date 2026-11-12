using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.Tricks;

namespace GamesLane.Server.Games.Spades;

/// <summary>
/// Spades rules for one game (see <see cref="ISpadesGame"/>). <c>shuffle</c> shuffles the deck before each deal (see
/// <see cref="TrickTakingGame"/>). Player 3 deals the first hand, so player 0 bids first and leads. The tricks of a Nil
/// bidder do not count for their partner's bid; they are bags of their team.
/// </summary>
public class SpadesGame : TrickTakingGame, ISpadesGame
{
    private const int Teams = 2;

    private readonly int[] _scores = new int[Teams];
    private readonly int[] _bags = new int[Teams];
    private readonly SpadesBid?[] _bids = new SpadesBid?[PlayerCount];
    private readonly bool[] _looked = new bool[PlayerCount];

    /// <summary>
    /// Deals the first hand.
    /// </summary>
    public SpadesGame(SpadesRules? rules = null, Action<Card[]>? shuffle = null) : base(shuffle)
    {
        Rules = rules ?? new SpadesRules();
        Dealer = PlayerCount - 1;
        StartHand();
    }

    /// <inheritdoc />
    public SpadesRules Rules { get; }

    /// <inheritdoc />
    public SpadesPhase Phase { get; private set; }

    /// <inheritdoc />
    public int Dealer { get; private set; }

    /// <inheritdoc />
    public int Bidder { get; private set; }

    /// <inheritdoc />
    public bool SpadesBroken { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Scores => _scores;

    /// <inheritdoc />
    public IReadOnlyList<int> Bags => _bags;

    /// <inheritdoc />
    public SpadesHandResult? LastResult { get; private set; }

    /// <inheritdoc />
    public int? Winner { get; private set; }

    /// <inheritdoc />
    public override bool IsOver => Phase == SpadesPhase.GameOver;

    /// <inheritdoc />
    public override IReadOnlyList<int> AwaitedPlayers => Phase == SpadesPhase.Bidding ? [Bidder] : base.AwaitedPlayers;

    /// <inheritdoc />
    protected override Suit? Trump => Suit.Spades;

    /// <inheritdoc />
    protected override bool IsHandOver => Phase == SpadesPhase.HandOver;

    /// <inheritdoc />
    public SpadesBid? BidOf(int player) => _bids[player];

    /// <inheritdoc />
    public bool HasLooked(int player) => _looked[player];

    /// <inheritdoc />
    public bool TryLook(int player, out string? error)
    {
        CheckPlayer(player);
        error = Phase != SpadesPhase.Bidding ? "The bidding is over." : null;
        if (error is null && !_looked[player])
        {
            _looked[player] = true;
            Actions++;
        }

        return error is null;
    }

    /// <inheritdoc />
    public bool TryBid(int player, int tricks, bool blind, out string? error)
    {
        CheckPlayer(player);
        error = Phase == SpadesPhase.GameOver ? "The game is over."
            : Phase != SpadesPhase.Bidding ? "The bidding is over."
            : player != Bidder ? "It is not your turn to bid."
            : tricks is < 0 or > HandSize ? $"Bid 1 to {HandSize} tricks."
            : tricks == 0 && !Rules.Nil ? $"Bid 1 to {HandSize} tricks."
            : blind && (tricks != 0 || !Rules.BlindNil) ? "Only Nil can be bid blind."
            : blind && _looked[player] ? "You have looked at your cards: Blind Nil is no longer possible."
            : null;
        if (error is not null)
        {
            return false;
        }

        _bids[player] = new SpadesBid(tricks, blind);
        _looked[player] = true;
        Actions++;
        Bidder = (player + 1) % PlayerCount;
        if (_bids.All(b => b is not null))
        {
            Phase = SpadesPhase.Playing;
            StartPlay((Dealer + 1) % PlayerCount);
        }

        return true;
    }

    /// <inheritdoc />
    protected override string? RuleError(int player, Card card) =>
        Trick.Count == 0 && card.Suit == Suit.Spades && !SpadesBroken && Hand(player).Any(c => c.Suit != Suit.Spades)
            ? "Spades are not broken yet."
            : null;

    /// <inheritdoc />
    protected override void OnCardPlayed(int player, Card card) => SpadesBroken |= card.Suit == Suit.Spades;

    /// <summary>
    /// Scores the hand for each team, then ends the game once a team has 500 or more, or -200 or less, and the scores
    /// differ: the higher score wins.
    /// </summary>
    protected override void OnHandPlayed()
    {
        var nils = Enumerable.Range(0, PlayerCount).Where(p => _bids[p]!.IsNil)
            .Select(p => new SpadesNilResult(p, _bids[p]!.Blind, TricksWon[p] == 0)).ToArray();
        var teams = Enumerable.Range(0, Teams).Select(ScoreTeam).ToArray();
        LastResult = new SpadesHandResult(teams, nils);
        Actions++;
        var decided = (_scores.Max() >= SpadesRules.Target || _scores.Min() <= SpadesRules.LosingScore) && _scores[0] != _scores[1];
        Winner = decided ? (_scores[0] > _scores[1] ? 0 : 1) : null;
        Phase = decided ? SpadesPhase.GameOver : SpadesPhase.HandOver;
    }

    /// <inheritdoc />
    protected override void StartHand()
    {
        Dealer = HandNumber == 0 ? Dealer : (Dealer + 1) % PlayerCount;
        Deal();
        SpadesBroken = false;
        Array.Fill(_bids, null);
        Array.Fill(_looked, !Rules.BlindNil);
        Phase = SpadesPhase.Bidding;
        Bidder = (Dealer + 1) % PlayerCount;
    }

    /// <summary>
    /// A team's points for the hand: its bid (without Nil bids) against the tricks of its other members, with the bags,
    /// the Nil bids and the bag penalty.
    /// </summary>
    private SpadesTeamResult ScoreTeam(int team)
    {
        var members = Enumerable.Range(0, PlayerCount).Where(p => SpadesRules.TeamOf(p) == team).ToArray();
        var bidders = members.Where(p => !_bids[p]!.IsNil).ToArray();
        var bid = bidders.Sum(p => _bids[p]!.Tricks);
        var tricks = bidders.Sum(p => TricksWon[p]);
        var made = tricks >= bid;
        var newBags = (made ? tricks - bid : 0) + members.Where(p => _bids[p]!.IsNil).Sum(p => TricksWon[p]);
        var points = (made ? bid * SpadesRules.PointsPerTrick : -bid * SpadesRules.PointsPerTrick) + newBags;
        foreach (var nil in members.Where(p => _bids[p]!.IsNil))
        {
            var value = _bids[nil]!.Blind ? SpadesRules.BlindNilPoints : SpadesRules.NilPoints;
            points += TricksWon[nil] == 0 ? value : -value;
        }

        _bags[team] += newBags;
        var penalty = _bags[team] >= SpadesRules.BagLimit;
        if (penalty)
        {
            _bags[team] -= SpadesRules.BagLimit;
            points -= SpadesRules.BagPenalty;
        }

        _scores[team] += points;
        return new SpadesTeamResult(team, bid, tricks, made, points, newBags, penalty, _scores[team], _bags[team]);
    }

    private static void CheckPlayer(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, PlayerCount);
    }
}
