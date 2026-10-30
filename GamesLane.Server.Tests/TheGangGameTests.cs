using GamesLane.Server.Games;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tests;

public class TheGangGameTests
{
    // Hand ranking

    [Theory]
    [InlineData("AS KS QS JS 10S", HandCategory.StraightFlush, "Royal flush")]
    [InlineData("9H 8H 7H 6H 5H", HandCategory.StraightFlush, "Straight flush, nine high")]
    [InlineData("QC QD QH QS 2C", HandCategory.FourOfAKind, "Four queens")]
    [InlineData("6C 6D 6H 4S 4C", HandCategory.FullHouse, "Full house, sixes over fours")]
    [InlineData("AD JD 9D 6D 2D", HandCategory.Flush, "Flush, ace high")]
    [InlineData("10C 9D 8H 7S 6C", HandCategory.Straight, "Straight, ten high")]
    [InlineData("AC 2D 3H 4S 5C", HandCategory.Straight, "Straight, five high")]
    [InlineData("7C 7D 7H KS 2C", HandCategory.ThreeOfAKind, "Three sevens")]
    [InlineData("KC KD 7H 7S 2C", HandCategory.TwoPair, "Two pair, kings and sevens")]
    [InlineData("JC JD 9H 5S 2C", HandCategory.OnePair, "Pair of jacks")]
    [InlineData("AC QD 9H 5S 2C", HandCategory.HighCard, "High card, ace")]
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
    public void Kickers_decide_between_hands_with_the_same_name()
    {
        var weaker = HandEvaluator.Best(Cards("2C 4D JH JS 9C 7D 5H"));
        var stronger = HandEvaluator.Best(Cards("AC 3D JH JS 9C 7D 5H"));

        Assert.True(weaker.IsWeakerThan(stronger));
        Assert.Equal("Pair of jacks", weaker.Name);
        Assert.Equal("Pair of jacks", stronger.Name);
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
    public void Ready_waits_until_everyone_has_a_chip_and_any_chip_change_clears_it()
    {
        var game = NewGame();
        game.TryTakeChip(0, 1, out _);
        game.TryTakeChip(1, 2, out _);
        Assert.False(game.TrySetReady(0, true, out var error));
        Assert.Equal("Wait until everyone has a chip.", error);

        game.TryTakeChip(2, 3, out _);
        Assert.True(game.TrySetReady(0, true, out _));
        Assert.True(game.IsReady(0));

        game.TryTakeChip(1, 1, out _);
        Assert.False(game.IsReady(0));
        Assert.False(game.TrySetReady(0, true, out _));
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

    // Challenge and specialist cards

    [Fact]
    public void Basic_mode_draws_no_cards()
    {
        var rules = new TheGangRules(TheGangMode.Basic, 3, [TheGangCards.Blackout], [TheGangCards.Investor]);
        var game = new TheGangGame(3, () => Cards(RankedDeck), rules);

        Assert.Empty(game.Cards);
        Assert.Equal(TheGangMode.Basic, game.Mode);
    }

    [Fact]
    public void The_first_heist_gets_the_chosen_number_of_challenge_cards_in_any_combination()
    {
        string[] challenges = [TheGangCards.QuickAccess, TheGangCards.MotionDetector, TheGangCards.HastyGetaway];
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(3, challenges));

        Assert.Equal(challenges.Order(), game.Cards.Select(c => c.Key).Order());
        Assert.All(game.Cards, c => Assert.Equal(TheGangCardKind.Challenge, c.Kind));
        Assert.Equal([0, 2], game.SkippedStages);
    }

    [Fact]
    public void Avoiding_clashes_keeps_quick_access_apart_from_the_white_chip_cards()
    {
        string[] challenges = [TheGangCards.QuickAccess, TheGangCards.MotionDetector, TheGangCards.LaserTripwires];
        for (var seed = 0; seed < 20; seed++)
        {
            var rules = new TheGangRules(TheGangMode.Advanced, 3, challenges, [], AvoidClashes: true);
            var keys = new TheGangGame(3, () => Cards(RankedDeck), rules, new Random(seed)).Cards.Select(c => c.Key).ToArray();

            Assert.True(keys is [TheGangCards.QuickAccess] || !keys.Contains(TheGangCards.QuickAccess), string.Join(", ", keys));
        }
    }

    [Fact]
    public void With_quick_access_the_white_chip_cards_have_no_effect()
    {
        var deck = "3D 4H 9C 5S KD KH JC 7D 9H JS KC AS AD";
        var game = new TheGangGame(3, () => Cards(deck), Advanced(2, [TheGangCards.QuickAccess, TheGangCards.MotionDetector]));
        var before = game.HoleCards(0).ToArray();

        PlayStage(game, [1, 2, 3]);

        Assert.Null(game.RedrawnPlayer);
        Assert.Equal(before, game.HoleCards(0));
    }

    [Fact]
    public void Cards_are_added_after_each_heist_and_stay_in_play_each_drawn_once()
    {
        var rules = Advanced(0, [TheGangCards.Blackout], [TheGangCards.Investor]);
        var game = new TheGangGame(3, () => Cards(RankedDeck), rules);
        Assert.Empty(game.Cards);

        PlayHeist(game, [1, 2, 3]);
        Assert.Equal(TheGangCards.Blackout, Assert.Single(game.Cards).Key);
        Assert.Equal([TheGangCards.Blackout], game.NewCards);

        PlayHeist(game, [3, 2, 1]);
        Assert.Equal([TheGangCards.Blackout, TheGangCards.Investor], game.Cards.Select(c => c.Key));
        Assert.Equal([TheGangCards.Investor], game.NewCards);

        PlayHeist(game, [1, 2, 3]);
        Assert.Equal(2, game.Cards.Count);
        Assert.Empty(game.NewCards);
    }

    [Fact]
    public void Avoiding_clashes_also_considers_the_cards_already_in_play()
    {
        var rules = new TheGangRules(TheGangMode.Advanced, 1, [TheGangCards.QuickAccess, TheGangCards.MotionDetector], [], AvoidClashes: true);
        var game = new TheGangGame(3, () => Cards(RankedDeck), rules);
        var first = Assert.Single(game.Cards).Key;

        PlayHeist(game, [1, 2, 3]);

        Assert.Equal([first], game.Cards.Select(c => c.Key));
    }

    [Fact]
    public void Coordinator_then_con_artist_happen_by_themselves_before_the_betting()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(0, [], [TheGangCards.ConArtist, TheGangCards.Coordinator]));
        FailHeist(game);
        FailHeist(game);
        Assert.Equal(2, game.Cards.Count);
        Assert.Empty(game.Decisions);

