using System.Text.Json;
using GamesLane.Server.Data;
using GamesLane.Server.Games;
using GamesLane.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace GamesLane.Server.Users;

/// <summary>
/// Saved users' preferred options per game, stored as JSON in the database.
/// </summary>
public class GameOptionsService(GamesLaneDbContext db, TimeProvider timeProvider) : IGameOptionsService
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>?> GetAsync(Guid userId, GameType gameType)
    {
        var key = gameType.ToString();
        var saved = await db.UserGameOptions.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == userId && o.GameType == key);
        return saved is null ? null : GameOptions.Normalize(gameType, Parse(saved.Options));
    }

    /// <inheritdoc />
    public async Task<string?> SaveAsync(Guid userId, GameType gameType, IReadOnlyDictionary<string, string>? options)
    {
        if (!GameOptions.TryValidate(gameType, options, out var normalized, out var error))
        {
            return error;
        }

        var key = gameType.ToString();
        var saved = await db.UserGameOptions.FirstOrDefaultAsync(o => o.UserId == userId && o.GameType == key);
        if (saved is null)
        {
            saved = new UserGameOptions { UserId = userId, GameType = key, Options = "" };
            db.UserGameOptions.Add(saved);
        }

        saved.Options = JsonSerializer.Serialize(normalized);
        saved.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>
    /// The saved JSON as a map, or null if it cannot be read.
    /// </summary>
    private static Dictionary<string, string>? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
