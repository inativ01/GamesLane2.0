using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using GamesLane.Server.Sessions;
using GamesLane.Server.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace GamesLane.Server.Auth;

/// <summary>
/// The external sign-in providers that are configured (have a client ID and secret).
/// </summary>
public record AuthProviders(IReadOnlyList<string> Names);

/// <summary>
/// An external account just signed in with: the provider (e.g. Google), the account's ID there, its name and picture,
/// and the provider's refresh token to keep with it, if any (see <see cref="AppleAccountTokens"/>).
/// </summary>
public record ExternalAccount(string Provider, string Key, string? Name, string? PictureUrl, ExternalLoginToken? Token = null);

/// <summary>
/// The body of <c>POST /auth/app-signin</c>: the code the iPhone app's sign-in window returned with, and the verifier
/// whose challenge started the sign-in.
/// </summary>
public record AppSignInRequest(string? Code, string? Verifier);

/// <summary>
/// What an iPhone app sign-in code holds: the external account and the challenge the sign-in started with.
/// </summary>
public record AppSignInCodeContent(ExternalAccount Account, string Challenge);

/// <summary>
/// The iPhone app sign-in codes already exchanged, kept until they expire, so each code works only once.
/// </summary>
public class UsedAppSignInCodes(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> used = new();

    /// <summary>
    /// Marks <paramref name="code"/>, valid until <paramref name="expiration"/>, as used. Returns false if it already was.
    /// </summary>
    public bool TryUse(string code, DateTimeOffset expiration)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (key, until) in used)
        {
            if (until <= now)
            {
                used.TryRemove(key, out _);
            }
        }

        return used.TryAdd(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))), expiration);
    }
}

