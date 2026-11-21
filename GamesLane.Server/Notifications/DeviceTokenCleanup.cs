namespace GamesLane.Server.Notifications;

/// <summary>
/// Forgets the phones whose app has not registered for <see cref="UnusedLimit"/> (it registers every time it opens the
/// site). Runs shortly after the server starts, then every few hours.
/// </summary>
public class DeviceTokenCleanup(
    IDeviceTokenRepository devices,
    TimeProvider timeProvider,
    ILogger<DeviceTokenCleanup> logger) : BackgroundService
{
    /// <summary>
    /// How long a device is kept without use.
    /// </summary>
    public static readonly TimeSpan UnusedLimit = TimeSpan.FromDays(30);

    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Forgets unused devices now and then on every interval, until the server stops.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstRunDelay, timeProvider, stoppingToken);
        using var timer = new PeriodicTimer(RunInterval, timeProvider);
        do
        {
            try
            {
                var deleted = await devices.DeleteUnusedAsync(timeProvider.GetUtcNow() - UnusedLimit);
                if (deleted > 0)
                {
                    logger.LogInformation("Forgot {Count} unused devices.", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not forget unused devices.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