        Assert.Equal((TheGangPhase.Preparing, TheGangCards.Coordinator), (game.Phase, game.PreparingCard));
        Enumerable.Range(0, 3).ToList().ForEach(p => Assert.True(game.TryPassCard(p, 0, out _)));
        Assert.Equal((TheGangPhase.Preparing, TheGangCards.ConArtist), (game.Phase, game.PreparingCard));
        Enumerable.Range(0, 3).ToList().ForEach(p => Assert.True(game.TryConfirmSeen(p, out _)));

        Assert.Equal((TheGangPhase.Betting, 0), (game.Phase, game.Stage));
    }

    [Fact]
    public void Investor_and_math_whiz_together_show_both_facts()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(0, [], [TheGangCards.Investor, TheGangCards.MathWhiz]));
        PlayHeist(game, [3, 2, 1]);
        PlayHeist(game, [3, 2, 1]);

        Assert.Equal("2 face cards · 20 total", game.Announcement(2));
    }

    [Fact]
    public void Investor_announces_the_cards_held_when_the_betting_starts()
    {
        // Player 0: 3D 4H; player 1: 5S KD; player 2: KH 2C. Coordinator passes each first card to the next player.
        var game = new TheGangGame(3, () => Cards("3D 4H 5S KD KH 2C 7D 9H JS KC QD"),
            Advanced(0, [], [TheGangCards.Investor, TheGangCards.Coordinator]));
        FailHeist(game);
        FailHeist(game);
        Assert.Equal(2, game.Cards.Count);
        Assert.Null(game.Announcement(0));

        Enumerable.Range(0, 3).ToList().ForEach(p => Assert.True(game.TryPassCard(p, 0, out _)));

        Assert.Equal(["1 face card", "1 face card", "0 face cards"], Enumerable.Range(0, 3).Select(game.Announcement));
    }

    [Fact]
    public void Quick_access_starts_with_three_shared_cards_and_yellow_chips()
    {
        var game = WithChallenge(TheGangCards.QuickAccess);

        Assert.Equal(1, game.Stage);
        Assert.Equal(3, game.VisibleCommunityCards.Count);
        Assert.Equal([0], game.SkippedStages);
        PlayAllStages(game, [1, 2, 3]);
        Assert.Null(game.Chip(0, 0));
        Assert.Equal(TheGangPhase.Reveal, game.Phase);
    }

    [Fact]
    public void Hasty_getaway_goes_from_yellow_straight_to_red()
    {
        var game = WithChallenge(TheGangCards.HastyGetaway);
        PlayStage(game, [1, 2, 3]);
        PlayStage(game, [1, 2, 3]);

        Assert.Equal(3, game.Stage);
        Assert.Equal(5, game.VisibleCommunityCards.Count);
    }

    [Fact]
    public void Noise_sensors_lock_the_one_star_chip_until_the_red_stage()
    {
        var game = WithChallenge(TheGangCards.NoiseSensors);
        Assert.True(game.TryTakeChip(0, 1, out _));

        Assert.Equal([1], game.LockedChips);
        Assert.False(game.TryTakeChip(1, 1, out var error));
        Assert.Equal("That chip is locked: whoever took it first keeps it.", error);
        Assert.False(game.TryReturnChip(0, out error));
        Assert.Equal("Your chip is locked: you keep it for this stage.", error);
        Assert.False(game.TryTakeChip(0, 2, out _));

        game.TryTakeChip(1, 2, out _);
        game.TryTakeChip(2, 3, out _);
        Enumerable.Range(0, 3).ToList().ForEach(p => game.TrySetReady(p, true, out _));
        PlayStage(game, [1, 2, 3]);
        PlayStage(game, [1, 2, 3]);
        Assert.Equal(3, game.Stage);
        Assert.Empty(game.LockedChips);
        Assert.True(game.TryTakeChip(0, 1, out _));
        Assert.True(game.TryTakeChip(1, 1, out _));
    }

    [Fact]
    public void Ventilation_shaft_locks_the_highest_chip()
    {
        var game = WithChallenge(TheGangCards.VentilationShaft);
        game.TryTakeChip(0, 3, out _);

        Assert.Equal([3], game.LockedChips);
        Assert.False(game.TryTakeChip(1, 3, out _));
        Assert.True(game.TryTakeChip(1, 1, out _));
        Assert.True(game.TryTakeChip(2, 1, out _));
    }

    [Theory]
    // First 3 shared cards 2C 7D 9H: no face card, so only Laser Tripwires goes off (highest white chip: player 2).
    [InlineData(TheGangCards.LaserTripwires, RankedDeck + " AS AD", 2)]
    [InlineData(TheGangCards.MotionDetector, RankedDeck + " AS AD", null)]
    // First 3 shared cards JC 7D 9H: a face card, so only Motion Detector goes off (1-star white chip: player 0).
    [InlineData(TheGangCards.MotionDetector, "3D 4H 9C 5S KD KH JC 7D 9H JS KC AS AD", 0)]
    [InlineData(TheGangCards.LaserTripwires, "3D 4H 9C 5S KD KH JC 7D 9H JS KC AS AD", null)]
    public void Alarm_cards_give_new_private_cards_when_the_first_shared_cards_appear(string challenge, string deck, int? redrawn)
    {
        var game = new TheGangGame(3, () => Cards(deck), Advanced(1, [challenge]));
        var before = Enumerable.Range(0, 3).Select(p => game.HoleCards(p).ToArray()).ToArray();

        PlayStage(game, [1, 2, 3]);

        Assert.Equal(redrawn, game.RedrawnPlayer);
        for (var player = 0; player < 3; player++)
        {
            Assert.Equal(player == redrawn ? Cards("AS AD") : before[player], game.HoleCards(player));
        }
    }

    [Fact]
    public void Blackout_removes_each_stages_chips_when_the_next_starts()
    {
        var game = WithChallenge(TheGangCards.Blackout);
        Assert.False(game.AreChipsDiscarded(0));

        PlayStage(game, [1, 2, 3]);
        Assert.True(game.AreChipsDiscarded(0));
        Assert.False(game.AreChipsDiscarded(1));
    }

    [Fact]
    public void Security_cameras_deal_three_private_cards_and_count_the_best_five_of_eight()
    {
        // Player 0: 3D 4H 9C; player 1: 5S KD KH; player 2: 2C 7D 9H; shared: JS KC 9S 9D 2D.
        var game = new TheGangGame(3, () => Cards("3D 4H 9C 5S KD KH 2C 7D 9H JS KC 9S 9D 2D"),
            Advanced(1, [TheGangCards.SecurityCameras]));

        Assert.All(Enumerable.Range(0, 3), p => Assert.Equal(3, game.HoleCards(p).Count));
        PlayAllStages(game, [1, 2, 3]);
        Assert.Equal(Cards("JS KC 9S 9D 2D"), game.VisibleCommunityCards);
        Assert.Equal("Full house, kings over nines", game.Hand(1).Name);
    }

    [Theory]
    [InlineData(TheGangCards.Investor, "0 face cards", "0 face cards", "2 face cards")]
    [InlineData(TheGangCards.MathWhiz, "7 total", "14 total", "20 total")]
    public void Specialists_show_everyone_a_fact_about_each_hand(string specialist, string first, string second, string third)
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck + " AS AD"), Advanced(0, [], [specialist]));
        Assert.All(Enumerable.Range(0, 3), p => Assert.Null(game.Announcement(p)));

        PlayHeist(game, [3, 2, 1]);
        Assert.Equal([first, second, third], Enumerable.Range(0, 3).Select(game.Announcement));
    }

    [Fact]
    public void Coordinator_everyone_passes_a_card_to_the_next_player_before_the_betting()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(0, [], [TheGangCards.Coordinator]));
        PlayHeist(game, [3, 2, 1]);

        Assert.Equal(TheGangPhase.Preparing, game.Phase);
        Assert.False(game.TryTakeChip(0, 2, out var error));
        Assert.Equal("The betting starts once everyone has done the specialist card's step.", error);

        Assert.True(game.TryPassCard(0, 1, out _));
        Assert.True(game.TryPassCard(0, 0, out _));
        Assert.Equal(Card.Parse("3D"), game.PassingCard(0));
        Assert.True(game.IsPrepared(0));
        Assert.True(game.TryPassCard(1, 0, out _));
        Assert.Equal(TheGangPhase.Preparing, game.Phase);
        Assert.True(game.TryPassCard(2, 1, out _));

        Assert.Equal(TheGangPhase.Betting, game.Phase);
        Assert.Equal(Cards("4H KH"), game.HoleCards(0));
        Assert.Equal(Cards("5S 3D"), game.HoleCards(1));
        Assert.Equal(Cards("KD 9C"), game.HoleCards(2));
        Assert.False(game.TryPassCard(0, 0, out _));
    }

    [Fact]
    public void Con_artist_reshuffles_the_private_cards_once_everyone_has_seen_them()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(0, [], [TheGangCards.ConArtist]), new Random(7));
        PlayHeist(game, [3, 2, 1]);
        var before = Enumerable.Range(0, 3).SelectMany(game.HoleCards).ToArray();

        Assert.True(game.TryConfirmSeen(0, out _));
        Assert.True(game.TryConfirmSeen(1, out _));
        Assert.Equal(TheGangPhase.Preparing, game.Phase);
        Assert.False(game.TryPassCard(2, 0, out _));
        Assert.True(game.TryConfirmSeen(2, out _));

        Assert.Equal(TheGangPhase.Betting, game.Phase);
        Assert.All(Enumerable.Range(0, 3), p => Assert.Equal(2, game.HoleCards(p).Count));
        Assert.Equal(before.Select(c => c.ToString()).Order(), Enumerable.Range(0, 3).SelectMany(game.HoleCards).Select(c => c.ToString()).Order());
    }

    // Group decisions

    [Fact]
    public void A_group_decision_takes_effect_once_everyone_agrees_to_the_latest_proposal()
    {
        var game = WithSpecialist(TheGangCards.GetawayDriver);

        Assert.False(game.TryAgree(0, TheGangCards.GetawayDriver, true, out var error));
        Assert.Equal("Complete the proposal first.", error);
        Assert.False(game.TryPropose(0, TheGangCards.GetawayDriver, null, null, out error));
        Assert.Equal("Choose a player.", error);
        Assert.False(game.TryPropose(0, TheGangCards.GetawayDriver, 1, "9", out error));
        Assert.Equal("This card does not choose a value.", error);

        Assert.True(game.TryPropose(0, TheGangCards.GetawayDriver, 1, null, out _));
        game.TryAgree(0, TheGangCards.GetawayDriver, true, out _);
        game.TryAgree(1, TheGangCards.GetawayDriver, true, out _);
        Assert.True(game.TryPropose(1, TheGangCards.GetawayDriver, 2, null, out _));
        Assert.Empty(game.Decisions[0].Agreed);

        AgreeAll(game, TheGangCards.GetawayDriver);
        Assert.True(game.Decisions[0].IsDone);
        Assert.Equal(new TheGangResult(2, TheGangCards.GetawayDriver, "Pair"), Assert.Single(game.Results));
        Assert.False(game.TryPropose(0, TheGangCards.GetawayDriver, 0, null, out error));
        Assert.Equal("That cannot be decided now.", error);
    }

    [Theory]
    [InlineData(1, "9", "one 9")]
    [InlineData(2, "K", "2 Ks")]
    [InlineData(0, "A", "no As")]
    public void Mastermind_shows_how_many_of_a_value_the_chosen_player_has(int player, string value, string text)
    {
        var game = WithSpecialist(TheGangCards.Mastermind);

        Assert.False(game.TryPropose(0, TheGangCards.Mastermind, player, "1", out _));
        Assert.True(game.TryPropose(0, TheGangCards.Mastermind, player, null, out _));
        Assert.False(game.TryAgree(0, TheGangCards.Mastermind, true, out _));
        Assert.True(game.TryPropose(0, TheGangCards.Mastermind, player, value, out _));
        AgreeAll(game, TheGangCards.Mastermind);

        Assert.Equal(text, Assert.Single(game.Results).Text);
    }

    [Theory]
    [InlineData(TheGangCards.Hacker, "A♠")]
    [InlineData(TheGangCards.Jack, "J★")]
    public void Hacker_and_jack_add_a_card_and_the_stage_waits_for_a_discard(string specialist, string added)
    {
        var game = WithSpecialist(specialist);
        game.TryPropose(1, specialist, 0, null, out _);
        AgreeAll(game, specialist);

        Assert.Equal(["3♦", "4♥", added], game.HoleCards(0).Select(c => c.ToString()));
        Assert.Equal((0, TheGangStep.Discard), (game.PendingPlayer, game.PendingStep));
        PlayStage(game, [1, 2, 3]);
        Assert.Equal(0, game.Stage);
        Assert.False(game.TryDiscard(1, 0, out _));

        Assert.True(game.TryDiscard(0, 0, out _));
        Assert.Equal(["4♥", added], game.HoleCards(0).Select(c => c.ToString()));
        Assert.Equal(1, game.Stage);
    }

    [Fact]
    public void Informant_shows_one_card_to_one_other_player_only()
    {
        var game = WithSpecialist(TheGangCards.Informant);
        game.TryPropose(2, TheGangCards.Informant, 0, null, out _);
        AgreeAll(game, TheGangCards.Informant);

        Assert.Equal(TheGangStep.ShowCard, game.PendingStep);
        Assert.False(game.TryShowCard(0, 1, 0, out var error));
        Assert.Equal("Choose another player to show the card to.", error);
        Assert.True(game.TryShowCard(0, 1, 2, out _));

        Assert.Equal((0, 2), game.ShownBetween);
        Assert.Equal(Card.Parse("4H"), game.ShownCardFor(2));
        Assert.Null(game.ShownCardFor(1));
        Assert.Equal(TheGangStep.None, game.PendingStep);
    }

    [Fact]
    public void Muscle_makes_the_chosen_hand_beat_every_hand_of_the_same_ranking()
    {
        // Player 0: pair of twos; player 1: pair of nines; player 2: pair of kings. Shared cards do not help.
        const string deck = "2C 2D 9C 9D KD KH 3S 5H 7C JS QD";
        var game = new TheGangGame(3, () => Cards(deck), Advanced(0, [], [TheGangCards.Muscle]));
        PlayHeist(game, [3, 2, 1]);
        game.TryPropose(0, TheGangCards.Muscle, 0, null, out _);
        AgreeAll(game, TheGangCards.Muscle);

        PlayAllStages(game, [3, 1, 2]);
        RevealAll(game);

        Assert.Equal([true, true, true], game.RevealedInOrder);
        Assert.Equal(1, game.Vaults);
    }

    [Theory]
    [InlineData(TheGangCards.RetinaScan, "K", true)]
    [InlineData(TheGangCards.RetinaScan, "A", false)]
    [InlineData(TheGangCards.FingerprintScan, "Three of a kind", true)]
    [InlineData(TheGangCards.FingerprintScan, "Pair", false)]
    public void Scans_need_a_right_guess_about_the_strongest_hand(string scan, string guess, bool right)
    {
        var game = WithChallenge(scan);
        PlayAllStages(game, [1, 2, 3]);
        game.TryRevealNext(0, out _);
        game.TryRevealNext(1, out _);

        Assert.False(game.TryRevealNext(2, out var error));
        Assert.Equal("First agree on the guess about the strongest hand.", error);
        Assert.False(game.TryPropose(2, scan, null, guess, out error));
        Assert.Equal("You do not take part in this decision.", error);
        Assert.True(game.TryPropose(0, scan, null, guess, out error), error);
        Assert.True(game.TryAgree(0, scan, true, out _));
        Assert.True(game.TryAgree(1, scan, true, out _));
        Assert.True(game.TryRevealNext(2, out error), error);

        Assert.Equal([true, true, true], game.RevealedInOrder);
        Assert.Equal(right ? (1, 0) : (0, 1), (game.Vaults, game.Alarms));
        Assert.Equal(right, game.Succeeded);
        Assert.Equal($"guessed {guess}: {(right ? "right" : "wrong")}", Assert.Single(game.Results).Text);
    }

    [Fact]
    public void A_user_who_sees_the_strongest_hand_takes_no_part_in_the_scan()
    {
        // Players 1 and 2 are on the same screen; player 2 has the strongest hand.
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(1, [TheGangCards.RetinaScan]),
            sameHolder: (a, b) => a == b || (a >= 1 && b >= 1));
        PlayAllStages(game, [1, 2, 3]);
        game.TryRevealNext(0, out _);
        game.TryRevealNext(1, out _);

        Assert.Equal([0], Assert.Single(game.Decisions).Voters);
        Assert.False(game.TryAgree(1, TheGangCards.RetinaScan, true, out var error));
        Assert.Equal("You do not take part in this decision.", error);
        Assert.True(game.TryPropose(0, TheGangCards.RetinaScan, null, "K", out _));
        Assert.True(game.TryAgree(0, TheGangCards.RetinaScan, true, out _));
        Assert.True(Assert.Single(game.Decisions).IsDone);
    }

    [Fact]
    public void When_one_user_plays_every_seat_only_the_strongest_hand_sits_out_the_scan()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(1, [TheGangCards.FingerprintScan]), sameHolder: (_, _) => true);
        PlayAllStages(game, [1, 2, 3]);
        game.TryRevealNext(0, out _);
        game.TryRevealNext(1, out _);

        Assert.Equal([0, 1], Assert.Single(game.Decisions).Voters);
    }

    [Fact]
    public void Each_user_is_ready_once_for_all_their_hands()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck), sameHolder: OneUserPlaysOneAndTwo);
        game.TryTakeChip(0, 1, out _);
        game.TryTakeChip(1, 2, out _);
        game.TryTakeChip(2, 3, out _);

        Assert.True(game.TrySetReady(2, true, out _));
        Assert.True(game.IsReady(1));
        Assert.True(game.TrySetReady(1, false, out _));
        Assert.False(game.IsReady(2));

        game.TrySetReady(1, true, out _);
        game.TrySetReady(0, true, out _);
        Assert.Equal(1, game.Stage);
    }

    [Fact]
    public void Each_user_agrees_once_for_all_their_seats()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck + " AS AD"), Advanced(0, [], [TheGangCards.GetawayDriver]),
            sameHolder: OneUserPlaysOneAndTwo);
        PlayHeist(game, [3, 2, 1]);
        game.TryPropose(0, TheGangCards.GetawayDriver, 1, null, out _);

        Assert.True(game.TryAgree(2, TheGangCards.GetawayDriver, true, out _));
        Assert.Equal([1, 2], game.Decisions[0].Agreed);
        Assert.True(game.TryAgree(0, TheGangCards.GetawayDriver, true, out _));
        Assert.True(game.Decisions[0].IsDone);
    }

    [Fact]
    public void Each_user_confirms_once_that_they_have_seen_their_cards()
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck), Advanced(0, [], [TheGangCards.ConArtist]),
            sameHolder: OneUserPlaysOneAndTwo);
        PlayHeist(game, [3, 2, 1]);

        Assert.True(game.TryConfirmSeen(1, out _));
        Assert.True(game.IsPrepared(2));
        Assert.True(game.TryConfirmSeen(0, out _));
        Assert.Equal(TheGangPhase.Betting, game.Phase);
    }

    [Theory]
    [InlineData("JX AH KH QH 10H", "Straight, ace high")]
    [InlineData("JC JD JH JS JX", "Four jacks")]
    public void The_jack_card_has_no_suit(string cards, string name)
    {
        Assert.Equal(name, HandEvaluator.Evaluate(Cards(cards)).Name);
    }

    [Fact]
    public void Options_turn_into_rules()
    {
        var defaults = TheGangRules.FromOptions(GameOptions.Normalize(GameType.TheGang, new Dictionary<string, string>
        {
            [TheGangOptions.Mode] = "Advanced",
            [TheGangOptions.Specialists] = TheGangCards.MathWhiz,
        }));

        Assert.Equal(TheGangMode.Advanced, defaults.Mode);
        Assert.Equal(1, defaults.FirstHeistChallenges);
        Assert.Equal(TheGangCards.Challenges.Select(c => c.Key), defaults.Challenges);
        Assert.Equal(TheGangCards.Specialists.Select(c => c.Key).Where(k => k != TheGangCards.MathWhiz), defaults.Specialists);
        Assert.False(defaults.AvoidClashes);
    }

    // Player 0: high card king; player 1: pair of nines; player 2: three kings.
    private const string RankedDeck = "3D 4H 9C 5S KD KH 2C 7D 9H JS KC";

    private static TheGangGame NewGame() => new(3, () => Cards(RankedDeck));

    // Players 1 and 2 are played by the same user.
    private static bool OneUserPlaysOneAndTwo(int a, int b) => a == b || (a >= 1 && b >= 1);

    private static TheGangRules Advanced(int firstHeist, string[] challenges, string[]? specialists = null) =>
        new(TheGangMode.Advanced, firstHeist, challenges, specialists ?? []);

    private static TheGangGame WithChallenge(string challenge) =>
        new(3, () => Cards(RankedDeck), Advanced(1, [challenge]));

    // A failed first heist, so the second heist has this specialist card.
    private static TheGangGame WithSpecialist(string specialist)
    {
        var game = new TheGangGame(3, () => Cards(RankedDeck + " AS AD"), Advanced(0, [], [specialist]));
        PlayHeist(game, [3, 2, 1]);
        return game;
    }

    // Does the steps before the betting (Coordinator: everyone passes their first card; Con Artist: everyone has seen).
    private static void Prepare(TheGangGame game)
    {
        while (game.Phase == TheGangPhase.Preparing)
        {
            var card = game.PreparingCard;
            for (var player = 0; player < game.PlayerCount && game.PreparingCard == card; player++)
            {
                if (game.IsPrepared(player) && card == TheGangCards.ConArtist)
                {
                    continue;
                }

                var done = card == TheGangCards.Coordinator
                    ? game.TryPassCard(player, 0, out var error)
                    : game.TryConfirmSeen(player, out error);
                Assert.True(done, error);
            }
        }
    }

    private static void AgreeAll(TheGangGame game, string card)
    {
        for (var player = 0; player < game.PlayerCount; player++)
        {
            Assert.True(game.TryAgree(player, card, true, out var error), error);
        }
    }

    private static void PlayHeist(TheGangGame game, int[] chips)
    {
        var heist = game.Heist;
        Prepare(game);
        PlayAllStages(game, chips);
        RevealAll(game);
        Assert.True(game.TryNextHeist(heist, out var error), error);
    }

    // Plays a heist that fails: the strongest hand takes the 1-star chip.
    private static void FailHeist(TheGangGame game)
    {
        Prepare(game);
        var strongestFirst = Enumerable.Range(0, game.PlayerCount)
            .OrderByDescending(p => p, Comparer<int>.Create((a, b) => game.Hand(a).IsWeakerThan(game.Hand(b)) ? -1 : 1))
            .ToList();
        PlayHeist(game, [.. Enumerable.Range(0, game.PlayerCount).Select(p => strongestFirst.IndexOf(p) + 1)]);
    }

    private static Card[] Cards(string cards) => cards.Split(' ').Select(Card.Parse).ToArray();

    private static void PlayStage(TheGangGame game, int[] chips)
    {
        for (var player = 0; player < chips.Length; player++)
        {
            Assert.True(game.TryTakeChip(player, chips[player], out var error), error);
        }

        // A user is ready once for all their players, so players already made ready are skipped.
        var stage = game.Stage;
        for (var player = 0; player < chips.Length && game.Stage == stage && game.Phase == TheGangPhase.Betting; player++)
        {
            if (!game.IsReady(player))
            {
                Assert.True(game.TrySetReady(player, true, out var error), error);
            }
        }
    }

    private static void PlayAllStages(TheGangGame game, int[] chips)
    {
        while (game.Phase == TheGangPhase.Betting)
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
