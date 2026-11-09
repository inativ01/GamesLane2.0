using GamesLane.Server.Games.FinalCard;

namespace GamesLane.Server.Tests;

public class FinalCardGameTests
{
    private const string Reds = "R2 R3 R4 R6 R7 R8 R9";

    // Cards and dealing

    [Fact]
    public void The_deck_has_108_cards_each_with_its_short_text()
    {
        var deck = FinalCardCard.Deck();

        Assert.Equal(108, deck.Count);
        Assert.Equal(25, deck.Count(c => c.Color == FinalCardColor.Red));
        Assert.Equal(1, deck.Count(c => c == Card("G0")));
        Assert.Equal(2, deck.Count(c => c == Card("B+2")));
        Assert.Equal(4, deck.Count(c => c == Card("W+4")));
        Assert.Equal(1240, deck.Sum(c => c.Points));
        Assert.All(deck, c => Assert.Equal(c, Card(c.ToString())));
        Assert.False(FinalCardCard.TryParse("X5", out _));
        Assert.False(FinalCardCard.TryParse("R10", out _));
    }

    [Fact]
    public void Each_player_gets_7_cards_and_the_next_card_is_turned_up()
    {
        var game = Deal(null, "B5", "", "R5 B7 G2 Y9 R1 B1 G1", Reds);

        Assert.All(Enumerable.Range(0, 2), p => Assert.Equal(7, game.Hand(p).Count));
        Assert.Equal((Card("B5"), FinalCardColor.Blue, 0, 1), (game.TopCard, game.Color, game.Turn, game.Direction));
        Assert.Equal(108 - 15, game.DrawPileCount);
    }

    [Theory]
    [InlineData("RS", 1, 1, 7)]
    [InlineData("RR", 2, -1, 7)]
    [InlineData("R+2", 1, 1, 9)]
    public void An_action_card_turned_up_acts_on_the_first_player(string turnedUp, int turn, int direction, int firstHand)
    {
        var game = Deal(null, turnedUp, "", Reds, Reds, "B1 B2 B3 B4 B5 B6 B7");

        Assert.Equal((turn, direction, firstHand), (game.Turn, game.Direction, game.Hand(0).Count));
    }

    [Fact]
    public void A_wild_card_turned_up_goes_back_under_the_draw_pile()
    {
        var game = Deal(null, "W", "R5", "B1 B2 B3 B4 B6 B7 B8", Reds);

        Assert.Equal(Card("R5"), game.TopCard);
        Assert.Equal(108 - 15, game.DrawPileCount);
    }

    // Playing

    [Fact]
    public void A_card_must_match_the_colour_or_the_number_of_the_pile()
    {
        var game = Deal(null, "B5", "", "R5 B7 G2 Y9 R1 B1 G1", Reds);

        AssertRefused(game.TryPlay(1, Card("R2"), null, out var error), error, "It is not your turn.");
        AssertRefused(game.TryPlay(0, Card("R0"), null, out error), error, "You do not have that card.");
        AssertRefused(game.TryPlay(0, Card("G2"), null, out error), error, "That card does not match the colour or the symbol on the pile.");
        Assert.True(game.TryPlay(0, Card("R5"), null, out error), error);
        Assert.Equal((FinalCardColor.Red, 1, 6), (game.Color, game.Turn, game.Hand(0).Count));
        Assert.True(game.TryPlay(1, Card("R2"), null, out error), error);
    }

    [Fact]
    public void A_wild_card_sets_the_colour_and_a_wild_draw_four_needs_no_card_of_the_colour()
    {
        var game = Deal(null, "R5", "G5", "W W+4 R1 B1 G1 Y1 B2", Reds);

        AssertRefused(game.TryPlay(0, Card("W+4"), FinalCardColor.Blue, out var error), error,
            "You can play a Wild Draw Four only when you have no card of the colour to play.");
        AssertRefused(game.TryPlay(0, Card("W"), null, out error), error, "Choose a colour for the wild card.");
        Assert.True(game.TryPlay(0, Card("W"), FinalCardColor.Green, out error), error);
        Assert.Equal((Card("W"), FinalCardColor.Green), (game.TopCard, game.Color));
        AssertRefused(game.TryPlay(1, Card("R2"), null, out error), error, "That card does not match the colour or the symbol on the pile.");
    }

