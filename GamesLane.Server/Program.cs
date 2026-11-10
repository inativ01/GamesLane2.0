using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using GamesLane.Server.Auth;
using GamesLane.Server.Data;
using GamesLane.Server.Feedback;
using GamesLane.Server.Hubs;
using GamesLane.Server.Sessions;
using GamesLane.Server.Stats;
using GamesLane.Server.Users;

var builder = WebApplication.CreateBuilder(args);

// Hosting platforms such as Render provide the port to listen on in the PORT environment variable.
if (builder.Configuration["PORT"] is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

builder.AddGamesLaneDatabase();
builder.AddGamesLaneAuth();
builder.AddGamesLaneFeedback();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IGameOptionsService, GameOptionsService>();
builder.Services.AddSingleton(AdminUsers.From(builder.Configuration));
builder.Services.AddHostedService<InactiveUserCleanup>();
builder.Services.AddSingleton<ISessionRepository, SessionRepository>();
builder.Services.AddSingleton<IGamePlayRepository, GamePlayRepository>();
builder.Services.AddSingleton<IGameSessionStore, GameSessionStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<DisconnectedSeatsMonitor>();
builder.Services.AddHostedService<ExpiredSessionCleanup>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Each side sends a keep-alive message every 5 seconds and treats 10 seconds of silence as a lost connection, so a
// player whose phone or browser closed without saying goodbye shows as Reconnecting within about 10 seconds.
// The page's settings in common.js must match.
builder.Services.AddSignalR(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(5);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(10);
    })
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
// Browsers must check for a newer version before reusing a saved page or script, so a deploy takes effect immediately.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});
// Developer pages in the dev folder (e.g. /dev/cards.html, every playing card), only when running locally.
if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "dev")),
        RequestPath = "/dev",
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
    });
}
app.UseAuthentication();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapAccountEndpoints();
app.MapExternalSignInEndpoints();
app.MapFeedbackEndpoints();
app.MapStatsEndpoints();

// Public games cannot be joined by code, so they are not found here.
app.MapGet("/api/sessions/{code}", async (string code, IGameSessionStore store) =>
    await store.GetAsync(code) is { IsPublic: false } session ? Results.Ok(SessionInfo(session)) : Results.NotFound());

app.MapGet("/join/{code}", async (string code, IGameSessionStore store) =>
    await store.GetAsync(code) is { IsPublic: false } session
        ? Results.Redirect($"{GameSessionStore.PageFor(session.Type)}?session={session.Id}")
        : Results.Redirect($"/?session={Uri.EscapeDataString(code.ToUpperInvariant())}&notfound=1"));

app.MapHub<GameHub>("/hubs/game");

await app.MigrateDatabaseAsync();
app.Run();

// The response of GET /api/sessions/{code}: the session code, its game, and the page that plays it.
static object SessionInfo(IGameSession session) =>
    new { sessionId = session.Id, gameType = session.Type, page = GameSessionStore.PageFor(session.Type) };
