using GamesLane.Server.Data;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Where sessions are saved (the database). Codes are in upper case.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// The saved session with this code, or null.
    /// </summary>
    Task<SavedSession?> FindAsync(string id);

    /// <summary>
    /// Whether a session with this code is saved.
    /// </summary>
    Task<bool> ExistsAsync(string id);

    /// <summary>
    /// Saves a session, replacing its previous save.
    /// </summary>
    Task SaveAsync(SavedSession session);

    /// <summary>
    /// Deletes the saved session with this code, if any.
    /// </summary>
    Task DeleteAsync(string id);

    /// <summary>
    /// Deletes the sessions last saved before <paramref name="cutoff"/>, and returns how many there were.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff);
}
