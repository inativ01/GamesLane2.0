using GamesLane.Server.Data;

namespace GamesLane.Server.Notifications;

/// <summary>
/// Sends each notification through the service of the device's platform: Apple's for iPhones, Firebase's for Android.
/// </summary>
public class PushSender(ApnsPushSender apple, FcmPushSender firebase) : IPushSender
{
    /// <inheritdoc />
    /// <remarks>Whether either service can be used; a device whose service has no key is not sent anything.</remarks>
    public bool IsConfigured => apple.IsConfigured || firebase.IsConfigured;

    /// <inheritdoc />
    public Task<PushResult> SendAsync(DeviceToken device, PushNotification notification) =>
        (device.Platform == DevicePlatform.Android ? (IPushSender)firebase : apple).SendAsync(device, notification);
}
