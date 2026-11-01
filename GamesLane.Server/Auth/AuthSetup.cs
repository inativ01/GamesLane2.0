using System.Security.Claims;
using System.Threading.RateLimiting;
using GamesLane.Server.Sessions;
using GamesLane.Server.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace GamesLane.Server.Auth;

/// <summary>
/// The external sign-in providers that are configured (have a client ID and secret).
/// </summary>
public record AuthProviders(IReadOnlyList<string> Names);

/// <summary>
/// Users log in as a guest (a name only), as a registered user (username and password), or with an external account.
/// Each external provider is enabled only when its client ID and secret are configured, e.g. through the environment
/// variables <c>Authentication__Google__ClientId</c> and <c>Authentication__Google__ClientSecret</c>.
/// A logged-in user is identified by a signed login token, which the page keeps per browser tab and sends with each
/// request (and as the <c>access_token</c> query parameter when connecting to the hub), so each tab can have its own user.
/// The token holds the user's ID, display name, icon and kind. A cookie is used only briefly, to bring the result of an
/// external sign-in back to the page.
/// </summary>
public static class AuthSetup
{
    /// <summary>
    /// The Google sign-in provider and its configuration section.
    /// </summary>
    public const string Google = "Google";

    /// <summary>
    /// The Microsoft sign-in provider and its configuration section.
    /// </summary>
    public const string Microsoft = "Microsoft";

    /// <summary>
    /// The kind of user who logged in with just a name. Guests are not saved.
    /// </summary>
    public const string GuestKind = "Guest";

    /// <summary>
    /// The kind of user who logs in with a username and password.
    /// </summary>
    public const string RegisteredKind = "Registered";

    /// <summary>
    /// The rate-limiting policy for logging in and registering.
    /// </summary>
    public const string LoginRateLimit = "login";

