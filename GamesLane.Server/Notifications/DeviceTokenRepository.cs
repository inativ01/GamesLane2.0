using GamesLane.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GamesLane.Server.Notifications;

/// <summary>
/// Keeps the phones that get turn notifications in the database. Each call uses its own database context, so it can be
/// used by services that live as long as the server.
/// </summary>
public class DeviceTokenRepository(IServiceScopeFactory scopeFactory) : IDeviceTokenRepository
{
    /// <inheritdoc />
    public Task SaveAsync(string token, DevicePlatform platform, string userKey, bool isSandbox, string language, DateTimeOffset now) =>
        Use(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "DeviceTokens" ("Token", "Platform", "UserKey", "IsSandbox", "Language", "UpdatedAt")
            VALUES ({token}, {platform.ToString()}, {userKey}, {isSandbox}, {language}, {now})
            ON CONFLICT ("Token")
            DO UPDATE SET "Platform" = EXCLUDED."Platform", "UserKey" = EXCLUDED."UserKey", "IsSandbox" = EXCLUDED."IsSandbox",
                "Language" = EXCLUDED."Language", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """));

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceToken>> GetForUserAsync(string userKey) =>
        await Use(db => db.DeviceTokens.AsNoTracking().Where(d => d.UserKey == userKey).ToListAsync());

    /// <inheritdoc />
    public Task DeleteAsync(string token) =>
        Use(db => db.DeviceTokens.Where(d => d.Token == token).ExecuteDeleteAsync());

    /// <inheritdoc />
    public Task DeleteForUserAsync(string userKey) =>
        Use(db => db.DeviceTokens.Where(d => d.UserKey == userKey).ExecuteDeleteAsync());

    /// <inheritdoc />
    public Task<int> DeleteUnusedAsync(DateTimeOffset before) =>
        Use(db => db.DeviceTokens.Where(d => d.UpdatedAt < before).ExecuteDeleteAsync());

    private async Task<T> Use<T>(Func<GamesLaneDbContext, Task<T>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GamesLaneDbContext>());
    }
}
