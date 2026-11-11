using GamesLane.Server.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesLane.Server.Stats;

/// <summary>
/// What the activity counts count (see <see cref="ActivityCounter"/>).
/// </summary>
public enum ActivityEvent
{
    /// <summary>A browser's first visit of the day; the game is the game page it arrived on, if any.</summary>
    Visit,

    /// <summary>Where a visit came from (the detail): a campaign's <c>utm_source</c>, a site, "invite" or "direct".</summary>
    VisitSource,

    /// <summary>The browser's language of a visit (the detail), e.g. "en-US".</summary>
    VisitLanguage,

    /// <summary>How long since the browser's previous visit (the detail, a <see cref="VisitRules"/> return bucket).</summary>
    VisitReturn,

    /// <summary>A browser opened a game's page, at most once a day per game.</summary>
    OpenGame,

    /// <summary>A game was created (the detail: "Public" or "Private").</summary>
    CreateGame,

    /// <summary>A player joined someone else's game (the detail: "Public", or "Private" for a game they were invited to).</summary>
    JoinGame,

    /// <summary>A game was played to its end.</summary>
    FinishGame,

    /// <summary>An invite link (<c>/join/{code}</c>) of a game was opened.</summary>
    OpenInvite,
}

/// <summary>
/// Counts activity on the current day (UTC), with nothing about who did it. Errors are logged, not thrown, so counting
/// never stops what is counted.
/// </summary>
public class ActivityCounter(IActivityRepository repository, TimeProvider timeProvider, ILogger<ActivityCounter>? logger = null)
{
    /// <summary>The detail of a public game created or joined.</summary>
    public const string Public = "Public";

    /// <summary>The detail of a private game created or joined.</summary>
    public const string Private = "Private";

    private readonly ILogger _logger = logger ?? NullLogger<ActivityCounter>.Instance;

    /// <summary>
    /// Adds <paramref name="count"/> to today's count of <paramref name="activity"/>, for <paramref name="gameType"/> (if
    /// about a game) and <paramref name="detail"/>.
    /// </summary>
    public async Task CountAsync(ActivityEvent activity, GameType? gameType = null, string detail = "", int count = 1)
    {
        try
        {
            await repository.AddAsync(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), activity,
                gameType?.ToString() ?? "", detail, count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not count {Activity}.", activity);
        }
    }
}
