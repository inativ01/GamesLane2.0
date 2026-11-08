using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.Yaniv;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player as everyone sees them: their seat, how many cards they hold, their points, and whether they are out.
/// </summary>
public record YanivPlayerState(int Seat, int CardCount, int Score, bool IsOut);

/// <summary>
/// The last turn: the seat, the cards it discarded (short text, a straight from lowest to highest), whether it took the
/// open pile's top card (<see cref="Taken"/>) or a closed card (not shown), and whether the turn was skipped.
/// </summary>
public record YanivTurnState(int Seat, IReadOnlyList<string> Cards, bool TookOpen, string? Taken, bool Skipped);

/// <summary>
/// A player at the end of a round: their cards, hand score, the points they gained, whether the bonus took 50 points off,
/// their points after the round, and whether they are out.
/// </summary>
public record YanivRoundPlayerState(int Seat, IReadOnlyList<string> Cards, int HandScore, int Points, bool Bonus, int Score, bool IsOut);

/// <summary>
/// How the last round ended: the seat that called Yaniv, the round's winner, whether the caller was Assaf, and the players.
/// </summary>
public record YanivResultState(int CallerSeat, int WinnerSeat, bool Assaf, IReadOnlyList<YanivRoundPlayerState> Players);

/// <summary>
/// Public Yaniv state. <see cref="Turn"/>, <see cref="Caller"/>, <see cref="LastTurns"/> and <see cref="Winner"/> are
/// seats; <see cref="Players"/> are in seat order. <see cref="OpenTop"/> is the card that can be taken, or null.
/// <see cref="Actions"/> counts the actions, so a page can tell a new one.
/// </summary>
public record YanivState(
    YanivEndMode End,
    int Target,
    bool Bonus,
    int RoundNumber,
    YanivPhase Phase,
    int Turn,
    int? Caller,
    IReadOnlyList<int> LastTurns,
    string? OpenTop,
    int OpenPile,
    int ClosedPile,
    IReadOnlyList<YanivPlayerState> Players,
    YanivTurnState? LastTurn,
    int Actions,
    YanivResultState? LastResult,
    int? Winner);

/// <summary>
/// A seat's private Yaniv information: its cards (jokers first, then from ace to king), its hand score, and whether it can
/// call Yaniv now.
/// </summary>
public record YanivHand(int Seat, IReadOnlyList<string> Cards, int HandScore, bool CanCallYaniv);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record YanivPrivateState(IReadOnlyList<YanivHand> Hands);

/// <summary>
/// A Yaniv game for 2 to 4 seats, with the options' end (one round, or a score target) and bonus. The players are the
/// game's seats in seat order. The players can skip the turn of an absent player; in a public game, the turn of a player
/// who is Away or has left is skipped by itself, and the game goes on when a player leaves.
/// </summary>
public class YanivSession(string id, Action<Card[]>? shuffle = null) : GameSession(id), IYanivSession
{
    private YanivGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.Yaniv;

    /// <inheritdoc />
    protected override int MinSeats => YanivRules.MinPlayers;

    /// <inheritdoc />
    protected override int MaxSeats => YanivRules.MaxPlayers;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    /// <inheritdoc />
    protected override bool ContinuesWithAbsentPlayers => true;

    private YanivGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryDiscard(string userKey, int seat, IReadOnlyList<string>? cards, bool takeOpen, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => TryParse(cards, out var parsed)
            ? (game.TryDiscard(player, parsed, takeOpen, out var e), e)
            : (false, "Unknown card."), out error);

