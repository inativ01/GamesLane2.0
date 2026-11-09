using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Wazoo;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player's scorecard: their seat, the points of each box in <see cref="WazooCategory"/> order (null while open),
/// the section totals and bonuses, and the grand total.
/// </summary>
public record WazooPlayerState(
    int Seat,
    IReadOnlyList<int?> Scores,
    int UpperTotal,
    int UpperBonus,
    int LowerTotal,
    int WazooBonus,
    int Total);

/// <summary>
/// The last turn scored: the seat, the box, the points scored there, whether it earned a Wazoo bonus, and the dice.
/// </summary>
public record WazooTurnState(int Seat, WazooCategory Category, int Points, bool Bonus, IReadOnlyList<int> Dice);

/// <summary>
/// Public Wazoo state. <see cref="Players"/> are in turn order (seat order); <see cref="Turn"/> is the seat to play.
/// <see cref="Dice"/> are all 0 before the turn's first roll. <see cref="PossibleScores"/> are, for the seat to play,
/// what the dice would score in each box (in <see cref="WazooCategory"/> order), or null where it cannot score.
/// <see cref="Rolls"/> counts the rolls in the game, so a page can tell a new roll. <see cref="Winners"/> are the seats
/// with the highest total once the game is over.
/// </summary>
public record WazooState(
    int Round,
    int Turn,
    IReadOnlyList<int> Dice,
    IReadOnlyList<bool> Held,
    int RollsLeft,
    int Rolls,
    IReadOnlyList<int?> PossibleScores,
    IReadOnlyList<WazooPlayerState> Players,
    WazooTurnState? LastTurn,
    bool IsOver,
    IReadOnlyList<int> Winners);

/// <summary>
/// A Wazoo game for 1 to 8 seats, who play in seat order. A game that waits for an absent player is paused.
/// </summary>
public class WazooSession(string id, Func<int>? rollDie = null) : GameSession(id), IWazooSession
{
    private WazooGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.Wazoo;

    /// <inheritdoc />
    protected override int MinSeats => 1;

    /// <inheritdoc />
    protected override int MaxSeats => 8;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    private WazooGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryRoll(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryRoll(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryHold(string userKey, int seat, int die, bool held, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryHold(player, die, held, out var e), e), out error);

    /// <inheritdoc />
    public bool TryScore(string userKey, int seat, WazooCategory category, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryScore(player, category, out var e), e), out error);

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) => _game = new WazooGame(seats.Count, rollDie);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(rollDie);

    /// <inheritdoc />
    protected override IEnumerable<int> AwaitedSeats => _game is { IsOver: false } game ? [GameSeats[game.Turn]] : [];

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var seats = GameSeats;
        return new WazooState(
            game.Round,
            seats[game.Turn],
            [.. game.Dice],
            [.. game.Held],
            WazooGame.RollsPerTurn - game.RollsUsed,
            game.Rolls,
            [.. game.PossibleScores],
            [.. game.Scorecards.Select((card, player) => new WazooPlayerState(
                seats[player], [.. card.Scores], card.UpperTotal, card.UpperBonus, card.LowerTotal, card.WazooBonus, card.GrandTotal))],
            game.LastTurn is { } last
                ? new WazooTurnState(seats[last.Player], last.Category, last.Points, last.Bonus, [.. last.Dice])
                : null,
            game.IsOver,
            [.. game.Winners.Select(p => seats[p])]);
    }

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played. Players are numbered in seat order.
    /// </summary>
    private bool WithSeat(string userKey, int seat, Func<WazooGame, int, (bool, string?)> action, out string? error)
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
