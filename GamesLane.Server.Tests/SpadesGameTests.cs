using GamesLane.Server.Games.Spades;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Tests;

public class SpadesGameTests
{
    // Unshuffled, player 0 holds the clubs, 1 the diamonds, 2 the spades and 3 the hearts.
    private static SpadesGame Unshuffled(SpadesRules? rules = null) => new(rules, _ => { });

    [Fact]
    public void Players_bid_in_turn_from_the_dealers_left_who_then_leads()
    {
        var game = Unshuffled();
        Assert.Equal((SpadesPhase.Bidding, 3, 0), (game.Phase, game.Dealer, game.Bidder));
        Assert.Equal([0], game.AwaitedPlayers);
        AssertRefused(game.TryBid(1, 3, false, out var error), error, "It is not your turn to bid.");
        AssertRefused(game.TryBid(0, 14, false, out error), error, "Bid 1 to 13 tricks.");
        AssertRefused(game.TryPlay(0, Card("2♣"), out error), error, "Wait until it is time to play a card.");

        Bid(game, 1, 1, 13, 0);
        Assert.Equal((SpadesPhase.Playing, 0), (game.Phase, game.Turn));
        Assert.Equal(new SpadesBid(0), game.BidOf(3));
        AssertRefused(game.TryBid(0, 3, false, out error), error, "The bidding is over.");
    }

    [Fact]
    public void Without_the_nil_option_every_player_bids_at_least_one_and_blind_nil_needs_its_option()
    {
        var game = Unshuffled(new SpadesRules(Nil: false));
        AssertRefused(game.TryBid(0, 0, false, out var error), error, "Bid 1 to 13 tricks.");
        AssertRefused(game.TryBid(0, 0, true, out error), error, "Bid 1 to 13 tricks.");

        game = Unshuffled();
        Assert.All(Enumerable.Range(0, 4), p => Assert.True(game.HasLooked(p)));
        AssertRefused(game.TryBid(0, 0, true, out error), error, "Only Nil can be bid blind.");
    }

    [Fact]
    public void Blind_nil_is_bid_before_looking_at_the_cards()
    {
        var game = Unshuffled(new SpadesRules(BlindNil: true));
        Assert.All(Enumerable.Range(0, 4), p => Assert.False(game.HasLooked(p)));
        AssertRefused(game.TryBid(0, 3, true, out var error), error, "Only Nil can be bid blind.");
        Assert.True(game.TryBid(0, 0, true, out error), error);
        Assert.True(game.HasLooked(0));

        Assert.True(game.TryLook(1, out error), error);
        Assert.True(game.TryLook(1, out error), error);
        AssertRefused(game.TryBid(1, 0, true, out error), error, "You have looked at your cards: Blind Nil is no longer possible.");
    }

    [Fact]
    public void Spades_are_trumps_and_cannot_be_led_before_they_are_broken()
    {
        var game = Unshuffled();
        Bid(game, 4, 4, 4, 1);
        // Player 0 (all clubs) leads; player 2 (all spades) trumps; player 2 may then lead spades.
        Assert.True(game.TryPlay(0, Card("A♣"), out var error), error);
        Assert.True(game.TryPlay(1, Card("A♦"), out error), error);
        Assert.True(game.TryPlay(2, Card("2♠"), out error), error);
        Assert.True(game.SpadesBroken);
        Assert.True(game.TryPlay(3, Card("A♥"), out error), error);
        Assert.Equal((2, 1), (game.Turn, game.TricksWon[2]));
        Assert.True(game.TryPlay(2, Card("3♠"), out error), error);
    }

