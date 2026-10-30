using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A revealed hand: its private cards and its name, and whether it was at least as strong as the hand revealed before it.
/// </summary>
public record TheGangRevealedHand(int Seat, IReadOnlyList<string> Cards, string Name, bool InOrder);

/// <summary>
/// What a specialist card shows everyone about a seat's cards, e.g. "2 face cards".
/// </summary>
public record TheGangAnnouncement(int Seat, string Text);

/// <summary>
/// Public state of The Gang. <see cref="Chips"/> has one list per stage (white, yellow, orange, red), each with one
/// entry per seat (null if the seat holds no chip in that stage, or does not play, or the stage's chips were removed by
/// Blackout). Cards are short text such as "10♥". Private cards are not included until revealed.
/// <see cref="Mode"/> is the mode this game is played in; <see cref="Cards"/> are the current heist's challenge or
/// specialist cards, <see cref="SkippedStages"/> the stages it skips, <see cref="LockedChips"/> the current stage's
/// locked chips, and <see cref="RedrawnSeat"/> the seat that got new private cards (Motion Detector, Laser Tripwires).
/// </summary>
public record TheGangState(
    TheGangMode Mode,
    IReadOnlyList<TheGangCard> Cards,
    int? RedrawnSeat,
    IReadOnlyList<int> SkippedStages,
    IReadOnlyList<int> LockedChips,
    IReadOnlyList<TheGangAnnouncement> Announcements,
    int Heist,
    int Stage,
    TheGangPhase Phase,
    int Vaults,
    int Alarms,
    IReadOnlyList<string> CommunityCards,
    IReadOnlyList<IReadOnlyList<int?>> Chips,
    IReadOnlyList<int> MiddleChips,
    IReadOnlyList<int> ReadySeats,
    IReadOnlyList<int> PlayingSeats,
    IReadOnlyList<int> RevealOrder,
    IReadOnlyList<TheGangRevealedHand> Revealed);

/// <summary>
/// A player's own cards in The Gang, and the name of their current best hand (once there are shared cards).
/// </summary>
public record TheGangHand(int Seat, IReadOnlyList<string> Cards, string? BestHand);

/// <summary>
/// Private state of The Gang, sent only to the user holding these seats.
/// </summary>
public record TheGangPrivateState(IReadOnlyList<TheGangHand> Hands);

/// <summary>
/// A session of The Gang for 3 to 6 seats. Each player's cards are private and sent only to them.
/// Players in the game are numbered in seat order.
/// </summary>
public class TheGangSession(string id, Func<IReadOnlyList<Card>>? newDeck = null, Random? random = null)
    : GameSession(id), ITheGangSession
{
    private readonly Func<IReadOnlyList<Card>> _newDeck = newDeck ?? (() => Deck.Shuffled(Random.Shared));
    private readonly Random _random = random ?? Random.Shared;
    private TheGangGame? _game;

    /// <inheritdoc />
    public override GameType Type => GameType.TheGang;

    /// <inheritdoc />
    protected override int MinSeats => TheGangGame.MinPlayers;

    /// <inheritdoc />
    protected override int MaxSeats => TheGangGame.MaxPlayers;

    /// <inheritdoc />
    protected override bool IsGameOver => _game?.IsOver ?? false;

    /// <inheritdoc />
    public bool TryTakeChip(string userKey, int seat, int chip, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryTakeChip(player, chip, out var e), e), out error);

    /// <inheritdoc />
    public bool TryReturnChip(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryReturnChip(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TrySetReady(string userKey, int seat, bool ready, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TrySetReady(player, ready, out var e), e), out error);

    /// <inheritdoc />
    public bool TryRevealNext(string userKey, int revealed, [NotNullWhen(false)] out string? error) =>
        WithGame(userKey, game => (game.TryRevealNext(revealed, out var e), e), out error);

    /// <inheritdoc />
    public bool TryNextHeist(string userKey, int heist, [NotNullWhen(false)] out string? error) =>
        WithGame(userKey, game => (game.TryNextHeist(heist, out var e), e), out error);

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) =>
        _game = new TheGangGame(seats.Count, _newDeck, TheGangRules.FromOptions(Options), _random);

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = _game!;
        var bySeat = SeatOfPlayer();
        var showsAllCards = game.Phase != TheGangPhase.Betting;
        return new TheGangState(
            game.Mode,
            game.Cards,
            game.RedrawnPlayer is { } redrawn ? bySeat[redrawn] : null,
            game.SkippedStages,
            game.LockedChips,
            Enumerable.Range(0, game.PlayerCount)
                .Select(player => game.Announcement(player) is { } text ? new TheGangAnnouncement(bySeat[player], text) : null)
                .OfType<TheGangAnnouncement>()
                .ToArray(),
            game.Heist,
            game.Stage,
            game.Phase,
            game.Vaults,
            game.Alarms,
            Text(game.VisibleCommunityCards),
            Enumerable.Range(0, TheGangGame.StageCount)
                .Select(stage => (IReadOnlyList<int?>)Enumerable.Range(0, MaxSeats)
                    .Select(seat => PlayerAt(seat) is { } player && !game.AreChipsDiscarded(stage) ? game.Chip(stage, player) : null)
                    .ToArray())
                .ToArray(),
            game.MiddleChips,
            Enumerable.Range(0, game.PlayerCount).Where(game.IsReady).Select(p => bySeat[p]).ToArray(),
            bySeat,
            game.RevealOrder.Select(p => bySeat[p]).ToArray(),
            showsAllCards
                ? game.RevealOrder.Take(game.Revealed).Select((player, i) => new TheGangRevealedHand(
                    bySeat[player], Text(game.HoleCards(player)), game.Hand(player).Name, game.RevealedInOrder[i])).ToArray()
                : []);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = _game!;
        var cards = game.VisibleCommunityCards;
        return new TheGangPrivateState(seats.Select(seat =>
        {
            var player = PlayerAt(seat)!.Value;
            var holeCards = game.HoleCards(player);
            var best = cards.Count >= 3 ? HandEvaluator.Best([.. holeCards, .. cards]).Name : null;
            return new TheGangHand(seat, Text(holeCards), best);
        }).ToArray());
    }

    /// <summary>
    /// Runs a player's action, after checking that <paramref name="userKey"/> holds <paramref name="seat"/> in the game.
    /// </summary>
    private bool WithPlayer(string userKey, int seat, Func<TheGangGame, int, (bool, string?)> action, out string? error)
    {
        lock (Sync)
        {
            if (!TryGetPlayingSeat(userKey, seat, out error))
            {
                return false;
            }

            (var done, error) = action(_game!, PlayerAt(seat)!.Value);
            return done;
        }
    }

    /// <summary>
    /// Runs an action any player in the game may take.
    /// </summary>
    private bool WithGame(string userKey, Func<TheGangGame, (bool, string?)> action, out string? error)
    {
        lock (Sync)
        {
            if (!IsInGame(userKey, out error))
            {
                return false;
            }

            (var done, error) = action(_game!);
            return done;
        }
    }

    /// <summary>
    /// The game's player number for <paramref name="seat"/>, or null if the seat does not play.
    /// </summary>
    private int? PlayerAt(int seat)
    {
        var player = GameSeats.ToList().IndexOf(seat);
        return player < 0 ? null : player;
    }

    /// <summary>
    /// The seat of each player, by player number.
    /// </summary>
    private int[] SeatOfPlayer() => [.. GameSeats];

    /// <summary>
    /// Cards as short text such as "10♥".
    /// </summary>
    private static string[] Text(IEnumerable<Card> cards) => cards.Select(c => c.ToString()).ToArray();
}
