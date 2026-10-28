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
/// <see cref="UserKey"/> is the player's stable identity (signed-in account or guest ID), used to rejoin after reconnecting.
/// <see cref="ConnectionId"/> is the player's current SignalR connection.
/// </summary>
public class Player(string id, string name, string userKey, string connectionId, int seat)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string UserKey { get; } = userKey;

    public int Seat { get; } = seat;

    public string ConnectionId { get; internal set; } = connectionId;

    public bool IsConnected { get; internal set; } = true;
}

public record PlayerInfo(string PlayerId, int Seat, GameType GameType);

/// <summary>
/// Returned when a connection enters a game page: the public state, plus the caller's seat if they are a player.
/// </summary>
public record GameEntry(object State, PlayerInfo? Player);

/// <summary>
/// A single two-player game. Actions are authorized by the player's current SignalR connection, so a client can only act for itself.
/// All public members are thread-safe; derived classes must lock <see cref="Sync"/> when accessing state.
/// </summary>
public abstract class GameSession(string id) : IGameSession
{
    public const int MaxPlayers = 2;

    protected readonly object Sync = new();
    protected readonly List<Player> Players = [];

    public string Id { get; } = id;

    public abstract GameType Type { get; }

    /// <summary>
    /// Adds a player. <paramref name="userKey"/> identifies the player for rejoining; it defaults to the connection ID.
    /// </summary>
    public bool TryAddPlayer(
        string name,
        string connectionId,
        [NotNullWhen(true)] out Player? player,
        [NotNullWhen(false)] out string? error,
        string? userKey = null)
    {
        userKey ??= connectionId;
        lock (Sync)
        {
            player = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Player name is required.";
                return false;
            }

            if (Players.Any(p => p.UserKey == userKey || p.ConnectionId == connectionId))
            {
                error = "You have already joined this game.";
                return false;
            }

            if (Players.Count >= MaxPlayers)
            {
                error = "The game is full.";
                return false;
            }

            player = new Player(Guid.NewGuid().ToString("N"), name.Trim(), userKey, connectionId, Players.Count);
            Players.Add(player);
            error = null;
            return true;
        }
    }

    /// <summary>
    /// If <paramref name="userKey"/> holds a seat, moves that seat to <paramref name="connectionId"/> and marks the player connected.
    /// </summary>
    public bool TryRejoin(string userKey, string connectionId, [NotNullWhen(true)] out Player? player)
    {
        lock (Sync)
        {
            player = Players.FirstOrDefault(p => p.UserKey == userKey);
            if (player is null)
            {
                return false;
            }

            player.ConnectionId = connectionId;
            player.IsConnected = true;
            return true;
        }
    }

    /// <summary>
    /// Marks the player using <paramref name="connectionId"/> as disconnected. Returns true if a player was affected.
    /// </summary>
    public bool Disconnect(string connectionId)
    {
        lock (Sync)
        {
            var player = Players.FirstOrDefault(p => p.ConnectionId == connectionId && p.IsConnected);
            if (player is null)
            {
                return false;
            }

            player.IsConnected = false;
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
