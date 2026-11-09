using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.FinalCard;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A Final Card game for 2 to 10 seats, who play in seat order (until a Change Direction). In a game to a score target, the
/// next hand starts with <see cref="IRoundBasedSession.TryNextRound"/> (the round is the hand number). Cards are short text
/// such as "R7", "GS", "B+2", "W" or "W+4", and in Israeli Style "R+", "RT", "WT", "WC", "W+3" or "W+3X" (see
/// <see cref="FinalCardCard"/>).
/// </summary>
public interface IFinalCardSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, plays <paramref name="card"/>, choosing
    /// <paramref name="color"/> for a Change Color, a Change Color +4, or a SuperTAKI after a Crown. In Israeli Style, a +3
    /// Breaker can answer a +3 out of turn.
    /// </summary>
    bool TryPlay(string userKey, int seat, string? card, FinalCardColor? color, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> draws: its penalty cards (at any time during a hand), or else on its turn the cards stacked
    /// for it, or one card (or, with the option, until it can play).
    /// </summary>
    bool TryDraw(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// Israeli Style: <paramref name="seat"/>, on its turn, closes its TAKI.
    /// </summary>
    bool TryCloseTaki(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> keeps the card it drew, which ends its turn.
    /// </summary>
    bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> challenges the Change Color +4 played on it (with the challenge option).
    /// </summary>
    bool TryChallenge(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/> calls Final Card: it protects the seat on its final card (or before playing its second to
    /// last), and every other seat holding one card unprotected has 2 penalty cards to draw (4 in Israeli Style).
    /// </summary>
    bool TryCallFinalCard(string userKey, int seat, [NotNullWhen(false)] out string? error);
}
