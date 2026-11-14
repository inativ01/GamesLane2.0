using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.Yaniv;

namespace GamesLane.Server.Tests;

public class YanivGameTests
{
    // 6 points, so player 0 can call Yaniv at once.
    private const string Low = "A♠ A♥ 2♠ 2♥ JOKER";

    // Cards and discards

    [Fact]
    public void The_deck_has_54_cards_and_each_card_has_its_value()
    {
        var deck = YanivCards.Deck();

        Assert.Equal(54, deck.Length);
        Assert.Equal(2, deck.Count(c => c.IsJoker));
        Assert.Equal([1, 0, 7, 10, 10, 10], Cards("A♣ JOKER 7♦ J♠ Q♥ K♣").Select(YanivCards.Value));
        Assert.Equal(28, YanivCards.Score(Cards("A♥ 7♣ 10♦ K♠")));
    }

    [Theory]
    [InlineData("7♥", "7♥")]
    [InlineData("7♥ 7♣", "7♥ 7♣")]
    [InlineData("Q♦ Q♣ Q♠", "Q♦ Q♣ Q♠")]
    [InlineData("7♥ 7♠ JOKER", "JOKER 7♥ 7♠")]
    [InlineData("7♣ 5♣ 6♣", "5♣ 6♣ 7♣")]
    [InlineData("Q♥ 9♥ JOKER J♥", "9♥ JOKER J♥ Q♥")]
    [InlineData("A♦ 3♦ 2♦", "A♦ 2♦ 3♦")]
    [InlineData("5♠ 6♠ JOKER", "JOKER 5♠ 6♠")]
    [InlineData("Q♠ K♠ JOKER", "JOKER Q♠ K♠")]
    [InlineData("2♣ 4♣ JOKER JOKER", "JOKER 2♣ JOKER 4♣")]
    [InlineData("JOKER JOKER", "JOKER JOKER")]
    public void A_single_card_a_series_or_a_straight_can_be_discarded_and_a_joker_goes_under_the_other_cards(string cards, string onThePile)
    {
        Assert.True(YanivCards.TryArrange(Cards(cards), out var ordered));
        Assert.Equal(Cards(onThePile), ordered);
    }

    [Theory]
    [InlineData("A♦ 2♦ JOKER", "A♦ 2♦ JOKER")]
    [InlineData("A♣ 3♣ JOKER JOKER", "A♣ JOKER 3♣ JOKER")]
    public void A_joker_that_must_follow_the_highest_card_of_a_straight_from_the_ace_goes_on_top(string cards, string onThePile)
    {
        Assert.True(YanivCards.TryArrange(Cards(cards), out var ordered));
        Assert.Equal(Cards(onThePile), ordered);
        Assert.True(ordered[^1].IsJoker);
    }

    [Theory]
    [InlineData("5♣ 6♣")]
    [InlineData("5♣ 6♥ 7♣")]
    [InlineData("Q♠ K♠ A♠")]
    [InlineData("5♣ 7♣ 9♣ JOKER")]
    [InlineData("7♥ 7♣ 7♦ 7♠ JOKER")]
    [InlineData("5♣ 5♣ 6♣")]
    public void Other_cards_cannot_be_discarded_together(string cards)
    {
        Assert.False(YanivCards.TryArrange(Cards(cards), out _));
    }

    // Turns

    [Fact]
    public void Each_player_gets_5_cards_and_the_next_card_is_turned_up_on_the_open_pile()
    {
        var game = Game(null, "A♣ 2♣ 3♣ 4♣ 5♣ 6♦ 7♦ 8♦ 9♦ 10♦ K♥");

        Assert.Equal(["A♣", "2♣", "3♣", "4♣", "5♣"], game.Hand(0).Select(c => c.ToString()));
        Assert.Equal((0, 43, 1, YanivPhase.Playing), (game.Turn, game.ClosedPileCount, game.OpenPileCount, game.Phase));
        Assert.Equal(Card("K♥"), game.OpenTop);
        Assert.True(game.TryDiscard(0, Cards("5♣"), true, out var error), error);
        Assert.Contains(Card("K♥"), game.Hand(0));
    }

