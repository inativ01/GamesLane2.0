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
/// A card's group decision, by seat: the proposed seat and value, the seats that vote and those that agreed, whether it
/// can be decided now, and whether it was carried out.
/// </summary>
public record TheGangDecisionState(
    string Card,
    TheGangChoice Choice,
    int? Seat,
    string? Value,
    IReadOnlyList<int> Voters,
    IReadOnlyList<int> Agreed,
    bool IsOpen,
    bool IsDone);

/// <summary>
/// What a card showed everyone about a seat (Getaway Driver, Mastermind, the scans' guesses), e.g. "Pair".
/// </summary>
public record TheGangResultState(int Seat, string Card, string Text);

/// <summary>
/// A card shown to this seat by another seat (Informant).
/// </summary>
public record TheGangShownCard(int FromSeat, string Card);

/// <summary>
/// Public state of The Gang. <see cref="Chips"/> has one list per stage (white, yellow, orange, red), each with one
/// entry per seat (null if the seat holds no chip in that stage, or does not play, or the stage's chips were removed by
/// Blackout). Cards are short text such as "10♥". Private cards are not included until revealed.
/// <see cref="Mode"/> is the mode this game is played in; <see cref="Cards"/> are the current heist's challenge or
/// specialist cards, <see cref="SkippedStages"/> the stages it skips, <see cref="LockedChips"/> the current stage's
/// locked chips, and <see cref="RedrawnSeat"/> the seat that got new private cards (Motion Detector, Laser Tripwires).
/// <see cref="Decisions"/> are the cards' group decisions and <see cref="Results"/> what they showed;
/// <see cref="PendingSeat"/> must take <see cref="PendingStep"/> before the stage can end; with Informant,
/// <see cref="ShownFromSeat"/> showed a card to <see cref="ShownToSeat"/>.
/// </summary>
public record TheGangState(
    TheGangMode Mode,
    IReadOnlyList<TheGangCard> Cards,
    IReadOnlyList<string> NewCards,
    string? PreparingCard,
    int? RedrawnSeat,
    IReadOnlyList<int> SkippedStages,
    IReadOnlyList<int> LockedChips,
    IReadOnlyList<TheGangAnnouncement> Announcements,
    IReadOnlyList<TheGangDecisionState> Decisions,
    IReadOnlyList<TheGangResultState> Results,
    int? PendingSeat,
    TheGangStep PendingStep,
    int? ShownFromSeat,
    int? ShownToSeat,
    bool? Succeeded,
    int Heist,
    int Stage,
    TheGangPhase Phase,
    int Vaults,
    int Alarms,
    IReadOnlyList<string> CommunityCards,
    IReadOnlyList<IReadOnlyList<int?>> Chips,
    IReadOnlyList<int> MiddleChips,
    IReadOnlyList<int> ReadySeats,
    IReadOnlyList<int> PreparedSeats,
    IReadOnlyList<int> PlayingSeats,
    IReadOnlyList<int> RevealOrder,
    IReadOnlyList<TheGangRevealedHand> Revealed);

/// <summary>
/// A player's own cards in The Gang, with Coordinator, the card they picked to pass on, and with Informant, a card
/// another seat showed them. The hand's name is only shown when hands are revealed.
/// </summary>
public record TheGangHand(int Seat, IReadOnlyList<string> Cards, string? PassingCard, TheGangShownCard? ShownCard);

/// <summary>
/// Private state of The Gang, sent only to the user holding these seats.
/// </summary>
public record TheGangPrivateState(IReadOnlyList<TheGangHand> Hands);

/// <summary>
/// A session of The Gang for 3 to 8 seats. Each player's cards are private and sent only to them.
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
    public bool TryPassCard(string userKey, int seat, int cardIndex, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryPassCard(player, cardIndex, out var e), e), out error);

    /// <inheritdoc />
    public bool TryConfirmSeen(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryConfirmSeen(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryPropose(string userKey, int seat, string card, int? targetSeat, string? value, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) =>
        {
            int? target = targetSeat is { } s ? PlayerAt(s) ?? -1 : null;
            return (game.TryPropose(player, card, target, value, out var e), e);
        }, out error);

    /// <inheritdoc />
    public bool TryAgree(string userKey, int seat, string card, bool agree, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryAgree(player, card, agree, out var e), e), out error);

    /// <inheritdoc />
    public bool TryShowCard(string userKey, int seat, int cardIndex, int toSeat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryShowCard(player, cardIndex, PlayerAt(toSeat) ?? -1, out var e), e), out error);

    /// <inheritdoc />
    public bool TryDiscard(string userKey, int seat, int cardIndex, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryDiscard(player, cardIndex, out var e), e), out error);

    /// <inheritdoc />
    public bool TryRevealNext(string userKey, int revealed, [NotNullWhen(false)] out string? error) =>
        WithGame(userKey, game => (game.TryRevealNext(revealed, out var e), e), out error);

    /// <inheritdoc />
    public bool TryNextHeist(string userKey, int heist, [NotNullWhen(false)] out string? error) =>
        WithGame(userKey, game => (game.TryNextHeist(heist, out var e), e), out error);

    /// <inheritdoc />
    protected override void StartNewGame(IReadOnlyList<int> seats) =>
        _game = new TheGangGame(seats.Count, _newDeck, TheGangRules.FromOptions(Options), _random,
            (a, b) => HolderOf(GameSeats[a]) is { } holder && holder == HolderOf(GameSeats[b]));

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = _game!;
        var bySeat = SeatOfPlayer();
        var showsAllCards = game.Phase is TheGangPhase.Reveal or TheGangPhase.HeistOver;
        return new TheGangState(
            game.Mode,
            game.Cards,
            game.NewCards,
            game.PreparingCard,
            game.RedrawnPlayer is { } redrawn ? bySeat[redrawn] : null,
            game.SkippedStages,
            game.LockedChips,
            Enumerable.Range(0, game.PlayerCount)
                .Select(player => game.Announcement(player) is { } text ? new TheGangAnnouncement(bySeat[player], text) : null)
                .OfType<TheGangAnnouncement>()
                .ToArray(),
            [.. game.Decisions.Select(d => new TheGangDecisionState(
                d.Card,
                d.Choice,
                d.Player is { } p ? bySeat[p] : null,
                d.Value,
                [.. d.Voters.Select(v => bySeat[v])],
                [.. d.Agreed.Select(a => bySeat[a])],
                d.IsOpen,
                d.IsDone))],
            [.. game.Results.Select(r => new TheGangResultState(bySeat[r.Player], r.Card, r.Text))],
            game.PendingPlayer is { } pending ? bySeat[pending] : null,
            game.PendingStep,
            game.ShownBetween is { } shown ? bySeat[shown.From] : null,
            game.ShownBetween is { } shownTo ? bySeat[shownTo.To] : null,
            game.Succeeded,
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
            Enumerable.Range(0, game.PlayerCount).Where(game.IsPrepared).Select(p => bySeat[p]).ToArray(),
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
        var bySeat = SeatOfPlayer();
        return new TheGangPrivateState(seats.Select(seat =>
        {
            var player = PlayerAt(seat)!.Value;
            var shown = game.ShownCardFor(player) is { } card && game.ShownBetween is { } between
                ? new TheGangShownCard(bySeat[between.From], card.ToString())
                : null;
            return new TheGangHand(seat, Text(game.HoleCards(player)), game.PassingCard(player)?.ToString(), shown);
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
