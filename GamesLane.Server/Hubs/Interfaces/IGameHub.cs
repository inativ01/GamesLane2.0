using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Methods clients can call on the game hub.
/// </summary>
public interface IGameHub
{
    /// <summary>
    /// Opens a session page: subscribes to updates and, for a logged-in user, reconnects them to their seats.
    /// </summary>
    Task<SessionEntry> EnterSession(string sessionId);

    /// <summary>
    /// Logged-in users only. Creates a session and returns its code. The creator is not seated.
    /// </summary>
    string CreateSession(GameType gameType);

    /// <summary>
    /// Takes a seat as the logged-in user, using their display name.
    /// </summary>
    Task TakeSeat(string sessionId, int seat);

    /// <summary>
    /// Gives up one of the caller's seats (used for Leave seat and Stand up).
    /// </summary>
    Task LeaveSeat(string sessionId, int seat);

    /// <summary>
    /// Stops receiving updates and gives up all the caller's seats (used when going back to the main screen).
    /// </summary>
    Task LeaveSession(string sessionId);

    /// <summary>
    /// Seated users only. Starts a new game for everyone seated, replacing game <paramref name="gameNumber"/>
    /// (0 before the first game). If that game has already been replaced, nothing changes.
    /// </summary>
    Task NewGame(string sessionId, int gameNumber);

    /// <summary>
    /// Seated users only. Continues the paused game; whoever now holds each of its seats plays that seat.
    /// </summary>
    Task ResumeGame(string sessionId);

    /// <summary>
    /// Seated users only. After a game has ended, takes everyone back to the session lobby.
    /// </summary>
    Task ReturnToLobby(string sessionId);

    /// <summary>
    /// Tic-tac-toe: places the mark of <paramref name="seat"/>, which the caller must hold, in <paramref name="cell"/> (0-8).
    /// </summary>
    Task MakeMove(string sessionId, int seat, int cell);

    /// <summary>
    /// Rock-paper-scissors: picks for <paramref name="seat"/>, which the caller must hold.
    /// </summary>
    Task Pick(string sessionId, int seat, Choice choice);

    /// <summary>
    /// Rock-paper-scissors and tic-tac-toe: starts the round after <paramref name="round"/>. If it has already started,
    /// nothing changes.
    /// </summary>
    Task NextRound(string sessionId, int round);

    /// <summary>
    /// The Gang: <paramref name="seat"/> takes a chip of the current stage, from the middle or from another player.
    /// </summary>
    Task TakeChip(string sessionId, int seat, int chip);

    /// <summary>
    /// The Gang: <paramref name="seat"/> puts its chip of the current stage back in the middle.
    /// </summary>
    Task ReturnChip(string sessionId, int seat);

    /// <summary>
    /// The Gang: marks <paramref name="seat"/> ready (or not) to end the current stage.
    /// </summary>
    Task SetReady(string sessionId, int seat, bool ready);

    /// <summary>
    /// The Gang: reveals the next hand, if <paramref name="revealed"/> hands have been revealed so far.
    /// </summary>
    Task RevealNext(string sessionId, int revealed);

    /// <summary>
    /// The Gang: deals the heist after <paramref name="heist"/>, once its hands are revealed.
    /// </summary>
    Task NextHeist(string sessionId, int heist);
}
