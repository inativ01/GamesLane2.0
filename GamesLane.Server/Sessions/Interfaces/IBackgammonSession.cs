using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A backgammon match for two seats: seat 0 plays white, seat 1 black. The next game of the match starts with
/// <see cref="IRoundBasedSession.TryNextRound"/> (the round is the game number). Points are the seat's own numbering:
/// 1 to 24 in the direction it moves, 25 for the bar and 0 for off the board.
/// </summary>
public interface IBackgammonSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, rolls the dice.
    /// </summary>
    bool TryRoll(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> moves a checker from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    bool TryMove(string userKey, int seat, int from, int to, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> takes back the last checker it moved in this turn.
    /// </summary>
    bool TryUndo(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> ends its turn.
    /// </summary>
    bool TryEndTurn(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> offers to double before rolling.
    /// </summary>
    bool TryDouble(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> takes the double.
    /// </summary>
    bool TryTake(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> drops the double, losing the game.
    /// </summary>
    bool TryDrop(string userKey, int seat, [NotNullWhen(false)] out string? error);
}