    [Fact]
    public void A_player_discards_then_takes_a_card_and_the_discards_go_on_the_open_pile_lowest_first()
    {
        var game = Game(null, "A♣ 2♣ 3♣ 9♠ 5♥ 6♦ 7♦ 8♦ K♦ K♥ 4♠ Q♥");

        AssertRefused(game.TryDiscard(1, Cards("K♥"), false, out var error), error, "It is not your turn.");
        AssertRefused(game.TryDiscard(0, Cards("K♥"), false, out error), error, "You do not have those cards.");
        AssertRefused(game.TryDiscard(0, Cards("9♠ 5♥"), false, out error), error, YanivCards.InvalidDiscard);
        AssertRefused(game.TryCallYaniv(0, out error), error, "You can call Yaniv only with 7 points or less.");
        Assert.True(game.TryDiscard(0, Cards("3♣ A♣ 2♣"), false, out error), error);

        Assert.Equal(["9♠", "5♥", "Q♥"], game.Hand(0).Select(c => c.ToString()));
        Assert.Equal((1, 4, 42), (game.Turn, game.OpenPileCount, game.ClosedPileCount));
        Assert.Equal(Card("3♣"), game.OpenTop);
        Assert.Equal(new[] { "A♣", "2♣", "3♣" }, game.LastTurn!.Cards.Select(c => c.ToString()));

        Assert.True(game.TryDiscard(1, Cards("K♦ K♥"), true, out error), error);
        Assert.Contains(Card("3♣"), game.Hand(1));
        Assert.Equal(Card("K♥"), game.OpenTop);
        Assert.Equal(new[] { Card("3♣") }, game.LastTurn!.Taken);
        Assert.Equal(0, game.Turn);
    }

    [Fact]
    public void The_turn_of_an_absent_player_can_be_skipped()
    {
        var game = Game(null, $"{Low} 3♣ 4♣ 5♣ 6♣ 7♣");

        Assert.True(game.TrySkip(0, out var error), error);
        Assert.Equal((1, 5), (game.Turn, game.Hand(0).Count));
        Assert.True(game.LastTurn!.Skipped);
        AssertRefused(game.TrySkip(0, out error), error, "It is not your turn.");
    }

    [Fact]
    public void Each_deal_and_each_new_closed_pile_from_the_open_pile_counts_as_a_shuffle()
    {
        var game = Game(null);
        Assert.Equal(1, game.Shuffles);

        // Each turn discards one card and takes one from the closed pile, until the 43 cards of the closed pile are taken.
        for (var turn = 0; turn < 43; turn++)
        {
            Assert.True(game.TryDiscard(game.Turn, [game.Hand(game.Turn)[0]], false, out var error), error);
        }

        Assert.Equal((0, 44, 1), (game.ClosedPileCount, game.OpenPileCount, game.Shuffles));
        Assert.True(game.TryDiscard(game.Turn, [game.Hand(game.Turn)[0]], false, out var last), last);
        Assert.Equal((42, 2, 2), (game.ClosedPileCount, game.OpenPileCount, game.Shuffles));
    }

    // Yaniv

    [Fact]
    public void After_Yaniv_the_others_play_one_last_turn_and_the_caller_wins_a_tie()
    {
        var game = Game(null, $"{Low} K♠ A♦ A♣ 2♦ 2♣ 9♣ JOKER");
        Assert.True(game.CanCallYaniv(0));
        Assert.True(game.TryCallYaniv(0, out var error), error);
        Assert.Equal((1, 0), (game.Turn, game.Caller));
        Assert.False(game.CanCallYaniv(1));
        AssertRefused(game.TryCallYaniv(1, out error), error, "Yaniv has already been called.");

        Assert.True(game.TryDiscard(1, Cards("K♠"), false, out error), error);

        var result = game.LastResult!;
        Assert.Equal((0, 0, false), (result.Caller, result.Winner, result.Assaf));
        Assert.Equal([0, 6], result.Players.Select(p => p.Points));
        Assert.Equal([6, 6], result.Players.Select(p => p.HandScore));
        Assert.Equal([0, 6], game.Scores);
        Assert.Equal((YanivPhase.RoundOver, 0), (game.Phase, game.Turn));
    }

    [Fact]
    public void After_Yaniv_a_player_can_pass_their_last_turn_and_keep_their_hand()
    {
        var game = new YanivGame(3, null, Stacked([$"{Low} K♠ K♥ K♦ K♣ Q♠ 3♣ 3♦ 3♥ 3♠ 4♣"]));
        AssertRefused(game.TryPass(0, out var error), error, "You can pass only after Yaniv is called.");
        Assert.True(game.TryCallYaniv(0, out error), error);
        AssertRefused(game.TryPass(2, out error), error, "It is not your turn.");

        Assert.True(game.TryPass(1, out error), error);

        Assert.Equal(Cards("K♠ K♥ K♦ K♣ Q♠"), game.Hand(1));
        Assert.True(game.LastTurn!.Passed);
        Assert.Equal((2, YanivPhase.Playing), (game.Turn, game.Phase));
        Assert.True(game.TryPass(2, out error), error);
        Assert.Equal((YanivPhase.RoundOver, 0), (game.Phase, game.LastResult!.Winner));
    }

