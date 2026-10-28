using System.Security.Claims;
using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Real-time connection point for game sessions.
/// Everyone in a game (players and spectators) joins the game's group and receives public "StateUpdated" messages.
/// Private, per-player information is returned only to the calling connection.
/// Players are identified by their signed-in account or, for guests, by a random guest ID the browser keeps
/// (sent as the <c>guestId</c> query parameter), so they can rejoin their seat after a refresh or reconnect.
/// </summary>
public class GameHub(GameSessionStore store) : Hub
{
    public const string StateUpdated = "StateUpdated";

    public static string GameGroup(string gameId) => $"game:{gameId.ToUpperInvariant()}";

    /// <summary>
    /// The caller's stable identity: their account if signed in, otherwise their guest ID, otherwise their connection.
    /// </summary>
    public static string GetUserKey(ClaimsPrincipal? user, string? guestId, string connectionId)
    {
        var account = user?.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier) : null;
        if (account is not null)
        {
            return $"user:{account.Issuer}:{account.Value}";
        }

        return IsValidGuestId(guestId) ? $"guest:{guestId}" : $"connection:{connectionId}";
    }

    private static bool IsValidGuestId(string? guestId) =>
        guestId is { Length: >= 16 and <= 64 } && guestId.All(char.IsAsciiLetterOrDigit);

    private string UserKey => GetUserKey(
        Context.User,
        Context.GetHttpContext()?.Request.Query["guestId"],
        Context.ConnectionId);

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

    public async Task<PlayerInfo> JoinAsPlayer(string gameId, string playerName)
    {
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
        var session = GetSession<TicTacToeSession>(gameId);
        if (!session.TryMove(Context.ConnectionId, cell, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task PlayAgain(string gameId, int gameNumber)
    {
        var session = GetSession<TicTacToeSession>(gameId);
        if (!session.TryPlayAgain(Context.ConnectionId, gameNumber, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task Pick(string gameId, Choice choice)
    {
        var session = GetSession<RockPaperScissorsSession>(gameId);
        if (!session.TryPick(Context.ConnectionId, choice, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    public async Task NextRound(string gameId, int round)
    {
        var session = GetSession<RockPaperScissorsSession>(gameId);
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

    private GameSession GetSession(string gameId) =>
        store.Get(gameId) ?? throw new HubException($"Game '{gameId}' was not found.");

    private T GetSession<T>(string gameId) where T : GameSession =>
        GetSession(gameId) as T ?? throw new HubException("That action is not available in this game.");

    private Task BroadcastState(GameSession session) =>
        Clients.Group(GameGroup(session.Id)).SendAsync(StateUpdated, session.GetPublicState());
}
