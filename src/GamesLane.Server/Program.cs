using System.Text.Json.Serialization;
using GamesLane.Server.Hubs;
using GamesLane.Server.Sessions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GameSessionStore>();
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapHub<GameHub>("/hubs/game");

app.Run();
