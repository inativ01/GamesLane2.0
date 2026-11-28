using System.Threading.RateLimiting;
using GamesLane.Server.Auth;
using GamesLane.Server.Notifications;

namespace GamesLane.Server.Tv;

/// <summary>
/// The body of <c>POST /api/tv/register</c>: the TV's name, e.g. "Living Room".
/// </summary>
public record TvRegisterRequest(string? Name);

/// <summary>
/// The body of <c>POST /api/tv/pair</c>: the code the TV shows.
/// </summary>
public record TvPairRequest(string? Code);

/// <summary>
/// The site's translations, for the Apple TV app: per language code, each English text's translation.
/// </summary>
public record TvTexts(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations);

/// <summary>
/// The TV apps (the Apple TV app, and the web TV app, <c>tv.html</c>; see <see cref="TvService"/>): the services, and the
/// endpoints where a TV registers and gets the site's texts and its pairing QR code, and where users pair TVs, list them
/// and unpair them.
/// </summary>
public static class TvEndpoints
{
    /// <summary>
    /// The rate limit for registering TVs: at most <see cref="TvsPerHour"/> per address in an hour.
    /// </summary>
    public const string TvRateLimit = "tvs";

    /// <summary>
    /// The name of a TV that gives none.
    /// </summary>
    public const string DefaultName = "TV";

    /// <summary>
    /// The icon of TVs.
    /// </summary>
    public const string Icon = "📺";

    /// <summary>
    /// The longest TV name kept.
    /// </summary>
    public const int MaxNameLength = 30;

    /// <summary>
    /// The longest text made into a QR code.
    /// </summary>
    public const int MaxQrTextLength = 200;

    private const int TvsPerHour = 20;

    /// <summary>
    /// Registers the links, the pairings, the service, the texts, the cleanup of unused pairings, the expiry of links,
    /// and the registrations' rate limit.
    /// </summary>
    public static void AddGamesLaneTv(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options => options.AddPolicy(TvRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = TvsPerHour, Window = TimeSpan.FromHours(1) })));
        builder.Services.AddSingleton<TvLinks>();
        builder.Services.AddSingleton<ITvPairingRepository, TvPairingRepository>();
        builder.Services.AddSingleton<TvService>();
        builder.Services.AddSingleton(services =>
            new TvTexts(NotificationTexts.ReadTranslations(services.GetRequiredService<IWebHostEnvironment>().WebRootPath ?? "")));
        builder.Services.AddHostedService<TvPairingCleanup>();
        builder.Services.AddHostedService<TvLinkMonitor>();
    }

    /// <summary>
    /// The name a TV is shown with: <paramref name="name"/> on one line, cut to <see cref="MaxNameLength"/> characters,
    /// or <see cref="DefaultName"/>.
    /// </summary>
    public static string TvName(string? name)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length == 0 ? DefaultName : clean.Length <= MaxNameLength ? clean : clean[..MaxNameLength].TrimEnd();
    }

    /// <summary>
    /// The QR code of <paramref name="text"/> as an SVG picture, or null for no text or one longer than
    /// <see cref="MaxQrTextLength"/>.
    /// </summary>
    public static string? QrSvg(string? text) =>
        text is { Length: > 0 and <= MaxQrTextLength } ? QrCode.Encode(text)?.ToSvg() : null;

    /// <summary>
    /// Maps <c>/api/tv/register</c>, <c>/api/tv/texts/{language}</c>, <c>/api/tv/qr</c>, <c>/api/tv/pair</c>,
    /// <c>/api/tv</c> and <c>/api/tv/{tvId}</c>.
    /// </summary>
    public static void MapTvEndpoints(this WebApplication app)
    {
        // A TV registers once and keeps its login token, which does not expire.
        app.MapPost("/api/tv/register", (TvRegisterRequest request, HttpContext context) =>
        {
            var tvId = $"tv:{Guid.NewGuid():N}";
            var tv = AuthSetup.CreateUser(tvId, TvName(request.Name), Icon, AuthSetup.TvKind);
            return Results.Ok(new { tvId, accessToken = AuthSetup.IssueToken(context, tv) });
        }).RequireRateLimiting(TvRateLimit);

        app.MapGet("/api/tv/texts/{language}", (string language, TvTexts texts) =>
            Results.Ok(texts.Translations.TryGetValue(language, out var translations) ? translations : new Dictionary<string, string>()));

        // The web TV app's pairing screen shows the QR code of the site's address with its code.
        app.MapGet("/api/tv/qr", (string? text) =>
            QrSvg(text) is { } svg ? Results.Text(svg, "image/svg+xml") : Results.BadRequest(new { error = "Give a short text." }));

        // Typing or scanning the code proves the user is in front of the TV; codes are guessed no faster than logins.
        app.MapPost("/api/tv/pair", async (TvPairRequest request, HttpContext context, TvService tvs) =>
        {
            if (AuthSetup.GetSavedUserId(context.User) is not { } userId)
            {
                return Results.Unauthorized();
            }

            return await tvs.PairAsync(userId, request.Code) is { } name
                ? Results.Ok(new { name })
                : Results.BadRequest(new { error = "No TV shows this code. Check the code on the TV." });
        }).RequireRateLimiting(AuthSetup.LoginRateLimit);

        app.MapGet("/api/tv", async (HttpContext context, TvService tvs) =>
            AuthSetup.GetSavedUserId(context.User) is { } userId ? Results.Ok(await tvs.ListAsync(userId)) : Results.Unauthorized());

        app.MapDelete("/api/tv/{tvId}", async (string tvId, HttpContext context, TvService tvs) =>
        {
            if (AuthSetup.GetSavedUserId(context.User) is not { } userId)
            {
                return Results.Unauthorized();
            }

            await tvs.UnpairAsync(userId, tvId);
            return Results.NoContent();
        });
    }
}
