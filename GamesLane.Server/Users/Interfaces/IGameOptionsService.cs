using GamesLane.Server.Sessions;

namespace GamesLane.Server.Users;

/// <summary>
/// Saved users' preferred options per game.
/// </summary>
public interface IGameOptionsService
{
    /// <summary>
    /// The user's saved options for <paramref name="gameType"/>, with every option of the game (invalid or missing
    /// values get their default), or null if they have saved none.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>?> GetAsync(Guid userId, GameType gameType);

    /// <summary>
    /// Saves the user's options for <paramref name="gameType"/>, replacing any saved before. Returns an error message if
    /// an option or value is unknown, otherwise null.
    /// </summary>
    Task<string?> SaveAsync(Guid userId, GameType gameType, IReadOnlyDictionary<string, string>? options);
}
