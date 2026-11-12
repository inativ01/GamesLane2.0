using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A trick-taking game for 4 seats, who play in seat order (see <see cref="TrickTakingSession{TGame}"/>). The next hand
/// starts with <see cref="IRoundBasedSession.TryNextRound"/>. Cards are short text such as "10♥" or "A♠".
/// </summary>
public interface ITrickTakingSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, plays <paramref name="card"/> to the trick
    /// on its turn.
    /// </summary>
    bool TryPlayCard(string userKey, int seat, string? card, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, claims the remaining tricks of the hand when
    /// it leads and its cards win them all.
    /// </summary>
    bool TryClaim(string userKey, int seat, [NotNullWhen(false)] out string? error);
}
