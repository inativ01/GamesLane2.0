using GamesLane.Server.Sessions;

namespace GamesLane.Server.Notifications;

/// <summary>
/// Tells the iPhone and Android apps' users when it becomes their turn while they are not watching the game.
/// </summary>
public interface ITurnNotifier
{
    /// <summary>
    /// The page on <paramref name="connectionId"/> went out of view (the app in the background, the phone locked,
    /// another tab) or came back.
    /// </summary>
    void SetPageHidden(string connectionId, bool hidden);

    /// <summary>
    /// Forgets a closed connection.
    /// </summary>
    void ConnectionClosed(string connectionId);

    /// <summary>
    /// Called after every change to <paramref name="session"/>: notifies each user whose turn it has just become and who
    /// has no page of the session in view. The returned task ends once the notifications are sent.
    /// </summary>
    Task SessionChangedAsync(IGameSession session);
}
