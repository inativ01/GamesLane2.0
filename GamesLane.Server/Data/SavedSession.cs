namespace GamesLane.Server.Data;

/// <summary>
/// A game session saved in the database, so it can be opened again later (after it was removed from memory, or after
/// the server restarted). Sessions are deleted 6 hours after their last activity.
/// </summary>
public class SavedSession
{
    /// <summary>
    /// The session code, in upper case.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The game, e.g. "Backgammon".
    /// </summary>
    public required string GameType { get; set; }

    /// <summary>
    /// The fingerprint of the code that saved it (see <c>SessionSnapshot.Version</c>): a save of another version is not loaded.
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// The session's state, as JSON.
    /// </summary>
    public required string Data { get; set; }

    /// <summary>
    /// When the session was last saved, i.e. its last activity.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
