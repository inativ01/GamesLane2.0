namespace GamesLane.Server.Games.Backgammon;

/// <summary>
/// Where the checkers are. Players are numbered 0 (white) and 1 (black). Each player numbers the points from their own
/// side: 1 to 24 in the direction they move, so their home board is points 1 to 6 and they bear off past point 1.
/// <see cref="Bar"/> (25) is the bar and <see cref="Off"/> (0) is off the board. White's point <c>n</c> is black's
/// point <c>25 - n</c>. Positions are immutable: <see cref="Apply"/> returns a new one.
/// </summary>
public sealed class BackgammonPosition
{
    /// <summary>
    /// The bar, as a point number.
    /// </summary>
    public const int Bar = 25;

    /// <summary>
    /// Off the board, as a point number.
    /// </summary>
    public const int Off = 0;

    /// <summary>
    /// The number of checkers each player has.
    /// </summary>
    public const int Checkers = 15;

    // Index i is white's point i + 1 (black's point 24 - i); positive counts are white checkers, negative black.
    private readonly int[] _points;
    private readonly int[] _bar;
    private readonly int[] _off;

    private BackgammonPosition(int[] points, int[] bar, int[] off)
    {
        _points = points;
        _bar = bar;
        _off = off;
    }

    /// <summary>
    /// The starting position: each player has 2 checkers on their 24-point, 5 on the 13-point, 3 on the 8-point and 5
    /// on the 6-point.
    /// </summary>
    public static BackgammonPosition Start { get; } = Create(
        new Dictionary<int, int> { [24] = 2, [13] = 5, [8] = 3, [6] = 5 },
        new Dictionary<int, int> { [24] = 2, [13] = 5, [8] = 3, [6] = 5 });

    /// <summary>
    /// A position from each player's checkers by point (their own numbering, 1 to 24) and on the bar. The rest of each
    /// player's 15 checkers are off the board. Throws if a point holds checkers of both players or there are too many.
    /// </summary>
    public static BackgammonPosition Create(
        IReadOnlyDictionary<int, int> white,
        IReadOnlyDictionary<int, int> black,
        int whiteBar = 0,
        int blackBar = 0)
    {
        var points = new int[24];
        foreach (var (point, count) in white)
        {
            points[Index(0, point)] += count;
        }

        foreach (var (point, count) in black)
        {
            var index = Index(1, point);
            if (points[index] > 0)
            {
                throw new ArgumentException($"White's point {index + 1} holds checkers of both players.");
            }

            points[index] -= count;
        }

        int[] bar = [whiteBar, blackBar];
        int[] off = [Checkers - white.Values.Sum() - whiteBar, Checkers - black.Values.Sum() - blackBar];
        if (off.Any(o => o < 0))
        {
            throw new ArgumentException($"A player has more than {Checkers} checkers.");
        }

        return new BackgammonPosition(points, bar, off);
    }

    /// <summary>
    /// The board from white's side: entry <c>i</c> is white's point <c>i + 1</c>, with white checkers as positive
    /// counts and black checkers as negative counts.
    /// </summary>
    public IReadOnlyList<int> Points => _points;

    /// <summary>
    /// The number of checkers <paramref name="player"/> has on their own point <paramref name="point"/> (1 to 24, or
    /// <see cref="Bar"/>, or <see cref="Off"/>).
    /// </summary>
    public int Count(int player, int point) => point switch
    {
        Bar => _bar[player],
        Off => _off[player],
        _ => Math.Max(0, player == 0 ? _points[Index(0, point)] : -_points[Index(1, point)]),
    };

    /// <summary>
    /// The number of the other player's checkers on <paramref name="player"/>'s point <paramref name="point"/> (1 to 24).
    /// </summary>
    public int OpponentCount(int player, int point) => Count(1 - player, 25 - point);

    /// <summary>
    /// Whether all of <paramref name="player"/>'s checkers still on the board are in their home board (points 1 to 6),
    /// so they may bear off.
    /// </summary>
    public bool AllHome(int player) =>
        _bar[player] == 0 && Enumerable.Range(7, 18).All(point => Count(player, point) == 0);

    /// <summary>
    /// <paramref name="player"/>'s pip count: how far all their checkers must still travel to bear off.
    /// </summary>
    public int Pips(int player) =>
        (_bar[player] * Bar) + Enumerable.Range(1, 24).Sum(point => point * Count(player, point));

    /// <summary>
    /// The position after <paramref name="player"/> moves one checker from <paramref name="from"/> to
    /// <paramref name="to"/> (their own numbering), hitting a single opponent checker there. Does not check the rules.
    /// </summary>
    public BackgammonPosition Apply(int player, int from, int to)
    {
        var points = (int[])_points.Clone();
        var bar = (int[])_bar.Clone();
        var off = (int[])_off.Clone();
        var sign = player == 0 ? 1 : -1;
        if (from == Bar)
        {
            bar[player]--;
        }
        else
        {
            points[Index(player, from)] -= sign;
        }

        if (to == Off)
        {
            off[player]++;
        }
        else
        {
            var index = Index(player, to);
            if (points[index] == -sign)
            {
                points[index] = 0;
                bar[1 - player]++;
            }

            points[index] += sign;
        }

        return new BackgammonPosition(points, bar, off);
    }

    /// <summary>
    /// A text that identifies the position, for comparing positions.
    /// </summary>
    public string Key => $"{string.Join(",", _points)}|{_bar[0]},{_bar[1]}";

    /// <summary>
    /// The index in <see cref="Points"/> of <paramref name="player"/>'s point <paramref name="point"/> (1 to 24).
    /// </summary>
    public static int Index(int player, int point) => player == 0 ? point - 1 : 24 - point;
}
