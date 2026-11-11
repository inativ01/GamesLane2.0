using GamesLane.Server.Data;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Stats;

/// <summary>
/// One game's activity in a report, from opening its page to finishing a game: pages opened, games created, players
/// joining someone else's game, games started and finished, invite links opened, and players joining a private game
/// they were invited to.
/// </summary>
public record ActivityGameRow(
    GameType GameType, int Opened, int Created, int Joined, int Started, int Finished, int InvitesOpened, int InvitedJoins);

/// <summary>
/// A count with its name, e.g. a source and its visits.
/// </summary>
public record NamedCount(string Name, int Count);

/// <summary>
/// The admin page's activity report: the days covered (oldest first), the visits on each, each game's activity (in the
/// usual order of the games), and the visits by return bucket (in order), source and language (most first).
/// </summary>
public record ActivityReport(
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<int> Visits,
    IReadOnlyList<ActivityGameRow> Games,
    IReadOnlyList<NamedCount> Returning,
    IReadOnlyList<NamedCount> Sources,
    IReadOnlyList<NamedCount> Languages);

/// <summary>
/// Reports on the activity counts.
/// </summary>
public static class ActivityStats
{
    /// <summary>
    /// The report of the last <paramref name="days"/> days ending on <paramref name="today"/>, from the activity counts
    /// and the games started kept. Counts of other days, or of games that no longer exist, are left out.
    /// </summary>
    public static ActivityReport Report(IEnumerable<ActivityCount> counts, IEnumerable<GamePlay> plays, DateOnly today, int days)
    {
        var first = GamePlayStats.FirstDay(today, days);
        var dayList = Enumerable.Range(0, days).Select(first.AddDays).ToArray();
        var kept = counts.Where(c => c.Day >= first && c.Day <= today).ToList();
        var started = plays.Where(p => p.Day >= first && p.Day <= today).ToList();

        int Sum(ActivityEvent activity, GameType type, string? detail = null) => kept
            .Where(c => c.Event == activity.ToString() && c.GameType == type.ToString() && (detail is null || c.Detail == detail))
            .Sum(c => c.Count);

        return new ActivityReport(
            dayList,
            [.. dayList.Select(day => kept.Where(c => c.Day == day && c.Event == nameof(ActivityEvent.Visit)).Sum(c => c.Count))],
            [.. GamePlayStats.UsualOrder.Select(type => new ActivityGameRow(
                type,
                Sum(ActivityEvent.OpenGame, type),
                Sum(ActivityEvent.CreateGame, type),
                Sum(ActivityEvent.JoinGame, type),
                started.Where(p => p.GameType == type.ToString()).Sum(p => p.Count),
                Sum(ActivityEvent.FinishGame, type),
                Sum(ActivityEvent.OpenInvite, type),
                Sum(ActivityEvent.JoinGame, type, ActivityCounter.Private)))],
            [.. VisitRules.ReturnBuckets.Select(bucket => new NamedCount(bucket,
                kept.Where(c => c.Event == nameof(ActivityEvent.VisitReturn) && c.Detail == bucket).Sum(c => c.Count)))],
            Ranked(kept, ActivityEvent.VisitSource),
            Ranked(kept, ActivityEvent.VisitLanguage));
    }

    // The details of an activity with their totals, most first; ties in alphabetical order.
    private static IReadOnlyList<NamedCount> Ranked(IEnumerable<ActivityCount> counts, ActivityEvent activity) =>
    [
        .. counts.Where(c => c.Event == activity.ToString())
            .GroupBy(c => c.Detail)
            .Select(g => new NamedCount(g.Key, g.Sum(c => c.Count)))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Name, StringComparer.Ordinal),
    ];
}
