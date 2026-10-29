using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Frees the seats of users who have been disconnected for longer than <see cref="GracePeriod"/>,
/// so a game pauses (or others can sit down) when a player does not come back.
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
    /// Every few seconds, frees expired seats in all sessions and sends the new state to each affected session.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var cutoff = timeProvider.GetUtcNow() - GracePeriod;
            foreach (var session in store.All)
            {
                try
                {
                    if (session.ReleaseDisconnectedSeats(cutoff))
                    {
                        await GameHub.BroadcastState(hub.Clients, session);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not release disconnected seats in session {SessionId}.", session.Id);
                }
            }
        }
    }
}