    // Drawing

    [Fact]
    public void A_player_who_draws_a_card_they_can_play_may_play_it_or_keep_it()
    {
        var game = Deal(null, "R5", "B5 G5", "B1 B2 B3 B4 B6 B7 B8", Reds);
        AssertRefused(game.TryPass(0, out var error), error, "Draw a card first.");

        Assert.True(game.TryDraw(0, out error), error);
        Assert.Equal((Card("B5"), 0), (game.DrawnCard, game.Turn));
        Assert.Equal([Card("B5")], game.Playable(0));
        AssertRefused(game.TryPlay(0, Card("B1"), null, out error), error, "You can play only the card you drew, or keep it.");
        AssertRefused(game.TryDraw(0, out error), error, "You have already drawn. Play the card you drew, or keep it.");
        Assert.True(game.TryPass(0, out error), error);
        Assert.Equal((null, 1, 8), (game.DrawnCard, game.Turn, game.Hand(0).Count));

        Assert.True(game.TryDraw(1, out error), error);
        Assert.True(game.TryPlay(1, Card("G5"), null, out error), error);
        Assert.Equal(0, game.Turn);
    }

    [Fact]
    public void A_player_who_draws_a_card_they_cannot_play_ends_their_turn()
    {
        var game = Deal(null, "R5", "G1", "B1 B2 B3 B4 B6 B7 B8", Reds);

        Assert.True(game.TryDraw(0, out var error), error);

        Assert.Equal((null, 1, 8), (game.DrawnCard, game.Turn, game.Hand(0).Count));
    }

    [Fact]
    public void With_the_option_a_player_draws_until_they_can_play()
    {
        var game = Deal(new FinalCardRules(DrawUntilPlayable: true), "R5", "G1 G2 R3", "B1 B2 B3 B4 B6 B7 B8", Reds);

        Assert.True(game.TryDraw(0, out var error), error);
        Assert.Equal(new FinalCardEvent(0, FinalCardEventKind.Drew, Count: 3), game.Events.Single());
        Assert.Equal(Card("R3"), game.DrawnCard);
        Assert.True(game.TryPlay(0, Card("R3"), null, out error), error);
        Assert.Equal(9, game.Hand(0).Count);
    }

    // Final Card

    [Fact]
    public void A_player_on_one_card_without_calling_final_card_can_be_caught_at_any_time_and_draws_2()
    {
        var game = DownToOne();
        Assert.True(game.CanBeCaught(0));
        Assert.True(game.TryPlay(1, Card("G0"), null, out var error), error);
        Assert.True(game.CanBeCaught(0));

        Assert.True(game.TryCallFinalCard(1, out error), error);

        Assert.Equal(3, game.Hand(0).Count);
        Assert.Equal(new FinalCardEvent(0, FinalCardEventKind.Caught, Count: 2, By: 1), game.Events[^1]);
        Assert.False(game.CanBeCaught(0));
        AssertRefused(game.TryCallFinalCard(1, out error), error, "Nobody to catch.");
    }

    [Fact]
    public void Calling_final_card_before_playing_the_second_to_last_card_or_on_the_final_card_protects_the_player()
    {
        var before = DownToTwo(final: "G5 B5");
        Assert.True(before.TryCallFinalCard(0, out var error), error);
        AssertRefused(before.TryCallFinalCard(0, out error), error, "Nobody to catch.");
        Assert.True(before.TryPlay(0, Card("G5"), null, out error), error);
        Assert.True(before.CalledFinalCard(0));
        Assert.False(before.CanBeCaught(0));
        AssertRefused(before.TryCallFinalCard(1, out error), error, "Nobody to catch.");

        var after = DownToOne();
        Assert.True(after.TryPlay(1, Card("G0"), null, out error), error);
        Assert.True(after.TryCallFinalCard(0, out error), error);
        Assert.Equal(new FinalCardEvent(0, FinalCardEventKind.CalledFinalCard), after.Events[^1]);
        Assert.False(after.CanBeCaught(0));
        AssertRefused(after.TryCallFinalCard(1, out error), error, "Nobody to catch.");
    }

