using GamesLane.Server.Data;
using GamesLane.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace GamesLane.Server.Stats;

/// <summary>
/// Keeps the games started per day in the database. Each call uses its own database context, so it can be used by the
/// session store, which lives as long as the server.
/// </summary>
public class GamePlayRepository(IServiceScopeFactory scopeFactory) : IGamePlayRepository
{
    /// <inheritdoc />
    public Task AddAsync(GameType type, DateOnly day, int count) => Use(db => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "GamePlays" ("Day", "GameType", "Count") VALUES ({day}, {type.ToString()}, {count})
        ON CONFLICT ("Day", "GameType") DO UPDATE SET "Count" = "GamePlays"."Count" + EXCLUDED."Count"
        """));

    /// <inheritdoc />
    public async Task<IReadOnlyList<GamePlay>> GetAsync(DateOnly from, DateOnly to) =>
        await Use(db => db.GamePlays.AsNoTracking().Where(p => p.Day >= from && p.Day <= to).ToListAsync());

    private async Task<T> Use<T>(Func<GamesLaneDbContext, Task<T>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GamesLaneDbContext>());
    }
}
