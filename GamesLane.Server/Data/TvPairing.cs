namespace GamesLane.Server.Data;

/// <summary>
/// A TV running a TV app, paired with a saved user, who can then watch their games on it. A user can pair many
/// TVs, and a TV many users. Deleted 90 days after its last use, and with the user.
/// </summary>
public class TvPairing
{
    /// <summary>
    /// The user who paired the TV.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The TV's ID, the user ID in its login token, e.g. <c>tv:0123…</c>.
    /// </summary>
    public required string TvId { get; set; }

    /// <summary>
    /// The TV's name when it was paired, e.g. "Living Room".
    /// </summary>
    public required string TvName { get; set; }

    /// <summary>
    /// When the user paired the TV.
    /// </summary>
    public DateTimeOffset PairedAt { get; set; }

    /// <summary>
    /// When the user last paired the TV or watched their games on it.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; set; }
}
