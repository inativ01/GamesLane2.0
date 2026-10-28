using System.Diagnostics.CodeAnalysis;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A single two-player game. Actions are authorized by the player's current SignalR connection.
/// </summary>
public interface IGameSession
{
    string Id { get; }

    GameType Type { get; }

    /// <summary>
    /// Adds a player. <paramref name="userKey"/> identifies the player for rejoining; it defaults to the connection ID.
    /// </summary>
    bool TryAddPlayer(
        string name,
        string connectionId,
        [NotNullWhen(true)] out Player? player,
        [NotNullWhen(false)] out string? error,
        string? userKey = null);

    /// <summary>
    /// If <paramref name="userKey"/> holds a seat, moves that seat to <paramref name="connectionId"/> and marks the player connected.
    /// </summary>
    bool TryRejoin(string userKey, string connectionId, [NotNullWhen(true)] out Player? player);

    /// <summary>
    /// Marks the player using <paramref name="connectionId"/> as disconnected. Returns true if a player was affected.
    /// </summary>
    bool Disconnect(string connectionId);

    /// <summary>
    /// Public game state, safe to send to everyone in the game (players and spectators).
    /// </summary>
    object GetPublicState();
}
