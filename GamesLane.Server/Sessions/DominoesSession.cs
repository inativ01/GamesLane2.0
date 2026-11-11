using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Dominoes;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player as everyone sees them: their seat, their side (their team, or their own place in the scores), and how many
/// tiles they hold.
/// </summary>
public record DominoesPlayerState(int Seat, int Side, int TileCount);

/// <summary>
/// A side as everyone sees it: its seats (one, or a team's two) and its score.
/// </summary>
public record DominoesSideState(int Side, IReadOnlyList<int> Seats, int Score);

/// <summary>
/// A Mexican Train train: the seat it belongs to (null for the Mexican train), its tiles from the engine (short text, in
/// the direction laid), its open end, and whether it has a marker.
/// </summary>
public record DominoesTrainState(int? Seat, IReadOnlyList<string> Tiles, int End, bool Marker);

/// <summary>
/// The last action: the seat, what it did, the tile played and where (a seat in Mexican Train), and the points it scored.
/// </summary>
public record DominoesActionState(int Seat, DominoesActionKind Kind, string? Tile, int Target, int Points);

/// <summary>
/// A player at the end of a round: the tiles left in their hand and their pips.
/// </summary>
public record DominoesResultPlayerState(int Seat, IReadOnlyList<string> Tiles, int Pips);

/// <summary>
/// How a round ended: the seat that played its last tile (or null when blocked), the players, and per side the points
/// gained and the score after the round.
/// </summary>
public record DominoesResultState(int Round, int? DominoSeat, bool Blocked, IReadOnlyList<DominoesResultPlayerState> Players,
    IReadOnlyList<int> Points, IReadOnlyList<int> Scores);

/// <summary>
/// Public dominoes state. <see cref="Turn"/>, <see cref="Winners"/> and the seats in the players, sides, trains and actions
/// are seats; <see cref="Line"/> holds the line's tiles from left to right, in the direction laid (e.g. "6-3", "3-3",
/// "3-1"). <see cref="Actions"/> counts the actions, so a page can tell a new one.
/// </summary>
public record DominoesState(
    DominoesStyle Style,
    bool Teams,
    int Target,
    int? TotalRounds,
    int RoundNumber,
    DominoesPhase Phase,
    int Turn,
    bool HasDrawn,
    IReadOnlyList<string> Line,
    int? Engine,
    IReadOnlyList<DominoesTrainState> Trains,
    int Boneyard,
    IReadOnlyList<DominoesPlayerState> Players,
    IReadOnlyList<DominoesSideState> SideScores,
    DominoesActionState? LastAction,
    int Actions,
    DominoesResultState? LastResult,
    IReadOnlyList<int> Winners);

/// <summary>
/// A legal play of a seat: a tile and where it goes (0 or 1 for the line's ends; a seat's train or -1 in Mexican Train).
/// </summary>
public record DominoesMoveState(string Tile, int Target);

/// <summary>
/// A seat's private dominoes information: its tiles (in hand order), the plays it may make now, and whether it may draw
/// or pass.
/// </summary>
public record DominoesHand(int Seat, IReadOnlyList<string> Tiles, IReadOnlyList<DominoesMoveState> Moves, bool CanDraw, bool CanPass);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record DominoesPrivateState(IReadOnlyList<DominoesHand> Hands);

/// <summary>
/// A dominoes game with the options' style, teams, score target and rounds: 2 to 4 seats, or 2 to 8 in Mexican Train. The
/// players are the game's seats in seat order. The players can skip the turn of an absent player.
/// </summary>
public class DominoesSession(string id, Action<Tile[]>? shuffle = null) : GameSession(id), IDominoesSession
{
    private DominoesGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.Dominoes;

    /// <inheritdoc />
    protected override int MinSeats => DominoesRules.MinPlayers;

    /// <inheritdoc />
    protected override int MaxSeats => DominoesRules.TrainMaxPlayers;

