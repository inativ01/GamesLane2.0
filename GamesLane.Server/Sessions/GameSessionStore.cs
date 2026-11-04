using System.Collections.Concurrent;
using System.Security.Cryptography;
using GamesLane.Server.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesLane.Server.Sessions;

/// <summary>
/// The sessions: those being played in memory, all of them saved in the database (see <see cref="ISessionRepository"/>).
/// Codes are 6 characters, without easily confused characters such as 0/O and 1/I.
/// </summary>
public class GameSessionStore(
    ISessionRepository repository,
    TimeProvider? timeProvider = null,
    ILogger<GameSessionStore>? logger = null) : IGameSessionStore
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<GameSessionStore>.Instance;
    private readonly ConcurrentDictionary<string, IGameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _saving = new(StringComparer.OrdinalIgnoreCase);

    // Loading and unloading wait for each other, so a session is never in memory twice.
    private readonly SemaphoreSlim _loading = new(1, 1);

    /// <inheritdoc />
    public async Task<IGameSession> CreateAsync(GameType type)
    {
        while (true)
        {
            var id = RandomNumberGenerator.GetString(CodeChars, CodeLength);
            if (_sessions.ContainsKey(id) || await repository.ExistsAsync(id))
            {
                continue;
            }

            var session = New(type, id);
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
            await repository.SaveAsync(new SavedSession
            {
                Id = session.Id.ToUpperInvariant(),
                GameType = session.Type.ToString(),
                Version = SessionSnapshot.Version(session.GetType()),
                Data = session.Save(),
                UpdatedAt = _time.GetUtcNow(),
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
            if (session.HasHadSeats)
            {
                await SaveAsync(session);
            }
            else
            {
                await repository.DeleteAsync(session.Id.ToUpperInvariant());
            }

            _sessions.TryRemove(new KeyValuePair<string, IGameSession>(session.Id, session));
            _saving.TryRemove(session.Id, out _);
        }
        finally
        {
            _loading.Release();
        }
    }

    /// <inheritdoc />
    public IEnumerable<IGameSession> All => _sessions.Values;

    /// <summary>
    /// A new, empty session of the given game with the given code.
    /// </summary>
    public static GameSession New(GameType type, string id) => type switch
    {
        GameType.TicTacToe => new TicTacToeSession(id),
        GameType.RockPaperScissors => new RockPaperScissorsSession(id),
        GameType.TheGang => new TheGangSession(id),
        GameType.Backgammon => new BackgammonSession(id),
        GameType.RichManPoorMan => new RichManPoorManSession(id),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
    };

    /// <summary>
    /// The web page that plays the given game type.
    /// </summary>
    public static string PageFor(GameType type) => type switch
    {
        GameType.TicTacToe => "/tictactoe.html",
        GameType.RockPaperScissors => "/rock-paper-scissors.html",
        GameType.TheGang => "/the-gang.html",
        GameType.Backgammon => "/backgammon.html",
        GameType.RichManPoorMan => "/rich-man-poor-man.html",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
    };
}
