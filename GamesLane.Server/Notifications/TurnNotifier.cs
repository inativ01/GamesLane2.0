using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Notifications;

/// <summary>
/// Notifies the iPhone and Android apps' users when it becomes their turn and no page of the game is in view for them: the phone
/// asleep, another app in front, the app closed, or the app on another page. A user is watching while one of their
/// connections to the session is open and its page is in view (pages report going out of view, see
/// <see cref="SetPageHidden"/>, so a notification does not wait for the connection to time out). A turn notifies once,
/// when it comes: a user whose turn it already was is not notified again, nor when a session is first seen (e.g. loaded
/// again from the database). Users without a device registered by the app get nothing.
/// </summary>
public class TurnNotifier(
    IDeviceTokenRepository devices,
    IPushSender sender,
    NotificationTexts texts,
    ILogger<TurnNotifier> logger) : ITurnNotifier
{
    private readonly ConcurrentDictionary<string, byte> _hidden = new();

    // Per session in memory: the users it waited for at its previous change. Dropped with the session.
    private readonly ConditionalWeakTable<IGameSession, AwaitedUsers> _awaited = new();

    /// <inheritdoc />
    public void SetPageHidden(string connectionId, bool hidden)
    {
        if (hidden)
        {
            _hidden[connectionId] = 0;
        }
        else
        {
            _hidden.TryRemove(connectionId, out _);
        }
    }

    /// <inheritdoc />
    public void ConnectionClosed(string connectionId) => _hidden.TryRemove(connectionId, out _);

    /// <inheritdoc />
    /// <remarks>Never throws: errors are logged, so the change itself always goes on.</remarks>
    public Task SessionChangedAsync(IGameSession session)
    {
        List<string> newlyAwaited;
        try
        {
            var previous = _awaited.GetValue(session, _ => new AwaitedUsers());
            // The state is read while holding the lock, so overlapping changes are compared in the order they are read.
            lock (previous)
            {
                var awaited = session.GetAwaitedPlayers();
                newlyAwaited = previous.Seen
                    ? [.. awaited.Where(p => !previous.UserKeys.Contains(p.UserKey) && !IsWatching(p)).Select(p => p.UserKey)]
                    : [];
                previous.UserKeys = [.. awaited.Select(p => p.UserKey)];
                previous.Seen = true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check whose turn it is in session {SessionId}.", session.Id);
            return Task.CompletedTask;
        }

        if (newlyAwaited.Count == 0 || !sender.IsConfigured)
        {
            return Task.CompletedTask;
        }

        var path = $"{GameSessionStore.PageFor(session.Type)}?session={session.Id}";
        return Task.WhenAll(newlyAwaited.Select(userKey => NotifyAsync(userKey, session.Type, session.Id, path)));
    }

    /// <summary>
    /// Whether one of the user's connections to the session has its page in view.
    /// </summary>
    private bool IsWatching(AwaitedPlayer player) => player.ConnectionIds.Any(c => !_hidden.ContainsKey(c));

    /// <summary>
    /// Sends the turn notification to each of the user's devices, in the device's language, and forgets the devices
    /// that no longer have the app.
    /// </summary>
    private async Task NotifyAsync(string userKey, GameType game, string sessionId, string path)
    {
        try
        {
            foreach (var device in await devices.GetForUserAsync(userKey))
            {
                var (title, body) = texts.Turn(game, texts.Language(device.Language));
                if (await sender.SendAsync(device, new PushNotification(title, body, path, sessionId)) == PushResult.Unregistered)
                {
                    await devices.DeleteAsync(device.Token);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send a turn notification for session {SessionId}.", sessionId);
        }
    }

    private sealed class AwaitedUsers
    {
        public bool Seen { get; set; }

        public HashSet<string> UserKeys { get; set; } = [];
    }
}
