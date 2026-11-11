using GamesLane.Server.Data;

namespace GamesLane.Server.Stats;

/// <summary>
/// Where the anonymous activity counts per day are kept (the database).
/// </summary>
public interface IActivityRepository
{
    /// <summary>
    /// Adds <paramref name="count"/> to the count of <paramref name="activity"/> on <paramref name="day"/> (UTC), for
    /// <paramref name="gameType"/> ("" when not about a game) and <paramref name="detail"/> ("" when nothing more).
    /// </summary>
    Task AddAsync(DateOnly day, ActivityEvent activity, string gameType, string detail, int count);

    /// <summary>
    /// The counts of the days from <paramref name="from"/> to <paramref name="to"/>, both included.
    /// </summary>
    Task<IReadOnlyList<ActivityCount>> GetAsync(DateOnly from, DateOnly to);
}
