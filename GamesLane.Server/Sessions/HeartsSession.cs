using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Hearts;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player as everyone sees them: their seat, how many cards they hold, their score, the points they have taken in
/// this hand, whether they have passed their cards, and the hearts and whether the queen of spades they have taken in
/// this hand.
/// </summary>
public record HeartsPlayerState(int Seat, int CardCount, int Score, int HandPoints, bool HasPassed, int HeartsTaken, bool TookQueen);

/// <summary>
/// A player at the end of a hand: the points they gained, and their score after it.
/// </summary>
public record HeartsResultPlayerState(int Seat, int Points, int Score);

/// <summary>
/// How the last hand ended: each player's points, and the seat that shot the moon, or null.
/// </summary>
public record HeartsResultState(IReadOnlyList<HeartsResultPlayerState> Players, int? MoonSeat);

/// <summary>
/// Public Hearts state. <see cref="Turn"/>, <see cref="Winners"/> and the seats in the tricks, players and claim are
/// seats; <see cref="Players"/> are in seat order. <see cref="Claim"/> is the claim of this hand's remaining tricks, or
/// null. <see cref="Actions"/> counts the actions, so a page can tell a new one, and <see cref="Shuffles"/> the shuffles
/// of the deck, so a page can show each one.
/// </summary>
public record HeartsState(
    int Target,
    int HandNumber,
    HeartsPhase Phase,
    HeartsPass Pass,
    int Turn,
    bool HeartsBroken,
    TrickState Trick,
    TrickState? LastTrick,
    TrickClaimState? Claim,
    IReadOnlyList<HeartsPlayerState> Players,
    int Actions,
    HeartsResultState? LastResult,
    IReadOnlyList<int> Winners,
    int Shuffles);

/// <summary>
/// A seat's private Hearts information: its cards (by suit, from 2 to A), those it may play now, the seat it passes to
/// in this hand, the cards it chose to pass (or null), those it received, and whether it may claim the remaining tricks.
/// </summary>
public record HeartsHand(int Seat, IReadOnlyList<string> Cards, IReadOnlyList<string> Playable, int PassTo,
    IReadOnlyList<string>? Passed, IReadOnlyList<string> Received, bool CanClaim);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record HeartsPrivateState(IReadOnlyList<HeartsHand> Hands);

/// <summary>
/// A Hearts game for 4 seats (see <see cref="HeartsGame"/>). A game that waits for an absent player cannot skip them.
/// </summary>
public class HeartsSession(string id, Action<Card[]>? shuffle = null) : TrickTakingSession<HeartsGame>(id, shuffle), IHeartsSession
{
    /// <inheritdoc />
    public override GameType Type => GameType.Hearts;

    /// <inheritdoc />
    public bool TryPass(string userKey, int seat, IReadOnlyList<string>? cards, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => TryParse(cards, out var parsed)
            ? (game.TryPass(player, parsed, out var e), e)
            : (false, "Unknown card."), out error);

    /// <inheritdoc />
    protected override HeartsGame CreateGame(IReadOnlyDictionary<string, string> options) => new(Shuffle);

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        return new HeartsState(
            HeartsRules.Target,
            game.HandNumber,
            game.Phase,
            game.Pass,
            SeatOf(game.Turn),
            game.HeartsBroken,
            CurrentTrick(),
            LastTrick(),
            Claim(),
            [.. Enumerable.Range(0, game.Players).Select(p => new HeartsPlayerState(
                SeatOf(p), game.Hand(p).Count, game.Scores[p], game.HandPoints(p), game.Passed(p) is not null,
                game.Won(p).Count(c => c.Suit == Suit.Hearts), game.Won(p).Contains(HeartsRules.QueenOfSpades)))],
            game.Actions,
            game.LastResult is { } result
                ? new HeartsResultState([.. result.Players.Select(p => new HeartsResultPlayerState(SeatOf(p.Player), p.Points, p.Score))],
                    result.Moon is { } moon ? SeatOf(moon) : null)
                : null,
            [.. game.Winners.Select(SeatOf)],
            game.Shuffles);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new HeartsPrivateState([.. seats.Select(PlayerOf).Select(player => new HeartsHand(
            SeatOf(player),
            Text(game.Hand(player)),
            Text(game.Playable(player)),
            SeatOf(HeartsRules.PassTarget(player, game.Pass)),
            game.Passed(player) is { } passed ? Text(passed) : null,
            Text(game.Received(player)),
            game.CanClaim(player)))]);
    }
}
