using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GamesLane.Server.Auth;

/// <summary>
/// The external sign-in providers that are configured (have a client ID and secret).
/// </summary>
public record AuthProviders(IReadOnlyList<string> Names);

public record NameRequest(string? Name);

/// <summary>
/// Users log in by entering a name, or with an external account. Each external provider is enabled only when its
/// client ID and secret are configured, e.g. through the environment variables <c>Authentication__Google__ClientId</c>
/// and <c>Authentication__Google__ClientSecret</c>.
/// A logged-in user is identified by a signed login token, which the page keeps per browser tab and sends with each
/// request (and as the <c>access_token</c> query parameter when connecting to the hub), so each tab can have its own user.
/// A cookie is used only briefly, to bring the result of an external sign-in back to the page.
/// </summary>
public static class AuthSetup
{
    public const string Google = "Google";
    public const string Microsoft = "Microsoft";
    public const string NameProvider = "Name";
    public const string ProviderClaim = "provider";
    public const int MaxNameLength = 30;

    private const string TokenScheme = BearerTokenDefaults.AuthenticationScheme;
    private const string ExternalScheme = CookieAuthenticationDefaults.AuthenticationScheme;

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
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/me", (ClaimsPrincipal user, AuthProviders providers) =>
        {
            var userId = GetUserId(user);
            return new
            {
                signedIn = userId is not null,
                userId,
                name = userId is null ? null : GetUserName(user),
                provider = userId is null ? null : user.FindFirstValue(ProviderClaim),
                providers = providers.Names,
            };
        });

        app.MapPost("/auth/login", (NameRequest request) =>
            NormalizeName(request.Name) is { } name
                ? SignInUser($"name:{Guid.NewGuid():N}", name, NameProvider)
                : InvalidName());

        app.MapPost("/auth/name", (NameRequest request, ClaimsPrincipal user) =>
        {
            if (GetUserId(user) is not { } userId)
            {
                return Results.Unauthorized();
            }

            return NormalizeName(request.Name) is { } name
                ? SignInUser(userId, name, user.FindFirstValue(ProviderClaim) ?? NameProvider)
                : InvalidName();
        });

        app.MapGet("/auth/signin/{provider}", (string provider, string? returnUrl, AuthProviders providers) =>
        {
            var scheme = providers.Names.FirstOrDefault(n => n.Equals(provider, StringComparison.OrdinalIgnoreCase));
            return scheme is null
                ? Results.NotFound()
                : Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [scheme]);
        });

        app.MapPost("/auth/external", async (HttpContext context) =>
        {
            var result = await context.AuthenticateAsync(ExternalScheme);
            await context.SignOutAsync(ExternalScheme);
            var account = result.Principal?.FindFirst(ClaimTypes.NameIdentifier);
            if (!result.Succeeded || account is null)
            {
                return Results.Unauthorized();
            }

            var externalName = result.Principal!.FindFirstValue(ClaimTypes.Name)?.Trim() ?? "";
            var name = NormalizeName(externalName.Length > MaxNameLength ? externalName[..MaxNameLength] : externalName) ?? "Player";
            return SignInUser($"{account.Issuer.ToLowerInvariant()}:{account.Value}", name, account.Issuer);
        });
    }

    public static string? GetUserId(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public static string? GetUserName(ClaimsPrincipal? user) =>
        GetUserId(user) is null ? null : user!.FindFirstValue(ClaimTypes.Name);

    /// <summary>
    /// Returns the trimmed display name, or null if it is empty or longer than <see cref="MaxNameLength"/>.
    /// </summary>
    public static string? NormalizeName(string? name) =>
        name?.Trim() is { Length: > 0 and <= MaxNameLength } trimmed ? trimmed : null;

    public static ClaimsPrincipal CreateUser(string userId, string name, string provider) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, name),
                new Claim(ProviderClaim, provider),
            ],
            TokenScheme));

    /// <summary>
    /// Responds with a new login token: <c>{ tokenType, accessToken, expiresIn, refreshToken }</c>.
    /// </summary>
    private static IResult SignInUser(string userId, string name, string provider) =>
        Results.SignIn(CreateUser(userId, name, provider), authenticationScheme: TokenScheme);

    private static IResult InvalidName() =>
        Results.BadRequest(new { error = $"Enter a name of 1 to {MaxNameLength} characters." });

    /// <summary>
    /// Only allows returning to a page on this site, to prevent redirects to other sites.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl[0] == '/' && (returnUrl.Length == 1 || returnUrl[1] is not ('/' or '\\'))
            ? returnUrl
            : "/";

    private static bool TryGetCredentials(IConfiguration configuration, string provider, out string clientId, out string clientSecret)
    {
        var section = configuration.GetSection($"Authentication:{provider}");
        clientId = section["ClientId"] ?? "";
        clientSecret = section["ClientSecret"] ?? "";
        return clientId.Length > 0 && clientSecret.Length > 0;
    }

    private static Task HandleRemoteFailure(RemoteFailureContext context)
    {
        context.Response.Redirect("/?signin=failed");
        context.HandleResponse();
        return Task.CompletedTask;
    }
}
