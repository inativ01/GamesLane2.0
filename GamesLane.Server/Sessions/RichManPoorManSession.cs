using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.RichManPoorMan;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Sessions;

/// <summary>
/// Cards thrown together, as everyone sees them: the seat that threw them, and the cards (short text such as "10♥").
/// </summary>
public record RichManPoorManThrowState(int Seat, IReadOnlyList<string> Cards);

/// <summary>
/// A player as everyone sees them: their seat, their place in this game (1 for the rich man), how many cards they hold,
/// whether their last action in this round was to pass, their finishing place (null until their hand is empty), whether
/// they are still playing (false once they left, until someone takes their seat), and in the exchange, how many cards
/// they give and whether they have given them.
/// </summary>
public record RichManPoorManPlayerState(
    int Seat,
    int Place,
    int CardCount,
    bool Passed,
    int? Finished,
    bool IsActive,
    int ExchangeCount,
    bool HasGiven);

/// <summary>
/// A user asking to join at the next exchange: their player ID in the session, name and icon.
/// </summary>
public record RichManPoorManJoinRequest(string PlayerId, string Name, string Icon);

/// <summary>
/// Public rich man, poor man state. <see cref="Players"/> are in place order, from #1 (the rich man) to the last (the
/// poor man). <see cref="Turn"/>, <see cref="LastRoundWinner"/> and <see cref="Ranking"/> are seats.
/// <see cref="CurrentThrow"/> is the throw to beat in this round (null when its starter is to throw);
/// <see cref="LastThrow"/> is kept after its round ends. <see cref="Throws"/> counts all throws, so a page can tell a
/// new one. <see cref="JoinRequests"/> are the users asking to join, during the exchange.
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
    IReadOnlyList<int> Ranking,
    IReadOnlyList<RichManPoorManJoinRequest> JoinRequests);

/// <summary>
/// A seat's private rich man, poor man information: its cards from low to high, and in the exchange, its partner's seat,
/// the cards it must give (for the poorer player of a pair), the cards it has given, and once exchanged, those it
/// received. Cards are short text such as "10♥".
/// </summary>
public record RichManPoorManHand(
    int Seat,
    IReadOnlyList<string> Cards,
    int? PartnerSeat,
    IReadOnlyList<string>? RequiredGift,
    IReadOnlyList<string>? Gift,
    IReadOnlyList<string>? Received);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record RichManPoorManPrivateState(IReadOnlyList<RichManPoorManHand> Hands);

