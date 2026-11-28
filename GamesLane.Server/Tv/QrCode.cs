using System.Text;

namespace GamesLane.Server.Tv;

/// <summary>
/// A QR code (ISO/IEC 18004) of a short text, for the web TV app's pairing screen (<c>GET /api/tv/qr</c>): byte mode
/// (UTF-8), error correction level M, the smallest version from 1 to <see cref="MaxVersion"/> that holds the text, and
/// the mask with the lowest penalty.
/// </summary>
public sealed class QrCode
{
    /// <summary>
    /// The largest version made (57 × 57 modules), which holds up to 213 bytes.
    /// </summary>
    public const int MaxVersion = 10;

    // Per version, for level M: the error correction codewords of each block, and the number of blocks.
    private static readonly int[] EccPerBlock = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];

    // Indexed [y, x]: whether each module is dark, and whether it belongs to a function pattern (not data).
    private readonly bool[,] _modules;
    private readonly bool[,] _function;

    private QrCode(int version, byte[] data)
    {
        Version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _function = new bool[Size, Size];
        DrawFunctionPatterns();
        DrawCodewords(AddEccAndInterleave(data));

        var best = 0;
        var lowest = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            var penalty = Penalty();
            if (penalty < lowest)
            {
                lowest = penalty;
                best = mask;
            }

            ApplyMask(mask);
        }

        Mask = best;
        ApplyMask(best);
        DrawFormatBits(best);
    }

    /// <summary>The version, from 1 to <see cref="MaxVersion"/>.</summary>
    public int Version { get; }

    /// <summary>The number of modules on each side.</summary>
    public int Size { get; }

    /// <summary>The mask applied, from 0 to 7.</summary>
    public int Mask { get; }

    /// <summary>Whether the module in column <paramref name="x"/> and row <paramref name="y"/> is dark.</summary>
    public bool this[int x, int y] => _modules[y, x];

    /// <summary>
    /// The QR code of <paramref name="text"/>, or null if it is too long.
    /// </summary>
    public static QrCode? Encode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var version = 1; version <= MaxVersion; version++)
        {
            var capacity = DataCodewords(version) * 8;
            var countBits = version < 10 ? 8 : 16;
            if (4 + countBits + bytes.Length * 8 <= capacity)
            {
                return new QrCode(version, DataBytes(bytes, countBits, capacity));
            }
        }

        return null;
    }

    /// <summary>
    /// The code as an SVG picture, one unit per module, with the light margin of 4 modules around it.
    /// </summary>
    public string ToSvg()
    {
        const int border = 4;
        var path = new StringBuilder();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (_modules[y, x])
                {
                    path.Append($"M{x + border},{y + border}h1v1h-1z");
                }
            }
        }

        var side = Size + border * 2;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {side} {side}\" shape-rendering=\"crispEdges\">"
            + $"<rect width=\"{side}\" height=\"{side}\" fill=\"#fff\"/><path d=\"{path}\" fill=\"#000\"/></svg>";
    }

    /// <summary>
    /// The 15 format bits for level M and <paramref name="mask"/>, with their BCH error correction, masked.
    /// </summary>
    public static int FormatBits(int mask)
    {
        var rem = mask;
        for (var i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }

        return ((mask << 10) | rem) ^ 0x5412;
    }

    /// <summary>
    /// The Reed-Solomon generator polynomial of <paramref name="degree"/>, highest coefficient first, without the leading 1.
    /// </summary>
    public static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree)
                {
                    result[j] ^= result[j + 1];
                }
            }

            root = Multiply(root, 0x02);
        }

        return result;
    }

    /// <summary>
    /// The error correction codewords of <paramref name="data"/> for <paramref name="divisor"/>.
    /// </summary>
    public static byte[] ReedSolomonRemainder(IReadOnlyList<byte> data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var value in data)
        {
            var factor = value ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] ^= Multiply(divisor[i], factor);
            }
        }

        return result;
    }

    private static byte Multiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }

    private static int RawDataModules(int version)
    {
        var result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            var alignments = version / 7 + 2;
            result -= (25 * alignments - 10) * alignments - 55;
            if (version >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    private static int DataCodewords(int version) => RawDataModules(version) / 8 - EccPerBlock[version] * Blocks[version];

    // The mode (byte), the length, the text, the terminator, and the pad bytes up to the version's capacity.
    private static byte[] DataBytes(byte[] text, int countBits, int capacity)
    {
        var bits = new List<bool>();
        void Append(int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) != 0);
            }
        }

        Append(0b0100, 4);
        Append(text.Length, countBits);
        foreach (var value in text)
        {
            Append(value, 8);
        }

        Append(0, Math.Min(4, capacity - bits.Count));
        Append(0, (8 - bits.Count % 8) % 8);
        var result = new List<byte>();
        for (var i = 0; i < bits.Count; i += 8)
        {
            result.Add((byte)Enumerable.Range(0, 8).Aggregate(0, (value, j) => (value << 1) | (bits[i + j] ? 1 : 0)));
        }

        for (var pad = 0xEC; result.Count < capacity / 8; pad ^= 0xEC ^ 0x11)
        {
            result.Add((byte)pad);
        }

        return [.. result];
    }

    private byte[] AddEccAndInterleave(byte[] data)
    {
        var blockCount = Blocks[Version];
        var eccLength = EccPerBlock[Version];
        var raw = RawDataModules(Version) / 8;
        var shortBlocks = blockCount - raw % blockCount;
        var shortLength = raw / blockCount;
        var divisor = ReedSolomonDivisor(eccLength);

        // Each block is its data, then its error correction; a short block has a gap where long ones have one more byte.
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < blockCount; i++)
        {
            var length = shortLength - eccLength + (i < shortBlocks ? 0 : 1);
            var block = new byte[shortLength + 1];
            Array.Copy(data, k, block, 0, length);
            ReedSolomonRemainder(data[k..(k + length)], divisor).CopyTo(block, shortLength + 1 - eccLength);
            k += length;
            blocks.Add(block);
        }

        var result = new List<byte>();
        for (var i = 0; i <= shortLength; i++)
        {
            for (var j = 0; j < blockCount; j++)
            {
                if (i != shortLength - eccLength || j >= shortBlocks)
                {
                    result.Add(blocks[j][i]);
                }
            }
        }

        return [.. result];
    }

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _function[y, x] = true;
    }

    private void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var positions = AlignmentPositions();
        var last = positions.Length - 1;
        for (var i = 0; i <= last; i++)
        {
            for (var j = 0; j <= last; j++)
            {
                if (!(i == 0 && j == 0) && !(i == 0 && j == last) && !(i == last && j == 0))
                {
                    DrawAlignment(positions[i], positions[j]);
                }
            }
        }

        DrawFormatBits(0);
        DrawVersion();
    }

    private void DrawFinder(int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (x + dx >= 0 && x + dx < Size && y + dy >= 0 && y + dy < Size)
                {
                    SetFunction(x + dx, y + dy, distance != 2 && distance != 4);
                }
            }
        }
    }

    private void DrawAlignment(int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
    }

    private int[] AlignmentPositions()
    {
        if (Version == 1)
        {
            return [];
        }

        var count = Version / 7 + 2;
        var step = (Version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, position = Size - 7; i >= 1; i--, position -= step)
        {
            result[i] = position;
        }

        return result;
    }

    private void DrawFormatBits(int mask)
    {
        var bits = FormatBits(mask);
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (var i = 0; i <= 5; i++)
        {
            SetFunction(8, i, Bit(i));
        }

        SetFunction(8, 7, Bit(6));
        SetFunction(8, 8, Bit(7));
        SetFunction(7, 8, Bit(8));
        for (var i = 9; i < 15; i++)
        {
            SetFunction(14 - i, 8, Bit(i));
        }

        for (var i = 0; i < 8; i++)
        {
            SetFunction(Size - 1 - i, 8, Bit(i));
        }

        for (var i = 8; i < 15; i++)
        {
            SetFunction(8, Size - 15 + i, Bit(i));
        }

        SetFunction(8, Size - 8, true);
    }

    private void DrawVersion()
    {
        if (Version < 7)
        {
            return;
        }

        var rem = Version;
        for (var i = 0; i < 12; i++)
        {
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        }

        var bits = (Version << 12) | rem;
        for (var i = 0; i < 18; i++)
        {
            var dark = ((bits >> i) & 1) != 0;
            var a = Size - 11 + i % 3;
            var b = i / 3;
            SetFunction(a, b, dark);
            SetFunction(b, a, dark);
        }
    }

    // The codewords' bits, two columns at a time from the right, upwards then downwards, skipping the function patterns.
    private void DrawCodewords(byte[] data)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (var vertical = 0; vertical < Size; vertical++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vertical : vertical;
                    if (!_function[y, x] && i < data.Length * 8)
                    {
                        _modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
            }
        }
    }

    // Inverts the data modules where the mask's pattern is true; applying it twice undoes it.
    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (!_function[y, x] && invert)
                {
                    _modules[y, x] = !_modules[y, x];
                }
            }
        }
    }

    // The penalty of the standard's four rules: runs of 5 or more, 2 × 2 blocks, finder-like patterns, and the balance
    // of dark and light.
    private int Penalty()
    {
        var result = 0;
        for (var line = 0; line < Size; line++)
        {
            var row = line;
            result += LinePenalty(i => _modules[row, i]) + LinePenalty(i => _modules[i, row]);
        }

        var dark = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                dark += _modules[y, x] ? 1 : 0;
                if (x + 1 < Size && y + 1 < Size && _modules[y, x] == _modules[y, x + 1]
                    && _modules[y, x] == _modules[y + 1, x] && _modules[y, x] == _modules[y + 1, x + 1])
                {
                    result += 3;
                }
            }
        }

        var total = Size * Size;
        return result + ((Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1) * 10;
    }

    private int LinePenalty(Func<int, bool> at)
    {
        var result = 0;
        var run = 1;
        for (var i = 1; i <= Size; i++)
        {
            if (i < Size && at(i) == at(i - 1))
            {
                run++;
                continue;
            }

            if (run >= 5)
            {
                result += run - 2;
            }

            run = 1;
        }

        bool Dark(int i) => i >= 0 && i < Size && at(i);
        bool Light(int from) => Enumerable.Range(from, 4).All(i => !Dark(i));
        for (var i = 0; i + 7 <= Size; i++)
        {
            if (Dark(i) && !Dark(i + 1) && Dark(i + 2) && Dark(i + 3) && Dark(i + 4) && !Dark(i + 5) && Dark(i + 6)
                && (Light(i - 4) || Light(i + 7)))
            {
                result += 40;
            }
        }

        return result;
    }
}
