using System.Text.Json.Serialization;
using GamesLane.Server.Auth;
using GamesLane.Server.Hubs;
using GamesLane.Server.Sessions;

var builder = WebApplication.CreateBuilder(args);

// Hosting platforms such as Render provide the port to listen on in the PORT environment variable.
if (builder.Configuration["PORT"] is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

builder.AddGamesLaneAuth();
builder.Services.AddSingleton<IGameSessionStore, GameSessionStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<DisconnectedSeatsMonitor>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
// Browsers must check for a newer version before reusing a saved page or script, so a deploy takes effect immediately.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseAuthentication();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapAuthEndpoints();

app.MapGet("/api/sessions/{code}", (string code, IGameSessionStore store) =>
    store.Get(code) is { } session ? Results.Ok(SessionInfo(session)) : Results.NotFound());

app.MapGet("/join/{code}", (string code, IGameSessionStore store) =>
    store.Get(code) is { } session
        ? Results.Redirect($"{GameSessionStore.PageFor(session.Type)}?session={session.Id}")
        : Results.Redirect($"/?session={Uri.EscapeDataString(code.ToUpperInvariant())}&notfound=1"));

app.MapHub<GameHub>("/hubs/game");

app.Run();

// The response of GET /api/sessions/{code}: the session code, its game, and the page that plays it.
static object SessionInfo(IGameSession session) =>
    new { sessionId = session.Id, gameType = session.Type, page = GameSessionStore.PageFor(session.Type) };
