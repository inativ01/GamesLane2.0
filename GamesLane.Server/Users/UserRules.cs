using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using GamesLane.Server.Data;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Users;

/// <summary>
/// The rules for display names, usernames, passwords and icons.
/// </summary>
public static partial class UserRules
{
    /// <summary>
    /// The longest display name allowed.
    /// </summary>
    public const int MaxNameLength = 30;

    /// <summary>
    /// The shortest password allowed.
    /// </summary>
    public const int MinPasswordLength = 8;

    /// <summary>
    /// The largest icon file allowed. Pages shrink pictures to 128×128 before uploading, which is far smaller.
    /// </summary>
    public const int MaxIconBytes = 200 * 1024;

    /// <summary>
    /// The address prefix of uploaded icons.
    /// </summary>
    public const string IconPathPrefix = "/api/icons/";

    private const int MaxPasswordLength = 100;

    /// <summary>
    /// The built-in icons users can choose from. The first is the default.
    /// </summary>
    public static readonly IReadOnlyList<string> BuiltInIcons =
    [
        "🙂", "😎", "🤠", "🥳", "🤖", "👽", "👻", "🎃",
        "🦊", "🐼", "🐯", "🦁", "🐸", "🐙", "🦄", "🐧",
        "🐢", "🦉", "🐝", "🦋", "🚀", "⚽", "🎸", "🌈",
    ];

    /// <summary>
    /// The default icon.
    /// </summary>
    public static string DefaultIcon => BuiltInIcons[0];

    /// <summary>
    /// A name for a new guest: <c>Guest-</c> and a random 3-digit number, e.g. <c>Guest-042</c>.
    /// </summary>
    public static string RandomGuestName() => $"Guest-{Random.Shared.Next(1000):D3}";

    /// <summary>
    /// A random one of the <see cref="BuiltInIcons"/>, for a new guest.
    /// </summary>
    public static string RandomIcon() => BuiltInIcons[Random.Shared.Next(BuiltInIcons.Count)];

    /// <summary>
    /// Returns the trimmed display name, or null if it is empty or longer than <see cref="MaxNameLength"/> characters.
    /// </summary>
    public static string? NormalizeName(string? name) =>
        name?.Trim() is { Length: > 0 and <= MaxNameLength } trimmed ? trimmed : null;

    /// <summary>
    /// Shortens a name from an external account to fit, or returns null if nothing usable is left.
    /// </summary>
    public static string? FitName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return NormalizeName(trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed);
    }

    /// <summary>
    /// Whether <paramref name="username"/> is 3 to 20 letters, digits or underscores.
    /// </summary>
    public static bool IsValidUsername([NotNullWhen(true)] string? username) =>
        username is not null && UsernamePattern().IsMatch(username);

    /// <summary>
    /// The form used to compare usernames, so they are unique regardless of case.
    /// </summary>
    public static string NormalizeUsername(string username) => username.ToUpperInvariant();

    /// <summary>
    /// Whether <paramref name="password"/> is long enough (and not unreasonably long).
    /// </summary>
    public static bool IsValidPassword([NotNullWhen(true)] string? password) =>
        password is { Length: >= MinPasswordLength and <= MaxPasswordLength };

    /// <summary>
    /// Whether <paramref name="icon"/> is one of the <see cref="BuiltInIcons"/>.
    /// </summary>
    public static bool IsBuiltInIcon([NotNullWhen(true)] string? icon) => icon is not null && BuiltInIcons.Contains(icon);

    /// <summary>
    /// The address of an uploaded icon.
    /// </summary>
    public static string IconPath(Guid iconId) => $"{IconPathPrefix}{iconId}";

    /// <summary>
    /// The image type of an icon file, judged by its first bytes (PNG, JPEG or WebP), or null for anything else.
    /// </summary>
    public static string? DetectImageType(ReadOnlySpan<byte> data) => data switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="style"/> is one of the card styles (see <see cref="UserSettings.CardStyles"/>).
    /// </summary>
    public static bool IsCardStyle([NotNullWhen(true)] string? style) => style is not null && UserSettings.CardStyles.Contains(style);

    /// <summary>
    /// Checks the order of games chosen on the main screen: game names (e.g. <c>TheGang</c>), each an existing game,
    /// listed once. Games left out keep their usual place after the listed ones. On success, <paramref name="stored"/>
    /// is the order as saved: the names separated by commas.
    /// </summary>
    public static bool TryNormalizeGameOrder(IReadOnlyList<string>? games, out string stored, out string? error)
    {
        stored = "";
        var names = games ?? [];
        if (names.Any(name => !Enum.TryParse<GameType>(name, out var type) || !Enum.IsDefined(type) || type.ToString() != name))
        {
            error = "Unknown game.";
            return false;
        }

        if (names.Distinct().Count() != names.Count)
        {
            error = "Each game can be listed only once.";
            return false;
        }

        stored = string.Join(",", names);
        error = null;
        return true;
    }

    /// <summary>
    /// A saved game order: the games in it that still exist, in order.
    /// </summary>
    public static IReadOnlyList<string> ParseGameOrder(string? stored) =>
        [.. (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(name => Enum.TryParse<GameType>(name, out var type) && Enum.IsDefined(type) && type.ToString() == name)
            .Distinct()];

    /// <summary>
    /// 3 to 20 letters, digits or underscores.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9_]{3,20}$")]
    private static partial Regex UsernamePattern();
}
