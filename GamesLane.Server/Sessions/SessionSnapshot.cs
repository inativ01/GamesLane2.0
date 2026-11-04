using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Marks a field that is not saved with a session (e.g. its open connections); it keeps the value the session was
/// created with when the session is loaded again. Fields holding delegates or a <see cref="Random"/> are never saved;
/// a session sets them again in <see cref="GameSession.OnRestored"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class NotSavedAttribute : Attribute;

/// <summary>
/// Saves a session as JSON: every field of the session and of the objects it holds (its seats, its game and the game's
/// cards, checkers or chips), public or private. A session is loaded again by creating it as usual and filling in the
/// saved fields. <see cref="Version"/> is a fingerprint of the fields of the session's type and of every type it holds,
/// so a save made by code with different fields is recognised and not loaded.
/// </summary>
public static class SessionSnapshot
{
    private static readonly Assembly Own = typeof(SessionSnapshot).Assembly;
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>
    /// The session's saved fields, as JSON. Call while holding the session's lock.
    /// </summary>
    public static string Save(GameSession session)
    {
        var json = new JsonObject();
        foreach (var field in SavedFields(session.GetType()))
        {
            json[Key(field)] = JsonSerializer.SerializeToNode(field.GetValue(session), field.FieldType, Options);
        }

        return json.ToJsonString(Options);
    }

    /// <summary>
    /// Fills in <paramref name="session"/>'s saved fields from <paramref name="data"/> (made by <see cref="Save"/>).
    /// </summary>
    public static void Restore(GameSession session, string data)
    {
        var json = JsonNode.Parse(data)!.AsObject();
        foreach (var field in SavedFields(session.GetType()))
        {
            if (json.TryGetPropertyValue(Key(field), out var value))
            {
                field.SetValue(session, value.Deserialize(field.FieldType, Options));
            }
        }
    }

    /// <summary>
    /// A fingerprint of the saved fields of <paramref name="sessionType"/> and of every type they hold.
    /// </summary>
    public static string Version(Type sessionType)
    {
        var text = new StringBuilder();
        var seen = new HashSet<Type>();
        Describe(sessionType, text, seen, topLevel: true);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static void Describe(Type type, StringBuilder text, HashSet<Type> seen, bool topLevel = false)
    {
        if (!seen.Add(type))
        {
            return;
        }

        text.Append(type.FullName).Append('{');
        var fields = topLevel || IsOwnObject(type) ? SavedFields(type).ToArray() : [];
        foreach (var field in fields)
        {
            text.Append(Key(field)).Append(':').Append(field.FieldType.FullName ?? field.FieldType.Name).Append(';');
        }

        if (type.IsEnum)
        {
            text.Append(string.Join(',', Enum.GetNames(type)));
        }

        text.Append('}');
        var inner = fields.Select(f => f.FieldType)
            .Concat(type.IsGenericType ? type.GetGenericArguments() : [])
            .Concat(type.HasElementType ? [type.GetElementType()!] : []);
        foreach (var t in inner)
        {
            Describe(t, text, seen);
        }
    }

    /// <summary>
    /// The saved fields of a type and its base types, in a fixed order.
    /// </summary>
    private static IEnumerable<FieldInfo> SavedFields(Type type)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (field.GetCustomAttribute<NotSavedAttribute>() is null && !typeof(Delegate).IsAssignableFrom(field.FieldType)
                    && field.FieldType != typeof(Random) && field.FieldType != typeof(object))
                {
                    yield return field;
                }
            }
        }
    }

    private static string Key(FieldInfo field) => $"{field.DeclaringType!.Name}.{field.Name}";

    /// <summary>
    /// A class or struct of this application that is saved field by field (cards are saved as text such as "10♥").
    /// </summary>
    private static bool IsOwnObject(Type type) =>
        type.Assembly == Own && !type.IsEnum && type != typeof(Card) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object || !IsOwnObject(info.Type))
            {
                return;
            }

            info.Properties.Clear();
            info.CreateObject = () => RuntimeHelpers.GetUninitializedObject(info.Type);
            foreach (var field in SavedFields(info.Type))
            {
                var property = info.CreateJsonPropertyInfo(field.FieldType, Key(field));
                property.Get = field.GetValue;
                property.Set = field.SetValue;
                info.Properties.Add(property);
            }
        });

        return new JsonSerializerOptions
        {
            IncludeFields = true,
            TypeInfoResolver = resolver,
            Converters = { new JsonStringEnumConverter(), new CardConverter(), new SnapshotConverters.TwoDimensional(), new SnapshotConverters.ReadOnlySets() },
        };
    }

    /// <summary>
    /// Saves a card as its short text, e.g. "10♥".
    /// </summary>
    private sealed class CardConverter : JsonConverter<Card>
    {
        public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Card.Parse(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
