using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A Yaniv game for 2 to 4 seats, who play in seat order. In a game to a score target, the next round starts with
/// <see cref="IRoundBasedSession.TryNextRound"/>. Cards are short text such as "10♥", "A♠" or "JOKER".
/// </summary>
public interface IYanivSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, discards <paramref name="cards"/> and takes the
    /// open pile's top card (<paramref name="takeOpen"/>) or a card from the closed pile.
    /// </summary>
    bool TryDiscard(string userKey, int seat, IReadOnlyList<string>? cards, bool takeOpen, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> calls Yaniv, on its turn with a hand score of 7 or less.
    /// </summary>
    bool TryCallYaniv(string userKey, int seat, [NotNullWhen(false)] out string? error);
}
