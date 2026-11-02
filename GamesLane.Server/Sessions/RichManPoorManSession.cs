using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.RichManPoorMan;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Cards thrown together, as everyone sees them: the seat that threw them, their number (2 to 14, the Ace) and how many.
/// </summary>
public record RichManPoorManThrowState(int Seat, int Rank, int Count);

/// <summary>
/// A player as everyone sees them: their seat, their place in this game (1 for the rich man), how many cards they hold,
/// whether their last action in this round was to pass, their finishing place (null until their hand is empty), and in
/// the exchange, how many cards they give and whether they have given them.
/// </summary>
public record RichManPoorManPlayerState(
    int Seat,
    int Place,
    int CardCount,
    bool Passed,
    int? Finished,
    int ExchangeCount,
    bool HasGiven);

/// <summary>
/// Public rich man, poor man state. <see cref="Players"/> are in place order, from #1 (the rich man) to the last (the
/// poor man). <see cref="Turn"/>, <see cref="LastRoundWinner"/> and <see cref="Ranking"/> are seats.
/// <see cref="CurrentThrow"/> is the throw to beat in this round (null when its starter is to throw);
/// <see cref="LastThrow"/> is kept after its round ends. <see cref="Throws"/> counts all throws, so a page can tell a
/// new one.
/// </summary>
public record RichManPoorManState(
    int GameNumber,
    RichManPoorManPhase Phase,
    IReadOnlyList<RichManPoorManPlayerState> Players,
    int Turn,
    RichManPoorManThrowState? CurrentThrow,
    RichManPoorManThrowState? LastThrow,
    int Throws,
    int? LastRoundWinner,
    IReadOnlyList<int> Ranking);

/// <summary>
/// A seat's private rich man, poor man information: its cards from low to high, and in the exchange, its partner's seat,
/// the cards it must give (for the poorer player of a pair), the cards it has given, and once exchanged, those it received.
/// </summary>
public record RichManPoorManHand(
    int Seat,
    IReadOnlyList<int> Cards,
    int? PartnerSeat,
    IReadOnlyList<int>? RequiredGift,
    IReadOnlyList<int>? Gift,
    IReadOnlyList<int>? Received);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record RichManPoorManPrivateState(IReadOnlyList<RichManPoorManHand> Hands);

/// <summary>
/// Rich man, poor man for 6 to 12 seats. The players are the game's seats, in seat order; the game gives them their
/// places. It never ends by itself: after each game, a player starts the next one.
/// </summary>
public class RichManPoorManSession(string id, Action<int[]>? shuffle = null, Random? random = null)
    : GameSession(id), IRichManPoorManSession
{
    private RichManPoorManGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.RichManPoorMan;

    /// <inheritdoc />
    protected override int MinSeats => RichManPoorManRules.MinPlayers;

    /// <inheritdoc />
    protected override int MaxSeats => RichManPoorManRules.MaxPlayers;

    /// <inheritdoc />
    protected override bool IsGameOver => false;

    private RichManPoorManGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryThrow(string userKey, int seat, int rank, int count, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryThrow(player, rank, count, out var e), e), out error);

    /// <inheritdoc />
    public bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryPass(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryGive(string userKey, int seat, IReadOnlyList<int>? cards, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryGive(player, cards ?? [], out var e), e), out error);

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
        _game = new RichManPoorManGame(seats.Count, shuffle, random);

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var seats = GameSeats;
        RichManPoorManThrowState? Throw(RichManPoorManThrow? t) => t is null ? null : new(seats[t.Player], t.Rank, t.Count);
        return new RichManPoorManState(
            game.GameNumber,
            game.Phase,
            [.. game.Order.Select((player, place) => new RichManPoorManPlayerState(
                seats[player],
                place + 1,
                game.Hands[player].Count,
                game.Passed[player],
                game.Ranking.Contains(player) ? game.Ranking.ToList().IndexOf(player) + 1 : null,
                game.ExchangeCount(player),
                game.Gift(player) is not null))],
            seats[game.Turn],
            Throw(game.CurrentThrow),
            Throw(game.LastThrow),
            game.Throws,
            game.LastRoundWinner is { } winner ? seats[winner] : null,
            [.. game.Ranking.Select(player => seats[player])]);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new RichManPoorManPrivateState([.. seats.Select(seat =>
        {
            var player = GameSeats.ToList().IndexOf(seat);
            return new RichManPoorManHand(
                seat,
                [.. game.Hands[player]],
                game.ExchangePartner(player) is { } partner ? GameSeats[partner] : null,
                game.RequiredGift(player),
                game.Gift(player),
                game.Received(player));
        })]);
    }

    /// <summary>
    /// Runs an action of <paramref name="seat"/>'s player, after checking that <paramref name="userKey"/> holds it in
    /// the game being played.
    /// </summary>
    private bool WithPlayer(string userKey, int seat, Func<RichManPoorManGame, int, (bool, string?)> action, out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            (var done, error) = action(Game, GameSeats.ToList().IndexOf(seat));
            return done;
        }
    }
}