    private const string KindClaim = "kind";
    private const string IconClaim = "icon";
    private const string PictureClaim = "picture";
    private const string TokenScheme = BearerTokenDefaults.AuthenticationScheme;
    private const string ExternalScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>
    /// Registers login tokens, the short-lived external sign-in cookie, and each external provider that is configured.
    /// </summary>
    public static void AddGamesLaneAuth(this WebApplicationBuilder builder)
    {
        var enabled = new List<string>();
        var auth = builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = TokenScheme;
                options.DefaultSignInScheme = ExternalScheme;
            })
            .AddBearerToken(TokenScheme, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromDays(30);
                options.Events.OnMessageReceived = context =>
                {
                    // Browsers cannot send headers on WebSocket connections, so SignalR sends the token in the query string.
                    var token = context.Request.Query["access_token"].ToString();
                    if (token.Length > 0 && context.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = token;
                    }

                    return Task.CompletedTask;
                };
            })
            .AddCookie(ExternalScheme, options =>
            {
                options.Cookie.Name = "GamesLane.External";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            });

        if (TryGetCredentials(builder.Configuration, Google, out var googleId, out var googleSecret))
        {
            enabled.Add(Google);
            auth.AddGoogle(Google, options =>
            {
                options.ClientId = googleId;
                options.ClientSecret = googleSecret;
                options.ClaimActions.MapJsonKey(PictureClaim, PictureClaim);
                options.Events.OnRemoteFailure = HandleRemoteFailure;
            });
        }

        if (TryGetCredentials(builder.Configuration, Microsoft, out var microsoftId, out var microsoftSecret))
        {
            enabled.Add(Microsoft);
            auth.AddMicrosoftAccount(Microsoft, options =>
            {
                options.ClientId = microsoftId;
                options.ClientSecret = microsoftSecret;
                options.Events.OnRemoteFailure = HandleRemoteFailure;
            });
        }

        builder.Services.AddSingleton(new AuthProviders(enabled));

        // At most 10 login or registration attempts per minute from one address, so passwords cannot be guessed quickly.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });
    }

    /// <summary>
    /// Maps external sign-in: <c>GET /auth/signin/{provider}</c> starts it, and <c>POST /auth/external</c> exchanges
    /// its result for a login token, creating the user on first sign-in.
    /// </summary>
    public static void MapExternalSignInEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/signin/{provider}", (string provider, string? returnUrl, AuthProviders providers) =>
        {
            var scheme = providers.Names.FirstOrDefault(n => n.Equals(provider, StringComparison.OrdinalIgnoreCase));
            return scheme is null
                ? Results.NotFound()
                : Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [scheme]);
        });

        app.MapPost("/auth/external", async (HttpContext context, IUserService users) =>
        {
            var result = await context.AuthenticateAsync(ExternalScheme);
            await context.SignOutAsync(ExternalScheme);
            var account = result.Principal?.FindFirst(ClaimTypes.NameIdentifier);
            if (!result.Succeeded || account is null)
            {
                return Results.Unauthorized();
            }

            var signIn = await users.SignInExternalAsync(
                account.Issuer,
                account.Value,
                result.Principal!.FindFirstValue(ClaimTypes.Name),
                result.Principal.FindFirstValue(PictureClaim));
            var user = CreateUser(signIn.User.Id.ToString(), signIn.User.DisplayName, signIn.User.Icon, account.Issuer);
            return Results.Ok(new { accessToken = IssueToken(context, user), isNew = signIn.IsNew });
        });
    }

    /// <summary>
    /// The logged-in user's ID (a GUID for saved users, <c>guest:...</c> for guests), or null if no one is logged in.
    /// </summary>
    public static string? GetUserId(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    /// <summary>
    /// The saved user's ID, or null for guests and when no one is logged in.
    /// </summary>
    public static Guid? GetSavedUserId(ClaimsPrincipal? user) =>
        Guid.TryParse(GetUserId(user), out var id) ? id : null;

    /// <summary>
    /// How the logged-in user logged in: <see cref="GuestKind"/>, <see cref="RegisteredKind"/> or a provider such as
    /// <see cref="Google"/>. Null if no one is logged in.
    /// </summary>
    public static string? GetKind(ClaimsPrincipal? user) =>
        GetUserId(user) is null ? null : user!.FindFirstValue(KindClaim);

    /// <summary>
    /// The logged-in user's display name and icon as other players see them, or null if no one is logged in.
    /// </summary>
    public static PlayerProfile? GetProfile(ClaimsPrincipal? user) =>
        user is null || GetUserId(user) is null
            ? null
            : new PlayerProfile(user.FindFirstValue(ClaimTypes.Name) ?? "Player", user.FindFirstValue(IconClaim) ?? UserRules.DefaultIcon);

    /// <summary>
    /// The logged-in user stored in a login token: their user ID, display name, icon and how they logged in.
    /// </summary>
    public static ClaimsPrincipal CreateUser(string userId, string name, string icon, string kind) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, name),
                new Claim(IconClaim, icon),
                new Claim(KindClaim, kind),
            ],
            TokenScheme));

    /// <summary>
    /// A new login token for <paramref name="user"/>, valid for 30 days.
    /// </summary>
    public static string IssueToken(HttpContext context, ClaimsPrincipal user)
    {
        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(TokenScheme);
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + options.BearerTokenExpiration };
        return options.BearerTokenProtector.Protect(new AuthenticationTicket(user, properties, TokenScheme));
    }

    /// <summary>
    /// Only allows returning to a page on this site, to prevent redirects to other sites.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl[0] == '/' && (returnUrl.Length == 1 || returnUrl[1] is not ('/' or '\\'))
            ? returnUrl
            : "/";

    /// <summary>
    /// Reads a provider's client ID and secret from configuration. Returns false if either is missing.
    /// </summary>
    private static bool TryGetCredentials(IConfiguration configuration, string provider, out string clientId, out string clientSecret)
    {
        var section = configuration.GetSection($"Authentication:{provider}");
        clientId = section["ClientId"] ?? "";
        clientSecret = section["ClientSecret"] ?? "";
        return clientId.Length > 0 && clientSecret.Length > 0;
    }

    /// <summary>
    /// When an external sign-in fails or is cancelled, returns to the main screen with an error.
    /// </summary>
    private static Task HandleRemoteFailure(RemoteFailureContext context)
    {
        context.Response.Redirect("/?signin=failed");
        context.HandleResponse();
        return Task.CompletedTask;
    }
}
