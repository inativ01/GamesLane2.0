using GamesLane.Server.Data;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Stats;

/// <summary>
/// One game's plays in a report: a count per day (oldest first, days without plays as 0) and their total.
/// </summary>
public record GamePlaySeries(GameType GameType, IReadOnlyList<int> Counts, int Total);

/// <summary>
/// The admin page's report: the days covered (oldest first) and each game's plays, in the usual order of the games.
/// </summary>
public record GamePlayReport(IReadOnlyList<DateOnly> Days, IReadOnlyList<GamePlaySeries> Games);

/// <summary>
/// Reports on the games started per day: the admin page's report, and the order of the games by popularity.
/// </summary>
public static class GamePlayStats
{
    /// <summary>The days the admin page shows unless another number is chosen.</summary>
    public const int DefaultDays = 30;

    /// <summary>The most days the admin page can show.</summary>
    public const int MaxDays = 365;

    /// <summary>The days that make a game popular on the main screen.</summary>
    public const int PopularityDays = 30;

    /// <summary>
    /// The games in their usual order on the main screen (the order of <see cref="GameType"/> is not that order).
    /// </summary>
    public static IReadOnlyList<GameType> UsualOrder { get; } =
    [
        GameType.TheGang, GameType.Backgammon, GameType.RichManPoorMan, GameType.FinalCard, GameType.Yaniv, GameType.Dominoes,
        GameType.Wazoo, GameType.TicTacToe,
    ];

    /// <summary>
    /// The first day of the last <paramref name="days"/> days ending on <paramref name="today"/>.
    /// </summary>
    public static DateOnly FirstDay(DateOnly today, int days) => today.AddDays(1 - days);

    /// <summary>
    /// The report of the last <paramref name="days"/> days ending on <paramref name="today"/>, from the counts kept.
    /// Counts of other days, or of games that no longer exist, are left out.
    /// </summary>
    public static GamePlayReport Report(IEnumerable<GamePlay> plays, DateOnly today, int days)
    {
        var first = FirstDay(today, days);
        var dayList = Enumerable.Range(0, days).Select(first.AddDays).ToArray();
        var counts = plays.Where(p => p.Day >= first && p.Day <= today).ToList();
        return new GamePlayReport(dayList, [.. UsualOrder.Select(type =>
        {
            var series = dayList.Select(day => counts.Where(p => p.Day == day && p.GameType == type.ToString()).Sum(p => p.Count)).ToArray();
            return new GamePlaySeries(type, series, series.Sum());
        })]);
    }

    /// <summary>
    /// The games, most played first; ties keep their usual order.
    /// </summary>
    public static IReadOnlyList<GameType> PopularOrder(IEnumerable<GamePlay> plays)
    {
        var totals = plays.GroupBy(p => p.GameType).ToDictionary(g => g.Key, g => g.Sum(p => p.Count));
        return [.. UsualOrder.OrderByDescending(type => totals.GetValueOrDefault(type.ToString()))];
    }
}
