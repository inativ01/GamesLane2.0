namespace GamesLane.Server.Data;

/// <summary>
/// How many games of one kind were started on one day (UTC): on Start, on each Play again, and when the last seat of a
/// game fills. Nothing about the players or the session is kept.
/// </summary>
public class GamePlay
{
    /// <summary>
    /// The day, in UTC.
    /// </summary>
    public DateOnly Day { get; set; }

    /// <summary>
    /// The game, e.g. "Backgammon".
    /// </summary>
    public required string GameType { get; set; }

    /// <summary>
    /// The games started that day.
    /// </summary>
    public int Count { get; set; }
}