    /// <inheritdoc />
    protected override int SeatLimitFor(IReadOnlyDictionary<string, string> options) => DominoesOptions.RulesFrom(options).MaxPlayers;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    private DominoesGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryPlayTile(string userKey, int seat, string? tile, int target, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) =>
        {
            if (!TryParse(tile, out var parsed))
            {
                return (false, "Unknown tile.");
            }

            int? gameTarget = game.Rules.IsLine ? (target is 0 or 1 ? target : null)
                : target == DominoesGame.MexicanTarget ? target
                : GameSeats.Contains(target) ? PlayerOf(target)
                : null;
            return gameTarget is { } at
                ? (game.TryPlay(player, parsed, at, out var e), e)
                : (false, "That tile does not match there.");
        }, out error);

    /// <inheritdoc />
    public bool TryDraw(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryDraw(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryPass(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!IsInGame(userKey, out error))
            {
                return false;
            }

            var started = Game.TryNextRound(round, out var gameError);
            error = gameError ?? (started ? null : "The next round cannot start yet.");
            return started;
        }
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) =>
        _game = new DominoesGame(seats.Count, DominoesOptions.RulesFrom(Options), shuffle);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(shuffle);

    /// <inheritdoc />
    protected override IEnumerable<int> AwaitedSeats =>
        _game is { Phase: DominoesPhase.Playing } game ? [GameSeats[game.Turn]] : [];

    /// <inheritdoc />
    protected override bool CanSkipAwaited => _game?.Phase == DominoesPhase.Playing;

    /// <inheritdoc />
    protected override bool TrySkipAwaited(int seat, [NotNullWhen(false)] out string? error)
    {
        var skipped = Game.TrySkip(PlayerOf(seat), out error);
        error ??= skipped ? null : "This player cannot be skipped.";
        return skipped;
    }

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var players = Enumerable.Range(0, game.Players).ToArray();
        DominoesTrainState Train(int? player, IReadOnlyList<LaidTile> tiles) => new(
            player is { } p ? SeatOf(p) : null, Text(tiles), tiles is [.., var last] ? last.Right : game.Engine ?? 0,
            player is { } owner && game.HasMarker(owner));
        return new DominoesState(
            game.Rules.Style,
            game.HasTeams,
            game.Rules.Target,
            game.TotalRounds,
            game.RoundNumber,
            game.Phase,
            SeatOf(game.Turn),
            game.HasDrawn,
            Text(game.Line),
            game.Engine,
            game.Rules.IsLine ? [] : [.. players.Select(p => Train(p, game.Train(p))), Train(null, game.MexicanTrain)],
            game.BoneyardCount,
            [.. players.Select(p => new DominoesPlayerState(SeatOf(p), game.SideOf(p), game.Hand(p).Count))],
            [.. Enumerable.Range(0, game.Sides).Select(s => new DominoesSideState(s,
                [.. players.Where(p => game.SideOf(p) == s).Select(SeatOf)], game.Scores[s]))],
            game.LastAction is { } action
                ? new DominoesActionState(SeatOf(action.Player), action.Kind, action.Tile?.ToString(), TargetSeat(game, action.Target), action.Points)
                : null,
            game.Actions,
            game.LastResult is { } result
                ? new DominoesResultState(result.Round, result.Domino is { } d ? SeatOf(d) : null, result.Blocked,
                    [.. players.Select(p => new DominoesResultPlayerState(SeatOf(p), Text(result.Hands[p]), result.Pips[p]))],
                    result.Points, result.Scores)
                : null,
            [.. game.Winners]);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new DominoesPrivateState([.. seats.Select(PlayerOf).Select(player => new DominoesHand(
            SeatOf(player),
            Text(game.Hand(player)),
            [.. game.Moves(player).Select(m => new DominoesMoveState(m.Tile.ToString(), TargetSeat(game, m.Target)))],
            game.CanDraw(player),
            game.CanPass(player)))]);
    }

    /// <summary>A play's target for the pages: a seat for a Mexican Train player's train; otherwise as in the game.</summary>
    private int TargetSeat(DominoesGame game, int target) =>
        game.Rules.IsLine || target == DominoesGame.MexicanTarget ? target : SeatOf(target);

    private int PlayerOf(int seat) => GameSeats.ToList().IndexOf(seat);

    private int SeatOf(int player) => GameSeats[player];

    private static string[] Text<T>(IEnumerable<T> tiles) where T : notnull => [.. tiles.Select(t => t.ToString()!)];

    private static bool TryParse(string? text, [NotNullWhen(true)] out Tile? tile)
    {
        try
        {
            tile = Tile.Parse(text ?? "");
            return true;
        }
        catch (FormatException)
        {
            tile = null;
            return false;
        }
    }

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played.
    /// </summary>
    private bool WithPlayer(string userKey, int seat, Func<DominoesGame, int, (bool, string?)> action, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            (var done, error) = action(Game, PlayerOf(seat));
            if (!done)
            {
                error ??= "Something went wrong.";
            }

            return done;
        }
    }
}
