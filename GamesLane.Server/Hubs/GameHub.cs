using System.Security.Claims;
using GamesLane.Server.Auth;
using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Real-time connection point for game sessions.
/// Everyone in a game (players and spectators) joins the game's group and receives public "StateUpdated" messages.
/// Private, per-player information is returned only to the calling connection.
/// Players are logged-in users, identified by their user ID, so they can rejoin their seat after a refresh or reconnect.
/// </summary>
public class GameHub(IGameSessionStore store) : Hub, IGameHub
{
    public const string StateUpdated = "StateUpdated";

    public static string GameGroup(string gameId) => $"game:{gameId.ToUpperInvariant()}";

    /// <summary>
    /// The caller's stable identity: their user ID if logged in, otherwise their connection.
    /// </summary>
    public static string GetUserKey(ClaimsPrincipal? user, string connectionId) =>
        AuthSetup.GetUserId(user) is { } userId ? $"user:{userId}" : $"connection:{connectionId}";

    private string UserKey => GetUserKey(Context.User, Context.ConnectionId);

    private string RequireUserName(string message) => AuthSetup.GetUserName(Context.User) ?? throw new HubException(message);

    /// <summary>
    /// Opens a game page: subscribes to updates and, if the caller already holds a seat, reconnects them to it.
    /// </summary>
    public async Task<GameEntry> EnterGame(string gameId)
    {
        var session = GetSession(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(session.Id));

        if (!session.TryRejoin(UserKey, Context.ConnectionId, out var player))
        {
            return new GameEntry(session.GetPublicState(), null);
        }

        await BroadcastState(session);
        return new GameEntry(session.GetPublicState(), new PlayerInfo(player.Id, player.Seat, session.Type));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var session in store.All)
        {
            if (session.Disconnect(Context.ConnectionId))
            {
                await BroadcastState(session);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public string CreateGame(GameType gameType)
    {
        RequireUserName("Log in to start a game.");
        if (!Enum.IsDefined(gameType))
        {
            throw new HubException("Unknown game type.");
        }

        return store.Create(gameType).Id;
    }

    public async Task<object> JoinAsSpectator(string gameId)
    {
        var session = GetSession(gameId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(session.Id));
        return session.GetPublicState();
    }

    public async Task<PlayerInfo> JoinAsPlayer(string gameId)
    {
        var playerName = RequireUserName("Log in to take a seat.");
        var session = GetSession(gameId);
        if (!session.TryAddPlayer(playerName, Context.ConnectionId, out var player, out var error, UserKey))
        {
            throw new HubException(error);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GameGroup(session.Id));
        await BroadcastState(session);
        return new PlayerInfo(player.Id, player.Seat, session.Type);
    }

    public async Task MakeMove(string gameId, int cell)
    {
        var session = GetSession<ITicTacToeSession>(gameId);
        if (!session.TryMove(Context.ConnectionId, cell, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task PlayAgain(string gameId, int gameNumber)
    {
        var session = GetSession<ITicTacToeSession>(gameId);
        if (!session.TryPlayAgain(Context.ConnectionId, gameNumber, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task Pick(string gameId, Choice choice)
    {
        var session = GetSession<IRockPaperScissorsSession>(gameId);
        if (!session.TryPick(Context.ConnectionId, choice, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task NextRound(string gameId, int round)
    {
        var session = GetSession<IRockPaperScissorsSession>(gameId);
        if (!session.TryNextRound(Context.ConnectionId, round, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task Leave(string gameId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GameGroup(gameId));
    }

    private IGameSession GetSession(string gameId) =>
        store.Get(gameId) ?? throw new HubException($"Game '{gameId}' was not found.");

    private T GetSession<T>(string gameId) where T : class, IGameSession =>
        GetSession(gameId) as T ?? throw new HubException("That action is not available in this game.");

    private Task BroadcastState(IGameSession session) =>
        Clients.Group(GameGroup(session.Id)).SendAsync(StateUpdated, session.GetPublicState());
}
