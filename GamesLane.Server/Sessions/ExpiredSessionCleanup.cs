namespace GamesLane.Server.Sessions;

/// <summary>
/// Deletes saved sessions with no activity for <see cref="Expiry"/>, so a game survives a server restart while its
/// players are around but is not kept long after. Sessions in memory are saved first, so a session someone has open is
/// never deleted. Runs shortly after the server starts, then every hour while it runs (a server that is asleep cleans
/// up after it wakes).
/// </summary>
public class ExpiredSessionCleanup(
    ISessionRepository repository,
    IGameSessionStore store,
    TimeProvider timeProvider,
    ILogger<ExpiredSessionCleanup> logger) : BackgroundService
{
    /// <summary>
    /// How long a session is kept after its last activity.
    /// </summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromHours(6);

    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Deletes expired sessions now and then every hour, until the server stops.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstRunDelay, timeProvider, stoppingToken);
        using var timer = new PeriodicTimer(RunInterval, timeProvider);
        do
        {
            try
            {
                await CleanUpAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not delete expired sessions.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Saves the sessions in memory, then deletes the saved sessions with no activity for <see cref="Expiry"/>.
    /// </summary>
    public async Task CleanUpAsync()
    {
        foreach (var session in store.All)
        {
            await store.SaveAsync(session);
        }

        var deleted = await repository.DeleteOlderThanAsync(timeProvider.GetUtcNow() - Expiry);
        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} expired sessions.", deleted);
        }
    }
}
