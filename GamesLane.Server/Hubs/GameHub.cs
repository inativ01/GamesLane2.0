using System.Security.Claims;
using GamesLane.Server.Auth;
using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Real-time connection point for game sessions.
/// Everyone with a session's page open (players and spectators) joins the session's group and receives public
/// <see cref="StateUpdated"/> messages. Private, per-player information is returned only to the calling connection.
/// Players are logged-in users, identified by their user ID, so they keep their seats after a refresh or reconnect.
/// </summary>
public class GameHub(IGameSessionStore store, TimeProvider timeProvider) : Hub, IGameHub
{
    /// <summary>
    /// The message clients receive with the new <see cref="SessionState"/> after every change.
    /// </summary>
    public const string StateUpdated = "StateUpdated";

    /// <summary>
    /// The caller's stable identity: their user ID if logged in, otherwise their connection.
    /// </summary>
    public static string GetUserKey(ClaimsPrincipal? user, string connectionId) =>
        AuthSetup.GetUserId(user) is { } userId ? $"user:{userId}" : $"connection:{connectionId}";

    /// <summary>
    /// The calling connection's user key.
    /// </summary>
    private string UserKey => GetUserKey(Context.User, Context.ConnectionId);

    /// <summary>
    /// The caller's display name; throws <paramref name="message"/> to the caller if they are not logged in.
    /// </summary>
    private string RequireUserName(string message) => AuthSetup.GetUserName(Context.User) ?? throw new HubException(message);

    /// <inheritdoc />
    public async Task<SessionEntry> EnterSession(string sessionId)
    {
        var session = GetSession(sessionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(session.Id));
        var playerId = session.Enter(UserKey, AuthSetup.GetUserName(Context.User), Context.ConnectionId);
        if (playerId is not null)
        {
            await BroadcastState(session);
        }

        return new SessionEntry(session.GetPublicState(), playerId);
    }

    /// <summary>
    /// Marks the connection's user as disconnected in every session where they hold a seat.
    /// Their seats are freed later by <see cref="DisconnectedSeatsMonitor"/> unless they reconnect.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var session in store.All)
        {
            if (session.Disconnect(Context.ConnectionId, timeProvider.GetUtcNow()))
            {
                await BroadcastState(session);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <inheritdoc />
    public string CreateSession(GameType gameType)
    {
        RequireUserName("Log in to start a session.");
        if (!Enum.IsDefined(gameType))
        {
            throw new HubException("Unknown game type.");
        }

        return store.Create(gameType).Id;
    }

    /// <inheritdoc />
    public Task TakeSeat(string sessionId, int seat)
    {
        var name = RequireUserName("Log in to take a seat.");
        return Act(sessionId, (IGameSession s, out string? error) => s.TryTakeSeat(UserKey, name, Context.ConnectionId, seat, out error));
    }

    /// <inheritdoc />
    public Task LeaveSeat(string sessionId, int seat) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryLeaveSeat(UserKey, seat, out error));

    /// <inheritdoc />
    public async Task LeaveSession(string sessionId)
    {
        var session = GetSession(sessionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionGroup(session.Id));
        if (session.LeaveAllSeats(UserKey))
        {
            await BroadcastState(session);
        }
    }

    /// <inheritdoc />
    public Task NewGame(string sessionId, int gameNumber) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryNewGame(UserKey, gameNumber, out error));

    /// <inheritdoc />
    public Task ResumeGame(string sessionId) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryResume(UserKey, out error));

    /// <inheritdoc />
    public Task ReturnToLobby(string sessionId) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryReturnToLobby(UserKey, out error));

    /// <inheritdoc />
    public Task MakeMove(string sessionId, int seat, int cell) =>
        Act(sessionId, (ITicTacToeSession s, out string? error) => s.TryMove(UserKey, seat, cell, out error));

    /// <inheritdoc />
    public Task Pick(string sessionId, int seat, Choice choice) =>
        Act(sessionId, (IRockPaperScissorsSession s, out string? error) => s.TryPick(UserKey, seat, choice, out error));

    /// <inheritdoc />
    public Task NextRound(string sessionId, int round) =>
        Act(sessionId, (IRockPaperScissorsSession s, out string? error) => s.TryNextRound(UserKey, round, out error));

    /// <summary>
    /// Sends the session's public state to everyone in the session. Also used outside the hub, by <see cref="DisconnectedSeatsMonitor"/>.
    /// </summary>
    internal static Task BroadcastState(IHubClients<IClientProxy> clients, IGameSession session) =>
        clients.Group(SessionGroup(session.Id)).SendAsync(StateUpdated, session.GetPublicState());

    /// <summary>
    /// The SignalR group of everyone who has the session's page open.
    /// </summary>
    private static string SessionGroup(string sessionId) => $"session:{sessionId.ToUpperInvariant()}";

    /// <summary>
    /// A session action that returns false with an error message when it is refused.
    /// </summary>
    private delegate bool SessionAction<in T>(T session, out string? error);

    /// <summary>
    /// Runs an action on a session; on success, sends the new state to everyone in the session, otherwise returns the error to the caller.
    /// </summary>
    private async Task Act<T>(string sessionId, SessionAction<T> action) where T : class, IGameSession
    {
        var session = GetSession(sessionId) as T ?? throw new HubException("That action is not available in this game.");
        if (!action(session, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    /// <summary>
    /// The session with the given code; throws an error to the caller if there is none.
    /// </summary>
    private IGameSession GetSession(string sessionId) =>
        store.Get(sessionId) ?? throw new HubException($"Session '{sessionId}' was not found.");

    /// <summary>
    /// Sends the session's public state to everyone in the session.
    /// </summary>
    private Task BroadcastState(IGameSession session) => BroadcastState(Clients, session);
}
