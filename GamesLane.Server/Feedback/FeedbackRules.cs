using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GamesLane.Server.Feedback;

/// <summary>
/// The kinds of comment users can send.
/// </summary>
public enum FeedbackType
{
    Problem,
    Idea,
    Other,
}

/// <summary>
/// A comment from the feedback form: its kind and text, whether to show the sender's display name, and the page and
/// language it was sent from.
/// </summary>
public record FeedbackRequest(FeedbackType Type, string? Text, bool IncludeName, string? Page, string? Language);

/// <summary>
/// A GitHub issue to create: its title, its text (Markdown) and its labels.
/// </summary>
public record IssueDraft(string Title, string Body, IReadOnlyList<string> Labels);

/// <summary>
/// Turns a comment into a GitHub issue. The repository is public, so the issue holds only the comment, its kind, the
/// page and language, and the sender's display name if they chose to include it. Mentions (@name) are broken up so they
/// notify nobody.
/// </summary>
public static partial class FeedbackRules
{
    /// <summary>
    /// The longest comment, in characters.
    /// </summary>
    public const int MaxLength = 2000;

    /// <summary>
    /// The label every feedback issue gets.
    /// </summary>
    public const string Label = "feedback";

    private const int TitleLength = 60;

    [GeneratedRegex(@"^/[A-Za-z0-9._/-]{0,60}$")]
    private static partial Regex PagePath();

    [GeneratedRegex(@"^[a-z]{2}(-[A-Z]{2})?$")]
    private static partial Regex LanguageCode();

    /// <summary>
    /// The issue for <paramref name="request"/>, sent by <paramref name="name"/> (null to leave the name out), or an
    /// error if the comment is empty or too long.
    /// </summary>
    public static bool TryCreateIssue(FeedbackRequest request, string? name,
        [NotNullWhen(true)] out IssueDraft? issue, [NotNullWhen(false)] out string? error)
    {
        issue = null;
        var text = (request.Text ?? "").ReplaceLineEndings("\n").Trim();
        if (text.Length is 0 or > MaxLength)
        {
            error = $"Write a comment of 1 to {MaxLength} characters.";
            return false;
        }

        if (!Enum.IsDefined(request.Type))
        {
            error = "Choose the kind of comment.";
            return false;
        }

        text = NoMentions(text);
        var details = new List<string> { $"Type: {request.Type}" };
        if (request.Page is { } page && PagePath().IsMatch(page))
        {
            details.Add($"Page: {page}");
        }

        if (request.Language is { } language && LanguageCode().IsMatch(language))
        {
            details.Add($"Language: {language}");
        }

        details.Add(name is null ? "From: (name not shared)" : $"From: {NoMentions(name)}");
        var body = $"{text}\n\n---\n{string.Join("  \n", details)}\n\n_Sent from the GamesLane feedback form._";
        var labels = request.Type switch
        {
            FeedbackType.Problem => new[] { Label, "bug" },
            FeedbackType.Idea => [Label, "enhancement"],
            _ => [Label],
        };

        issue = new IssueDraft($"[{request.Type}] {Shorten(text.Split('\n')[0])}", body, labels);
        error = null;
        return true;
    }

    /// <summary>
    /// The text with a zero-width space after each @, so GitHub does not notify anyone it names.
    /// </summary>
    private static string NoMentions(string text) => text.Replace("@", "@\u200B");

    /// <summary>
    /// The first <see cref="TitleLength"/> characters of <paramref name="line"/>, with "…" if it is longer.
    /// </summary>
    private static string Shorten(string line)
    {
        if (line.Length <= TitleLength)
        {
            return line;
        }

        var length = char.IsHighSurrogate(line[TitleLength - 1]) ? TitleLength - 1 : TitleLength;
        return $"{line[..length].TrimEnd()}…";
    }
}
