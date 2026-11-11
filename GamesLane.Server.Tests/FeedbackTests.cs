using System.Net;
using System.Text.Json;
using GamesLane.Server.Feedback;
using Microsoft.Extensions.Configuration;

namespace GamesLane.Server.Tests;

public class FeedbackTests
{
    private static readonly IssueDraft Draft = new("[Idea] More games", "Body", ["feedback", "enhancement"]);

    [Fact]
    public void A_comment_becomes_an_issue_with_its_kind_page_language_and_name()
    {
        var request = new FeedbackRequest(FeedbackType.Idea, "  Add chess!\r\nIt would be fun, @someone  ", true, "/final-card.html", "fr");

        Assert.True(FeedbackRules.TryCreateIssue(request, "Ada", out var issue, out _));

        Assert.Equal("[Idea] Add chess!", issue.Title);
        Assert.Equal(["feedback", "enhancement"], issue.Labels);
        Assert.StartsWith("Add chess!\nIt would be fun, @\u200Bsomeone\n\n---\n", issue.Body);
        Assert.Contains("Type: Idea  \nPage: /final-card.html  \nLanguage: fr  \nFrom: Ada", issue.Body);
    }

    [Fact]
    public void The_name_is_left_out_unless_included_and_unexpected_details_are_dropped()
    {
        var request = new FeedbackRequest(FeedbackType.Problem, "The dice froze.", false, "https://evil.example/", "<b>");

        Assert.True(FeedbackRules.TryCreateIssue(request, null, out var issue, out _));

        Assert.Equal(["feedback", "bug"], issue.Labels);
        Assert.Contains("From: (name not shared)", issue.Body);
        Assert.DoesNotContain("Page:", issue.Body);
        Assert.DoesNotContain("Language:", issue.Body);
    }

    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", true)]
    [InlineData("zh-tw", false)]
    [InlineData("zh-Hant", false)]
    public void Languages_with_a_region_are_kept(string language, bool kept)
    {
        var request = new FeedbackRequest(FeedbackType.Other, "Hi", false, null, language);

        Assert.True(FeedbackRules.TryCreateIssue(request, null, out var issue, out _));

        Assert.Equal(kept, issue.Body.Contains($"Language: {language}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Empty_comments_are_refused(string? text)
    {
        Assert.False(FeedbackRules.TryCreateIssue(new FeedbackRequest(FeedbackType.Other, text, false, null, null), null, out _, out var error));
        Assert.Equal("Write a comment of 1 to 2000 characters.", error);
    }

    [Fact]
    public void Long_comments_and_unknown_kinds_are_refused_and_long_titles_are_shortened()
    {
        var tooLong = new FeedbackRequest(FeedbackType.Other, new string('a', FeedbackRules.MaxLength + 1), false, null, null);
        Assert.False(FeedbackRules.TryCreateIssue(tooLong, null, out _, out _));
        Assert.False(FeedbackRules.TryCreateIssue(new FeedbackRequest((FeedbackType)7, "Hi", false, null, null), null, out _, out var kind));
        Assert.Equal("Choose the kind of comment.", kind);

        var longest = new FeedbackRequest(FeedbackType.Other, new string('b', FeedbackRules.MaxLength), false, null, null);
        Assert.True(FeedbackRules.TryCreateIssue(longest, null, out var issue, out _));
        Assert.Equal($"[Other] {new string('b', 60)}…", issue.Title);
        Assert.Equal(["feedback"], issue.Labels);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "inativ01/GamesLane2.0")]
    [InlineData("secret", "not a repository")]
    public void Feedback_needs_a_token_and_a_valid_repository(string? token, string? repository)
    {
        Assert.False(Tracker(new RecordingHandler(HttpStatusCode.Created), token, repository).IsConfigured);
    }

    [Fact]
    public async Task Issues_are_created_with_the_GitHub_API_and_return_their_address()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """{ "html_url": "https://github.com/inativ01/GamesLane2.0/issues/9" }""");
        var tracker = Tracker(handler, "secret", null);

        Assert.True(tracker.IsConfigured);
        Assert.Equal("https://github.com/inativ01/GamesLane2.0/issues/9", await tracker.CreateAsync(Draft, CancellationToken.None));

        var request = handler.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.github.com/repos/inativ01/GamesLane2.0/issues", request.RequestUri!.ToString());
        Assert.Equal(("Bearer", "secret"), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        Assert.Contains("GamesLane", request.Headers.UserAgent.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("[Idea] More games", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("Body", body.RootElement.GetProperty("body").GetString());
        Assert.Equal(["feedback", "enhancement"], body.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task A_refused_request_throws()
    {
        var tracker = Tracker(new RecordingHandler(HttpStatusCode.Unauthorized), "expired", null);

        await Assert.ThrowsAsync<HttpRequestException>(() => tracker.CreateAsync(Draft, CancellationToken.None));
    }

    private static GitHubIssueTracker Tracker(HttpMessageHandler handler, string? token, string? repository) =>
        new(new HttpClient(handler), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Feedback:GitHubToken"] = token,
                ["Feedback:GitHubRepository"] = repository,
            })
            .Build());

    /// <summary>
    /// Answers every request with <paramref name="status"/> and <paramref name="response"/>, and keeps the last request.
    /// </summary>
    private sealed class RecordingHandler(HttpStatusCode status, string response = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }
}