    /// <inheritdoc />
    public bool TryCallYaniv(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryCallYaniv(player, out var e), e), out error);

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
            SkipAwayPlayers();
            error = gameError ?? (started ? null : "The next round cannot start yet.");
            return started;
        }
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) =>
        _game = new YanivGame(seats.Count, YanivOptions.RulesFrom(Options), shuffle);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(shuffle);

    /// <inheritdoc />
    protected override IEnumerable<int> AwaitedSeats =>
        _game is { Phase: YanivPhase.Playing } game ? [GameSeats[game.Turn]] : [];

    /// <inheritdoc />
    protected override bool CanSkipAwaited => _game?.Phase == YanivPhase.Playing;

    /// <inheritdoc />
    protected override bool TrySkipAwaited(int seat, [NotNullWhen(false)] out string? error)
    {
        var skipped = Game.TrySkip(PlayerOf(seat), out error);
        SkipAwayPlayers();
        error ??= skipped ? null : "This player cannot be skipped.";
        return skipped;
    }

    /// <inheritdoc />
    protected override void OnSeatsChanged() => SkipAwayPlayers();

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var seats = GameSeats;
        return new YanivState(
            game.Rules.End,
            game.Rules.Target,
            game.Rules.Bonus,
            game.RoundNumber,
            game.Phase,
            seats[game.Turn],
            game.Caller is { } caller ? seats[caller] : null,
            [.. game.LastTurns.Select(p => seats[p])],
            game.OpenTop?.ToString(),
            game.OpenPileCount,
            game.ClosedPileCount,
            [.. Enumerable.Range(0, game.Players).Select(p => new YanivPlayerState(
                seats[p], game.Hand(p).Count, game.Scores[p], game.IsOut(p)))],
            game.LastTurn is { } turn
                ? new YanivTurnState(seats[turn.Player], Text(turn.Cards), turn.TookOpen, turn.Taken.Length > 0 ? turn.Taken[0].ToString() : null, turn.Skipped)
                : null,
            game.Actions,
            game.LastResult is { } result
                ? new YanivResultState(seats[result.Caller], seats[result.Winner], result.Assaf, [.. result.Players.Select(p =>
                    new YanivRoundPlayerState(seats[p.Player], Text(p.Cards), p.HandScore, p.Points, p.Bonus, p.Score, p.IsOut))])
                : null,
            game.Winner is { } winner ? seats[winner] : null);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new YanivPrivateState([.. seats.Select(seat => PlayerOf(seat)).Select(player => new YanivHand(
            GameSeats[player],
            Text(YanivCards.Sorted(game.Hand(player))),
            game.HandScore(player),
            game.CanCallYaniv(player)))]);
    }

    /// <summary>
    /// In a public game, skips the turns of players who are Away or have left, as long as a player in the game is here.
    /// </summary>
    private void SkipAwayPlayers()
    {
        if (!IsPublic || !IsPlaying || _game is not { } game)
        {
            return;
        }

        var anyoneHere = Enumerable.Range(0, game.Players).Any(p => !game.IsOut(p) && !IsAbsent(GameSeats[p]));
        for (var i = 0; anyoneHere && i < game.Players * 2 && game.Phase == YanivPhase.Playing && IsAwayOrVacant(GameSeats[game.Turn]); i++)
        {
            game.TrySkip(game.Turn, out _);
        }
    }

    private int PlayerOf(int seat) => GameSeats.ToList().IndexOf(seat);

    private static string[] Text(IEnumerable<Card> cards) => [.. cards.Select(c => c.ToString())];

    /// <summary>
    /// Reads cards sent by a page, such as "10♥" or "JOKER".
    /// </summary>
    private static bool TryParse(IReadOnlyList<string>? cards, out Card[] parsed)
    {
        try
        {
            parsed = [.. (cards ?? []).Select(text => string.IsNullOrEmpty(text) ? throw new FormatException() : Card.Parse(text))];
            return parsed.All(c => c.Suit != Suit.None);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            parsed = [];
            return false;
        }
    }

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played; then, in a public game, skips the players who are away.
    /// </summary>
    private bool WithPlayer(string userKey, int seat, Func<YanivGame, int, (bool, string?)> action, out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            (var done, error) = action(Game, PlayerOf(seat));
            SkipAwayPlayers();
            return done;
        }
    }
}
