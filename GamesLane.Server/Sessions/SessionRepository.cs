using GamesLane.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Saves sessions in the database. Each call uses its own database context, so it can be used by the session store,
/// which lives as long as the server.
/// </summary>
public class SessionRepository(IServiceScopeFactory scopeFactory) : ISessionRepository
{
    /// <inheritdoc />
    public Task<SavedSession?> FindAsync(string id) =>
        Use(db => db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id));

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string id) => Use(db => db.Sessions.AnyAsync(s => s.Id == id));

    /// <inheritdoc />
    public Task SaveAsync(SavedSession session) => Use(async db =>
    {
        var saved = await db.Sessions.FirstOrDefaultAsync(s => s.Id == session.Id);
        if (saved is null)
        {
            db.Sessions.Add(session);
        }
        else
        {
            saved.GameType = session.GameType;
            saved.Version = session.Version;
            saved.Data = session.Data;
            saved.UpdatedAt = session.UpdatedAt;
        }

        return await db.SaveChangesAsync();
    });

    /// <inheritdoc />
    public Task DeleteAsync(string id) => Use(db => db.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync());

    /// <inheritdoc />
    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff) =>
        Use(db => db.Sessions.Where(s => s.UpdatedAt < cutoff).ExecuteDeleteAsync());

    private async Task<T> Use<T>(Func<GamesLaneDbContext, Task<T>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GamesLaneDbContext>());
    }
}