    [Fact]
    public void One_call_protects_the_caller_and_catches_the_others()
    {
        var game = DownToOne("GS BS BS BR RR RR B1");
        foreach (var card in new[] { "GS", "BS", "BS", "BR", "RR", "RR" })
        {
            Assert.True(game.TryPlay(1, Card(card), null, out var error), error);
        }

        Assert.True(game.TryCallFinalCard(1, out var called), called);

        Assert.Equal(new FinalCardEvent(1, FinalCardEventKind.CalledFinalCard), game.Events[^2]);
        Assert.Equal(new FinalCardEvent(0, FinalCardEventKind.Caught, Count: 2, By: 1), game.Events[^1]);
        Assert.Equal((3, 1), (game.Hand(0).Count, game.Hand(1).Count));
        Assert.True(game.CalledFinalCard(1));
    }

    // Player 0 has played down to B5 without calling Final Card, and it is player 1's turn.
    private static FinalCardGame DownToOne(string second = "R1 R2 R3 B+2 W W+4 G0")
    {
        var game = DownToTwo(final: "G5 B5", second: second);
        Assert.True(game.TryPlay(0, Card("G5"), null, out var error), error);
        Assert.Equal((1, 1), (game.Hand(0).Count, game.Turn));
        return game;
    }

    // Player 0 holds their two final cards (by default GS and BS), and it is their turn (each Skip gave them another turn).
    // Player 1's cards (by default) count 126 points.
    private static FinalCardGame DownToTwo(FinalCardRules? rules = null, string final = "GS BS", string second = "R1 R2 R3 B+2 W W+4 G0")
    {
        var game = Deal(rules, "R9", "", $"RS RS YS YS GS {final}", second);
        foreach (var card in new[] { "RS", "RS", "YS", "YS", "GS" })
        {
            Assert.True(game.TryPlay(0, Card(card), null, out var error), error);
        }

        return game;
    }

    private static void PlayLastTwo(FinalCardGame game)
    {
        Assert.True(game.TryPlay(0, Card("GS"), null, out var error), error);
        Assert.True(game.TryPlay(0, Card("BS"), null, out error), error);
    }

    // Ending

    [Fact]
    public void The_winner_of_a_hand_scores_the_cards_left_and_the_next_player_starts_the_next_hand()
    {
        var game = DownToTwo(new FinalCardRules(FinalCardEndMode.Score, Target: 500));
        PlayLastTwo(game);

        Assert.Equal(FinalCardPhase.HandOver, game.Phase);
        Assert.Equal(new FinalCardHandResult(0, 126), game.LastResult);
        Assert.Equal([126, 0], game.Scores);
        Assert.Equal(new FinalCardEvent(0, FinalCardEventKind.Finished, Count: 126), game.Events[^1]);
        AssertRefused(game.TryDraw(1, out var error), error, "This hand is over.");

        Assert.True(game.TryNextHand(1, out error), error);
        Assert.Equal((2, FinalCardPhase.Playing, 1), (game.HandNumber, game.Phase, game.Turn));
        Assert.All(Enumerable.Range(0, 2), p => Assert.Equal(7, game.Hand(p).Count));
        Assert.Equal([126, 0], game.Scores);
        Assert.True(game.TryNextHand(1, out error), error);
        Assert.Equal(2, game.HandNumber);
        AssertRefused(game.TryNextHand(2, out error), error, "This hand is not finished yet.");
    }

