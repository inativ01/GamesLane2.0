using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

public enum GameType
{
    TicTacToe,
    RockPaperScissors,
}

public enum GameStatus
{
    WaitingForPlayers,
    InProgress,
    Finished,
}

/// <summary>
/// A player in a game. Seats are assigned in join order, starting at 0.
/// </summary>
public record Player(string Id, string Name, string ConnectionId, int Seat);

public record PlayerInfo(string PlayerId, int Seat, GameType GameType);

/// <summary>
/// A single two-player game. Players are identified by their SignalR connection, so a client can only act for itself.
/// All public members are thread-safe; derived classes must lock <see cref="Sync"/> when accessing state.
/// </summary>
public abstract class GameSession(string id)
{
    public const int MaxPlayers = 2;

    protected readonly object Sync = new();
    protected readonly List<Player> Players = [];

    public string Id { get; } = id;

    public abstract GameType Type { get; }

    public bool TryAddPlayer(
        string name,
        string connectionId,
        [NotNullWhen(true)] out Player? player,
        [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            player = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Player name is required.";
                return false;
            }

            if (Players.Any(p => p.ConnectionId == connectionId))
            {
                error = "You have already joined this game.";
                return false;
            }

            if (Players.Count >= MaxPlayers)
            {
                error = "The game is full.";
                return false;
            }

            player = new Player(Guid.NewGuid().ToString("N"), name.Trim(), connectionId, Players.Count);
            Players.Add(player);
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Public game state, safe to send to everyone in the game (players and spectators).
    /// </summary>
    public abstract object GetPublicState();

    protected bool TryGetPlayer(
        string connectionId,
        [NotNullWhen(true)] out Player? player,
        [NotNullWhen(false)] out string? error)
    {
        player = Players.FirstOrDefault(p => p.ConnectionId == connectionId);
        error = player is null ? "Only players in this game can make moves." : null;
        return player is not null;
    }
}
