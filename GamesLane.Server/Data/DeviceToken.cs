namespace GamesLane.Server.Data;

/// <summary>
/// The kind of phone a <see cref="DeviceToken"/> is for, which decides the service that delivers its notifications.
/// </summary>
public enum DevicePlatform
{
    /// <summary>An iPhone or iPad: Apple's push notification service.</summary>
    Ios,

    /// <summary>An Android phone or tablet: Firebase Cloud Messaging.</summary>
    Android,
}

/// <summary>
/// A phone running the app, which gets a notification when it is its user's turn: the token the notification service
/// gave the app, the kind of phone, the user (a saved user or a guest), and the language of the notifications. Deleted
/// after 30 days without use.
/// </summary>
public class DeviceToken
{
    /// <summary>
    /// The longest token kept: Firebase's tokens have no fixed length (about 160 characters today).
    /// </summary>
    public const int MaxTokenLength = 1000;

    /// <summary>
    /// The token the notification service gave the app: Apple's device token in hexadecimal on an iPhone, or
    /// Firebase's registration token on Android.
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// The kind of phone.
    /// </summary>
    public DevicePlatform Platform { get; set; }

    /// <summary>
    /// The user the device belongs to, as the game hub knows them, e.g. <c>user:guest:...</c>.
    /// </summary>
    public required string UserKey { get; set; }

    /// <summary>
    /// Whether the token is for Apple's development (sandbox) servers: an app built for development, not TestFlight or
    /// the App Store.
    /// </summary>
    public bool IsSandbox { get; set; }

    /// <summary>
    /// The language of the notifications, a code from i18n.js such as <c>he</c>.
    /// </summary>
    public required string Language { get; set; }

    /// <summary>
    /// When the app last registered the device.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
