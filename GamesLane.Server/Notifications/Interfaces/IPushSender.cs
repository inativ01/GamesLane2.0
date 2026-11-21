using GamesLane.Server.Data;

namespace GamesLane.Server.Notifications;

/// <summary>
/// A notification to show on a phone: its <see cref="Title"/> and <see cref="Body"/>, the page the app opens when it
/// is tapped (<see cref="Path"/>, e.g. <c>/backgammon.html?session=ABC123</c>), and the session it is about, so a newer
/// notification about the same session replaces it.
/// </summary>
public record PushNotification(string Title, string Body, string Path, string SessionId);

/// <summary>
/// What became of a notification sent to a device.
/// </summary>
public enum PushResult
{
    /// <summary>The notification service accepted it.</summary>
    Sent,

    /// <summary>The device no longer has the app (or the token is not valid), so it should be forgotten.</summary>
    Unregistered,

    /// <summary>It could not be sent this time.</summary>
    Failed,
}

/// <summary>
/// Sends notifications to the app: on iPhones through Apple's push notification service (<see cref="ApnsPushSender"/>),
/// on Android through Firebase Cloud Messaging (<see cref="FcmPushSender"/>), chosen by <see cref="PushSender"/>.
/// </summary>
public interface IPushSender
{
    /// <summary>
    /// Whether notifications can be sent: the server has the service's key.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends <paramref name="notification"/> to <paramref name="device"/>.
    /// </summary>
    Task<PushResult> SendAsync(DeviceToken device, PushNotification notification);
}
