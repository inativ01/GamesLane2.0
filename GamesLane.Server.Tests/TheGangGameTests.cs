using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Tests;

public class TheGangGameTests
{
    // Hand ranking

    [Theory]
    [InlineData("AS KS QS JS 10S", HandCategory.StraightFlush, "Royal flush")]
    [InlineData("9H 8H 7H 6H 5H", HandCategory.StraightFlush, "Straight flush, nine high")]
    [InlineData("QC QD QH QS 2C", HandCategory.FourOfAKind, "Four queens (2)")]
    [InlineData("6C 6D 6H 4S 4C", HandCategory.FullHouse, "Full house, sixes over fours")]
    [InlineData("AD JD 9D 6D 2D", HandCategory.Flush, "Flush, ace high (J 9 6 2)")]
    [InlineData("10C 9D 8H 7S 6C", HandCategory.Straight, "Straight, ten high")]
    [InlineData("AC 2D 3H 4S 5C", HandCategory.Straight, "Straight, five high")]
    [InlineData("7C 7D 7H KS 2C", HandCategory.ThreeOfAKind, "Three sevens (K 2)")]
    [InlineData("KC KD 7H 7S 2C", HandCategory.TwoPair, "Two pair, kings and sevens (2)")]
    [InlineData("JC JD 9H 5S 2C", HandCategory.OnePair, "Pair of jacks (9 5 2)")]
    [InlineData("AC QD 9H 5S 2C", HandCategory.HighCard, "High card, ace (Q 9 5 2)")]
    public void Recognises_each_hand(string cards, HandCategory category, string name)
    {
        var hand = HandEvaluator.Evaluate(Cards(cards));

        Assert.Equal(category, hand.Category);
        Assert.Equal(name, hand.Name);
    }

    [Theory]
    [InlineData("AC KD QH JS 9C", "2C 2D 5H 7S 9C")]
    [InlineData("AC KD QH JS 8C", "AD KH QS JC 9D")]
    [InlineData("KC KD 7H 7S 2C", "KH KS 7C 7D 3C")]
    [InlineData("AC 2D 3H 4S 5C", "2C 3D 4H 5S 6C")]
    [InlineData("AD JD 9D 6D 2D", "10C 10D 10H 10S 2C")]
    [InlineData("9C 9D 9H 2S 2C", "9C 9D 9H 3S 3C")]
    public void Ranks_weaker_hands_below_stronger_ones(string weaker, string stronger)
    {
        Assert.True(HandEvaluator.Evaluate(Cards(weaker)).IsWeakerThan(HandEvaluator.Evaluate(Cards(stronger))));
        Assert.False(HandEvaluator.Evaluate(Cards(stronger)).IsWeakerThan(HandEvaluator.Evaluate(Cards(weaker))));
    }

    [Fact]
    public void Hands_differing_only_in_suits_are_equal()
    {
        var first = HandEvaluator.Evaluate(Cards("AC KD 9H 5S 2C"));
        var second = HandEvaluator.Evaluate(Cards("AD KH 9S 5C 2D"));

        Assert.Equal(first, second);
        Assert.False(first.IsWeakerThan(second));
    }

    [Fact]
    public void Uses_the_best_five_of_seven_cards()
    {
        var hand = HandEvaluator.Best(Cards("2H 7D KS KC 7S 7H 3C"));

        Assert.Equal(HandCategory.FullHouse, hand.Category);
        Assert.Equal("Full house, sevens over kings", hand.Name);
    }

    [Fact]
    public void Names_show_the_kickers_that_decide_between_equal_kinds()
    {
        var weaker = HandEvaluator.Best(Cards("2C 4D JH JS 9C 7D 5H"));
        var stronger = HandEvaluator.Best(Cards("AC 3D JH JS 9C 7D 5H"));

        Assert.True(weaker.IsWeakerThan(stronger));
        Assert.Equal("Pair of jacks (9 7 5)", weaker.Name);
        Assert.Equal("Pair of jacks (A 9 7)", stronger.Name);
    }

    [Fact]
    public void Shared_cards_can_make_everyone_equal()
    {
        var board = "AS KS QS JS 10S";

        Assert.Equal(HandEvaluator.Best(Cards($"2C 3D {board}")), HandEvaluator.Best(Cards($"AH AD {board}")));
    }

