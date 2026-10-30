using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Games;

/// <summary>
/// One of the values a game option can take, e.g. "Advanced". <see cref="Hint"/> is an optional short explanation.
/// </summary>
public record GameOptionChoice(string Value, string Label, string? Hint = null);

/// <summary>
/// A rule variant players choose before a game starts: one of <see cref="Choices"/>, <see cref="Default"/> unless chosen.
/// </summary>
public record GameOption(string Key, string Label, IReadOnlyList<GameOptionChoice> Choices, string Default);

/// <summary>
/// The options each game offers, and checking chosen values against them. Chosen values are a map from option key to
/// value, e.g. <c>{ "mode": "Advanced" }</c>.
/// </summary>
public static class GameOptions
{
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
        For(type).ToDictionary(
            option => option.Key,
            option => values?.GetValueOrDefault(option.Key) is { } value && IsValid(option, value) ? value : option.Default);

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

    private static bool IsValid(GameOption option, string value) => option.Choices.Any(c => c.Value == value);
}