    [Theory]
    [InlineData(FinalCardEndMode.Score, 100, 126)]
    [InlineData(FinalCardEndMode.OneGame, 500, 0)]
    public void The_game_ends_at_the_score_target_or_after_one_hand(FinalCardEndMode end, int target, int score)
    {
        var game = DownToTwo(new FinalCardRules(end, target));
        PlayLastTwo(game);

        Assert.True(game.IsOver);
        Assert.Equal(0, game.Winner);
        Assert.Equal(score, game.Scores[0]);
        AssertRefused(game.TryDraw(1, out var error), error, "The game is over.");
    }

    [Fact]
    public void With_stacking_the_cards_stacked_for_the_next_player_count_for_the_winner()
    {
        var game = Deal(new FinalCardRules(FinalCardEndMode.Score, Stacking: true), "R9", "", "RS RS YS YS GS GS G+2", "R1 R2 R3 B+2 W W+4 G0");
        foreach (var card in new[] { "RS", "RS", "YS", "YS", "GS", "GS", "G+2" })
        {
            Assert.True(game.TryPlay(0, Card(card), null, out var error), error);
        }

        Assert.Equal(9, game.Hand(1).Count);
        Assert.Equal(0, game.PendingDraw);
        Assert.Equal(126 + game.Hand(1).Skip(7).Sum(c => c.Points), game.LastResult!.Points);
    }

    [Fact]
    public void In_a_pyramid_each_player_gets_one_card_fewer_each_time_until_everyone_is_ranked()
    {
        var game = new FinalCardGame(3, new FinalCardRules(FinalCardEndMode.Pyramid), new Random(3).Shuffle);
        var dealt = Enumerable.Range(0, 3).Select(_ => new List<int>()).ToArray();

        PlayOut(game, () =>
        {
            foreach (var e in game.Events.Where(e => e.Kind == FinalCardEventKind.NewCards))
            {
                dealt[e.Player].Add(e.Count);
                Assert.Equal(e.Count, game.HandSize(e.Player));
            }
        });

        Assert.Equal(3, game.Ranking.Distinct().Count());
        Assert.Equal(game.Ranking[0], game.Winner);
        Assert.Equal(1, game.HandNumber);
        foreach (var player in game.Ranking.Take(2))
        {
            Assert.Equal([6, 5, 4, 3, 2, 1], dealt[player]);
            Assert.Empty(game.Hand(player));
        }
    }

    [Fact]
    public void A_game_to_a_score_target_is_played_hand_after_hand_until_a_player_reaches_it()
    {
        var game = new FinalCardGame(4, new FinalCardRules(FinalCardEndMode.Score, 200, Stacking: true, DrawUntilPlayable: true), new Random(5).Shuffle);

        PlayOut(game);

        Assert.True(game.Scores[game.Winner!.Value] >= 200);
        Assert.All(game.Scores.Where((_, p) => p != game.Winner), score => Assert.True(score < 200));
    }

    // Plays to the end: the player to play plays their first card they can (calling Final Card first), otherwise draws.
    private static void PlayOut(FinalCardGame game, Action? afterEach = null)
    {
        for (var actions = 0; actions < 20000 && !game.IsOver; actions++)
        {
            string? error;
            if (game.Phase == FinalCardPhase.HandOver)
            {
                Assert.True(game.TryNextHand(game.HandNumber, out error), error);
                continue;
            }

            var player = game.Turn;
            var playable = game.Playable(player);
            if (playable.Count == 0)
            {
                Assert.True(game.TryDraw(player, out error), error);
            }
            else
            {
                if (game.Hand(player).Count == 2)
                {
                    game.TryCallFinalCard(player, out _);
                }

                Assert.True(game.TryPlay(player, playable[0], FinalCardColor.Red, out error), error);
            }

            afterEach?.Invoke();
        }

        Assert.True(game.IsOver);
    }

