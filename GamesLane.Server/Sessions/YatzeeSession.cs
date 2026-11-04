using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Yatzee;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player's scorecard: their seat, the points of each box in <see cref="YatzeeCategory"/> order (null while open),
/// the section totals and bonuses, and the grand total.
/// </summary>
public record YatzeePlayerState(
    int Seat,
    IReadOnlyList<int?> Scores,
    int UpperTotal,
    int UpperBonus,
    int LowerTotal,
    int YatzeeBonus,
    int Total);

/// <summary>
/// The last turn scored: the seat, the box, the points scored there, whether it earned a Yatzee bonus, and the dice.
/// </summary>
public record YatzeeTurnState(int Seat, YatzeeCategory Category, int Points, bool Bonus, IReadOnlyList<int> Dice);

/// <summary>
/// Public Yatzee state. <see cref="Players"/> are in turn order (seat order); <see cref="Turn"/> is the seat to play.
/// <see cref="Dice"/> are all 0 before the turn's first roll. <see cref="PossibleScores"/> are, for the seat to play,
/// what the dice would score in each box (in <see cref="YatzeeCategory"/> order), or null where it cannot score.
/// <see cref="Rolls"/> counts the rolls in the game, so a page can tell a new roll. <see cref="Winners"/> are the seats
/// with the highest total once the game is over.
/// </summary>
public record YatzeeState(
    int Round,
    int Turn,
    IReadOnlyList<int> Dice,
    IReadOnlyList<bool> Held,
    int RollsLeft,
    int Rolls,
    IReadOnlyList<int?> PossibleScores,
    IReadOnlyList<YatzeePlayerState> Players,
    YatzeeTurnState? LastTurn,
    bool IsOver,
    IReadOnlyList<int> Winners);

/// <summary>
/// A Yatzee game for 1 to 8 seats, who play in seat order. A game that waits for an absent player is paused.
/// </summary>
public class YatzeeSession(string id, Func<int>? rollDie = null) : GameSession(id), IYatzeeSession
{
    private YatzeeGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.Yatzee;

    /// <inheritdoc />
    protected override int MinSeats => 1;

    /// <inheritdoc />
    protected override int MaxSeats => 8;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    private YatzeeGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryRoll(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryRoll(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryHold(string userKey, int seat, int die, bool held, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryHold(player, die, held, out var e), e), out error);

    /// <inheritdoc />
    public bool TryScore(string userKey, int seat, YatzeeCategory category, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryScore(player, category, out var e), e), out error);

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) => _game = new YatzeeGame(seats.Count, rollDie);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(rollDie);

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var seats = GameSeats;
        return new YatzeeState(
            game.Round,
            seats[game.Turn],
            [.. game.Dice],
            [.. game.Held],
            YatzeeGame.RollsPerTurn - game.RollsUsed,
            game.Rolls,
            [.. game.PossibleScores],
            [.. game.Scorecards.Select((card, player) => new YatzeePlayerState(
                seats[player], [.. card.Scores], card.UpperTotal, card.UpperBonus, card.LowerTotal, card.YatzeeBonus, card.GrandTotal))],
            game.LastTurn is { } last
                ? new YatzeeTurnState(seats[last.Player], last.Category, last.Points, last.Bonus, [.. last.Dice])
                : null,
            game.IsOver,
            [.. game.Winners.Select(p => seats[p])]);
    }

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played. Players are numbered in seat order.
    /// </summary>
    private bool WithSeat(string userKey, int seat, Func<YatzeeGame, int, (bool, string?)> action, out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            var player = Enumerable.Range(0, GameSeats.Count).First(p => GameSeats[p] == seat);
            (var done, error) = action(Game, player);
            return done;
        }
    }
}
