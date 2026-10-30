using System.Security.Claims;
using GamesLane.Server.Auth;
using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Sessions;
using GamesLane.Server.Users;
using Microsoft.AspNetCore.SignalR;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Real-time connection point for game sessions.
/// Everyone with a session's page open (players and spectators) joins the session's group and receives public
/// <see cref="StateUpdated"/> messages. Private, per-player information is returned only to the calling connection.
/// Players are logged-in users, identified by their user ID, so they keep their seats after a refresh or reconnect.
/// </summary>
public class GameHub(
    IGameSessionStore store,
    TimeProvider timeProvider,
    IGameOptionsService savedOptions,
    ILogger<GameHub> logger) : Hub, IGameHub
{
    /// <summary>
    /// The message clients receive with the new <see cref="SessionState"/> after every change.
    /// </summary>
    public const string StateUpdated = "StateUpdated";

    /// <summary>
    /// The message a player's connections receive with their private game information (e.g. their cards) after every change.
    /// </summary>
    public const string PrivateStateUpdated = "PrivateStateUpdated";

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
    /// The caller's display name and icon; throws <paramref name="message"/> to the caller if they are not logged in.
    /// </summary>
    private PlayerProfile RequireProfile(string message) => AuthSetup.GetProfile(Context.User) ?? throw new HubException(message);

    /// <inheritdoc />
    public async Task<SessionEntry> EnterSession(string sessionId)
    {
        var session = GetSession(sessionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(session.Id));
        var playerId = session.Enter(UserKey, AuthSetup.GetProfile(Context.User), Context.ConnectionId);
        if (playerId is not null)
        {
            await BroadcastState(session);
        }

        return new SessionEntry(session.GetPublicState(), playerId, session.GetPrivateState(UserKey));
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
    public async Task<string> CreateSession(GameType gameType)
    {
        RequireProfile("Log in to start a game.");
        if (!Enum.IsDefined(gameType))
        {
            throw new HubException("Unknown game type.");
        }

        var session = store.Create(gameType);
        if (AuthSetup.GetSavedUserId(Context.User) is { } userId)
        {
            try
            {
                session.UseSavedOptions(await savedOptions.GetAsync(userId, gameType));
            }
            catch (Exception ex)
            {
                // The game still starts, with the default options.
                logger.LogWarning(ex, "Could not load the saved options for {GameType}.", gameType);
            }
        }

        return session.Id;
    }

    /// <inheritdoc />
    public Task TakeSeat(string sessionId)
    {
        var profile = RequireProfile("Log in to take a seat.");
        return Act(sessionId, (IGameSession s, out string? error) => s.TryTakeNextSeat(UserKey, profile, Context.ConnectionId, out error));
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
    public Task SetOptions(string sessionId, Dictionary<string, string>? options) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TrySetOptions(UserKey, options, out error));

    /// <inheritdoc />
    public Task ResumeGame(string sessionId) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryResume(UserKey, out error));

    /// <inheritdoc />
    public Task EndGame(string sessionId) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryEndGame(UserKey, out error));

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
        Act(sessionId, (IRoundBasedSession s, out string? error) => s.TryNextRound(UserKey, round, out error));

    /// <inheritdoc />
    public Task TakeChip(string sessionId, int seat, int chip) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryTakeChip(UserKey, seat, chip, out error));

    /// <inheritdoc />
    public Task ReturnChip(string sessionId, int seat) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryReturnChip(UserKey, seat, out error));

    /// <inheritdoc />
    public Task SetReady(string sessionId, int seat, bool ready) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TrySetReady(UserKey, seat, ready, out error));

    /// <inheritdoc />
    public Task PassCard(string sessionId, int seat, int cardIndex) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryPassCard(UserKey, seat, cardIndex, out error));

    /// <inheritdoc />
    public Task ConfirmSeen(string sessionId, int seat) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryConfirmSeen(UserKey, seat, out error));

    /// <inheritdoc />
    public Task Propose(string sessionId, int seat, string card, int? targetSeat, string? value) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryPropose(UserKey, seat, card, targetSeat, value, out error));

    /// <inheritdoc />
    public Task Agree(string sessionId, int seat, string card, bool agree) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryAgree(UserKey, seat, card, agree, out error));

    /// <inheritdoc />
    public Task ShowCard(string sessionId, int seat, int cardIndex, int toSeat) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryShowCard(UserKey, seat, cardIndex, toSeat, out error));

    /// <inheritdoc />
    public Task DiscardCard(string sessionId, int seat, int cardIndex) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryDiscard(UserKey, seat, cardIndex, out error));

    /// <inheritdoc />
    public Task RevealNext(string sessionId, int revealed) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryRevealNext(UserKey, revealed, out error));

    /// <inheritdoc />
    public Task NextHeist(string sessionId, int heist) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryNextHeist(UserKey, heist, out error));

    /// <summary>
    /// Sends the session's public state to everyone in the session, and each player's private information to their
    /// own connections only. Also used outside the hub, by <see cref="DisconnectedSeatsMonitor"/>.
    /// </summary>
    internal static async Task BroadcastState(IHubClients<IClientProxy> clients, IGameSession session)
    {
        await clients.Group(SessionGroup(session.Id)).SendAsync(StateUpdated, session.GetPublicState());
        foreach (var update in session.GetPrivateUpdates())
        {
            await clients.Client(update.ConnectionId).SendAsync(PrivateStateUpdated, update.State);
        }
    }

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
        store.Get(sessionId) ?? throw new HubException($"No game with code {sessionId.ToUpperInvariant()} was found. It may have ended.");

    /// <summary>
    /// Sends the session's public state to everyone in the session.
    /// </summary>
    private Task BroadcastState(IGameSession session) => BroadcastState(Clients, session);
}
