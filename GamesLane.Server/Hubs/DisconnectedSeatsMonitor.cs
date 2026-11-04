using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Marks users disconnected for <see cref="GameSession.ReconnectWindow"/> as Away, so a game pauses (or others can take
/// their seats) when a player does not come back; outside a game, their seats are freed.
/// Also deletes sessions that nobody has had open, with no seat taken, for longer than <see cref="GracePeriod"/>.
/// </summary>
public class DisconnectedSeatsMonitor(
    IGameSessionStore store,
    IHubContext<GameHub> hub,
    TimeProvider timeProvider,
    ILogger<DisconnectedSeatsMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Every few seconds, updates the Away seats in all sessions and sends the new state to each affected session,
    /// then deletes abandoned sessions.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = timeProvider.GetUtcNow();
            foreach (var session in store.All)
            {
                try
                {
                    if (session.UpdateAwaySeats(now))
                    {
                        await GameHub.BroadcastState(hub.Clients, session);
                    }

                    if (session.IsAbandoned(now, GracePeriod))
                    {
                        store.Remove(session.Id);
                        logger.LogInformation("Deleted abandoned session {SessionId}.", session.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not check session {SessionId}.", session.Id);
                }
            }
        }
    }
}
