using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.FinalCard;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player as everyone sees them: their seat, how many cards they hold, their points (in a game to a score target), the
/// cards they were last dealt (in a pyramid), their finishing place (in a pyramid, null until they finish), and whether
/// they are protected: they called Final Card and hold one card.
/// </summary>
public record FinalCardPlayerState(int Seat, int CardCount, int Score, int HandSize, int? Finished, bool Protected);

/// <summary>
/// Something that happened in the last action: the seat it is about, what, and the card (short text), count, chosen colour
/// or other seat involved (see <see cref="FinalCardEventKind"/>).
/// </summary>
public record FinalCardEventState(int Seat, FinalCardEventKind Kind, string? Card, int Count, FinalCardColor? Color, int? BySeat);

/// <summary>
/// How the last hand ended: the winning seat and the points of the cards left in the other hands.
/// </summary>
public record FinalCardResultState(int Seat, int Points);

/// <summary>
/// Public Final Card state. <see cref="Turn"/>, <see cref="Ranking"/> and <see cref="Winner"/> are seats; <see cref="Players"/>
/// are in seat order. <see cref="Direction"/> is 1 in seat order, -1 after a Reverse. <see cref="HasDrawn"/> tells that
/// the seat to play has drawn a card it may play or keep, and <see cref="CanChallenge"/> that it can challenge the Wild
/// Draw Four played on it. <see cref="Actions"/> counts the actions, so a page can tell a new one.
/// </summary>
public record FinalCardState(
    FinalCardEndMode End,
    int Target,
    FinalCardStacking Stacking,
    bool DrawUntilPlayable,
    FinalCardWildDrawFour WildDrawFour,
    int HandNumber,
    FinalCardPhase Phase,
    int Turn,
    int Direction,
    string TopCard,
    FinalCardColor Color,
    int DrawPile,
    int PendingDraw,
    bool HasDrawn,
    bool CanChallenge,
    IReadOnlyList<FinalCardPlayerState> Players,
    IReadOnlyList<FinalCardEventState> Events,
    int Actions,
    FinalCardResultState? LastResult,
    IReadOnlyList<int> Ranking,
    int? Winner);

/// <summary>
/// A seat's private Final Card information: its cards, sorted by colour and value, and those it can play now.
/// </summary>
public record FinalCardHand(int Seat, IReadOnlyList<string> Cards, IReadOnlyList<string> Playable);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record FinalCardPrivateState(IReadOnlyList<FinalCardHand> Hands);

/// <summary>
/// A Final Card game for 2 to 10 seats, with the options' end (one game, a score target or a pyramid), stacking and drawing. The
/// players are the game's seats in seat order. A game that waits for an absent player is paused.
/// </summary>
public class FinalCardSession(string id, Action<FinalCardCard[]>? shuffle = null) : GameSession(id), IFinalCardSession
{
    private FinalCardGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.FinalCard;

    /// <inheritdoc />
    protected override int MinSeats => FinalCardRules.MinPlayers;

    /// <inheritdoc />
    protected override int MaxSeats => FinalCardRules.MaxPlayers;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    private FinalCardGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryPlay(string userKey, int seat, string? card, FinalCardColor? color, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => FinalCardCard.TryParse(card, out var parsed)
            ? (game.TryPlay(player, parsed, color, out var e), e)
            : (false, "Unknown card."), out error);

    /// <inheritdoc />
    public bool TryDraw(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryDraw(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryPass(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryChallenge(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryChallenge(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryCallFinalCard(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryCallFinalCard(player, out var e), e), out error);

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
        _game = new FinalCardGame(seats.Count, FinalCardOptions.RulesFrom(Options), shuffle);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(shuffle);

    /// <inheritdoc />
    protected override IEnumerable<int> AwaitedSeats =>
        _game is { Phase: FinalCardPhase.Playing } game ? [GameSeats[game.Turn]] : [];

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var seats = GameSeats;
        var ranking = game.Ranking.ToList();
        return new FinalCardState(
            game.Rules.End,
            game.Rules.Target,
            game.Rules.Stacking,
            game.Rules.DrawUntilPlayable,
            game.Rules.WildDrawFour,
            game.HandNumber,
            game.Phase,
            seats[game.Turn],
            game.Direction,
            game.TopCard.ToString(),
            game.Color,
            game.DrawPileCount,
            game.PendingDraw,
            game.DrawnCard is not null,
            game.CanChallenge,
            [.. Enumerable.Range(0, game.Players).Select(p => new FinalCardPlayerState(
                seats[p],
                game.Hand(p).Count,
                game.Scores[p],
                game.HandSize(p),
                game.Rules.End == FinalCardEndMode.Pyramid && ranking.Contains(p) ? ranking.IndexOf(p) + 1 : null,
                game.CalledFinalCard(p) && game.Hand(p).Count == 1))],
            [.. game.Events.Select(e => new FinalCardEventState(
                seats[e.Player], e.Kind, e.Card?.ToString(), e.Count, e.Color, e.By is { } by ? seats[by] : null))],
            game.Actions,
            game.LastResult is { } result ? new FinalCardResultState(seats[result.Winner], result.Points) : null,
            [.. ranking.Select(p => seats[p])],
            game.Winner is { } winner ? seats[winner] : null);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new FinalCardPrivateState([.. seats.Select(seat => new FinalCardHand(
            seat,
            Text(game.Hand(PlayerOf(seat)).OrderBy(c => c.Color).ThenBy(c => c.Value)),
            Text(game.Playable(PlayerOf(seat)))))]);
    }

    private int PlayerOf(int seat) => GameSeats.ToList().IndexOf(seat);

    private static string[] Text(IEnumerable<FinalCardCard> cards) => [.. cards.Select(c => c.ToString())];

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played.
    /// </summary>
    private bool WithPlayer(string userKey, int seat, Func<FinalCardGame, int, (bool, string?)> action, out string? error)
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
