using System.Threading.RateLimiting;
using GamesLane.Server.Auth;

namespace GamesLane.Server.Stats;

/// <summary>
/// The games-played and activity endpoints: the admin page's reports, the order of the games by popularity for the main
/// screen, and the pages' visit reports.
/// </summary>
public static class StatsEndpoints
{
    /// <summary>
    /// The rate limit for visit reports: at most <see cref="VisitReportsPerHour"/> per user (or per address, for requests
    /// without a login) in an hour.
    /// </summary>
    public const string VisitRateLimit = "visits";

    private const int VisitReportsPerHour = 30;

    /// <summary>
    /// Registers where the games started and the activity counts are kept, the activity counter, and the visit reports'
    /// rate limit.
    /// </summary>
    public static void AddGamesLaneStats(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IGamePlayRepository, GamePlayRepository>();
        builder.Services.AddSingleton<IActivityRepository, ActivityRepository>();
        builder.Services.AddSingleton<ActivityCounter>();
        builder.Services.AddRateLimiter(options => options.AddPolicy(VisitRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
            AuthSetup.GetUserId(context.User) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = VisitReportsPerHour, Window = TimeSpan.FromHours(1) })));
    }

    /// <summary>
    /// Maps <c>/api/admin/stats</c>, <c>/api/admin/activity</c>, <c>/api/games/popular</c> and <c>/api/visits</c>.
    /// </summary>
    public static void MapStatsEndpoints(this WebApplication app)
    {
        // Admins only (anyone else gets "not found"): the games started per day over the last `days` days (UTC, today
        // included; 30 by default, 1 to 365), and each game's total.
        app.MapGet("/api/admin/stats", async (int? days, HttpContext context, AdminUsers admins, IGamePlayRepository plays,
            TimeProvider timeProvider) =>
        {
            if (!admins.IsAdmin(context.User))
            {
                return Results.NotFound();
            }

            if (!TryGetDays(days, out var count, out var error))
            {
                return error;
            }

            var today = Today(timeProvider);
            var kept = await plays.GetAsync(GamePlayStats.FirstDay(today, count), today);
            return Results.Ok(GamePlayStats.Report(kept, today, count));
        });

        // Admins only (anyone else gets "not found"): the activity over the same days as /api/admin/stats (see
        // ActivityReport).
        app.MapGet("/api/admin/activity", async (int? days, HttpContext context, AdminUsers admins, IActivityRepository activity,
            IGamePlayRepository plays, TimeProvider timeProvider) =>
        {
            if (!admins.IsAdmin(context.User))
            {
                return Results.NotFound();
            }

            if (!TryGetDays(days, out var count, out var error))
            {
                return error;
            }

            var today = Today(timeProvider);
            var first = GamePlayStats.FirstDay(today, count);
            return Results.Ok(ActivityStats.Report(await activity.GetAsync(first, today), await plays.GetAsync(first, today), today, count));
        });

        // Logged-in users (guests too) report their visits (see VisitReport); only counts are kept. 401 without a login,
        // 429 after too many reports.
        app.MapPost("/api/visits", async (VisitReport report, HttpContext context, ActivityCounter counter) =>
        {
            if (AuthSetup.GetUserId(context.User) is null)
            {
                return Results.Unauthorized();
            }

            foreach (var (activity, gameType, detail) in VisitRules.Counts(report))
            {
                await counter.CountAsync(activity, gameType, detail);
            }

            return Results.NoContent();
        }).RequireRateLimiting(VisitRateLimit);

        // For everyone: the games, most played in the last 30 days first. Only the order, not the counts.
        app.MapGet("/api/games/popular", async (IGamePlayRepository plays, TimeProvider timeProvider) =>
        {
            var today = Today(timeProvider);
            var kept = await plays.GetAsync(GamePlayStats.FirstDay(today, GamePlayStats.PopularityDays), today);
            return Results.Ok(GamePlayStats.PopularOrder(kept));
        });
    }

    private static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    // The days a report covers: `days`, 30 by default; a "bad request" unless 1 to 365.
    private static bool TryGetDays(int? days, out int count, out IResult error)
    {
        count = days ?? GamePlayStats.DefaultDays;
        error = Results.BadRequest(new { error = $"Choose 1 to {GamePlayStats.MaxDays} days." });
        return count is >= 1 and <= GamePlayStats.MaxDays;
    }
}
