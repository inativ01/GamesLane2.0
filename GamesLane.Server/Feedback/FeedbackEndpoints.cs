using System.Threading.RateLimiting;
using GamesLane.Server.Auth;

namespace GamesLane.Server.Feedback;

/// <summary>
/// The feedback form's endpoint: <c>POST /api/feedback</c> posts a comment as a GitHub issue.
/// </summary>
public static class FeedbackEndpoints
{
    /// <summary>
    /// The rate limit for comments: at most <see cref="CommentsPerHour"/> per user (or per address, for requests
    /// without a login) in an hour.
    /// </summary>
    public const string RateLimit = "feedback";

    private const int CommentsPerHour = 5;

    /// <summary>
    /// Registers the issue tracker and the comments' rate limit.
    /// </summary>
    public static void AddGamesLaneFeedback(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient<IIssueTracker, GitHubIssueTracker>(client => client.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddRateLimiter(options => options.AddPolicy(RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
            AuthSetup.GetUserId(context.User) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = CommentsPerHour, Window = TimeSpan.FromHours(1) })));
    }

    /// <summary>
    /// Maps <c>POST /api/feedback</c>: logged-in users (guests too) send <see cref="FeedbackRequest"/> and get
    /// <c>{ url }</c>, the new issue's address. 400 with <c>{ error }</c> for an invalid comment, 404 when feedback is
    /// not configured, 429 after too many comments, and 502 with <c>{ error }</c> if GitHub could not be reached.
    /// </summary>
    public static void MapFeedbackEndpoints(this WebApplication app)
    {
        app.MapPost("/api/feedback", async (FeedbackRequest request, HttpContext context, IIssueTracker issues, ILoggerFactory loggers) =>
        {
            if (AuthSetup.GetProfile(context.User) is not { } profile)
            {
                return Results.Unauthorized();
            }

            if (!issues.IsConfigured)
            {
                return Results.NotFound();
            }

            if (!FeedbackRules.TryCreateIssue(request, request.IncludeName ? profile.Name : null, out var issue, out var error))
            {
                return Results.BadRequest(new { error });
            }

            try
            {
                return Results.Ok(new { url = await issues.CreateAsync(issue, context.RequestAborted) });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                loggers.CreateLogger(typeof(FeedbackEndpoints)).LogWarning(ex, "Could not create a GitHub issue for a comment.");
                return Results.Json(new { error = "Your comment could not be sent. Try again later." }, statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireRateLimiting(RateLimit);
    }
}
