using GamesLane.Server.Data;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Stats;

/// <summary>
/// Where the number of games started per day and game is kept (the database).
/// </summary>
public interface IGamePlayRepository
{
    /// <summary>
    /// Adds <paramref name="count"/> games of <paramref name="type"/> started on <paramref name="day"/> (UTC).
    /// </summary>
    Task AddAsync(GameType type, DateOnly day, int count);

    /// <summary>
    /// The counts of the days from <paramref name="from"/> to <paramref name="to"/>, both included. Days and games with
    /// no game started are left out.
    /// </summary>
    Task<IReadOnlyList<GamePlay>> GetAsync(DateOnly from, DateOnly to);
}