    [Fact]
    public void A_team_that_makes_its_bid_scores_10_a_trick_and_a_bag_for_each_trick_over()
    {
        var game = Unshuffled();
        // Team 0 (0 and 2) bids 5 and takes 13; team 1 bids 2 and takes 0. Each player plays their lowest card: player 2
        // trumps every trick.
        Bid(game, 4, 1, 1, 1);
        PlayOut(game);

        var result = game.LastResult!;
        Assert.Equal(new SpadesTeamResult(0, 5, 13, true, 58, 8, false, 58, 8), result.Teams[0]);
        Assert.Equal(new SpadesTeamResult(1, 2, 0, false, -20, 0, false, -20, 0), result.Teams[1]);
        Assert.Equal([58, -20], game.Scores);
        Assert.Equal((SpadesPhase.HandOver, null), (game.Phase, game.Winner));

        Assert.True(game.TryNextHand(1, out var error), error);
        Assert.Equal((0, 1, SpadesPhase.Bidding), (game.Dealer, game.Bidder, game.Phase));
    }

    [Fact]
    public void A_nil_bid_scores_100_if_it_takes_no_trick_and_10_bags_cost_100()
    {
        var game = Unshuffled();
        // Player 3 bids Nil and takes no trick; player 0 (Nil) takes none either; player 2 takes all 13.
        Bid(game, 0, 1, 3, 0);
        PlayOut(game);

        var teams = game.LastResult!.Teams;
        Assert.Equal([new SpadesNilResult(0, false, true), new SpadesNilResult(3, false, true)], game.LastResult.Nils);
        Assert.Equal(new SpadesTeamResult(0, 3, 13, true, 30 + 10 + 100 - 100, 10, true, 40, 0), teams[0]);
        Assert.Equal(new SpadesTeamResult(1, 1, 0, false, -10 + 100, 0, false, 90, 0), teams[1]);
    }

    [Fact]
    public void The_first_team_to_500_wins_and_a_team_at_minus_200_loses()
    {
        var game = new SpadesGame(null, new Random(4).Shuffle);
        while (!game.IsOver)
        {
            for (var i = 0; i < 4; i++)
            {
                Assert.True(game.TryBid(game.Bidder, 3, false, out var error), error);
            }

            PlayOut(game);
            game.TryNextHand(game.HandNumber, out _);
        }

        Assert.True(game.Scores.Max() >= SpadesRules.Target || game.Scores.Min() <= SpadesRules.LosingScore);
        Assert.Equal(game.Scores[0] > game.Scores[1] ? 0 : 1, game.Winner);
    }

    [Fact]
    public void A_leader_may_claim_only_when_no_trump_is_out_or_they_hold_only_trumps()
    {
        var game = Unshuffled();
        Bid(game, 4, 4, 4, 1);
        // Player 0 holds every club, but the spades are out: a void player could trump.
        Assert.False(game.CanClaim(0));
        AssertRefused(game.TryClaim(0, out var error), error, "Your cards do not win every remaining trick.");
        Assert.True(game.TryPlay(0, Card("A♣"), out error), error);
        Assert.True(game.TryPlay(1, Card("A♦"), out error), error);
        Assert.True(game.TryPlay(2, Card("2♠"), out error), error);
        Assert.True(game.TryPlay(3, Card("A♥"), out error), error);

        // Player 2, holding only spades, leads and claims the other 12 tricks.
        Assert.True(game.CanClaim(2));
        Assert.True(game.TryClaim(2, out error), error);
        Assert.Equal((2, 13), (game.Claimer, game.TricksWon[2]));
        Assert.Equal(SpadesPhase.HandOver, game.Phase);
        Assert.True(game.LastResult!.Teams[0].Made);
    }

    // The players bid in turn from the bidder.
    private static void Bid(SpadesGame game, params int[] bids)
    {
        foreach (var bid in bids)
        {
            Assert.True(game.TryBid(game.Bidder, bid, false, out var error), error);
        }
    }

    // Plays the first card each player may play until the hand is over.
    private static void PlayOut(SpadesGame game)
    {
        while (game.IsPlaying)
        {
            Assert.True(game.TryPlay(game.Turn, game.Playable(game.Turn)[0], out var error), error);
        }
    }

    private static Card Card(string text) => GamesLane.Server.Games.TheGang.Card.Parse(text);

    private static void AssertRefused(bool done, string? error, string expected)
    {
        Assert.False(done);
        Assert.Equal(expected, error);
    }
}