    // Betting

    [Fact]
    public void A_heist_starts_with_no_shared_cards_and_all_chips_in_the_middle()
    {
        var game = NewGame();

        Assert.Equal(1, game.Heist);
        Assert.Equal(0, game.Stage);
        Assert.Equal(TheGangPhase.Betting, game.Phase);
        Assert.Empty(game.VisibleCommunityCards);
        Assert.Equal([1, 2, 3], game.MiddleChips);
        Assert.Equal(2, game.HoleCards(0).Count);
    }

    [Fact]
    public void Taking_a_chip_from_another_player_leaves_them_without_one()
    {
        var game = NewGame();
        Assert.True(game.TryTakeChip(0, 3, out _));
        Assert.True(game.TryTakeChip(1, 3, out _));

        Assert.Null(game.Chip(0, 0));
        Assert.Equal(3, game.Chip(0, 1));
        Assert.Equal([1, 2], game.MiddleChips);
    }

    [Fact]
    public void Taking_another_chip_returns_the_old_one_and_chips_can_be_put_back()
    {
        var game = NewGame();
        game.TryTakeChip(0, 1, out _);
        game.TryTakeChip(0, 2, out _);

        Assert.Equal(2, game.Chip(0, 0));
        Assert.Equal([1, 3], game.MiddleChips);
        Assert.True(game.TryReturnChip(0, out _));
        Assert.Equal([1, 2, 3], game.MiddleChips);
        Assert.False(game.TryReturnChip(0, out var error));
        Assert.Equal("You have no chip to return.", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Rejects_chips_that_do_not_exist(int chip)
    {
        Assert.False(NewGame().TryTakeChip(0, chip, out var error));
        Assert.Equal("Chips are numbered 1 to 3.", error);
    }

    [Fact]
    public void Ready_needs_a_chip_and_any_chip_change_clears_it()
    {
        var game = NewGame();
        Assert.False(game.TrySetReady(0, true, out var error));
        Assert.Equal("Take a chip first.", error);

        game.TryTakeChip(0, 1, out _);
        game.TrySetReady(0, true, out _);
        Assert.True(game.IsReady(0));

        game.TryTakeChip(1, 2, out _);
        Assert.False(game.IsReady(0));
    }

    [Fact]
    public void Each_stage_ends_when_everyone_holds_a_chip_and_is_ready_and_shows_more_shared_cards()
    {
        var game = NewGame();
        int[] chips = [2, 3, 1];
        int[] sharedCards = [3, 4, 5];
        for (var stage = 0; stage < 3; stage++)
        {
            PlayStage(game, chips);
            Assert.Equal(stage + 1, game.Stage);
            Assert.Equal(sharedCards[stage], game.VisibleCommunityCards.Count);
            Assert.All(Enumerable.Range(0, 3), p => Assert.False(game.IsReady(p)));
            Assert.Equal(chips, Enumerable.Range(0, 3).Select(p => game.Chip(stage, p) ?? 0));
            Assert.Equal([1, 2, 3], game.MiddleChips);
        }
    }

    [Fact]
    public void A_stage_does_not_end_while_a_chip_is_in_the_middle()
    {
        var game = NewGame();
        game.TryTakeChip(0, 1, out _);
        game.TryTakeChip(1, 2, out _);
        game.TrySetReady(0, true, out _);
        game.TrySetReady(1, true, out _);

        Assert.Equal(0, game.Stage);
        Assert.False(game.TrySetReady(2, true, out _));
    }

    // Revealing

    [Fact]
    public void Hands_ranked_correctly_open_a_vault()
    {
        var game = NewGame();
        PlayAllStages(game, [1, 2, 3]);

        Assert.Equal(TheGangPhase.Reveal, game.Phase);
        Assert.Equal(5, game.VisibleCommunityCards.Count);
        Assert.Equal([0, 1, 2], game.RevealOrder);

        RevealAll(game);
        Assert.Equal(TheGangPhase.HeistOver, game.Phase);
        Assert.Equal([true, true, true], game.RevealedInOrder);
        Assert.Equal((1, 0), (game.Vaults, game.Alarms));
    }

    [Fact]
    public void A_weaker_hand_after_a_stronger_one_sets_off_an_alarm()
    {
        var game = NewGame();
        PlayAllStages(game, [2, 1, 3]);

        Assert.Equal([1, 0, 2], game.RevealOrder);
        RevealAll(game);
        Assert.Equal([true, false, true], game.RevealedInOrder);
        Assert.Equal((0, 1), (game.Vaults, game.Alarms));
    }

    [Fact]
    public void Equal_hands_may_be_in_either_order()
    {
        var game = new TheGangGame(3, () => Cards("2C 3D 4C 5D 6C 7D AS KS QS JS 10S"));
        PlayAllStages(game, [3, 2, 1]);
        RevealAll(game);

        Assert.Equal(1, game.Vaults);
    }

    [Fact]
    public void Revealing_one_hand_at_a_time_ignores_repeated_presses()
    {
        var game = NewGame();
        PlayAllStages(game, [1, 2, 3]);

        Assert.True(game.TryRevealNext(0, out _));
        Assert.True(game.TryRevealNext(0, out _));
        Assert.Equal(1, game.Revealed);
        Assert.Equal([true], game.RevealedInOrder);
    }

    [Fact]
    public void No_chips_can_be_taken_while_revealing()
    {
        var game = NewGame();
        PlayAllStages(game, [1, 2, 3]);

        Assert.False(game.TryTakeChip(0, 2, out var error));
        Assert.Equal("The betting is over for this heist.", error);
    }

    // Heists and the end of the game

    [Fact]
    public void The_next_heist_deals_new_cards_and_clears_the_chips_once()
    {
        var game = NewGame();
        PlayAllStages(game, [1, 2, 3]);
        Assert.False(game.TryNextHeist(1, out var error));
        Assert.Equal("The heist is not over yet.", error);
        RevealAll(game);

        Assert.True(game.TryNextHeist(1, out _));
        Assert.True(game.TryNextHeist(1, out _));

        Assert.Equal(2, game.Heist);
        Assert.Equal(TheGangPhase.Betting, game.Phase);
        Assert.Equal([1, 2, 3], game.MiddleChips);
        Assert.Null(game.Chip(3, 0));
    }

    [Fact]
    public void Three_vaults_win_and_three_alarms_lose()
    {
        var won = NewGame();
        var lost = NewGame();
        for (var heist = 1; heist <= 3; heist++)
        {
            PlayAllStages(won, [1, 2, 3]);
            RevealAll(won);
            PlayAllStages(lost, [3, 2, 1]);
            RevealAll(lost);
            Assert.Equal(heist == 3, won.IsOver);
            won.TryNextHeist(heist, out _);
            lost.TryNextHeist(heist, out _);
        }

        Assert.Equal((3, 0), (won.Vaults, won.Alarms));
        Assert.Equal((0, 3), (lost.Vaults, lost.Alarms));
        Assert.True(won.IsOver && lost.IsOver);
        Assert.False(won.TryNextHeist(3, out var error));
        Assert.Equal("The game is over.", error);
    }

    // Player 0: high card king; player 1: pair of nines; player 2: three kings.
    private const string RankedDeck = "3D 4H 9C 5S KD KH 2C 7D 9H JS KC";

    private static TheGangGame NewGame() => new(3, () => Cards(RankedDeck));

    private static Card[] Cards(string cards) => cards.Split(' ').Select(Card.Parse).ToArray();

    private static void PlayStage(TheGangGame game, int[] chips)
    {
        for (var player = 0; player < chips.Length; player++)
        {
            Assert.True(game.TryTakeChip(player, chips[player], out var error), error);
        }

        for (var player = 0; player < chips.Length; player++)
        {
            Assert.True(game.TrySetReady(player, true, out var error), error);
        }
    }

    private static void PlayAllStages(TheGangGame game, int[] chips)
    {
        for (var stage = 0; stage < TheGangGame.StageCount; stage++)
        {
            PlayStage(game, chips);
        }
    }

    private static void RevealAll(TheGangGame game)
    {
        while (game.Phase == TheGangPhase.Reveal)
        {
            Assert.True(game.TryRevealNext(game.Revealed, out var error), error);
        }
    }
}
