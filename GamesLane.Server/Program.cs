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
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapAuthEndpoints();

app.MapGet("/api/games/{code}", (string code, IGameSessionStore store) =>
    store.Get(code) is { } session ? Results.Ok(GameInfo(session)) : Results.NotFound());

app.MapGet("/join/{code}", (string code, IGameSessionStore store) =>
    store.Get(code) is { } session
        ? Results.Redirect($"{GameSessionStore.PageFor(session.Type)}?game={session.Id}")
        : Results.Redirect($"/?game={Uri.EscapeDataString(code.ToUpperInvariant())}&notfound=1"));

app.MapHub<GameHub>("/hubs/game");

app.Run();

static object GameInfo(IGameSession session) =>
    new { gameId = session.Id, gameType = session.Type, page = GameSessionStore.PageFor(session.Type) };
