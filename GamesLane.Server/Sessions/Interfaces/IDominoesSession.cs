using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A dominoes game for 2 to 4 seats (2 to 8 in Mexican Train), who play in seat order. The next round starts with
/// <see cref="IRoundBasedSession.TryNextRound"/>. Tiles are short text such as "3-5".
/// </summary>
public interface IDominoesSession : IRoundBasedSession
{
    /// <summary>
    /// <paramref name="seat"/>, which <paramref name="userKey"/> must hold, plays <paramref name="tile"/> on its turn: in
    /// the line styles at the left end (<paramref name="target"/> 0) or the right end (1) of the line; in Mexican Train on
    /// the train of seat <paramref name="target"/>, or on the Mexican train (-1).
    /// </summary>
    bool TryPlayTile(string userKey, int seat, string? tile, int target, [NotNullWhen(false)] out string? error);

    /// <summary><paramref name="seat"/>, which cannot play, draws a tile from the boneyard.</summary>
    bool TryDraw(string userKey, int seat, [NotNullWhen(false)] out string? error);

    /// <summary><paramref name="seat"/>, which cannot play or draw, passes.</summary>
    bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error);
}
