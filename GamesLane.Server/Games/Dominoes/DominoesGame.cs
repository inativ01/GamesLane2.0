namespace GamesLane.Server.Games.Dominoes;

/// <summary>
/// Dominoes rules for one game (see <see cref="IDominoesGame"/>). <c>shuffle</c> shuffles the tiles before each deal,
/// which gives player 0 the first tiles, player 1 the next, and so on, and leaves the rest as the boneyard, drawn from the
/// start (for tests); in Mexican Train, the engine is taken out first. The line has no spinner: a double has two ends,
/// like any tile. In round 1 of the line styles, the holder of the highest double starts (player 0 if nobody holds one);
/// later, the player who played their last tile, or after a blocked round the player after the last starter. In Mexican
/// Train, player 0 starts, then the next player each round. A Mexican Train player who plays a double plays again, and
/// until a double at the end of a train is covered, every play must cover it.
/// </summary>
public partial class DominoesGame : IDominoesGame
{
    /// <summary>The Mexican train, as a target of <see cref="TryPlay"/>.</summary>
    public const int MexicanTarget = -1;

    private Action<Tile[]> _shuffle;
    private readonly List<Tile>[] _hands;
    private readonly List<Tile> _boneyard = [];
    private readonly List<LaidTile> _line = [];
    private readonly List<LaidTile>[] _trains;
    private readonly List<LaidTile> _mexican = [];
    private readonly bool[] _markers;
    private readonly int[] _scores;
    private readonly List<int> _winners = [];
    private int _starter;

    /// <summary>
    /// Deals the first round for <paramref name="players"/> players (2 to the style's most).
    /// </summary>
    public DominoesGame(int players, DominoesRules? rules = null, Action<Tile[]>? shuffle = null)
    {
        Rules = rules ?? new DominoesRules();
        ArgumentOutOfRangeException.ThrowIfLessThan(players, DominoesRules.MinPlayers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(players, Rules.MaxPlayers);
        _shuffle = shuffle ?? Random.Shared.Shuffle;
        _hands = [.. Enumerable.Range(0, players).Select(_ => new List<Tile>())];
        _trains = [.. Enumerable.Range(0, players).Select(_ => new List<LaidTile>())];
        _markers = new bool[players];
        HasTeams = Rules.IsLine && Rules.Teams && players == 4;
        _scores = new int[HasTeams ? 2 : players];
        Deal();
    }

    /// <summary>
    /// Gives a game loaded from a save its shuffle again (see the constructor).
    /// </summary>
    internal void Reattach(Action<Tile[]>? shuffle) => _shuffle = shuffle ?? Random.Shared.Shuffle;

    /// <inheritdoc />
    public DominoesRules Rules { get; }

    /// <inheritdoc />
    public int Players => _hands.Length;

    /// <inheritdoc />
    public bool HasTeams { get; }

    /// <inheritdoc />
    public int Sides => _scores.Length;

    /// <inheritdoc />
    public int SideOf(int player) => HasTeams ? player % 2 : player;

    /// <inheritdoc />
    public int RoundNumber { get; private set; }

    /// <inheritdoc />
    public int? TotalRounds => Rules.IsLine ? null : Rules.TrainRounds;

    /// <inheritdoc />
    public DominoesPhase Phase { get; private set; }

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public bool HasDrawn { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<LaidTile> Line => _line;

    /// <inheritdoc />
    public int? Engine { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<LaidTile> Train(int player) => _trains[player];

    /// <inheritdoc />
    public IReadOnlyList<LaidTile> MexicanTrain => _mexican;

    /// <inheritdoc />
    public bool HasMarker(int player) => _markers[player];

    /// <inheritdoc />
    public int BoneyardCount => _boneyard.Count;

    /// <inheritdoc />
    public IReadOnlyList<Tile> Hand(int player) => _hands[player];

    /// <inheritdoc />
    public IReadOnlyList<int> Scores => _scores;

    /// <inheritdoc />
    public DominoesAction? LastAction { get; private set; }

    /// <inheritdoc />
    public int Actions { get; private set; }

    /// <inheritdoc />
    public DominoesRoundResult? LastResult { get; private set; }

    /// <inheritdoc />
    public bool IsOver => Phase == DominoesPhase.GameOver;

    /// <inheritdoc />
    public IReadOnlyList<int> Winners => _winners;

    /// <inheritdoc />
    public IReadOnlyList<DominoesMove> Moves(int player) =>
        Phase == DominoesPhase.Playing && Turn == player ? MovesOf(player, openTrains: false) : [];

    /// <inheritdoc />
    public bool CanDraw(int player) =>
        Moves(player).Count == 0 && Phase == DominoesPhase.Playing && Turn == player && Rules.Draws && _boneyard.Count > 0
        && !(HasDrawn && !Rules.IsLine);

    /// <inheritdoc />
    public bool CanPass(int player) =>
        Phase == DominoesPhase.Playing && Turn == player && Moves(player).Count == 0 && !CanDraw(player);

    /// <inheritdoc />
    public bool TryPlay(int player, Tile tile, int target, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        error = !_hands[player].Contains(tile) ? "You do not have that tile."
            : !Moves(player).Contains(new DominoesMove(tile, target)) ? "That tile does not match there."
            : null;
        if (error is not null)
        {
            return false;
        }

        _hands[player].Remove(tile);
        var laid = Lay(tile, target);
        if (!Rules.IsLine && target == player)
        {
            _markers[player] = false;
        }

        var points = Rules.Style == DominoesStyle.AllFives ? FivesPoints() : 0;
        _scores[SideOf(player)] += points;
        Actions++;
        LastAction = new DominoesAction(player, DominoesActionKind.Played, tile, target, points);
        if (points > 0 && _scores[SideOf(player)] >= Rules.Target)
        {
            EndGame(highestWins: true);
        }
        else if (_hands[player].Count == 0)
        {
            EndRound(player);
        }
        else if (!(laid.IsDouble && !Rules.IsLine))
        {
            NextTurn();
        }
        else
        {
            HasDrawn = false;
        }

        CheckBlocked();
        return true;
    }
}
