using System.Text.Json;
using System.Text.Json.Serialization;

namespace GamesLane.Server.Sessions;

/// <summary>
/// JSON converters for the field types <see cref="SessionSnapshot"/> saves that JSON does not handle by itself.
/// </summary>
internal static class SnapshotConverters
{
    /// <summary>
    /// Two-dimensional arrays (e.g. The Gang's chips by stage and player), saved as a list of rows.
    /// </summary>
    internal sealed class TwoDimensional : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsArray && typeToConvert.GetArrayRank() == 2;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(TwoDimensionalConverter<>).MakeGenericType(typeToConvert.GetElementType()!))!;
    }

    private sealed class TwoDimensionalConverter<T> : JsonConverter<T[,]>
    {
        public override T[,] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var rows = JsonSerializer.Deserialize<T[][]>(ref reader, options) ?? [];
            var columns = rows.Length == 0 ? 0 : rows[0].Length;
            var result = new T[rows.Length, columns];
            for (var i = 0; i < rows.Length; i++)
            {
                for (var j = 0; j < columns; j++)
                {
                    result[i, j] = rows[i][j];
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, T[,] value, JsonSerializerOptions options)
        {
            var rows = Enumerable.Range(0, value.GetLength(0))
                .Select(i => Enumerable.Range(0, value.GetLength(1)).Select(j => value[i, j]).ToArray())
                .ToArray();
            JsonSerializer.Serialize(writer, rows, options);
        }
    }

    /// <summary>
    /// Read-only sets, saved as a list and loaded as a <see cref="HashSet{T}"/>.
    /// </summary>
    internal sealed class ReadOnlySets : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlySet<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ReadOnlySetConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }

    private sealed class ReadOnlySetConverter<T> : JsonConverter<IReadOnlySet<T>>
    {
        public override IReadOnlySet<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new HashSet<T>(JsonSerializer.Deserialize<T[]>(ref reader, options) ?? []);

        public override void Write(Utf8JsonWriter writer, IReadOnlySet<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}
