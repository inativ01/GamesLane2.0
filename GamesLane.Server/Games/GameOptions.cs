using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Games;

/// <summary>
/// One of the values a game option can take, e.g. "Advanced". <see cref="Hint"/> is an optional short explanation.
/// </summary>
public record GameOptionChoice(string Value, string Label, string? Hint = null);

/// <summary>
/// How a game option is chosen.
/// </summary>
public enum GameOptionKind
{
    /// <summary>Exactly one of the choices.</summary>
    Choice,

    /// <summary>
    /// Any of the choices, all on unless turned off. The value lists the choices that are turned off, separated by
    /// commas (empty when all are on), so choices added later are on by default.
    /// </summary>
    Set,
}

/// <summary>
/// An option that only matters when option <see cref="Key"/> has the value <see cref="Value"/>; pages show it only then.
/// </summary>
public record GameOptionCondition(string Key, string Value);

/// <summary>
/// A rule variant players choose before a game starts. A <see cref="GameOptionKind.Choice"/> option is one of
/// <see cref="Choices"/>; a <see cref="GameOptionKind.Set"/> option lists the choices turned off. <see cref="Default"/>
/// applies unless chosen.
/// </summary>
public record GameOption(
    string Key,
    string Label,
    IReadOnlyList<GameOptionChoice> Choices,
    string Default,
    GameOptionKind Kind = GameOptionKind.Choice,
    GameOptionCondition? ShownWhen = null);

/// <summary>
/// The options each game offers, and checking chosen values against them. Chosen values are a map from option key to
/// value, e.g. <c>{ "mode": "Advanced", "challenges": "blackout" }</c>.
/// </summary>
public static class GameOptions
{
    /// <summary>
    /// The choices turned off in a <see cref="GameOptionKind.Set"/> option's value.
    /// </summary>
    public static IReadOnlyList<string> TurnedOff(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The choices of a <see cref="GameOptionKind.Set"/> option that are on, given the chosen values.
    /// </summary>
    public static IReadOnlyList<string> TurnedOn(GameOption option, IReadOnlyDictionary<string, string> values)
    {
        var off = TurnedOff(values.GetValueOrDefault(option.Key));
        return option.Choices.Select(c => c.Value).Where(v => !off.Contains(v)).ToArray();
    }

    /// <summary>
    /// The options <paramref name="type"/> offers, in the order they are shown. Empty for games without options.
    /// </summary>
    public static IReadOnlyList<GameOption> For(GameType type) => type switch
    {
        GameType.TheGang => TheGangOptions.All,
        _ => [],
    };

    /// <summary>
    /// Every option of <paramref name="type"/> with its default value.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Defaults(GameType type) => Normalize(type, null);

    /// <summary>
    /// Every option of <paramref name="type"/>, with its value from <paramref name="values"/> if that value is valid,
    /// otherwise its default. Unknown keys are dropped. Used for saved preferences, which may predate option changes.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Normalize(GameType type, IReadOnlyDictionary<string, string>? values) =>
        For(type).ToDictionary(option => option.Key, option => NormalizeValue(option, values?.GetValueOrDefault(option.Key)));

    /// <summary>
    /// Checks values chosen by a player: every key must be an option of <paramref name="type"/> and every value one of
    /// its choices. Options left out get their default. On success, <paramref name="normalized"/> holds every option.
    /// </summary>
    public static bool TryValidate(
        GameType type,
        IReadOnlyDictionary<string, string>? values,
        [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? normalized,
        [NotNullWhen(false)] out string? error)
    {
        var options = For(type);
        foreach (var (key, value) in values ?? new Dictionary<string, string>())
        {
            if (options.FirstOrDefault(o => o.Key == key) is not { } option)
            {
                (normalized, error) = (null, $"This game has no option '{key}'.");
                return false;
            }

            if (!IsValid(option, value))
            {
                (normalized, error) = (null, $"'{value}' is not a choice for {option.Label}.");
                return false;
            }
        }

        (normalized, error) = (Normalize(type, values), null);
        return true;
    }

    /// <summary>
    /// A choice option's value if it is one of its choices; a set option's value if it lists only its choices, once each.
    /// </summary>
    private static bool IsValid(GameOption option, string value)
    {
        if (option.Kind == GameOptionKind.Choice)
        {
            return option.Choices.Any(c => c.Value == value);
        }

        var off = TurnedOff(value);
        return off.Distinct().Count() == off.Count && off.All(v => option.Choices.Any(c => c.Value == v));
    }

    /// <summary>
    /// The value to keep: a valid value as is (set values in the order of the choices), otherwise the default. For a set
    /// option, choices that no longer exist are dropped and the rest are kept.
    /// </summary>
    private static string NormalizeValue(GameOption option, string? value)
    {
        if (value is null)
        {
            return option.Default;
        }

        if (option.Kind == GameOptionKind.Choice)
        {
            return IsValid(option, value) ? value : option.Default;
        }

        var off = TurnedOff(value);
        return string.Join(',', option.Choices.Select(c => c.Value).Where(off.Contains));
    }
}
