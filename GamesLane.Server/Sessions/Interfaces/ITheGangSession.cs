using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A session of The Gang for 3 to 6 seats. Each player's cards are private and sent only to them.
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
    /// Marks <paramref name="seat"/>, which <paramref name="userKey"/> must hold, ready (or not) to end the stage,
    /// together with the user's other seats.
    /// </summary>
    bool TrySetReady(string userKey, int seat, bool ready, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Coordinator: <paramref name="seat"/>, which <paramref name="userKey"/> must hold, picks the card to pass on.
    /// </summary>
    bool TryPassCard(string userKey, int seat, int cardIndex, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Con Artist: <paramref name="seat"/>, which <paramref name="userKey"/> must hold, has seen its cards (and so have
    /// the user's other seats).
    /// </summary>
    bool TryConfirmSeen(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, proposes a choice for a card's group decision:
    /// a seat (<paramref name="targetSeat"/>) and/or a value such as "9" or "Pair".
    /// </summary>
    bool TryPropose(string userKey, int seat, string card, int? targetSeat, string? value, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, agrees (or no longer agrees) to the proposal,
    /// for all the user's seats that vote.
    /// </summary>
    bool TryAgree(string userKey, int seat, string card, bool agree, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Informant: <paramref name="seat"/> secretly shows its card number <paramref name="cardIndex"/> to <paramref name="toSeat"/>.
    /// </summary>
    bool TryShowCard(string userKey, int seat, int cardIndex, int toSeat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Hacker, Jack: <paramref name="seat"/> discards its card number <paramref name="cardIndex"/>.
    /// </summary>
    bool TryDiscard(string userKey, int seat, int cardIndex, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Reveals the next hand, if <paramref name="revealed"/> hands have been revealed so far. Players only.
    /// </summary>
    bool TryRevealNext(string userKey, int revealed, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Deals the heist after <paramref name="heist"/>, once its hands are revealed. Players only.
    /// </summary>
    bool TryNextHeist(string userKey, int heist, [NotNullWhen(false)] out string? error);
}
