using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Real-time connection point for game sessions.
/// Everyone in a game (players and spectators) joins the game's group and receives public "StateUpdated" messages.
/// Private, per-player information is returned only to the calling connection.
/// </summary>
public class GameHub(GameSessionStore store) : Hub
{
    public const string StateUpdated = "StateUpdated";

    public static string GameGroup(string gameId) => $"game:{gameId.ToUpperInvariant()}";

    public string CreateGame() => store.Create().Id;

    public async Task<GameState> JoinAsSpectator(string gameId)
    {
        var session = GetSession(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(session.Id));
        return session.GetState();
    }

    public async Task<PlayerInfo> JoinAsPlayer(string gameId, string playerName)
    {
        var session = GetSession(gameId);
        if (!session.TryAddPlayer(playerName, Context.ConnectionId, out var player, out var error))
        {
            throw new HubException(error);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(session.Id));
        await BroadcastState(session);
        return new PlayerInfo(player.Id, player.Mark);
    }

    public async Task MakeMove(string gameId, int cell)
    {
        var session = GetSession(gameId);
        if (!session.TryMove(Context.ConnectionId, cell, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task Leave(string gameId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GameGroup(gameId));
    }

    private GameSession GetSession(string gameId) =>
        store.Get(gameId) ?? throw new HubException($"Game '{gameId}' was not found.");

    private Task BroadcastState(GameSession session) =>
        Clients.Group(GameGroup(session.Id)).SendAsync(StateUpdated, session.GetState());
}
