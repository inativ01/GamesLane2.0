using GamesLane.Server.Games.Hearts;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Tests;

public class HeartsGameTests
{
    // Unshuffled, player 0 holds the clubs, 1 the diamonds, 2 the spades and 3 the hearts.
    private static HeartsGame Unshuffled() => new(_ => { });

    [Fact]
    public void The_cards_go_left_right_across_and_then_are_held()
    {
        Assert.Equal([HeartsPass.Left, HeartsPass.Right, HeartsPass.Across, HeartsPass.Hold, HeartsPass.Left],
            Enumerable.Range(1, 5).Select(HeartsRules.PassFor));
        Assert.Equal([1, 3, 2, 0], new[] { HeartsPass.Left, HeartsPass.Right, HeartsPass.Across, HeartsPass.Hold }.Select(p => HeartsRules.PassTarget(0, p)));
        Assert.Equal((13, 1, 0), (HeartsRules.Points(Card("Q♠")), HeartsRules.Points(Card("7♥")), HeartsRules.Points(Card("K♠"))));
    }

    [Fact]
    public void Every_player_passes_3_cards_then_the_holder_of_the_two_of_clubs_leads()
    {
        var game = Unshuffled();
        Assert.Equal((HeartsPhase.Passing, HeartsPass.Left), (game.Phase, game.Pass));
        AssertRefused(game.TryPass(0, Cards("2♣ 3♣"), out var error), error, "Choose 3 cards to pass.");
        AssertRefused(game.TryPass(0, Cards("2♣ 3♣ 2♦"), out error), error, "You do not have those cards.");
        AssertRefused(game.TryPlay(0, Card("2♣"), out error), error, "Wait until it is time to play a card.");

        Assert.True(game.TryPass(0, Cards("2♣ 3♣ 4♣"), out error), error);
        AssertRefused(game.TryPass(0, Cards("5♣ 6♣ 7♣"), out error), error, "You have already passed your cards.");
        Assert.Equal([1, 2, 3], game.AwaitedPlayers);
        Assert.Equal(13, game.Hand(0).Count);

        PassLowest(game);
        Assert.Equal((HeartsPhase.Playing, 1), (game.Phase, game.Turn));
        Assert.Equal(Cards("2♣ 3♣ 4♣"), game.Received(1));
        Assert.Equal(Cards("2♣ 3♣ 4♣ 5♦ 6♦ 7♦ 8♦ 9♦ 10♦ J♦ Q♦ K♦ A♦"), game.Hand(1));
        Assert.Equal(Cards("2♥ 3♥ 4♥"), game.Hand(0).Where(c => c.Suit == Suit.Hearts));
    }

    [Fact]
    public void The_two_of_clubs_leads_and_nobody_may_give_points_on_the_first_trick_or_lead_hearts_before_they_are_broken()
    {
        var game = Unshuffled();
        PassLowest(game);
        // Player 0: 5♣ to A♣ and 2♥ 3♥ 4♥; 1: 2♣ 3♣ 4♣ and the diamonds from 5; 2: 2♦ 3♦ 4♦ and the spades from 5;
        // 3: 2♠ 3♠ 4♠ and the hearts from 5.
        AssertRefused(game.TryPlay(1, Card("3♣"), out var error), error, "Lead the 2♣.");
        Assert.True(game.TryPlay(1, Card("2♣"), out error), error);
        AssertRefused(game.TryPlay(2, Card("Q♠"), out error), error, "No points on the first trick: play another card.");
        Assert.DoesNotContain(Card("Q♠"), game.Playable(2));
        Assert.True(game.TryPlay(2, Card("2♦"), out error), error);
        AssertRefused(game.TryPlay(3, Card("5♥"), out error), error, "No points on the first trick: play another card.");
        Assert.True(game.TryPlay(3, Card("2♠"), out error), error);
        AssertRefused(game.TryPlay(0, Card("2♥"), out error), error, "You must follow suit.");
        Assert.True(game.TryPlay(0, Card("5♣"), out error), error);

        Assert.Equal(0, game.Turn);
        AssertRefused(game.TryPlay(0, Card("2♥"), out error), error, "Hearts are not broken yet.");
        Assert.False(game.HeartsBroken);
        Assert.True(game.TryPlay(0, Card("6♣"), out error), error);
        Assert.True(game.TryPlay(1, Card("3♣"), out error), error);
        Assert.True(game.TryPlay(2, Card("3♦"), out error), error);
        Assert.True(game.TryPlay(3, Card("5♥"), out error), error);
        Assert.True(game.HeartsBroken);
        Assert.Equal(1, game.HandPoints(0));
        Assert.True(game.TryPlay(0, Card("2♥"), out error), error);
    }

