using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using GamesLane.Server.Auth;
using GamesLane.Server.Data;
using GamesLane.Server.Hubs;

namespace GamesLane.Server.Notifications;

/// <summary>
/// The body of <c>POST /api/notifications/device</c>: the token the notification service gave the app (Apple's device
/// token in hexadecimal, or Firebase's registration token), whether it is for Apple's development servers, the language
/// of the notifications (a code from i18n.js), and the platform (<c>ios</c>, the default, or <c>android</c>).
/// </summary>
public record DeviceRequest(string? Token, bool Sandbox, string? Language, string? Platform = null);

/// <summary>
/// Turn notifications in the iPhone and Android apps (see <see cref="TurnNotifier"/>): the services, and the endpoint
/// where the app registers the device for the logged-in user.
/// </summary>
public static partial class NotificationEndpoints
{
    /// <summary>
    /// The rate limit for registering devices: at most <see cref="DevicesPerHour"/> per address in an hour, so made-up
    /// tokens cannot fill the database (anyone can become a guest).
    /// </summary>
    public const string DeviceRateLimit = "devices";

    private const int DevicesPerHour = 60;

    [GeneratedRegex("^[0-9a-fA-F]{64,200}$")]
    private static partial Regex DeviceTokenFormat();

    [GeneratedRegex("^[A-Za-z0-9_:-]{100,1000}$")]
    private static partial Regex FirebaseTokenFormat();

    /// <summary>
    /// Registers the devices, Apple's and Firebase's notification services, the texts, the notifier, the cleanup of
    /// unused devices, and the registrations' rate limit.
    /// </summary>
    public static void AddGamesLaneNotifications(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options => options.AddPolicy(DeviceRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = DevicesPerHour, Window = TimeSpan.FromHours(1) })));
        builder.Services.AddHttpClient(ApnsPushSender.HttpClientName);
        builder.Services.AddHttpClient(FcmPushSender.HttpClientName);
        builder.Services.AddSingleton<IDeviceTokenRepository, DeviceTokenRepository>();
        builder.Services.AddSingleton(services => FirebaseSettings.From(services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<ILogger<FirebaseSettings>>()));
        builder.Services.AddSingleton<ApnsPushSender>();
        builder.Services.AddSingleton<FcmPushSender>();
        builder.Services.AddSingleton<IPushSender, PushSender>();
        builder.Services.AddSingleton(services =>
            NotificationTexts.Load(services.GetRequiredService<IWebHostEnvironment>().WebRootPath ?? ""));
        builder.Services.AddSingleton<ITurnNotifier, TurnNotifier>();
        builder.Services.AddHostedService<DeviceTokenCleanup>();
    }

    /// <summary>
    /// Whether <paramref name="token"/> looks like a device token from Apple: 64 to 200 hexadecimal digits.
    /// </summary>
    public static bool IsDeviceToken(string? token) => token is not null && DeviceTokenFormat().IsMatch(token);

    /// <summary>
    /// Whether <paramref name="token"/> looks like a token for <paramref name="platform"/>: from Apple (see
    /// <see cref="IsDeviceToken(string?)"/>), or from Firebase: 100 to 1000 letters, digits, <c>_</c>, <c>-</c> and <c>:</c>.
    /// </summary>
    public static bool IsDeviceToken(string? token, DevicePlatform platform) =>
        platform == DevicePlatform.Android ? token is not null && FirebaseTokenFormat().IsMatch(token) : IsDeviceToken(token);

    /// <summary>
    /// The platform a registration names: <c>android</c>, or <c>ios</c> (also when it names none, as the iPhone app's
    /// first versions did); null for any other.
    /// </summary>
    public static DevicePlatform? ParsePlatform(string? platform) => platform?.ToLowerInvariant() switch
    {
        null or "" or "ios" => DevicePlatform.Ios,
        "android" => DevicePlatform.Android,
        _ => null,
    };

    /// <summary>
    /// The token as it is kept: Apple's in lowercase; Firebase's as it is, since its letters' case matters.
    /// </summary>
    public static string StoredToken(string token, DevicePlatform platform) =>
        platform == DevicePlatform.Ios ? token.ToLowerInvariant() : token;

    /// <summary>
    /// Maps <c>/api/notifications/device</c>.
    /// </summary>
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        // The app registers the device every time it opens the site, so the device follows the user who is logged in
        // and is kept as long as it is used.
        app.MapPost("/api/notifications/device", async (DeviceRequest request, HttpContext context,
            IDeviceTokenRepository devices, NotificationTexts texts, TimeProvider timeProvider) =>
        {
            if (AuthSetup.GetUserId(context.User) is null)
            {
                return Results.Unauthorized();
            }

            if (ParsePlatform(request.Platform) is not { } platform || !IsDeviceToken(request.Token, platform))
            {
                return Results.BadRequest(new { error = "That is not a device token." });
            }

            await devices.SaveAsync(StoredToken(request.Token!, platform), platform, GameHub.GetUserKey(context.User, ""),
                platform == DevicePlatform.Ios && request.Sandbox, texts.Language(request.Language), timeProvider.GetUtcNow());
            return Results.NoContent();
        }).RequireRateLimiting(DeviceRateLimit);
    }
}
