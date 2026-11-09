namespace GamesLane.Server.Games.Wazoo;

/// <summary>
/// One player's scorecard: the points scored in each box (null while open), and the Wazoo bonuses. The upper section
/// earns a bonus of 35 when its boxes add up to 63 or more; each extra Wazoo scored while the Wazoo box holds 50 earns
/// 100.
/// </summary>
public class WazooScorecard
{
    /// <summary>The upper section total needed for the bonus.</summary>
    public const int UpperBonusThreshold = 63;

    /// <summary>The upper section bonus.</summary>
    public const int UpperBonusPoints = 35;

    /// <summary>The points of each extra Wazoo.</summary>
    public const int WazooBonusPoints = 100;

    private readonly int?[] _scores = new int?[WazooScoring.Categories];
    private int _wazooBonuses;

    /// <summary>
    /// The points scored in <paramref name="category"/>, or null while it is open.
    /// </summary>
    public int? this[WazooCategory category] => _scores[(int)category];

    /// <summary>
    /// The points of every box, in <see cref="WazooCategory"/> order (null while open).
    /// </summary>
    public IReadOnlyList<int?> Scores => _scores;

    /// <summary>
    /// The number of extra Wazoos that earned a bonus.
    /// </summary>
    public int WazooBonuses => _wazooBonuses;

    /// <summary>
    /// The points of the upper section's boxes (Ones to Sixes).
    /// </summary>
    public int UpperTotal => Total(true);

    /// <summary>
    /// The upper section bonus: 35 once the upper section adds up to 63 or more, otherwise 0.
    /// </summary>
    public int UpperBonus => UpperTotal >= UpperBonusThreshold ? UpperBonusPoints : 0;

    /// <summary>
    /// The points of the lower section's boxes.
    /// </summary>
    public int LowerTotal => Total(false);

    /// <summary>
    /// The points of the Wazoo bonuses.
    /// </summary>
    public int WazooBonus => _wazooBonuses * WazooBonusPoints;

    /// <summary>
    /// All the points: both sections, the upper section bonus and the Wazoo bonuses.
    /// </summary>
    public int GrandTotal => UpperTotal + UpperBonus + LowerTotal + WazooBonus;

    /// <summary>
    /// Whether every box is filled.
    /// </summary>
    public bool IsComplete => _scores.All(s => s is not null);

    /// <summary>
    /// Fills <paramref name="category"/> with <paramref name="points"/>, plus a Wazoo bonus if <paramref name="bonus"/>.
    /// </summary>
    internal void Fill(WazooCategory category, int points, bool bonus)
    {
        _scores[(int)category] = points;
        if (bonus)
        {
            _wazooBonuses++;
        }
    }

    private int Total(bool upper) =>
        Enum.GetValues<WazooCategory>().Where(c => WazooScoring.IsUpper(c) == upper).Sum(c => _scores[(int)c] ?? 0);
}
