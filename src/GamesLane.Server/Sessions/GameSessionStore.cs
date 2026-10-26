using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace GamesLane.Server.Sessions;

/// <summary>
/// In-memory store of active games. Games are lost when the server restarts.
/// </summary>
public class GameSessionStore
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly ConcurrentDictionary<string, GameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public GameSession Create()
    {
        while (true)
        {
            var session = new GameSession(RandomNumberGenerator.GetString(CodeChars, CodeLength));
            if (_sessions.TryAdd(session.Id, session))
            {
                return session;
            }
        }
    }

    public GameSession? Get(string gameId) =>
        _sessions.TryGetValue(gameId, out var session) ? session : null;
}
