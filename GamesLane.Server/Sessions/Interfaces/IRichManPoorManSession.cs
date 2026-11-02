using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Rich man, poor man for 6 to 12 seats, game after game; it never ends by itself. The next game starts with
/// <see cref="IRoundBasedSession.TryNextRound"/> (the round is the game number). Cards are numbers from 2 to 14 (the Ace).
/// </summary>
public interface IRichManPoorManSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, throws <paramref name="count"/> cards of
    /// number <paramref name="rank"/>.
    /// </summary>
    bool TryThrow(string userKey, int seat, int rank, int count, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> passes.
    /// </summary>
    bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> gives <paramref name="cards"/> to its exchange partner, between games.
    /// </summary>
    bool TryGive(string userKey, int seat, IReadOnlyList<int>? cards, [NotNullWhen(false)] out string? error);
}
