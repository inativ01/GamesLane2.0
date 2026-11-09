using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Wazoo;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A Wazoo game for 1 to 8 seats, who take turns in seat order. A new game (from the session lobby, or Play again)
/// starts with clean scorecards.
/// </summary>
public interface IWazooSession : IGameSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, rolls the dice that are not held.
    /// </summary>
    bool TryRoll(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> holds (or releases) die <paramref name="die"/> (0 to 4) between rolls.
    /// </summary>
    bool TryHold(string userKey, int seat, int die, bool held, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> scores the dice in <paramref name="category"/>, which ends its turn.
    /// </summary>
    bool TryScore(string userKey, int seat, WazooCategory category, [NotNullWhen(false)] out string? error);
}
