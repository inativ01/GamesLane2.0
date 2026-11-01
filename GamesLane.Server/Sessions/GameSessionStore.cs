using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace GamesLane.Server.Sessions;

/// <summary>
/// In-memory store of active sessions. Sessions are lost when the server restarts.
/// Codes are 6 characters, without easily confused characters such as 0/O and 1/I.
/// </summary>
public class GameSessionStore : IGameSessionStore
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly ConcurrentDictionary<string, IGameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IGameSession Create(GameType type)
    {
        while (true)
        {
            var id = RandomNumberGenerator.GetString(CodeChars, CodeLength);
            IGameSession session = type switch
            {
                GameType.TicTacToe => new TicTacToeSession(id),
                GameType.RockPaperScissors => new RockPaperScissorsSession(id),
                GameType.TheGang => new TheGangSession(id),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
            };

            if (_sessions.TryAdd(session.Id, session))
            {
                return session;
            }
        }
    }

    /// <inheritdoc />
    public IGameSession? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    /// <inheritdoc />
    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    /// <inheritdoc />
    public IEnumerable<IGameSession> All => _sessions.Values;

    /// <summary>
    /// The web page that plays the given game type.
    /// </summary>
    public static string PageFor(GameType type) => type switch
    {
        GameType.TicTacToe => "/tictactoe.html",
        GameType.RockPaperScissors => "/rock-paper-scissors.html",
        GameType.TheGang => "/the-gang.html",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
    };
}
