using System.Security.Claims;
using GamesLane.Server.Auth;
using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Games.FinalCard;
using GamesLane.Server.Games.Wazoo;
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
    /// The message everyone in a session receives with each new <see cref="ChatMessage"/>.
    /// </summary>
    public const string ChatMessageReceived = "ChatMessageReceived";

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
        var session = await GetSession(sessionId);
        if (session.IsPublic && !session.IsSeated(UserKey))
        {
            throw new HubException("You are no longer in this game.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(session.Id));
        var playerId = session.Enter(UserKey, AuthSetup.GetProfile(Context.User), Context.ConnectionId);
        await (playerId is not null ? BroadcastState(session) : SendPublicState(session));

        return new SessionEntry(session.GetPublicState(), playerId, session.GetPrivateState(UserKey), session.GetChat(),
            session.HasSeatOffer(UserKey));
    }

    /// <summary>
    /// Marks the connection's user as disconnected in every session where they hold a seat, and updates the observer
    /// count of the sessions they were watching.
    /// Their seats are freed later by <see cref="DisconnectedSeatsMonitor"/> unless they reconnect.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var session in store.All)
        {
            if (session.Disconnect(Context.ConnectionId, timeProvider.GetUtcNow()))
            {
                await (session.IsSeated(UserKey) ? BroadcastState(session) : SendPublicState(session));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <inheritdoc />
    public async Task<string> CreateSession(GameType gameType, bool isPublic)
    {
        var profile = RequireProfile("Log in to start a game.");
        if (!Enum.IsDefined(gameType))
        {
            throw new HubException("Unknown game type.");
        }

        IReadOnlyDictionary<string, string>? options = null;
        if (AuthSetup.GetSavedUserId(Context.User) is { } userId)
        {
            try
            {
                options = await savedOptions.GetAsync(userId, gameType);
            }
            catch (Exception ex)
            {
                // The game still starts, with the default options.
                logger.LogWarning(ex, "Could not load the saved options for {GameType}.", gameType);
            }
        }

        var session = await store.CreateAsync(gameType, s =>
        {
            s.UseSavedOptions(options);
            if (isPublic)
            {
                s.MakePublic();
                s.TryTakeNextSeat(UserKey, profile, Context.ConnectionId, out _);
            }
        });
        return session.Id;
    }

    /// <inheritdoc />
    public async Task<string?> JoinPublicGame(GameType gameType)
    {
        var profile = RequireProfile("Log in to join a game.");
        var candidates = store.All.Where(s => s.Type == gameType && s.IsOpenToJoin).ToList();
        while (candidates.Count > 0)
        {
            var session = candidates[Random.Shared.Next(candidates.Count)];
            candidates.Remove(session);
            if (session.TryTakeNextSeat(UserKey, profile, Context.ConnectionId, out _))
            {
                await BroadcastState(session);
                return session.Id;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WaitingGame>> WaitingGames()
    {
        IReadOnlyList<WaitingGame> games = store.All
            .Where(s => s.IsWaitingFor(UserKey))
            .OrderByDescending(store.LastActivity)
            .Select(s => new WaitingGame(s.Id, s.Type, s.IsPublic))
            .ToList();
        return Task.FromResult(games);
    }

    /// <inheritdoc />
    public async Task TakeSeat(string sessionId)
    {
        var profile = RequireProfile("Log in to take a seat.");
        if ((await GetSession(sessionId)).IsPublic)
        {
            throw new HubException("Public games are joined from the main screen.");
        }

        await Act(sessionId, (IGameSession s, out string? error) => s.TryTakeNextSeat(UserKey, profile, Context.ConnectionId, out error));
    }

    /// <inheritdoc />
    public async Task TakeOfferedSeat(string sessionId, int seat)
    {
        var profile = RequireProfile("Log in to take a seat.");
        await Act(sessionId, (IGameSession s, out string? error) => s.TryTakeSeat(UserKey, profile, Context.ConnectionId, seat, out error));
    }

    /// <inheritdoc />
    public async Task DeclineSeatOffer(string sessionId)
    {
        var session = await GetSession(sessionId);
        if (session.DeclineSeatOffer(UserKey))
        {
            await store.SaveAsync(session);
        }
    }

    /// <inheritdoc />
    public Task SkipPlayer(string sessionId, int seat) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TrySkipPlayer(UserKey, seat, out error));

    /// <inheritdoc />
    public Task LeaveSeat(string sessionId, int seat) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryLeaveSeat(UserKey, seat, out error));

    /// <inheritdoc />
    public async Task LeaveSession(string sessionId)
    {
        var session = await GetSession(sessionId);
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
    public Task EndGame(string sessionId) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryEndGame(UserKey, out error));

    /// <inheritdoc />
    public Task ReturnToLobby(string sessionId) =>
        Act(sessionId, (IGameSession s, out string? error) => s.TryReturnToLobby(UserKey, out error));

    /// <inheritdoc />
    public async Task SendChatMessage(string sessionId, string text)
    {
        var profile = RequireProfile("Log in to chat.");
        var session = await GetSession(sessionId);
        if (!session.TrySendChat(UserKey, profile, text, timeProvider.GetUtcNow(), out var message, out var error))
        {
            throw new HubException(error);
        }

        await Clients.Group(SessionGroup(session.Id)).SendAsync(ChatMessageReceived, message);
        await store.SaveAsync(session);
    }

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
    public Task ChooseJokerCard(string sessionId, int seat, int index) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryChooseJokerCard(UserKey, seat, index, out error));

    /// <inheritdoc />
    public Task ConfirmRedraw(string sessionId, int seat) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryConfirmRedraw(UserKey, seat, out error));

    /// <inheritdoc />
    public Task Roll(string sessionId, int seat) =>
        Act(sessionId, (IBackgammonSession s, out string? error) => s.TryRoll(UserKey, seat, out error));

    /// <inheritdoc />
    public Task MoveChecker(string sessionId, int seat, int from, int to) =>
        Act(sessionId, (IBackgammonSession s, out string? error) => s.TryMove(UserKey, seat, from, to, out error));

    /// <inheritdoc />
    public Task UndoMove(string sessionId, int seat) =>
        Act(sessionId, (IBackgammonSession s, out string? error) => s.TryUndo(UserKey, seat, out error));

    /// <inheritdoc />
    public Task EndTurn(string sessionId, int seat) =>
        Act(sessionId, (IBackgammonSession s, out string? error) => s.TryEndTurn(UserKey, seat, out error));

    /// <inheritdoc />
    public Task OfferDouble(string sessionId, int seat) =>
        Act(sessionId, (IBackgammonSession s, out string? error) => s.TryDouble(UserKey, seat, out error));

    /// <inheritdoc />
    public Task TakeDouble(string sessionId, int seat) =>
        Act(sessionId, (IBackgammonSession s, out string? error) => s.TryTake(UserKey, seat, out error));

    /// <inheritdoc />
    public Task DropDouble(string sessionId, int seat) =>
        Act(sessionId, (IBackgammonSession s, out string? error) => s.TryDrop(UserKey, seat, out error));

    /// <inheritdoc />
    public Task ThrowCards(string sessionId, int seat, string[]? cards) =>
        Act(sessionId, (IRichManPoorManSession s, out string? error) => s.TryThrow(UserKey, seat, cards, out error));

    /// <inheritdoc />
    public Task Pass(string sessionId, int seat) =>
        Act(sessionId, (IRichManPoorManSession s, out string? error) => s.TryPass(UserKey, seat, out error));

    /// <inheritdoc />
    public Task GiveCards(string sessionId, int seat, string[]? cards) =>
        Act(sessionId, (IRichManPoorManSession s, out string? error) => s.TryGive(UserKey, seat, cards, out error));

    /// <inheritdoc />
    public Task RequestSeat(string sessionId)
    {
        var profile = RequireProfile("Log in to ask to join.");
        return Act(sessionId, (IRichManPoorManSession s, out string? error) => s.TryRequestSeat(UserKey, profile, out error));
    }

    /// <inheritdoc />
    public Task CancelSeatRequest(string sessionId) =>
        Act(sessionId, (IRichManPoorManSession s, out string? error) => s.TryCancelSeatRequest(UserKey, out error));

    /// <inheritdoc />
    public Task AcceptSeat(string sessionId, string playerId, int place) =>
        Act(sessionId, (IRichManPoorManSession s, out string? error) => s.TryAcceptSeat(UserKey, playerId, place, out error));

    /// <inheritdoc />
    public Task DeclineSeat(string sessionId, string playerId) =>
        Act(sessionId, (IRichManPoorManSession s, out string? error) => s.TryDeclineSeat(UserKey, playerId, out error));

    /// <inheritdoc />
    public Task RollDice(string sessionId, int seat) =>
        Act(sessionId, (IWazooSession s, out string? error) => s.TryRoll(UserKey, seat, out error));

    /// <inheritdoc />
    public Task HoldDie(string sessionId, int seat, int die, bool held) =>
        Act(sessionId, (IWazooSession s, out string? error) => s.TryHold(UserKey, seat, die, held, out error));

    /// <inheritdoc />
    public Task Score(string sessionId, int seat, WazooCategory category) =>
        Act(sessionId, (IWazooSession s, out string? error) => s.TryScore(UserKey, seat, category, out error));

    /// <inheritdoc />
    public Task PlayCard(string sessionId, int seat, string? card, FinalCardColor? color) =>
        Act(sessionId, (IFinalCardSession s, out string? error) => s.TryPlay(UserKey, seat, card, color, out error));

    /// <inheritdoc />
    public Task DrawCard(string sessionId, int seat) =>
        Act(sessionId, (IFinalCardSession s, out string? error) => s.TryDraw(UserKey, seat, out error));

    /// <inheritdoc />
    public Task PassAfterDraw(string sessionId, int seat) =>
        Act(sessionId, (IFinalCardSession s, out string? error) => s.TryPass(UserKey, seat, out error));

    /// <inheritdoc />
    public Task Challenge(string sessionId, int seat) =>
        Act(sessionId, (IFinalCardSession s, out string? error) => s.TryChallenge(UserKey, seat, out error));

    /// <inheritdoc />
    public Task CallFinalCard(string sessionId, int seat) =>
        Act(sessionId, (IFinalCardSession s, out string? error) => s.TryCallFinalCard(UserKey, seat, out error));

    /// <inheritdoc />
    public Task Discard(string sessionId, int seat, string[]? cards, bool takeOpen) =>
        Act(sessionId, (IYanivSession s, out string? error) => s.TryDiscard(UserKey, seat, cards, takeOpen, out error));

    /// <inheritdoc />
    public Task CallYaniv(string sessionId, int seat) =>
        Act(sessionId, (IYanivSession s, out string? error) => s.TryCallYaniv(UserKey, seat, out error));

    /// <inheritdoc />
    public Task RevealNext(string sessionId, int revealed) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryRevealNext(UserKey, revealed, out error));

    /// <inheritdoc />
    public Task NextHeist(string sessionId, int heist) =>
        Act(sessionId, (ITheGangSession s, out string? error) => s.TryNextHeist(UserKey, heist, out error));

    /// <summary>
    /// Sends the session's public state to everyone in the session, and each player's private information to their
    /// own connections only, then saves the session. Also used outside the hub, by <see cref="DisconnectedSeatsMonitor"/>.
    /// </summary>
    internal static async Task BroadcastState(IHubClients<IClientProxy> clients, IGameSessionStore store, IGameSession session)
    {
        await clients.Group(SessionGroup(session.Id)).SendAsync(StateUpdated, session.GetPublicState());
        foreach (var update in session.GetPrivateUpdates())
        {
            await clients.Client(update.ConnectionId).SendAsync(PrivateStateUpdated, update.State);
        }

        await store.SaveAsync(session);
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
        var session = await GetSession(sessionId) as T ?? throw new HubException("That action is not available in this game.");
        if (!action(session, out var error))
        {
            throw new HubException(error);
        }

        await BroadcastState(session);
    }

    /// <summary>
    /// The session with the given code; throws an error to the caller if there is none.
    /// </summary>
    private async Task<IGameSession> GetSession(string sessionId) =>
        await store.GetAsync(sessionId)
        ?? throw new HubException($"No game with code {sessionId.ToUpperInvariant()} was found. It may have ended.");

    /// <summary>
    /// Sends the session's public state to everyone in the session, and saves it.
    /// </summary>
    private Task BroadcastState(IGameSession session) => BroadcastState(Clients, store, session);

    /// <summary>
    /// Sends the session's public state to everyone in the session, without saving it: for changes that are not saved,
    /// such as the observer count.
    /// </summary>
    private Task SendPublicState(IGameSession session) =>
        Clients.Group(SessionGroup(session.Id)).SendAsync(StateUpdated, session.GetPublicState());
}
