namespace GamesLane.Server.Data;

/// <summary>
/// How many times something happened on one day (UTC), e.g. visits from one source, or games of one kind created.
/// Only counts are kept: nothing identifies a visitor, a player or a session.
/// </summary>
public class ActivityCount
{
    /// <summary>
    /// The day, in UTC.
    /// </summary>
    public DateOnly Day { get; set; }

    /// <summary>
    /// What happened, an <see cref="Stats.ActivityEvent"/> name, e.g. "CreateGame".
    /// </summary>
    public required string Event { get; set; }

    /// <summary>
    /// The game it happened in, e.g. "Backgammon", or "" when not about a game.
    /// </summary>
    public required string GameType { get; set; }

    /// <summary>
    /// More about it, e.g. the visit's source or "Public", or "" when nothing more.
    /// </summary>
    public required string Detail { get; set; }

    /// <summary>
    /// The times it happened that day.
    /// </summary>
    public int Count { get; set; }
}
