using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace GamesLane.Server.Sessions;

/// <summary>
/// In-memory store of active games. Games are lost when the server restarts.
/// </summary>
public class GameSessionStore : IGameSessionStore
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly ConcurrentDictionary<string, IGameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public IGameSession Create(GameType type)
    {
        while (true)
        {
            var id = RandomNumberGenerator.GetString(CodeChars, CodeLength);
            IGameSession session = type switch
            {
                GameType.TicTacToe => new TicTacToeSession(id),
                GameType.RockPaperScissors => new RockPaperScissorsSession(id),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
            };

            if (_sessions.TryAdd(session.Id, session))
            {
                return session;
            }
        }
    }

    public IGameSession? Get(string gameId) =>
        _sessions.TryGetValue(gameId, out var session) ? session : null;

    public IEnumerable<IGameSession> All => _sessions.Values;

    /// <summary>
    /// The web page that plays the given game type.
    /// </summary>
    public static string PageFor(GameType type) => type switch
    {
        GameType.TicTacToe => "/tictactoe.html",
        GameType.RockPaperScissors => "/rock-paper-scissors.html",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type."),
    };
}