/// <summary>
/// Every visitor is logged in: as a guest (created automatically, with a random name and icon), as a registered user
/// (username and password), or with an external account.
/// Each external provider is enabled only when its client ID and secret are configured, e.g. through the environment
/// variables <c>Authentication__Google__ClientId</c> and <c>Authentication__Google__ClientSecret</c>.
/// A logged-in user is identified by a signed login token, which the page keeps per browser tab and sends with each
/// request (and as the <c>access_token</c> query parameter when connecting to the hub), so each tab can have its own user.
/// The token holds the user's ID, display name, icon and kind. Guests exist only in their token, which does not expire;
/// saved users' tokens last 30 days. A cookie is used only briefly, to bring the result of an external sign-in back to
/// the page.
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
    /// The Discord sign-in provider and its configuration section.
    /// </summary>
    public const string Discord = "Discord";

    /// <summary>
    /// The Sign in with Apple provider (see <see cref="AppleSettings"/>).
    /// </summary>
    public const string Apple = "Apple";

    /// <summary>
    /// The kind of user who has not logged in with an account. Guests are not saved.
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

    /// <summary>
    /// Where an external sign-in started by the iPhone or Android app returns to (see <see cref="MapExternalSignInEndpoints"/>).
    /// </summary>
    public const string AppSignInPath = "/auth/app-signin";

    /// <summary>
    /// The address on the site that ends the app's sign-in window, which only the app can receive (see
    /// <see cref="AppleSettings.SiteAssociation"/> and <see cref="AndroidSettings.AssetLinks"/>). Opened anywhere else, it
    /// goes to the main screen.
    /// </summary>
    public const string AppReturnPath = "/auth/app-return";

    /// <summary>
    /// How long the code that brings an external sign-in back to the app is valid.
    /// </summary>
    public static readonly TimeSpan AppSignInCodeLifetime = TimeSpan.FromMinutes(5);

    private const string KindClaim = "kind";
    private const string IconClaim = "icon";
    private const string PictureClaim = "picture";
    private const string RefreshTokenClaim = "refresh_token";
    private const string ChallengeItem = "appChallenge";
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

        if (TryGetCredentials(builder.Configuration, Discord, out var discordId, out var discordSecret))
        {
            enabled.Add(Discord);
            auth.AddDiscord(Discord, options =>
            {
                options.ClientId = discordId;
                options.ClientSecret = discordSecret;
                // The name shown on Discord (global_name) when set, otherwise the username.
                options.ClaimActions.DeleteClaim(ClaimTypes.Name);
                options.ClaimActions.MapCustomJson(ClaimTypes.Name, u =>
                    (u.TryGetProperty("global_name", out var g) ? g.GetString() : null) ?? u.GetProperty("username").GetString());
                options.ClaimActions.MapCustomJson(PictureClaim, u => DiscordAvatarUrl(
                    u.GetProperty("id").GetString(), u.TryGetProperty("avatar", out var a) ? a.GetString() : null));
                options.Events.OnRemoteFailure = HandleRemoteFailure;
            });
        }

        var apple = AppleSettings.From(builder.Configuration);
        if (apple.CanSignInOnWebsite)
        {
            enabled.Add(Apple);
            auth.AddApple(Apple, options =>
            {
                options.ClientId = apple.ServicesId;
                options.TeamId = apple.TeamId;
                options.KeyId = apple.KeyId!;
                options.GenerateClientSecret = true;
                options.PrivateKey = (_, _) => Task.FromResult(apple.PrivateKey!.AsMemory());
                options.Events.OnCreatingTicket = AddAppleNameAndToken;
                options.Events.OnRemoteFailure = HandleRemoteFailure;
            });
        }

        builder.Services.AddSingleton(new AuthProviders(enabled));
        builder.Services.AddSingleton(apple);
        builder.Services.AddSingleton(AndroidSettings.From(builder.Configuration));
        builder.Services.AddSingleton<UsedAppSignInCodes>();
        builder.Services.AddSingleton<AppleIdentityTokens>();
        builder.Services.AddSingleton<AppleAccountTokens>();

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
    /// The iPhone and Android apps sign in with <c>?app=true&amp;challenge=...</c> in Apple's sign-in window or a Chrome
    /// Auth Tab, whose cookies the app's web view does not share: <c>GET /auth/app-signin</c> then returns to the app with
    /// a short-lived code (see <see cref="AppReturnUrl"/>), which the page exchanges for a login token with
    /// <c>POST /auth/app-signin</c>, with the verifier of the challenge (see <see cref="AppSignInChallenge"/>). Sign in
    /// with Apple in the iPhone app uses Apple's own sheet instead, whose identity token the page exchanges with
    /// <c>POST /auth/apple-app</c>; the Android app signs in with Apple as with the other providers.
    /// Also maps <c>/.well-known/apple-app-site-association</c> and <c>/.well-known/assetlinks.json</c>.
    /// </summary>
    public static void MapExternalSignInEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/signin/{provider}", (string provider, string? returnUrl, bool? app, string? challenge, AuthProviders providers) =>
        {
            var scheme = providers.Names.FirstOrDefault(n => n.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (scheme is null)
            {
                return Results.NotFound();
            }

            if (app != true)
            {
                return Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [scheme]);
            }

            if (!IsAppSignInChallenge(challenge))
            {
                return Results.BadRequest();
            }

            var properties = new AuthenticationProperties { RedirectUri = AppSignInPath };
            properties.Items[ChallengeItem] = challenge;
            return Results.Challenge(properties, [scheme]);
        });

        app.MapPost("/auth/external", async (HttpContext context, IUserService users) =>
            await ReadExternalAccountAsync(context) is ({ } account, _)
                ? await SignInExternalAsync(context, users, account)
                : Results.Unauthorized());

        app.MapGet(AppSignInPath, async Task<IResult> (HttpContext context) =>
            Results.Redirect(await ReadExternalAccountAsync(context) is ({ } account, var properties)
                && properties?.Items.TryGetValue(ChallengeItem, out var challenge) == true && IsAppSignInChallenge(challenge)
                    ? AppReturnUrl(CreateAppSignInCode(context, account, challenge!), null)
                    : AppReturnUrl(null, "failed")));

        app.MapPost(AppSignInPath, async (AppSignInRequest request, HttpContext context, IUserService users) =>
            ReadAppSignInCode(context, request.Code, request.Verifier) is { } account
                ? await SignInExternalAsync(context, users, account)
                : Results.Unauthorized())
            .RequireRateLimiting(LoginRateLimit);

        app.MapGet(AppReturnPath, () => Results.Redirect("/?signin=failed"));

        app.MapPost("/auth/apple-app", async (AppleAppSignInRequest request, HttpContext context, IUserService users,
            AppleIdentityTokens tokens, AppleAccountTokens accountTokens) =>
        {
            if (await tokens.ValidateAsync(request.IdentityToken, request.Nonce) is not { } subject)
            {
                return Results.Unauthorized();
            }

            var token = await accountTokens.RedeemAppCodeAsync(request.AuthorizationCode);
            return await SignInExternalAsync(context, users, new ExternalAccount(Apple, subject, NullIfEmpty(request.Name), null, token));
        }).RequireRateLimiting(LoginRateLimit);

        app.MapGet("/.well-known/apple-app-site-association", (AppleSettings apple) => Results.Json(apple.SiteAssociation()));
        app.MapGet("/.well-known/assetlinks.json", (AndroidSettings android) => Results.Json(android.AssetLinks()));
    }

    /// <summary>
    /// The challenge for an iPhone app sign-in's <paramref name="verifier"/>: its SHA-256 hash in base64url (as in PKCE).
    /// </summary>
    public static string AppSignInChallenge(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    /// <summary>
    /// Whether <paramref name="challenge"/> looks like one from <see cref="AppSignInChallenge"/>: 43 base64url characters.
    /// </summary>
    public static bool IsAppSignInChallenge(string? challenge) =>
        challenge is { Length: 43 } && Base64Url.IsValid(challenge);

    /// <summary>
    /// The address that ends an external sign-in in the iPhone app's sign-in window (<see cref="AppReturnPath"/>), with
    /// the sign-in <paramref name="code"/> or an <paramref name="error"/>.
    /// </summary>
    public static string AppReturnUrl(string? code, string? error) =>
        code is not null
            ? $"{AppReturnPath}?code={Uri.EscapeDataString(code)}"
            : $"{AppReturnPath}?error={Uri.EscapeDataString(error ?? "failed")}";

    /// <summary>
    /// The name in the <c>user</c> field Apple posts on a user's first Sign in with Apple on the website, e.g.
    /// <c>{"name":{"firstName":"Ada","lastName":"Lovelace"}}</c>, or null.
    /// </summary>
    public static string? AppleUserName(string? user)
    {
        try
        {
            using var json = JsonDocument.Parse(user ?? "");
            if (!json.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Part(string key) => name.TryGetProperty(key, out var part) && part.ValueKind == JsonValueKind.String ? part.GetString() : null;
            return NullIfEmpty(string.Join(" ", new[] { Part("firstName"), Part("lastName") }.Where(p => !string.IsNullOrWhiteSpace(p))));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A code holding an external account the iPhone app has just signed in with and the sign-in's
    /// <paramref name="challenge"/>, valid for <see cref="AppSignInCodeLifetime"/>.
    /// </summary>
    public static string CreateAppSignInCode(HttpContext context, ExternalAccount account, string challenge)
    {
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var content = JsonSerializer.Serialize(new AppSignInCodeContent(account, challenge));
        return AppSignInProtector(context).Protect(content, now + AppSignInCodeLifetime);
    }

    /// <summary>
    /// The external account in a code from <see cref="CreateAppSignInCode"/>, or null if the code is not valid, has
    /// expired, was already used, or <paramref name="verifier"/> is not the one of its challenge.
    /// </summary>
    public static ExternalAccount? ReadAppSignInCode(HttpContext context, string? code, string? verifier)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(verifier))
        {
            return null;
        }

        AppSignInCodeContent? content;
        DateTimeOffset expiration;
        try
        {
            content = JsonSerializer.Deserialize<AppSignInCodeContent>(AppSignInProtector(context).Unprotect(code, out expiration));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }

        var matches = content is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(content.Challenge), Encoding.UTF8.GetBytes(AppSignInChallenge(verifier)));
        return matches && context.RequestServices.GetRequiredService<UsedAppSignInCodes>().TryUse(code, expiration)
            ? content!.Account
            : null;
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
    /// A new login token for <paramref name="user"/>: valid for 30 days for saved users, and without end for guests,
    /// who exist only in their token.
    /// </summary>
    public static string IssueToken(HttpContext context, ClaimsPrincipal user)
    {
        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(TokenScheme);
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var expires = GetKind(user) == GuestKind ? DateTimeOffset.MaxValue : now + options.BearerTokenExpiration;
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = expires };
        return options.BearerTokenProtector.Protect(new AuthenticationTicket(user, properties, TokenScheme));
    }

    /// <summary>
    /// The user in a login token, or null if the token is not valid or has expired.
    /// </summary>
    public static ClaimsPrincipal? ReadToken(HttpContext context, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(TokenScheme);
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var ticket = options.BearerTokenProtector.Unprotect(token);
        return ticket?.Properties.ExpiresUtc is { } expires && expires > now ? ticket.Principal : null;
    }

    /// <summary>
    /// Only allows returning to a page on this site, to prevent redirects to other sites.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl[0] == '/' && (returnUrl.Length == 1 || returnUrl[1] is not ('/' or '\\'))
            ? returnUrl
            : "/";

    /// <summary>
    /// A Discord user's avatar picture (128×128 PNG), or null if they have none.
    /// </summary>
    public static string? DiscordAvatarUrl(string? userId, string? avatarHash) =>
        string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(avatarHash)
            ? null
            : $"https://cdn.discordapp.com/avatars/{Uri.EscapeDataString(userId)}/{Uri.EscapeDataString(avatarHash)}.png?size=128";

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
    /// When an external sign-in fails or is cancelled, returns to the main screen with an error (or to the iPhone app).
    /// </summary>
    private static Task HandleRemoteFailure(RemoteFailureContext context)
    {
        context.Response.Redirect(context.Properties?.RedirectUri == AppSignInPath ? AppReturnUrl(null, "failed") : "/?signin=failed");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds the name Apple posts on a user's first Sign in with Apple on the website (it is not in Apple's token), and
    /// Apple's refresh token, to keep and revoke when the account is deleted.
    /// </summary>
    private static Task AddAppleNameAndToken(OAuthCreatingTicketContext context)
    {
        if (context.Request.HasFormContentType && AppleUserName(context.Request.Form["user"]) is { } name)
        {
            context.Identity?.AddClaim(new Claim(ClaimTypes.Name, name, ClaimValueTypes.String, context.Options.ClaimsIssuer));
        }

        if (!string.IsNullOrEmpty(context.RefreshToken))
        {
            context.Identity?.AddClaim(new Claim(RefreshTokenClaim, context.RefreshToken));
        }

        return Task.CompletedTask;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The external account brought back by the short-lived sign-in cookie, which is then removed, and the sign-in's
    /// properties; the account is null if there is none. Apple's refresh token, if any, comes encrypted for the database.
    /// </summary>
    private static async Task<(ExternalAccount? Account, AuthenticationProperties? Properties)> ReadExternalAccountAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(ExternalScheme);
        await context.SignOutAsync(ExternalScheme);
        var account = result.Principal?.FindFirst(ClaimTypes.NameIdentifier);
        if (!result.Succeeded || account is null)
        {
            return (null, null);
        }

        var token = account.Issuer == Apple && result.Principal!.FindFirstValue(RefreshTokenClaim) is { Length: > 0 } refreshToken
            ? context.RequestServices.GetRequiredService<AppleAccountTokens>()
                .Protect(context.RequestServices.GetRequiredService<AppleSettings>().ServicesId, refreshToken)
            : null;
        return (new ExternalAccount(account.Issuer, account.Value, result.Principal!.FindFirstValue(ClaimTypes.Name),
            result.Principal.FindFirstValue(PictureClaim), token), result.Properties);
    }

    /// <summary>
    /// Signs in with an external account (creating the user on first sign-in): <c>{ accessToken, isNew }</c>.
    /// </summary>
    private static async Task<IResult> SignInExternalAsync(HttpContext context, IUserService users, ExternalAccount account)
    {
        var signIn = await users.SignInExternalAsync(account.Provider, account.Key, account.Name, account.PictureUrl, account.Token);
        var user = CreateUser(signIn.User.Id.ToString(), signIn.User.DisplayName, signIn.User.Icon, account.Provider);
        return Results.Ok(new { accessToken = IssueToken(context, user), isNew = signIn.IsNew });
    }

    private static ITimeLimitedDataProtector AppSignInProtector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("GamesLane.AppSignIn")
            .ToTimeLimitedDataProtector();
}