    [Fact]
    public void A_wild_draw_four_makes_the_next_player_draw_4_and_miss_their_turn()
    {
        var game = Deal(null, "R5", "", "W+4 B1 B2 B3 B4 B6 B7", Reds);

        Assert.True(game.TryPlay(0, Card("W+4"), FinalCardColor.Yellow, out var error), error);

        Assert.Equal((11, 0, FinalCardColor.Yellow), (game.Hand(1).Count, game.Turn, game.Color));
        Assert.Equal([FinalCardEventKind.Played, FinalCardEventKind.Drew, FinalCardEventKind.Skipped], game.Events.Select(e => e.Kind));
        Assert.Equal(new FinalCardEvent(1, FinalCardEventKind.Drew, Count: 4), game.Events[1]);
    }

    [Fact]
    public void Skip_passes_over_the_next_player_and_reverse_turns_the_direction()
    {
        var game = Deal(null, "R9", "", "RS R1 B2 B3 B4 B6 B7", "R7 B1 G2 G3 G4 G6 G7", "RR R6 Y1 Y2 Y3 Y4 Y6");

        Assert.True(game.TryPlay(0, Card("RS"), null, out var error), error);
        Assert.Equal(2, game.Turn);
        Assert.Equal(new FinalCardEvent(1, FinalCardEventKind.Skipped), game.Events[1]);
        Assert.True(game.TryPlay(2, Card("RR"), null, out error), error);
        Assert.Equal((1, -1), (game.Turn, game.Direction));
        Assert.True(game.TryPlay(1, Card("R7"), null, out error), error);
        Assert.Equal(0, game.Turn);
    }

    [Fact]
    public void With_two_players_a_reverse_gives_another_turn()
    {
        var game = Deal(null, "R9", "", "RR R1 B2 B3 B4 B6 B7", Reds);

        Assert.True(game.TryPlay(0, Card("RR"), null, out var error), error);

        Assert.Equal(0, game.Turn);
    }

    [Fact]
    public void With_stacking_draw_cards_add_up_for_the_next_player()
    {
        var game = Deal(new FinalCardRules(Stacking: true), "R9", "", "R+2 G1 G2 G3 G4 G6 G7", "B+2 R1 B2 B3 B4 B6 B7");

        Assert.True(game.TryPlay(0, Card("R+2"), null, out var error), error);
        Assert.Equal((2, 1, 7), (game.PendingDraw, game.Turn, game.Hand(1).Count));
        AssertRefused(game.TryPlay(1, Card("R1"), null, out error), error, "Answer with the same draw card, or draw the cards.");
        Assert.True(game.TryPlay(1, Card("B+2"), null, out error), error);
        Assert.Equal((4, 0), (game.PendingDraw, game.Turn));

        Assert.True(game.TryDraw(0, out error), error);
        Assert.Equal((0, 1, 10), (game.PendingDraw, game.Turn, game.Hand(0).Count));
    }

    // A game whose deck gives each player their 7 cards (short text, separated by spaces), turns up turnedUp, and then has
    // drawPile on top of the draw pile.
    private static FinalCardGame Deal(FinalCardRules? rules, string turnedUp, string drawPile, params string[] hands) =>
        new(hands.Length, rules, Stacked(string.Join(' ', [.. hands, turnedUp, drawPile])));

    // A shuffle that puts the given cards first, in order, the first time; later shuffles leave the cards as they are.
    private static Action<FinalCardCard[]> Stacked(string cards)
    {
        var used = false;
        return deck =>
        {
            if (used)
            {
                return;
            }

            used = true;
            var first = cards.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card).ToList();
            var rest = deck.ToList();
            Assert.All(first, card => Assert.True(rest.Remove(card), $"The deck has no more {card}."));
            first.Concat(rest).ToArray().CopyTo(deck, 0);
        };
    }

    private static FinalCardCard Card(string text)
    {
        Assert.True(FinalCardCard.TryParse(text, out var card), text);
        return card;
    }

    private static void AssertRefused(bool done, string? error, string expected)
    {
        Assert.False(done);
        Assert.Equal(expected, error);
    }
}
