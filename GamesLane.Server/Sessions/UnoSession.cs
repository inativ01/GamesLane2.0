using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Uno;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player as everyone sees them: their seat, how many cards they hold, their points (in a game to a score target), the
/// cards they were last dealt (in a pyramid), their finishing place (in a pyramid, null until they finish), whether they
/// called UNO, and whether they can be caught for not calling it.
/// </summary>
public record UnoPlayerState(int Seat, int CardCount, int Score, int HandSize, int? Finished, bool SaidUno, bool CanBeCaught);

/// <summary>
/// Something that happened in the last action: the seat it is about, what, and the card (short text), count, chosen colour
/// or other seat involved (see <see cref="UnoEventKind"/>).
/// </summary>
public record UnoEventState(int Seat, UnoEventKind Kind, string? Card, int Count, UnoColor? Color, int? BySeat);

/// <summary>
/// How the last hand ended: the winning seat and the points of the cards left in the other hands.
/// </summary>
public record UnoResultState(int Seat, int Points);

/// <summary>
/// Public Uno state. <see cref="Turn"/>, <see cref="Ranking"/> and <see cref="Winner"/> are seats; <see cref="Players"/>
/// are in seat order. <see cref="Direction"/> is 1 in seat order, -1 after a Reverse. <see cref="HasDrawn"/> tells that
/// the seat to play has drawn a card it may play or keep. <see cref="Actions"/> counts the actions, so a page can tell a
/// new one.
/// </summary>
public record UnoState(
    UnoEndMode End,
    int Target,
    bool Stacking,
    bool DrawUntilPlayable,
    int HandNumber,
    UnoPhase Phase,
    int Turn,
    int Direction,
    string TopCard,
    UnoColor Color,
    int DrawPile,
    int PendingDraw,
    bool HasDrawn,
    IReadOnlyList<UnoPlayerState> Players,
    IReadOnlyList<UnoEventState> Events,
    int Actions,
    UnoResultState? LastResult,
    IReadOnlyList<int> Ranking,
    int? Winner);

/// <summary>
/// A seat's private Uno information: its cards, sorted by colour and value, and those it can play now.
/// </summary>
public record UnoHand(int Seat, IReadOnlyList<string> Cards, IReadOnlyList<string> Playable);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record UnoPrivateState(IReadOnlyList<UnoHand> Hands);

/// <summary>
/// An Uno game for 2 to 10 seats, with the options' end (one game, a score target or a pyramid), stacking and drawing. The
/// players are the game's seats in seat order. A game that waits for an absent player is paused.
/// </summary>
public class UnoSession(string id, Action<UnoCard[]>? shuffle = null) : GameSession(id), IUnoSession
{
    private UnoGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.Uno;

    /// <inheritdoc />
    protected override int MinSeats => UnoRules.MinPlayers;

    /// <inheritdoc />
    protected override int MaxSeats => UnoRules.MaxPlayers;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    private UnoGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryPlay(string userKey, int seat, string? card, UnoColor? color, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => UnoCard.TryParse(card, out var parsed)
            ? (game.TryPlay(player, parsed, color, out var e), e)
            : (false, "Unknown card."), out error);

    /// <inheritdoc />
    public bool TryDraw(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryDraw(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryPass(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryCallUno(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryCallUno(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryCatchUno(string userKey, int seat, int targetSeat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => GameSeats.Contains(targetSeat)
            ? (game.TryCatch(player, PlayerOf(targetSeat), out var e), e)
            : (false, "There is no such player."), out error);

    /// <inheritdoc />
    public bool TryNextRound(string userKey, int round, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!IsInGame(userKey, out error))
            {
                return false;
            }

            var started = Game.TryNextHand(round, out var gameError);
            error = gameError ?? (started ? null : "The next hand cannot start yet.");
            return started;
        }
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) =>
        _game = new UnoGame(seats.Count, UnoOptions.RulesFrom(Options), shuffle);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(shuffle);

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var seats = GameSeats;
        var ranking = game.Ranking.ToList();
        return new UnoState(
            game.Rules.End,
            game.Rules.Target,
            game.Rules.Stacking,
            game.Rules.DrawUntilPlayable,
            game.HandNumber,
            game.Phase,
            seats[game.Turn],
            game.Direction,
            game.TopCard.ToString(),
            game.Color,
            game.DrawPileCount,
            game.PendingDraw,
            game.DrawnCard is not null,
            [.. Enumerable.Range(0, game.Players).Select(p => new UnoPlayerState(
                seats[p],
                game.Hand(p).Count,
                game.Scores[p],
                game.HandSize(p),
                game.Rules.End == UnoEndMode.Pyramid && ranking.Contains(p) ? ranking.IndexOf(p) + 1 : null,
                game.SaidUno(p),
                game.UnoPending == p))],
            [.. game.Events.Select(e => new UnoEventState(
                seats[e.Player], e.Kind, e.Card?.ToString(), e.Count, e.Color, e.By is { } by ? seats[by] : null))],
            game.Actions,
            game.LastResult is { } result ? new UnoResultState(seats[result.Winner], result.Points) : null,
            [.. ranking.Select(p => seats[p])],
            game.Winner is { } winner ? seats[winner] : null);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new UnoPrivateState([.. seats.Select(seat => new UnoHand(
            seat,
            Text(game.Hand(PlayerOf(seat)).OrderBy(c => c.Color).ThenBy(c => c.Value)),
            Text(game.Playable(PlayerOf(seat)))))]);
    }

    private int PlayerOf(int seat) => GameSeats.ToList().IndexOf(seat);

    private static string[] Text(IEnumerable<UnoCard> cards) => [.. cards.Select(c => c.ToString())];

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played.
    /// </summary>
    private bool WithPlayer(string userKey, int seat, Func<UnoGame, int, (bool, string?)> action, out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            (var done, error) = action(Game, PlayerOf(seat));
            return done;
        }
    }
}
