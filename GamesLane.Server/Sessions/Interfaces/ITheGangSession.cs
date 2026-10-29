using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A session of The Gang for 3 to 6 seats. Each player's two cards are private and sent only to them.
/// </summary>
public interface ITheGangSession : IGameSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, takes a chip of the current stage.
    /// </summary>
    bool TryTakeChip(string userKey, int seat, int chip, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, puts its chip back in the middle.
    /// </summary>
    bool TryReturnChip(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Marks <paramref name="seat"/>, which <paramref name="userKey"/> must hold, ready (or not) to end the stage.
    /// </summary>
    bool TrySetReady(string userKey, int seat, bool ready, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Reveals the next hand, if <paramref name="revealed"/> hands have been revealed so far. Players only.
    /// </summary>
    bool TryRevealNext(string userKey, int revealed, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Deals the heist after <paramref name="heist"/>, once its hands are revealed. Players only.
    /// </summary>
    bool TryNextHeist(string userKey, int heist, [NotNullWhen(false)] out string? error);
}