    [Fact]
    public void A_caller_with_a_higher_hand_is_Assaf_and_the_lowest_hand_wins_and_starts_the_next_round()
    {
        var game = Game(null, "A♠ A♥ 2♠ 3♥ JOKER K♠ A♦ A♣ 2♦ 2♣ 9♣ JOKER", "A♣ 2♣ 3♣ 4♣ 5♣ 6♦ 7♦ 8♦ 9♦ 10♦");
        game.TryCallYaniv(0, out _);
        game.TryDiscard(1, Cards("K♠"), false, out _);

        var result = game.LastResult!;
        Assert.Equal((0, 1, true), (result.Caller, result.Winner, result.Assaf));
        Assert.Equal([7 + YanivRules.AssafPenalty, 0], game.Scores);
        AssertRefused(game.TryNextRound(2, out var error), error, "There is no such round.");
        Assert.True(game.TryNextRound(1, out error), error);
        Assert.True(game.TryNextRound(1, out error), error);
        Assert.Equal((2, 1, YanivPhase.Playing, 5), (game.RoundNumber, game.Turn, game.Phase, game.Hand(0).Count));
        Assert.Equal(2, game.Shuffles);
        Assert.Null(game.Caller);
    }

    // Scores

    [Fact]
    public void Landing_exactly_on_half_the_target_or_on_the_target_takes_50_points_off()
    {
        var game = Game(new YanivRules(Target: 100), Round("K♠ K♥ K♦ K♣", "Q♠"), Round("K♠ K♥ K♦ K♣", "JOKER"),
            Round("K♠ K♥ K♦ K♣", "JOKER"), Round("K♠ 5♦ 4♣ A♣", "JOKER"));

        PlayRound(game);
        Assert.Equal([0, 0], game.Scores);
        Assert.True(game.LastResult!.Players[1].Bonus);
        PlayRound(game);
        PlayRound(game);
        Assert.Equal([0, 80], game.Scores);
        Assert.False(game.LastResult!.Players[1].Bonus);
        PlayRound(game);
        Assert.Equal([0, 50], game.Scores);
        Assert.True(game.LastResult!.Players[1].Bonus);
        Assert.False(game.IsOut(1));
    }

    [Fact]
    public void A_player_above_the_target_is_out_and_the_last_player_left_wins()
    {
        var game = Game(new YanivRules(Target: 100, Bonus: false), Round("K♠ K♥ K♦ K♣", "Q♠"), Round("K♠ K♥ K♦ K♣", "Q♠"),
            Round("K♠ K♥ 5♦ 4♣", "A♦"));

        PlayRound(game);
        PlayRound(game);
        Assert.Equal([0, 100], game.Scores);
        Assert.False(game.IsOut(1));
        PlayRound(game);

        Assert.True(game.IsOut(1));
        Assert.True(game.LastResult!.Players[1].IsOut);
        Assert.Equal((YanivPhase.GameOver, 0), (game.Phase, game.Winner));
        AssertRefused(game.TryNextRound(3, out var error), error, "The game is over.");
    }

    [Fact]
    public void A_game_of_one_round_ends_with_its_winner()
    {
        var game = Game(new YanivRules(YanivEndMode.OneRound), Round("K♠ K♥ K♦ K♣", "Q♠"));

        PlayRound(game);

        Assert.True(game.IsOver);
        Assert.Equal(0, game.Winner);
        Assert.Equal([0, 50], game.Scores);
    }

    // A round where player 0 calls Yaniv with 6 points, and player 1, in their last turn, discards 3♣ and takes the
    // closed pile's top card (under the 9♣ turned up): they then hold kept and drawn.
    private static string Round(string kept, string drawn) => $"{Low} 3♣ {kept} 9♣ {drawn}";

    private static void PlayRound(YanivGame game)
    {
        Assert.True(game.TryCallYaniv(0, out var error), error);
        Assert.True(game.TryDiscard(1, Cards("3♣"), false, out error), error);
        if (game.Phase == YanivPhase.RoundOver)
        {
            Assert.True(game.TryNextRound(game.RoundNumber, out error), error);
        }
    }

    // A game whose deals put the given cards (short text, separated by spaces) on top of the deck, one deal per round:
    // the first player's 5 cards, the next player's, and so on, then the card turned up, then the closed pile.
    private static YanivGame Game(YanivRules? rules, params string[] deals) => new(2, rules, Stacked(deals));

    private static Action<Card[]> Stacked(string[] deals)
    {
        var dealt = 0;
        return deck =>
        {
            if (deck.Length != 54 || dealt >= deals.Length)
            {
                return;
            }

            var first = Cards(deals[dealt++]).ToList();
            var rest = deck.ToList();
            Assert.All(first, card => Assert.True(rest.Remove(card), $"The deck has no more {card}."));
            first.Concat(rest).ToArray().CopyTo(deck, 0);
        };
    }

    private static Card[] Cards(string cards) => [.. cards.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card)];

    private static Card Card(string text) => GamesLane.Server.Games.TheGang.Card.Parse(text);

    private static void AssertRefused(bool done, string? error, string expected)
    {
        Assert.False(done);
        Assert.Equal(expected, error);
    }
}
