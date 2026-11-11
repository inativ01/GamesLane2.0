using System.Diagnostics.CodeAnalysis;
using GamesLane.Server.Games.Spades;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Sessions;

/// <summary>
/// A player as everyone sees them: their seat, their team (0 or 1), how many cards they hold, their bid (null before they
/// bid; 0 for Nil), whether it was blind, and the tricks they have taken in this hand.
/// </summary>
public record SpadesPlayerState(int Seat, int Team, int CardCount, int? Bid, bool Blind, int Tricks);

/// <summary>
/// A team as everyone sees it: its seats, its score and its bags.
/// </summary>
public record SpadesTeamState(int Team, IReadOnlyList<int> Seats, int Score, int Bags);

/// <summary>
/// A team at the end of a hand (see <see cref="SpadesTeamResult"/>).
/// </summary>
public record SpadesTeamResultState(int Team, int Bid, int Tricks, bool Made, int Points, int NewBags, bool BagPenalty, int Score, int Bags);

/// <summary>
/// A Nil bid at the end of a hand: its seat, whether blind, and whether it was made.
/// </summary>
public record SpadesNilResultState(int Seat, bool Blind, bool Made);

/// <summary>
/// How the last hand ended: each team, and the Nil bids.
/// </summary>
public record SpadesResultState(IReadOnlyList<SpadesTeamResultState> Teams, IReadOnlyList<SpadesNilResultState> Nils);

/// <summary>
/// Public Spades state. <see cref="Turn"/>, <see cref="Dealer"/>, <see cref="Bidder"/> and the seats in the tricks,
/// players and teams are seats; <see cref="Players"/> are in seat order, <see cref="Teams"/> by team, and
/// <see cref="Winner"/> is a team. <see cref="Actions"/> counts the actions, so a page can tell a new one.
/// </summary>
public record SpadesState(
    bool Nil,
    bool BlindNil,
    int Target,
    int HandNumber,
    SpadesPhase Phase,
    int Dealer,
    int Bidder,
    int Turn,
    bool SpadesBroken,
    TrickState Trick,
    TrickState? LastTrick,
    IReadOnlyList<SpadesPlayerState> Players,
    IReadOnlyList<SpadesTeamState> Teams,
    int Actions,
    SpadesResultState? LastResult,
    int? Winner);

/// <summary>
/// A seat's private Spades information: its cards (by suit, from 2 to A; none while it has not looked at them), those it
/// may play now, and whether it has looked at them.
/// </summary>
public record SpadesHand(int Seat, IReadOnlyList<string> Cards, IReadOnlyList<string> Playable, bool Looked);

/// <summary>
/// The private information of a user's seats in the game.
/// </summary>
public record SpadesPrivateState(IReadOnlyList<SpadesHand> Hands);

/// <summary>
/// A Spades game for 4 seats in 2 teams of partners across (see <see cref="SpadesGame"/>), with the options' Nil and Blind
/// Nil. A game that waits for an absent player cannot skip them.
/// </summary>
public class SpadesSession(string id, Action<Card[]>? shuffle = null) : TrickTakingSession<SpadesGame>(id, shuffle), ISpadesSession
{
    /// <inheritdoc />
    public override GameType Type => GameType.Spades;

    /// <inheritdoc />
    public bool TryLook(string userKey, int seat, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryLook(player, out var e), e), out error);

    /// <inheritdoc />
    public bool TryBid(string userKey, int seat, int tricks, bool blind, [NotNullWhen(false)] out string? error) =>
        WithPlayer(userKey, seat, (game, player) => (game.TryBid(player, tricks, blind, out var e), e), out error);

    /// <inheritdoc />
    protected override SpadesGame CreateGame(IReadOnlyDictionary<string, string> options) =>
        new(SpadesOptions.RulesFrom(options), Shuffle);

    /// <inheritdoc />
    protected override object GetGameState()
    {
        var game = Game;
        var players = Enumerable.Range(0, game.Players).ToArray();
        return new SpadesState(
            game.Rules.Nil,
            game.Rules.BlindNil,
            SpadesRules.Target,
            game.HandNumber,
            game.Phase,
            SeatOf(game.Dealer),
            SeatOf(game.Bidder),
            SeatOf(game.Turn),
            game.SpadesBroken,
            CurrentTrick(),
            LastTrick(),
            [.. players.Select(p => new SpadesPlayerState(SeatOf(p), SpadesRules.TeamOf(p), game.Hand(p).Count,
                game.BidOf(p)?.Tricks, game.BidOf(p)?.Blind ?? false, game.TricksWon[p]))],
            [.. Enumerable.Range(0, 2).Select(team => new SpadesTeamState(team,
                [.. players.Where(p => SpadesRules.TeamOf(p) == team).Select(SeatOf)], game.Scores[team], game.Bags[team]))],
            game.Actions,
            game.LastResult is { } result
                ? new SpadesResultState(
                    [.. result.Teams.Select(t => new SpadesTeamResultState(t.Team, t.Bid, t.Tricks, t.Made, t.Points, t.NewBags, t.BagPenalty, t.Score, t.Bags))],
                    [.. result.Nils.Select(n => new SpadesNilResultState(SeatOf(n.Player), n.Blind, n.Made))])
                : null,
            game.Winner);
    }

    /// <inheritdoc />
    protected override object? GetPrivateGameState(IReadOnlyList<int> seats)
    {
        var game = Game;
        return new SpadesPrivateState([.. seats.Select(PlayerOf).Select(player => new SpadesHand(
            SeatOf(player),
            game.HasLooked(player) ? Text(game.Hand(player)) : [],
            Text(game.Playable(player)),
            game.HasLooked(player)))]);
    }
}
