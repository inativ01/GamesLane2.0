using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.RockPaperScissors;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player's public rock-paper-scissors info. <see cref="Pick"/> is <see cref="Choice.None"/> until both players have picked.
/// </summary>
public record RockPaperScissorsPlayer(string Name, int Score, bool HasPicked, Choice Pick, bool IsConnected);

/// <summary>
/// Public rock-paper-scissors state. Players are listed in seat order.
/// </summary>
public record RockPaperScissorsState(
    string GameId,
    GameType GameType,
    GameStatus Status,
    int Round,
    bool IsRoundComplete,
    int? RoundWinner,
    int Draws,
    IReadOnlyList<RockPaperScissorsPlayer> Players);

/// <summary>
/// A rock-paper-scissors match that continues round after round, keeping score.
/// </summary>
public class RockPaperScissorsSession(string id) : GameSession(id)
{
    private readonly RockPaperScissorsGame _game = new();

    public override GameType Type => GameType.RockPaperScissors;

    public bool TryPick(string connectionId, Choice choice, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayer(connectionId, out var player, out error))
            {
                return false;
            }

            if (GetStatus() != GameStatus.InProgress)
            {
                error = "The game is not in progress.";
                return false;
            }

            return _game.TryPick(player.Seat, choice, out error);
        }
    }

    public bool TryNextRound(string connectionId, int round, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayer(connectionId, out _, out error))
            {
                return false;
            }

            return _game.TryNextRound(round, out error);
        }
    }

    public RockPaperScissorsState GetState()
    {
        lock (Sync)
        {
            var reveal = _game.IsRoundComplete;
            return new RockPaperScissorsState(
                Id,
                Type,
                GetStatus(),
                _game.Round,
                reveal,
                _game.RoundWinner,
                _game.Draws,
                Players.Select(p => new RockPaperScissorsPlayer(
                    p.Name,
                    _game.Scores[p.Seat],
                    _game.Picks[p.Seat] != Choice.None,
                    reveal ? _game.Picks[p.Seat] : Choice.None,
                    p.IsConnected)).ToArray());
        }
    }

    public override object GetPublicState() => GetState();

    private GameStatus GetStatus() =>
        Players.Count < MaxPlayers ? GameStatus.WaitingForPlayers : GameStatus.InProgress;
}
