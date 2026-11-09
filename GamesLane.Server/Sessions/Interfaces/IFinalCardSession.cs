using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.FinalCard;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A Final Card game for 2 to 10 seats, who play in seat order (until a Reverse). In a game to a score target, the next hand
/// starts with <see cref="IRoundBasedSession.TryNextRound"/> (the round is the hand number). Cards are short text such as
/// "R7", "GS", "B+2", "W" or "W+4" (see <see cref="FinalCardCard"/>).
/// </summary>
public interface IFinalCardSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, plays <paramref name="card"/>, choosing
    /// <paramref name="color"/> for a wild card.
    /// </summary>
    bool TryPlay(string userKey, int seat, string? card, FinalCardColor? color, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> draws: the cards stacked for it, or else one card (or, with the option, until it can play).
    /// </summary>
    bool TryDraw(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> keeps the card it drew, which ends its turn.
    /// </summary>
    bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> challenges the Wild Draw Four played on it (with the challenge option).
    /// </summary>
    bool TryChallenge(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> calls Final Card: it protects the seat on its final card (or before playing its second to
    /// last), and every other seat holding one card unprotected draws 2 cards.
    /// </summary>
    bool TryCallFinalCard(string userKey, int seat, [NotNullWhen(false)] out string? error);
}
