using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A Spades game for 4 seats in 2 teams (seats 1 and 3, 2 and 4 in the page's numbering), who play in seat order.
/// </summary>
public interface ISpadesSession : ITrickTakingSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, looks at its cards while bidding (with Blind
    /// Nil, its cards are hidden until then).
    /// </summary>
    bool TryLook(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary>
    /// <paramref name="seat"/>, whose turn it is to bid, bids <paramref name="tricks"/> (0 for Nil), blind or not.
    /// </summary>
    bool TryBid(string userKey, int seat, int tricks, bool blind, [NotNullWhen(false)] out string? error);
}
