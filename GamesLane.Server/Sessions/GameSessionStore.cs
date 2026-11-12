using System.Collections.Concurrent;
using System.Security.Cryptography;
using GamesLane.Server.Data;
using GamesLane.Server.Stats;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesLane.Server.Sessions;

/// <summary>
/// The sessions: those being played in memory, all of them saved in the database (see <see cref="ISessionRepository"/>).
/// Codes are 6 characters, without easily confused characters such as 0/O and 1/I. Each game started in a session is
/// counted in <paramref name="plays"/> (if given) when the session is next saved, on that day (UTC); each game played to
/// its end, in <paramref name="activity"/> (if given).
/// </summary>
public class GameSessionStore(
    ISessionRepository repository,
    TimeProvider? timeProvider = null,
    ILogger<GameSessionStore>? logger = null,
    IGamePlayRepository? plays = null,
    ActivityCounter? activity = null) : IGameSessionStore
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<GameSessionStore>.Instance;
    private readonly ConcurrentDictionary<string, IGameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _saving = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSaved = new(StringComparer.OrdinalIgnoreCase);

    // What each session in memory looked like when last saved or loaded, so removing it unchanged does not save it
    // again (which would count as activity).
    private readonly ConcurrentDictionary<string, string> _savedData = new(StringComparer.OrdinalIgnoreCase);

    // How many games each session in memory had started when they were last counted.
    private readonly ConcurrentDictionary<string, int> _countedGames = new(StringComparer.OrdinalIgnoreCase);

    // The number of the last game of each session in memory counted as finished.
    private readonly ConcurrentDictionary<string, int> _countedFinishes = new(StringComparer.OrdinalIgnoreCase);

    // Loading and unloading wait for each other, so a session is never in memory twice.
    private readonly SemaphoreSlim _loading = new(1, 1);

    /// <inheritdoc />
    public async Task<IGameSession> CreateAsync(GameType type, Action<IGameSession>? prepare = null)
    {
        while (true)
        {
            var id = RandomNumberGenerator.GetString(CodeChars, CodeLength);
            if (_sessions.ContainsKey(id) || await repository.ExistsAsync(id))
            {
                continue;
            }

            var session = New(type, id);
            prepare?.Invoke(session);
            if (_sessions.TryAdd(id, session))
            {
                await SaveAsync(session);
                return session;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IGameSession?> GetAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return session;
        }

        await _loading.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(sessionId, out session))
            {
                return session;
            }

            var id = sessionId.ToUpperInvariant();
            if (await repository.FindAsync(id) is not { } saved || !Enum.TryParse<GameType>(saved.GameType, out var type))
            {
                return null;
            }

            var loaded = New(type, id);
            if (saved.Version != SessionSnapshot.Version(loaded.GetType()))
            {
                _logger.LogWarning("Session {SessionId} was saved by another version of the game and cannot be loaded.", id);
                return null;
            }

            loaded.Restore(saved.Data, saved.UpdatedAt);
            loaded.UpdateAwaySeats(_time.GetUtcNow());
            _countedGames[id] = loaded.GamesStarted;
            _countedFinishes[id] = loaded.HasFinishedGame ? loaded.GamesStarted : 0;
            _savedData[id] = loaded.Save();
            _sessions[id] = loaded;
            return loaded;
        }
        finally
        {
            _loading.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(IGameSession session)
    {
        var gate = _saving.GetOrAdd(session.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await CountNewGamesAsync(session);
            await CountFinishedGameAsync(session);
            var now = _time.GetUtcNow();
            var data = session.Save();
            _lastSaved[session.Id] = now;
            _savedData[session.Id] = data;
            await repository.SaveAsync(new SavedSession
            {
                Id = session.Id.ToUpperInvariant(),
                GameType = session.Type.ToString(),
                Version = SessionSnapshot.Version(session.GetType()),
                Data = data,
                UpdatedAt = now,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save session {SessionId}.", session.Id);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnloadAsync(IGameSession session)
    {
        await _loading.WaitAsync();
        try
        {
            if (!session.HasStarted || (session.IsPublic && !session.HasSeatedPlayers))
            {
                await repository.DeleteAsync(session.Id.ToUpperInvariant());
            }
            else if (!_savedData.TryGetValue(session.Id, out var saved) || saved != session.Save())
            {
                await SaveAsync(session);
            }

            _sessions.TryRemove(new KeyValuePair<string, IGameSession>(session.Id, session));
            _saving.TryRemove(session.Id, out _);
            _lastSaved.TryRemove(session.Id, out _);
            _savedData.TryRemove(session.Id, out _);
            _countedGames.TryRemove(session.Id, out _);
            _countedFinishes.TryRemove(session.Id, out _);
        }
        finally
        {
            _loading.Release();
        }
    }

    /// <summary>
    /// Counts the games started in <paramref name="session"/> since they were last counted. Errors are logged, not thrown,
    /// and those games are not counted again.
    /// </summary>
    private async Task CountNewGamesAsync(IGameSession session)
    {
        var started = session.GamesStarted;
        var counted = _countedGames.GetValueOrDefault(session.Id);
        if (started <= counted)
        {
            return;
        }

        _countedGames[session.Id] = started;
        if (plays is null)
        {
            return;
        }

        try
        {
            await plays.AddAsync(session.Type, DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime), started - counted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not count the games started in session {SessionId}.", session.Id);
        }
    }

    /// <summary>
    /// Counts the current game of <paramref name="session"/> as finished if it was played to its end and has not been
    /// counted yet.
    /// </summary>
    private async Task CountFinishedGameAsync(IGameSession session)
    {
        var game = session.GamesStarted;
        if (!session.HasFinishedGame || _countedFinishes.GetValueOrDefault(session.Id) >= game)
        {
            return;
        }

        _countedFinishes[session.Id] = game;
        if (activity is not null)
        {
            await activity.CountAsync(ActivityEvent.FinishGame, session.Type);
        }
    }

    /// <inheritdoc />
    public IEnumerable<IGameSession> All => _sessions.Values;

    /// <inheritdoc />
    public DateTimeOffset LastActivity(IGameSession session) =>
        _lastSaved.TryGetValue(session.Id, out var saved) ? saved : DateTimeOffset.MinValue;

    /// <summary>
    /// A new, empty session of the given game with the given code.
    /// </summary>
    public static GameSession New(GameType type, string id) => type switch
    {
        GameType.TicTacToe => new TicTacToeSession(id),
        GameType.TheGang => new TheGangSession(id),
        GameType.Backgammon => new BackgammonSession(id),
        GameType.RichManPoorMan => new RichManPoorManSession(id),
        GameType.Wazoo => new WazooSession(id),
        GameType.FinalCard => new FinalCardSession(id),
        GameType.Yaniv => new YanivSession(id),
        GameType.Hearts => new HeartsSession(id),
        GameType.Spades => new SpadesSession(id),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
    };

    /// <summary>
    /// The web page that plays the given game type.
    /// </summary>
    public static string PageFor(GameType type) => type switch
    {
        GameType.TicTacToe => "/tictactoe.html",
        GameType.TheGang => "/the-gang.html",
        GameType.Backgammon => "/backgammon.html",
        GameType.RichManPoorMan => "/rich-man-poor-man.html",
        GameType.Wazoo => "/wazoo.html",
        GameType.FinalCard => "/final-card.html",
        GameType.Yaniv => "/yaniv.html",
        GameType.Hearts => "/hearts.html",
        GameType.Spades => "/spades.html",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
    };
}
