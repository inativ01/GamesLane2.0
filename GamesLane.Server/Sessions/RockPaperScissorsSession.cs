using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.RockPaperScissors;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A seat's public rock-paper-scissors info. <see cref="Pick"/> is <see cref="Choice.None"/> until both players have picked.
/// </summary>
public record RockPaperScissorsHand(int Score, bool HasPicked, Choice Pick);

/// <summary>
/// Public rock-paper-scissors state. <see cref="Hands"/> are in seat order.
/// </summary>
public record RockPaperScissorsState(
    int Round,
    bool IsRoundComplete,
    int? RoundWinner,
    int Draws,
    IReadOnlyList<RockPaperScissorsHand> Hands);

/// <summary>
/// A rock-paper-scissors match for two seats that continues round after round, keeping score. It never ends by itself.
/// </summary>
public class RockPaperScissorsSession(string id) : GameSession(id), IRockPaperScissorsSession
{
    private RockPaperScissorsGame _game = new();

    /// <inheritdoc />
    public override GameType Type => GameType.RockPaperScissors;

    /// <inheritdoc />
    protected override int MinSeats => 2;

    /// <inheritdoc />
    protected override int MaxSeats => 2;

    /// <inheritdoc />
    protected override bool IsGameOver => false;

    /// <inheritdoc />
    public bool TryPick(string userKey, int seat, Choice choice, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            var picked = _game.TryPick(seat, choice, out var pickError);
            error = pickError ?? (picked ? null : "That pick is not allowed.");
            return picked;
        }
    }

    /// <inheritdoc />
    public bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!IsInGame(userKey, out error))
            {
                return false;
            }

            var started = _game.TryNextRound(round, out var roundError);
            error = roundError ?? (started ? null : "The next round cannot start yet.");
            return started;
        }
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) => _game = new RockPaperScissorsGame();

    /// <summary>
    /// The public <see cref="RockPaperScissorsState"/>. Picks are hidden until both seats have picked.
    /// </summary>
    protected override object GetGameState()
    {
        var reveal = _game.IsRoundComplete;
        return new RockPaperScissorsState(
            _game.Round,
            reveal,
            _game.RoundWinner,
            _game.Draws,
            Enumerable.Range(0, RockPaperScissorsGame.PlayerCount).Select(seat => new RockPaperScissorsHand(
                _game.Scores[seat],
                _game.Picks[seat] != Choice.None,
                reveal ? _game.Picks[seat] : Choice.None)).ToArray());
    }
}
