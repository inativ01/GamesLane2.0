namespace GamesLane.Server.Games.Dominoes;

/// <summary>
/// A domino tile: its two ends, <see cref="Low"/> no higher than <see cref="High"/>.
/// </summary>
public record Tile(int Low, int High)
{
    /// <summary>Whether both ends are the same (a double).</summary>
    public bool IsDouble => Low == High;

    /// <summary>The pips on the tile: the sum of its ends.</summary>
    public int Pips => Low + High;

    /// <summary>Whether one of its ends is <paramref name="value"/>.</summary>
    public bool Has(int value) => Low == value || High == value;

    /// <summary>The other end than <paramref name="value"/> (for a double, the same).</summary>
    public int Other(int value) => Low == value ? High : Low;

    /// <summary>Short text such as <c>"3-5"</c>, low end first; also sent to the pages.</summary>
    public override string ToString() => $"{Low}-{High}";

    /// <summary>A tile with the ends in any order.</summary>
    public static Tile Of(int a, int b) => new(Math.Min(a, b), Math.Max(a, b));

    /// <summary>Parses short text such as <c>"3-5"</c> or <c>"5-3"</c>; throws <see cref="FormatException"/> otherwise.</summary>
    public static Tile Parse(string text)
    {
        var parts = text.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b) && a >= 0 && b >= 0
            ? Of(a, b)
            : throw new FormatException($"Unknown tile '{text}'.");
    }
}

/// <summary>
/// A tile placed in a line or a train, in the direction it was laid: <see cref="Left"/> is the end that touches the tile
/// before it, <see cref="Right"/> the end that is open (or touches the next one).
/// </summary>
public record LaidTile(int Left, int Right)
{
    /// <summary>Whether both ends are the same (a double).</summary>
    public bool IsDouble => Left == Right;

    /// <summary>Short text such as <c>"3-5"</c>, in the direction it was laid.</summary>
    public override string ToString() => $"{Left}-{Right}";
}

/// <summary>
/// The domino sets.
/// </summary>
public static class DominoesTiles
{
    /// <summary>
    /// Every tile of the set up to double <paramref name="highest"/>: 28 tiles for double-six, 91 for double-twelve.
    /// </summary>
    public static Tile[] Set(int highest) =>
        [.. Enumerable.Range(0, highest + 1).SelectMany(low => Enumerable.Range(low, highest - low + 1).Select(high => new Tile(low, high)))];

    /// <summary>The pips of tiles, e.g. a hand's.</summary>
    public static int Pips(IEnumerable<Tile> tiles) => tiles.Sum(t => t.Pips);

    /// <summary>The tiles in hand order: from the lowest low end, then the lowest high end.</summary>
    public static IEnumerable<Tile> Sorted(IEnumerable<Tile> tiles) => tiles.OrderBy(t => t.Low).ThenBy(t => t.High);
}
