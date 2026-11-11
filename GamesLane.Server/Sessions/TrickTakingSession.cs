using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.Tricks;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A card in a trick, as everyone sees it: the seat that played it, and the card (short text such as "10♥").
/// </summary>
public record TrickCardState(int Seat, string Card);

/// <summary>
/// A trick: its cards in the order played (the first one led), and the seat that won it once it is complete (otherwise null).
/// </summary>
public record TrickState(IReadOnlyList<TrickCardState> Cards, int? WinnerSeat);

/// <summary>
/// A trick-taking game for 4 seats (see <see cref="TrickTakingGame"/>). The players are the game's seats in seat order.
/// Each game adds its own actions and states; the trick, the cards and the hands are shared. A game that waits for an
/// absent player cannot skip them: the players wait, or end the game.
/// </summary>
public abstract class TrickTakingSession<TGame>(string id, Action<Card[]>? shuffle) : GameSession(id), ITrickTakingSession
    where TGame : TrickTakingGame
{
    private TGame? _game;

    /// <inheritdoc />
    protected override int MinSeats => TrickTakingGame.PlayerCount;

    /// <inheritdoc />
    protected override int MaxSeats => TrickTakingGame.PlayerCount;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    /// <summary>The game being played; throws before the first game.</summary>
    protected TGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <summary>The shuffle to give the game (null for a random one).</summary>
    protected Action<Card[]>? Shuffle => shuffle;

    /// <inheritdoc />
    public bool TryPlayCard(string userKey, int seat, string? card, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => TryParse(card is null ? null : [card], out var parsed) && parsed.Length == 1
            ? (game.TryPlay(player, parsed[0], out var e), e)
            : (false, "Unknown card."), out error);

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

    /// <summary>A new game with the chosen options.</summary>
    protected abstract TGame CreateGame(IReadOnlyDictionary<string, string> options);

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) => _game = CreateGame(Options);

    /// <inheritdoc />
    protected override void OnRestored() => _game?.Reattach(shuffle);

    /// <inheritdoc />
    protected override IEnumerable<int> AwaitedSeats => _game is { } game ? game.AwaitedPlayers.Select(SeatOf) : [];

    /// <summary>The trick being played, as everyone sees it.</summary>
    protected TrickState CurrentTrick() => new([.. Game.Trick.Select(c => new TrickCardState(SeatOf(c.Player), c.Card.ToString()))], null);

    /// <summary>The last trick completed in this hand, as everyone sees it, or null.</summary>
    protected TrickState? LastTrick() => Game.LastTrick is { } trick
        ? new TrickState([.. trick.Cards.Select(c => new TrickCardState(SeatOf(c.Player), c.Card.ToString()))], SeatOf(trick.Winner))
        : null;

    /// <summary>The game's player at <paramref name="seat"/>.</summary>
    protected int PlayerOf(int seat) => GameSeats.ToList().IndexOf(seat);

    /// <summary>The seat of the game's <paramref name="player"/>.</summary>
    protected int SeatOf(int player) => GameSeats[player];

    /// <summary>Cards as short text, e.g. "10♥".</summary>
    protected static string[] Text(IEnumerable<Card> cards) => [.. cards.Select(c => c.ToString())];

    /// <summary>
    /// Reads cards sent by a page, such as "10♥" or "AS". Jokers and the suitless jack are not cards of these games.
    /// </summary>
    protected static bool TryParse(IReadOnlyList<string>? cards, out Card[] parsed)
    {
        try
        {
            parsed = [.. (cards ?? []).Select(text => string.IsNullOrEmpty(text) ? throw new FormatException() : Card.Parse(text))];
            return parsed.All(c => c.Suit is not (Suit.None or Suit.Joker));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            parsed = [];
            return false;
        }
    }

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played.
    /// </summary>
    protected bool WithPlayer(string userKey, int seat, Func<TGame, int, (bool, string?)> action, [NotNullWhen(false)] out string? error)
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
