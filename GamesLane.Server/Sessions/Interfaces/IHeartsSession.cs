using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A Hearts game for 4 seats, who play in seat order. Cards are short text such as "10♥" or "Q♠".
/// </summary>
public interface IHeartsSession : ITrickTakingSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, passes <paramref name="cards"/> (3 of its
    /// cards) before the hand's play.
    /// </summary>
    bool TryPass(string userKey, int seat, IReadOnlyList<string>? cards, [NotNullWhen(false)] out string? error);
}
