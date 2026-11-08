using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Rich man, poor man for 4 to 12 seats, game after game; it never ends by itself. The next game starts with
/// <see cref="IRoundBasedSession.TryNextRound"/> (the round is the game number). Cards are short text such as "10♥".
/// An absent player keeps their cards and their turn, which the other players can skip; a newcomer can take their seat
/// and continue in their place. A game that ends with only absent players holding cards drops them; at the next game's
/// exchange, seats left empty are dropped. During the exchange, a user can ask to join, and any player can accept them
/// at a place of their choice.
/// </summary>
public interface IRichManPoorManSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, throws <paramref name="cards"/>, all of one number.
    /// </summary>
    bool TryThrow(string userKey, int seat, IReadOnlyList<string>? cards, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> passes.
    /// </summary>
    bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> gives <paramref name="cards"/> to its exchange partner, between games.
    /// </summary>
    bool TryGive(string userKey, int seat, IReadOnlyList<string>? cards, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// During the exchange, <paramref name="userKey"/> asks to join the next game, as <paramref name="profile"/>.
    /// </summary>
    bool TryRequestSeat(string userKey, PlayerProfile profile, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="userKey"/> withdraws their request to join.
    /// </summary>
    bool TryCancelSeatRequest(string userKey, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// A player (<paramref name="userKey"/>) accepts the request of <paramref name="playerId"/>, who joins at
    /// <paramref name="place"/> (1 for #1). The cards are dealt again for the new number of players.
    /// </summary>
    bool TryAcceptSeat(string userKey, string playerId, int place, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// A player (<paramref name="userKey"/>) declines the request of <paramref name="playerId"/>.
    /// </summary>
    bool TryDeclineSeat(string userKey, string playerId, [NotNullWhen(false)] out string? error);
}
