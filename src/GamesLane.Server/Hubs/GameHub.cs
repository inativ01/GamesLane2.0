using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Real-time connection point for game sessions.
/// Everyone in a game (players and spectators) joins the game's public group and receives shared board updates.
/// Players also join a per-player group, used to send private information such as their hand.
/// </summary>
public class GameHub : Hub
{
    public static string GameGroup(string gameId) => $"game:{gameId}";

    public static string PlayerGroup(string gameId, string playerId) => $"game:{gameId}:player:{playerId}";

    public async Task JoinAsSpectator(string gameId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(gameId));
    }

    public async Task JoinAsPlayer(string gameId, string playerId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(gameId));
        await Groups.AddToGroupAsync(Context.ConnectionId, PlayerGroup(gameId, playerId));
    }

    public async Task Leave(string gameId, string? playerId = null)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GameGroup(gameId));
        if (playerId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, PlayerGroup(gameId, playerId));
        }
    }
}
