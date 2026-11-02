namespace GamesLane.Server.Users;

/// <summary>
/// Deletes saved users who have not logged in or opened the site for <see cref="InactivityLimit"/>.
/// Runs shortly after the server starts, then every few hours.
/// </summary>
public class InactiveUserCleanup(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<InactiveUserCleanup> logger) : BackgroundService
{
    private static readonly TimeSpan InactivityLimit = TimeSpan.FromDays(30);
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Deletes inactive users now and then on every interval, until the server stops.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstRunDelay, timeProvider, stoppingToken);
        using var timer = new PeriodicTimer(RunInterval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var users = scope.ServiceProvider.GetRequiredService<IUserService>();
                var deleted = await users.DeleteInactiveUsersAsync(timeProvider.GetUtcNow() - InactivityLimit);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} inactive users.", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not delete inactive users.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
