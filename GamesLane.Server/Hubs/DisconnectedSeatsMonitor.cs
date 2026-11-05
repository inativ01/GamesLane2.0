using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Marks users disconnected for <see cref="GameSession.ReconnectWindow"/> as Away, so a game pauses (or others can take
/// their seats) when a player does not come back; outside a game, their seats are freed.
/// Also removes the sessions nobody has open (see <see cref="IdleLimit"/>): a session whose game never started is deleted,
/// others are removed from memory and stay saved. A public game with nobody seated is deleted at once.
/// </summary>
public class DisconnectedSeatsMonitor(
    IGameSessionStore store,
    IHubContext<GameHub> hub,
    TimeProvider timeProvider,
    ILogger<DisconnectedSeatsMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan UnloadAfter = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan NeverStartedLimit = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PrivateLobbyLimit = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a session may stay with nobody having it open: 5 minutes once a game has started; before that, 10 minutes
    /// for a private game with players seated (so friends have time to open the link), otherwise 30 seconds.
    /// </summary>
    public static TimeSpan IdleLimit(IGameSession session) =>
        session.HasStarted ? UnloadAfter
        : !session.IsPublic && session.HasSeatedPlayers ? PrivateLobbyLimit
        : NeverStartedLimit;

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

                    var idle = session.IsIdle(now, IdleLimit(session));
                    if (idle || (session.IsPublic && !session.HasSeatedPlayers))
                    {
                        await store.UnloadAsync(session);
                        logger.LogInformation("Removed session {SessionId} from memory.", session.Id);
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
