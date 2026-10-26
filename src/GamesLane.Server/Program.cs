using GamesLane.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapHub<GameHub>("/hubs/game");

app.Run();
