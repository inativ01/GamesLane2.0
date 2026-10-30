using System.Security.Claims;
using GamesLane.Server.Data;
using GamesLane.Server.Games;
using GamesLane.Server.Sessions;
using GamesLane.Server.Users;

namespace GamesLane.Server.Auth;

/// <summary>
/// The body of <c>POST /auth/guest</c> and <c>POST /auth/profile</c>.
/// </summary>
public record ProfileRequest(string? Name, string? Icon);

/// <summary>
/// The body of <c>POST /auth/register</c>.
/// </summary>
public record RegisterRequest(string? Username, string? Password, string? Name, string? Icon);

/// <summary>
/// The body of <c>POST /auth/login</c>.
/// </summary>
public record LoginRequest(string? Username, string? Password);

/// <summary>
/// The account endpoints: who is logged in, logging in as a guest or registered user, registering, the profile
/// (display name and icon), and saved game options. Every successful login or profile call returns a fresh login token,
/// which the page keeps for this tab.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>
    /// Maps <c>/auth/me</c>, <c>/auth/guest</c>, <c>/auth/register</c>, <c>/auth/login</c>, <c>/auth/profile</c>,
    /// <c>/auth/icon</c>, <c>/api/icons/{id}</c> and <c>/api/options/{gameType}</c>.
    /// </summary>
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/me", async (HttpContext context, IUserService users, AuthProviders providers) =>
        {
            var user = await RefreshAsync(context.User, users);
            var savedId = AuthSetup.GetSavedUserId(user);
            var profile = AuthSetup.GetProfile(user);
            return Results.Ok(new
            {
                signedIn = user is not null,
                kind = AuthSetup.GetKind(user),
                name = profile?.Name,
                icon = profile?.Icon,
                uploadedIcon = savedId is { } id ? await users.GetUploadedIconPathAsync(id) : null,
                accessToken = user is null ? null : AuthSetup.IssueToken(context, user),
                providers = providers.Names,
                icons = UserRules.BuiltInIcons,
            });
        });

        app.MapPost("/auth/guest", (ProfileRequest request, HttpContext context) =>
        {
            if (UserRules.NormalizeName(request.Name) is not { } name)
            {
                return Error($"Enter a name of 1 to {UserRules.MaxNameLength} characters.");
            }

            var icon = UserRules.IsBuiltInIcon(request.Icon) ? request.Icon : UserRules.DefaultIcon;
            return Token(context, AuthSetup.CreateUser($"guest:{Guid.NewGuid():N}", name, icon, AuthSetup.GuestKind));
        });

        app.MapPost("/auth/register", async (RegisterRequest request, HttpContext context, IUserService users) =>
        {
            var result = await users.RegisterAsync(request.Username, request.Password, request.Name, request.Icon);
            return result.User is { } user ? Token(context, Saved(user, AuthSetup.RegisteredKind)) : Error(result.Error);
        }).RequireRateLimiting(AuthSetup.LoginRateLimit);

        app.MapPost("/auth/login", async (LoginRequest request, HttpContext context, IUserService users) =>
            await users.LogInAsync(request.Username, request.Password) is { } user
                ? Token(context, Saved(user, AuthSetup.RegisteredKind))
                : Error("The username or password is not correct."))
            .RequireRateLimiting(AuthSetup.LoginRateLimit);

        app.MapPost("/auth/profile", async (ProfileRequest request, HttpContext context, IUserService users) =>
        {
            if (AuthSetup.GetKind(context.User) is not { } kind)
            {
                return Results.Unauthorized();
            }

            if (AuthSetup.GetSavedUserId(context.User) is { } id)
            {
                var result = await users.UpdateProfileAsync(id, request.Name, request.Icon);
                return result.User is { } user ? Token(context, Saved(user, kind)) : Error(result.Error);
            }

            if (UserRules.NormalizeName(request.Name) is not { } name)
            {
                return Error($"Enter a name of 1 to {UserRules.MaxNameLength} characters.");
            }

            if (!UserRules.IsBuiltInIcon(request.Icon))
            {
                return Error("Choose one of the icons.");
            }

            return Token(context, AuthSetup.CreateUser(AuthSetup.GetUserId(context.User)!, name, request.Icon, kind));
        });

        app.MapPost("/auth/icon", async (HttpContext context, IUserService users) =>
        {
            if (AuthSetup.GetSavedUserId(context.User) is not { } id || AuthSetup.GetKind(context.User) is not { } kind)
            {
                return Results.Unauthorized();
            }

            if (await ReadBodyAsync(context.Request, UserRules.MaxIconBytes) is not { } data)
            {
                return Error($"The picture must be smaller than {UserRules.MaxIconBytes / 1024} KB.");
            }

            var result = await users.SetUploadedIconAsync(id, data);
            return result.User is { } user ? Token(context, Saved(user, kind)) : Error(result.Error);
        });

        app.MapGet("/api/icons/{id:guid}", async (Guid id, HttpContext context, IUserService users) =>
        {
            if (await users.GetIconAsync(id) is not { } icon)
            {
                return Results.NotFound();
            }

            // An icon's address never changes content, so browsers may keep it.
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(icon.Data, icon.ContentType);
        });

        // Saved users' preferred options per game: { saved, options }, with the game's defaults if none are saved.
        app.MapGet("/api/options/{gameType}", async (string gameType, HttpContext context, IGameOptionsService options) =>
        {
            if (AuthSetup.GetSavedUserId(context.User) is not { } id)
            {
                return Results.Unauthorized();
            }

            if (ParseGameType(gameType) is not { } type)
            {
                return Results.NotFound();
            }

            var saved = await options.GetAsync(id, type);
            return Results.Ok(new { saved = saved is not null, options = saved ?? GameOptions.Defaults(type) });
        });

        app.MapPut("/api/options/{gameType}", async (string gameType, Dictionary<string, string>? body, HttpContext context, IGameOptionsService options) =>
        {
            if (AuthSetup.GetSavedUserId(context.User) is not { } id)
            {
                return Results.Unauthorized();
            }

            if (ParseGameType(gameType) is not { } type)
            {
                return Results.NotFound();
            }

            return await options.SaveAsync(id, type, body) is { } error ? Error(error) : Results.NoContent();
        });
    }

    /// <summary>
    /// The game type named in an address, e.g. <c>TheGang</c> (any case), or null if there is no such game.
    /// </summary>
    private static GameType? ParseGameType(string name) =>
        Enum.TryParse<GameType>(name, ignoreCase: true, out var type) && Enum.IsDefined(type) && !int.TryParse(name, out _)
            ? type
            : null;

    /// <summary>
    /// The logged-in user with their current saved name and icon, or null if no one is logged in or the saved user
    /// no longer exists. Records saved users as active.
    /// </summary>
    private static async Task<ClaimsPrincipal?> RefreshAsync(ClaimsPrincipal principal, IUserService users)
    {
        if (AuthSetup.GetKind(principal) is not { } kind)
        {
            return null;
        }

        if (AuthSetup.GetSavedUserId(principal) is not { } id)
        {
            return principal;
        }

        return await users.GetActiveUserAsync(id) is { } user ? Saved(user, kind) : null;
    }

    /// <summary>
    /// The login-token user for a saved user.
    /// </summary>
    private static ClaimsPrincipal Saved(User user, string kind) =>
        AuthSetup.CreateUser(user.Id.ToString(), user.DisplayName, user.Icon, kind);

    /// <summary>
    /// Responds with <c>{ accessToken }</c>, a new login token for <paramref name="user"/>.
    /// </summary>
    private static IResult Token(HttpContext context, ClaimsPrincipal user) =>
        Results.Ok(new { accessToken = AuthSetup.IssueToken(context, user) });

    /// <summary>
    /// Responds with a 400 error and a message to show.
    /// </summary>
    private static IResult Error(string? message) => Results.BadRequest(new { error = message ?? "Something went wrong." });

    /// <summary>
    /// The request body, or null if it is larger than <paramref name="maxBytes"/>.
    /// </summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maxBytes)
    {
        if (request.ContentLength > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
