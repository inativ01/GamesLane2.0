using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace GamesLane.Server.Auth;

/// <summary>
/// The external sign-in providers that are configured (have a client ID and secret).
/// </summary>
public record AuthProviders(IReadOnlyList<string> Names);

/// <summary>
/// Sign-in with external accounts. Each provider is enabled only when its client ID and secret are configured,
/// e.g. through the environment variables <c>Authentication__Google__ClientId</c> and <c>Authentication__Google__ClientSecret</c>.
/// After sign-in, the user is kept signed in with a cookie.
/// </summary>
public static class AuthSetup
{
    public const string Google = "Google";
    public const string Microsoft = "Microsoft";

    public static void AddGamesLaneAuth(this WebApplicationBuilder builder)
    {
        var enabled = new List<string>();
        var auth = builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "GamesLane.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
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
            var signedIn = user.Identity?.IsAuthenticated == true;
            return new
            {
                signedIn,
                name = signedIn ? user.FindFirstValue(ClaimTypes.Name) : null,
                provider = signedIn ? user.FindFirst(ClaimTypes.NameIdentifier)?.Issuer : null,
                providers = providers.Names,
            };
        });

        app.MapGet("/auth/signin/{provider}", (string provider, string? returnUrl, AuthProviders providers) =>
        {
            var scheme = providers.Names.FirstOrDefault(n => n.Equals(provider, StringComparison.OrdinalIgnoreCase));
            return scheme is null
                ? Results.NotFound()
                : Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [scheme]);
        });

        app.MapPost("/auth/signout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

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