    [Fact]
    public void A_player_who_takes_every_heart_and_the_queen_of_spades_shoots_the_moon()
    {
        var game = Unshuffled();
        for (var hand = 1; hand <= 3; hand++)
        {
            PlayHand(game);
            Assert.Equal((HeartsPhase.HandOver, hand), (game.Phase, game.HandNumber));
            Assert.True(game.TryNextHand(hand, out var error), error);
        }

        // The fourth hand is held: player 0 keeps the clubs, leads the 2♣ and wins every trick.
        var before = game.Scores.ToArray();
        Assert.Equal((HeartsPhase.Playing, HeartsPass.Hold, 0), (game.Phase, game.Pass, game.Turn));
        PlayHand(game);

        Assert.Equal(0, game.LastResult!.Moon);
        Assert.Equal([0, 26, 26, 26], game.LastResult.Players.Select(p => p.Points));
        Assert.Equal(before.Select((score, p) => score + (p == 0 ? 0 : 26)), game.Scores);
    }

    [Fact]
    public void The_game_ends_once_a_player_reaches_100_and_the_lowest_score_wins()
    {
        var game = new HeartsGame(new Random(7).Shuffle);
        while (!game.IsOver)
        {
            PlayHand(game);
            game.TryNextHand(game.HandNumber, out _);
        }

        Assert.True(game.Scores.Max() >= HeartsRules.Target);
        Assert.Equal(Enumerable.Range(0, 4).Where(p => game.Scores[p] == game.Scores.Min()), game.Winners);
        AssertRefused(game.TryNextHand(game.HandNumber, out var error), error, "The game is over.");
        AssertRefused(game.TryPlay(game.Turn, game.Hand(game.Turn).FirstOrDefault(), out error), error, "The game is over.");
    }

    [Fact]
    public void A_leader_whose_cards_win_every_remaining_trick_may_claim_them()
    {
        var game = Unshuffled();
        PassLowest(game);
        // Player 1 leads with 2♣ 3♣ 4♣ while player 0 holds the higher clubs.
        Assert.False(game.CanClaim(1));
        AssertRefused(game.TryClaim(1, out var error), error, "Your cards do not win every remaining trick.");
        AssertRefused(game.TryClaim(0, out error), error, "It is not your turn.");
        Assert.True(game.TryPlay(1, Card("2♣"), out error), error);
        AssertRefused(game.TryClaim(2, out error), error, "You can claim only at the start of a trick.");

        game = Unshuffled();
        for (var hand = 1; hand <= 3; hand++)
        {
            PlayHand(game);
            game.TryNextHand(hand, out _);
        }

        // The fourth hand is held: player 0 leads holding all 13 clubs.
        Assert.True(game.CanClaim(0));
        Assert.False(game.CanClaim(1));
        var before = game.Scores.ToArray();
        Assert.True(game.TryClaim(0, out error), error);
        Assert.Equal(0, game.Claimer);
        Assert.Equal([13, 13, 13, 13], Enumerable.Range(0, 4).Select(p => game.ClaimedHand(p).Count));
        Assert.All(game.ClaimedHand(0), c => Assert.Equal(Suit.Clubs, c.Suit));
        Assert.All(Enumerable.Range(0, 4), p => Assert.Empty(game.Hand(p)));
        Assert.Equal((13, 52), (game.TricksWon[0], game.Won(0).Count));
        Assert.Equal(0, game.LastResult!.Moon);
        Assert.Equal(before.Select((score, p) => score + (p == 0 ? 0 : 26)), game.Scores);

        game.TryNextHand(4, out _);
        Assert.Null(game.Claimer);
        Assert.Empty(game.ClaimedHand(1));
    }

    // Each player passes their first 3 cards, if they have not passed yet.
    private static void PassLowest(HeartsGame game)
    {
        for (var p = 0; p < 4; p++)
        {
            if (game.Passed(p) is null)
            {
                Assert.True(game.TryPass(p, game.Hand(p).Take(3).ToList(), out var error), error);
            }
        }
    }

    // Passes the lowest cards, then plays the first card each player may play until the hand is over.
    private static void PlayHand(HeartsGame game)
    {
        if (game.Phase == HeartsPhase.Passing)
        {
            PassLowest(game);
        }

        while (game.IsPlaying)
        {
            Assert.True(game.TryPlay(game.Turn, game.Playable(game.Turn)[0], out var error), error);
        }
    }

    private static Card[] Cards(string cards) => [.. cards.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card)];

    private static Card Card(string text) => GamesLane.Server.Games.TheGang.Card.Parse(text);

    private static void AssertRefused(bool done, string? error, string expected)
    {
        Assert.False(done);
        Assert.Equal(expected, error);
    }
}
