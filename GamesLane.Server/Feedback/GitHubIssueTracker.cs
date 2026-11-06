using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GamesLane.Server.Feedback;

/// <summary>
/// Where comments from the feedback form go.
/// </summary>
public interface IIssueTracker
{
    /// <summary>
    /// Whether comments can be sent: the feedback form is shown only then.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Creates the issue and returns its web address. Throws <see cref="HttpRequestException"/> if it fails.
    /// </summary>
    Task<string> CreateAsync(IssueDraft issue, CancellationToken cancellationToken);
}

/// <summary>
/// Creates GitHub issues with the GitHub REST API, using a token kept on the server (never sent to pages). Configured by
/// <c>Feedback:GitHubToken</c> (a fine-grained token with only "Issues: write" on the repository; on Render, the
/// environment variable <c>Feedback__GitHubToken</c>) and <c>Feedback:GitHubRepository</c> (owner/name, by default
/// this project's repository). Without a token, the feedback form is not shown.
/// </summary>
public sealed partial class GitHubIssueTracker : IIssueTracker
{
    /// <summary>
    /// The repository issues go to when <c>Feedback:GitHubRepository</c> is not set.
    /// </summary>
    public const string DefaultRepository = "inativ01/GamesLane2.0";

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _repository;

    public GitHubIssueTracker(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _token = configuration["Feedback:GitHubToken"] ?? "";
        var repository = configuration["Feedback:GitHubRepository"];
        _repository = string.IsNullOrEmpty(repository) ? DefaultRepository : repository;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryName();

    /// <inheritdoc />
    public bool IsConfigured => _token.Length > 0 && RepositoryName().IsMatch(_repository);

    /// <inheritdoc />
    public async Task<string> CreateAsync(IssueDraft issue, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new HttpRequestException("Feedback is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{_repository}/issues")
        {
            Content = JsonContent.Create(new { title = issue.Title, body = issue.Body, labels = issue.Labels }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("GamesLane");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedIssue>(cancellationToken);
        return created?.HtmlUrl ?? throw new HttpRequestException("GitHub did not return the new issue.");
    }

    /// <summary>
    /// The part of GitHub's answer that is used: the new issue's web address.
    /// </summary>
    private sealed record CreatedIssue([property: JsonPropertyName("html_url")] string? HtmlUrl);
}
