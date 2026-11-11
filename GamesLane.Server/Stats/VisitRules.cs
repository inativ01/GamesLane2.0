using System.Text.RegularExpressions;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Stats;

/// <summary>
/// What a page reports about a visit (<c>POST /api/visits</c>). <see cref="FirstToday"/>: the browser's first visit of
/// the day (UTC), with where it came from (<see cref="Source"/>), the browser's language (<see cref="Language"/>), and
/// the days since its previous visit (<see cref="DaysSinceLastVisit"/>, null for a first visit). <see cref="Game"/>: the
/// game whose page was opened, if not yet reported today.
/// </summary>
public record VisitReport(GameType? Game, bool FirstToday, string? Source, string? Language, int? DaysSinceLastVisit);

/// <summary>
/// Turns a <see cref="VisitReport"/> into the counts to add, keeping only short, plain values, so the counts cannot be
/// filled with anything else.
/// </summary>
public static partial class VisitRules
{
    /// <summary>A first visit (or the browser's site data was cleared).</summary>
    public const string New = "New";

    /// <summary>The previous visit was less than 7 days before.</summary>
    public const string Within7Days = "Within7Days";

    /// <summary>The previous visit was 7 days before or more.</summary>
    public const string After7Days = "After7Days";

    /// <summary>A visit from no other site and no campaign.</summary>
    public const string Direct = "direct";

    /// <summary>A source or language that is not a plain value.</summary>
    public const string Other = "other";

    private const int MaxSourceLength = 50;

    /// <summary>The return buckets, in order.</summary>
    public static IReadOnlyList<string> ReturnBuckets { get; } = [New, Within7Days, After7Days];

    /// <summary>
    /// The counts to add for <paramref name="report"/>: for a first visit of the day, the visit (with the game page it
    /// arrived on, if any), its source, language and return bucket; and opening the game, if any.
    /// </summary>
    public static IReadOnlyList<(ActivityEvent Activity, GameType? GameType, string Detail)> Counts(VisitReport report)
    {
        var game = report.Game is { } type && Enum.IsDefined(type) ? type : (GameType?)null;
        List<(ActivityEvent, GameType?, string)> counts = [];
        if (report.FirstToday)
        {
            counts.Add((ActivityEvent.Visit, game, ""));
            counts.Add((ActivityEvent.VisitSource, null, Source(report.Source)));
            counts.Add((ActivityEvent.VisitLanguage, null, Language(report.Language)));
            counts.Add((ActivityEvent.VisitReturn, null, ReturnBucket(report.DaysSinceLastVisit)));
        }

        if (game is not null)
        {
            counts.Add((ActivityEvent.OpenGame, game, ""));
        }

        return counts;
    }

    /// <summary>
    /// The return bucket of a visit <paramref name="days"/> days after the previous one (null: no previous one).
    /// </summary>
    public static string ReturnBucket(int? days) => days is null or < 0 ? New : days < 7 ? Within7Days : After7Days;

    /// <summary>
    /// A source in lower case without "www.", e.g. "facebook.com" or "newsletter"; <see cref="Direct"/> when empty, and
    /// <see cref="Other"/> unless it is up to 50 letters, digits, dots, dashes and underscores.
    /// </summary>
    public static string Source(string? source)
    {
        var value = (source ?? "").Trim().ToLowerInvariant();
        if (value.StartsWith("www.", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        return value.Length == 0 ? Direct
            : value.Length <= MaxSourceLength && SourcePattern().IsMatch(value) ? value
            : Other;
    }

    /// <summary>
    /// A browser language as its language and, if it has one, its region, e.g. "en-US", "he" or "zh-TW" (from
    /// "zh-Hant-TW"); <see cref="Other"/> unless the language is 2 or 3 letters.
    /// </summary>
    public static string Language(string? tag)
    {
        var parts = (tag ?? "").Trim().Split('-', '_');
        var language = parts[0].ToLowerInvariant();
        if (!LanguagePattern().IsMatch(language))
        {
            return Other;
        }

        var region = parts.Skip(1).LastOrDefault(p => p.Length == 2 && p.All(char.IsAsciiLetter));
        return region is null ? language : $"{language}-{region.ToUpperInvariant()}";
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex SourcePattern();

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LanguagePattern();
}
