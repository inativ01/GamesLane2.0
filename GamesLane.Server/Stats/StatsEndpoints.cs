using GamesLane.Server.Auth;

namespace GamesLane.Server.Stats;

/// <summary>
/// The games-played endpoints: the admin page's report, and the order of the games by popularity for the main screen.
/// </summary>
public static class StatsEndpoints
{
    /// <summary>
    /// Maps <c>/api/admin/stats</c> and <c>/api/games/popular</c>.
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

            var count = days ?? GamePlayStats.DefaultDays;
            if (count is < 1 or > GamePlayStats.MaxDays)
            {
                return Results.BadRequest(new { error = $"Choose 1 to {GamePlayStats.MaxDays} days." });
            }

            var today = Today(timeProvider);
            var kept = await plays.GetAsync(GamePlayStats.FirstDay(today, count), today);
            return Results.Ok(GamePlayStats.Report(kept, today, count));
        });

        // For everyone: the games, most played in the last 30 days first. Only the order, not the counts.
        app.MapGet("/api/games/popular", async (IGamePlayRepository plays, TimeProvider timeProvider) =>
        {
            var today = Today(timeProvider);
            var kept = await plays.GetAsync(GamePlayStats.FirstDay(today, GamePlayStats.PopularityDays), today);
            return Results.Ok(GamePlayStats.PopularOrder(kept));
        });
    }

    private static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
