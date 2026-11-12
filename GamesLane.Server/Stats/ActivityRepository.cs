using GamesLane.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GamesLane.Server.Stats;

/// <summary>
/// Keeps the activity counts per day in the database. Each call uses its own database context, so it can be used by
/// services that live as long as the server.
/// </summary>
public class ActivityRepository(IServiceScopeFactory scopeFactory) : IActivityRepository
{
    /// <inheritdoc />
    public Task AddAsync(DateOnly day, ActivityEvent activity, string gameType, string detail, int count) =>
        Use(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ActivityCounts" ("Day", "Event", "GameType", "Detail", "Count")
            VALUES ({day}, {activity.ToString()}, {gameType}, {detail}, {count})
            ON CONFLICT ("Day", "Event", "GameType", "Detail")
            DO UPDATE SET "Count" = "ActivityCounts"."Count" + EXCLUDED."Count"
            """));

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityCount>> GetAsync(DateOnly from, DateOnly to) =>
        await Use(db => db.ActivityCounts.AsNoTracking().Where(c => c.Day >= from && c.Day <= to).ToListAsync());

    private async Task<T> Use<T>(Func<GamesLaneDbContext, Task<T>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GamesLaneDbContext>());
    }
}
