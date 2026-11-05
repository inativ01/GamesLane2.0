using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Uno;

namespace GamesLane.Server.Sessions;

/// <summary>
/// An Uno game for 2 to 10 seats, who play in seat order (until a Reverse). In a game to a score target, the next hand
/// starts with <see cref="IRoundBasedSession.TryNextRound"/> (the round is the hand number). Cards are short text such as
/// "R7", "GS", "B+2", "W" or "W+4" (see <see cref="UnoCard"/>).
/// </summary>
public interface IUnoSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, plays <paramref name="card"/>, choosing
    /// <paramref name="color"/> for a wild card.
    /// </summary>
    bool TryPlay(string userKey, int seat, string? card, UnoColor? color, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> draws: the cards stacked for it, or else one card (or, with the option, until it can play).
    /// </summary>
    bool TryDraw(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> keeps the card it drew, which ends its turn.
    /// </summary>
    bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> calls UNO, before or right after going down to one card.
    /// </summary>
    bool TryCallUno(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> catches <paramref name="targetSeat"/>, who went down to one card without calling UNO;
    /// <paramref name="targetSeat"/> draws 2 cards.
    /// </summary>
    bool TryCatchUno(string userKey, int seat, int targetSeat, [NotNullWhen(false)] out string? error);
}
