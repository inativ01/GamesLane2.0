namespace GamesLane.Server.Sessions;

/// <summary>
/// Deletes saved sessions with no activity for <see cref="Expiry"/>. Sessions in memory are saved again on their next
/// change, so only sessions nobody has opened for that long are deleted. Runs shortly after the server starts, then
/// once a day.
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
    public static readonly TimeSpan Expiry = TimeSpan.FromDays(30);

    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RunInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// Deletes expired sessions now and then once a day, until the server stops.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstRunDelay, timeProvider, stoppingToken);
        using var timer = new PeriodicTimer(RunInterval, timeProvider);
        do
        {
            try
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not delete expired sessions.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
