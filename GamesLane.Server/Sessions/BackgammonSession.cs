using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Backgammon;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A checker move, in the moving seat's own point numbering (25 is the bar, 0 is off).
/// </summary>
public record BackgammonMoveState(int From, int To);

/// <summary>
/// A finished turn: the seat, its dice, the checkers it moved (none if it could not move), and whether the game played
/// the only possible move for it.
/// </summary>
public record BackgammonTurnState(int Seat, IReadOnlyList<int> Dice, IReadOnlyList<BackgammonMoveState> Moves, bool Forced);

/// <summary>
/// How the last game ended: the winning seat, the points won, and how (single, gammon, backgammon, dropped double).
/// </summary>
public record BackgammonResultState(int Seat, int Points, BackgammonResultKind Kind);

/// <summary>
/// Public backgammon state. <see cref="Points"/> is the board from white's (seat 0's) side: entry <c>i</c> is white's
/// point <c>i + 1</c>, positive for white checkers and negative for black. <see cref="Bar"/>, <see cref="Off"/>,
/// <see cref="Pips"/> and <see cref="Scores"/> are by seat. <see cref="Turn"/> is the seat to move (after a game, the
/// winner). <see cref="LegalMoves"/>, <see cref="Played"/> and the moves of <see cref="LastTurn"/> use the moving seat's
/// numbering. <see cref="CubeOwner"/> is a seat, or null while the cube is in the middle. <see cref="OpeningDice"/> are
/// each seat's opening die (null until rolled); <see cref="Rolls"/> counts the rolls in the match, so a page can tell a
/// new roll.
/// </summary>
public record BackgammonState(
    int MatchLength,
    bool CubeEnabled,
    int GameNumber,
    IReadOnlyList<int> Scores,
    IReadOnlyList<int> Points,
    IReadOnlyList<int> Bar,
    IReadOnlyList<int> Off,
    IReadOnlyList<int> Pips,
    BackgammonPhase Phase,
    int Turn,
    IReadOnlyList<int> Dice,
    IReadOnlyList<int> DiceLeft,
    IReadOnlyList<int>? OpeningRoll,
    IReadOnlyList<int?> OpeningDice,
    int Rolls,
    IReadOnlyList<BackgammonMoveState> Played,
    IReadOnlyList<BackgammonMoveState> LegalMoves,
    bool CanEndTurn,
    BackgammonTurnState? LastTurn,
    int CubeValue,
    int? CubeOwner,
    bool IsCrawford,
    bool CanDouble,
    BackgammonResultState? Result);

/// <summary>
/// A backgammon match for two seats, with the options' match length and doubling cube. Seat 0 plays white, seat 1
/// black. A new game (from the session lobby, or Play again) starts a new match.
/// </summary>
public class BackgammonSession(string id, Func<int>? rollDie = null) : GameSession(id), IBackgammonSession
{
    private BackgammonGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.Backgammon;

    /// <inheritdoc />
    protected override int MinSeats => 2;

    /// <inheritdoc />
    protected override int MaxSeats => 2;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsMatchOver ?? false;

    private BackgammonGame Game => _game ?? throw new InvalidOperationException("No match has started.");

    /// <inheritdoc />
    public bool TryRoll(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryRoll(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryMove(string userKey, int seat, int from, int to, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryMove(player, from, to, out var e), e), out error);

    /// <inheritdoc />
    public bool TryUndo(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryUndo(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryEndTurn(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryEndTurn(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryDouble(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryDouble(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryTake(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryTake(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryDrop(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithSeat(userKey, seat, (game, player) => (game.TryDrop(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!IsInGame(userKey, out error))
            {
                return false;
            }

            var started = Game.TryNextGame(round, out var gameError);
            error = gameError ?? (started ? null : "The next game cannot start yet.");
            return started;
        }
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) =>
        _game = new BackgammonGame(BackgammonOptions.RulesFrom(Options), rollDie);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(rollDie);

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var position = game.Position;
        int[] seats = [0, 1];
        return new BackgammonState(
            game.Rules.MatchLength,
            game.Rules.Cube,
            game.GameNumber,
            [.. game.Scores],
            [.. position.Points],
            [.. seats.Select(p => position.Count(p, BackgammonPosition.Bar))],
            [.. seats.Select(p => position.Count(p, BackgammonPosition.Off))],
            [.. seats.Select(position.Pips)],
            game.Phase,
            game.Turn,
            [.. game.Dice],
            [.. game.DiceLeft],
            game.OpeningRoll is { } opening ? [.. opening] : null,
            [.. game.OpeningDice],
            game.Rolls,
            Moves(game.Played),
            Moves(game.LegalMoves),
            game.CanEndTurn,
            game.LastTurn is { } last ? new BackgammonTurnState(last.Player, [.. last.Dice], Moves(last.Steps), last.Forced) : null,
            game.CubeValue,
            game.CubeOwner,
            game.IsCrawford,
            game.CanDouble,
            game.Result is { } result ? new BackgammonResultState(result.Winner, result.Points, result.Kind) : null);
    }

    private static BackgammonMoveState[] Moves(IEnumerable<BackgammonStep> steps) =>
        [.. steps.Select(s => new BackgammonMoveState(s.From, s.To))];

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the match being played. Seat 0 is player 0 (white) and seat 1 is player 1 (black).
    /// </summary>
    private bool WithSeat(string userKey, int seat, Func<BackgammonGame, int, (bool, string?)> action, out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            (var done, error) = action(Game, seat);
            return done;
        }
    }
}
