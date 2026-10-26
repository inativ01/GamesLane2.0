using System.Text.Json.Serialization;
using GamesLane.Server.Hubs;
using GamesLane.Server.Sessions;

var builder = WebApplication.CreateBuilder(args);

// Hosting platforms such as Render provide the port to listen on in the PORT environment variable.
if (builder.Configuration["PORT"] is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

builder.Services.AddSingleton<GameSessionStore>();
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapHub<GameHub>("/hubs/game");

app.Run();
