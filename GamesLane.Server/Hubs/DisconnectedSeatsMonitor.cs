using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Marks users disconnected for <see cref="GameSession.ReconnectWindow"/> as Away, so a game pauses (or others can take
/// their seats) when a player does not come back; outside a game, their seats are freed.
/// Also removes from memory the sessions that nobody has had open for <see cref="UnloadAfter"/> (they stay saved).
/// </summary>
public class DisconnectedSeatsMonitor(
    IGameSessionStore store,
    IHubContext<GameHub> hub,
    TimeProvider timeProvider,
    ILogger<DisconnectedSeatsMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan UnloadAfter = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Every few seconds, updates the Away seats in all sessions and sends the new state to each affected session,
    /// then removes idle sessions from memory.
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
                        await GameHub.BroadcastState(hub.Clients, store, session);
                    }

                    if (session.IsIdle(now, UnloadAfter))
                    {
                        await store.UnloadAsync(session);
                        logger.LogInformation("Removed idle session {SessionId} from memory.", session.Id);
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