/// <summary>
/// Rich man, poor man for 6 to 12 seats. The game's players are seat numbers: the first game seats them in seat order,
/// and each later game gives them their places by the last game's ranking. It never ends by itself: after each game, a
/// player starts the next one. An absent player (Away, or whose seat is empty) passes automatically, and anyone can take
/// their seat to continue in their place. When the next game starts, absent players are dropped (with fewer than 6 left,
/// everyone goes back to the session lobby); during its exchange, users ask to join and a player accepts them at a place.
/// </summary>
public class RichManPoorManSession(string id, Action<Card[]>? shuffle = null, Random? random = null)
    : GameSession(id), IRichManPoorManSession
{
    private readonly List<(string UserKey, PlayerProfile Profile)> _requests = [];
    private RichManPoorManGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.RichManPoorMan;

    /// <inheritdoc />
    protected override int MinSeats => RichManPoorManRules.MinPlayers;

    /// <inheritdoc />
    protected override int MaxSeats => RichManPoorManRules.MaxPlayers;

    /// <inheritdoc />
    protected override bool IsGameOver => false;

    /// <inheritdoc />
    protected override bool ContinuesWithAbsentPlayers => true;

    private RichManPoorManGame Game => _game ?? throw new InvalidOperationException("No game has started.");

    /// <inheritdoc />
    public bool TryThrow(string userKey, int seat, IReadOnlyList<string>? cards, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => TryParse(cards, out var parsed, out var e)
            ? (game.TryThrow(player, parsed, out e), e)
            : (false, e), out error);

    /// <inheritdoc />
    public bool TryPass(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryPass(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryGive(string userKey, int seat, IReadOnlyList<string>? cards, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            var given = WithPlayer(userKey, seat, (game, player) => TryParse(cards, out var parsed, out var e)
                ? (game.TryGive(player, parsed, out e), e)
                : (false, e), out error);
            if (given && Game.Phase != RichManPoorManPhase.Exchanging)
            {
                _requests.Clear();
            }

            return given;
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

            var game = Game;
            if (round == game.GameNumber && game.Phase == RichManPoorManPhase.GameOver)
            {
                var players = game.NextOrder.ToArray();
                var order = players.Where(seat => !IsAbsent(seat)).ToArray();
                if (order.Length < RichManPoorManRules.MinPlayers)
                {
                    EndGameInLobby();
                    return true;
                }

                if (!game.TryNextGame(round, order, out var nextError))
                {
                    error = nextError ?? "The next game cannot start yet.";
                    return false;
                }

                foreach (var dropped in players.Except(order))
                {
                    VacateSeat(dropped);
                }

                _requests.Clear();
                SetGameSeats(order);
                return true;
            }

            var started = game.TryNextGame(round, game.Order, out var gameError);
            error = gameError ?? (started ? null : "The next game cannot start yet.");
            return started;
        }
    }

    /// <inheritdoc />
    public bool TryRequestSeat(string userKey, PlayerProfile profile, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            error = !IsExchanging ? "You can ask to join during the card exchange, between games."
                : _requests.Any(r => r.UserKey == userKey) ? "You have already asked to join."
                : Game.Order.Count >= RichManPoorManRules.MaxPlayers ? "The table is full."
                : null;
            if (error is not null)
            {
                return false;
            }

            _requests.Add((userKey, profile));
            PlayerIdFor(userKey);
            return true;
        }
    }

    /// <inheritdoc />
    public bool TryCancelSeatRequest(string userKey, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            error = _requests.RemoveAll(r => r.UserKey == userKey) > 0 ? null : "You have not asked to join.";
            return error is null;
        }
    }

    /// <inheritdoc />
    public bool TryAcceptSeat(string userKey, string playerId, int place, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryFindRequest(userKey, playerId, out var index, out error))
            {
                return false;
            }

            var game = Game;
            if (place < 1 || place > game.Order.Count + 1)
            {
                error = "There is no such place.";
                return false;
            }

            var (requester, profile) = _requests[index];
            if (!IsConnected(requester))
            {
                _requests.RemoveAt(index);
                error = "They have left the game page.";
                return false;
            }

            if (game.Order.Count >= RichManPoorManRules.MaxPlayers || SeatUser(requester, profile) is not { } seat)
            {
                error = "The table is full.";
                return false;
            }

            _requests.RemoveAt(index);
            game.TryAddPlayer(seat, place - 1, out _);
            SetGameSeats(game.Order);
            return true;
        }
    }

    /// <inheritdoc />
    public bool TryDeclineSeat(string userKey, string playerId, [NotNullWhen(false)] out string? error)
    {
        lock (Sync)
        {
            if (!TryFindRequest(userKey, playerId, out var index, out error))
            {
                return false;
            }

            _requests.RemoveAt(index);
            return true;
        }
    }

    /// <inheritdoc />
    protected override string? SeatingDuringGameError(int seat) =>
        _game?.Phase == RichManPoorManPhase.Exchanging ? "Ask to join: a player will choose your place." : null;

    /// <summary>
    /// Absent players pass automatically, and play again when they come back or someone takes their seat. During the
    /// exchange, they are dropped (their seats are freed) and the cards are dealt again; with fewer than 6 players left,
    /// everyone goes back to the session lobby.
    /// </summary>
    protected override void OnSeatsChanged()
    {
        if (_game is not { } game)
        {
            return;
        }

        if (game.Phase != RichManPoorManPhase.Exchanging)
        {
            foreach (var seat in game.Order)
            {
                game.SetActive(seat, !IsAbsent(seat));
            }

            return;
        }

        var left = game.Order.Where(IsAbsent).ToArray();
        if (!IsPlaying || left.Length == 0)
        {
            return;
        }

        if (game.Order.Count - left.Length < RichManPoorManRules.MinPlayers)
        {
            _requests.Clear();
            EndGameInLobby();
            return;
        }

        foreach (var seat in left)
        {
            game.TryRemovePlayer(seat, out _);
            VacateSeat(seat);
        }

        SetGameSeats(game.Order);
    }

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats)
    {
        _requests.Clear();
        _game = new RichManPoorManGame(seats, shuffle, random);
    }

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var ranking = game.Ranking.ToList();
        return new RichManPoorManState(
            game.GameNumber,
            game.Phase,
            [.. game.Order.Select((seat, place) => new RichManPoorManPlayerState(
                seat,
                place + 1,
                game.Hand(seat).Count,
                game.HasPassed(seat),
                ranking.Contains(seat) ? ranking.IndexOf(seat) + 1 : null,
                game.IsActive(seat),
                game.ExchangeCount(seat),
                game.Gift(seat) is not null))],
            game.Turn,
            ThrowState(game.CurrentThrow),
            ThrowState(game.LastThrow),
            game.Throws,
            game.LastRoundWinner,
            ranking,
            IsExchanging
                ? [.. _requests.Where(r => IsConnected(r.UserKey))
                    .Select(r => new RichManPoorManJoinRequest(PlayerIdFor(r.UserKey), r.Profile.Name, r.Profile.Icon))]
                : []);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new RichManPoorManPrivateState([.. seats.Select(seat => new RichManPoorManHand(
            seat,
            Text(game.Hand(seat))!,
            game.ExchangePartner(seat),
            Text(game.RequiredGift(seat)),
            Text(game.Gift(seat)),
            Text(game.Received(seat))))]);
    }

    private bool IsExchanging => IsPlaying && _game?.Phase == RichManPoorManPhase.Exchanging;

    /// <summary>
    /// Checks that a player (<paramref name="userKey"/>) can answer the request of <paramref name="playerId"/>, and finds it.
    /// </summary>
    private bool TryFindRequest(string userKey, string playerId, out int index, [NotNullWhen(false)] out string? error)
    {
        index = -1;
        if (!IsInGame(userKey, out error))
        {
            return false;
        }

        var requester = UserKeyOf(playerId);
        index = _requests.FindIndex(r => r.UserKey == requester);
        error = !IsExchanging ? "Players can join only during the card exchange."
            : index < 0 ? "That request is no longer open."
            : null;
        return error is null;
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

            (var done, error) = action(Game, seat);
            return done;
        }
    }

    private static RichManPoorManThrowState? ThrowState(RichManPoorManThrow? thrown) =>
        thrown is null ? null : new(thrown.Player, Text(thrown.Cards)!);

    private static string[]? Text(IEnumerable<Card>? cards) => cards?.Select(c => c.ToString()).ToArray();

    /// <summary>
    /// Reads cards sent by a page, such as "10♥".
    /// </summary>
    private static bool TryParse(IReadOnlyList<string>? cards, out Card[] parsed, out string? error)
    {
        try
        {
            parsed = [.. (cards ?? []).Select(text => string.IsNullOrEmpty(text) ? throw new FormatException() : Card.Parse(text))];
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            parsed = [];
            error = "Unknown card.";
            return false;
        }
    }
}
