using GamesLane.Server.Games.Yatzee;

namespace GamesLane.Server.Tests;

public class YatzeeGameTests
{
    // Scoring

    [Theory]
    [InlineData(YatzeeCategory.Ones, new[] { 1, 1, 2, 3, 1 }, 3)]
    [InlineData(YatzeeCategory.Fives, new[] { 5, 5, 2, 5, 1 }, 15)]
    [InlineData(YatzeeCategory.Sixes, new[] { 1, 2, 3, 4, 5 }, 0)]
    [InlineData(YatzeeCategory.ThreeOfAKind, new[] { 4, 4, 4, 2, 6 }, 20)]
    [InlineData(YatzeeCategory.ThreeOfAKind, new[] { 4, 4, 3, 2, 6 }, 0)]
    [InlineData(YatzeeCategory.FourOfAKind, new[] { 3, 3, 3, 3, 6 }, 18)]
    [InlineData(YatzeeCategory.FourOfAKind, new[] { 3, 3, 3, 6, 6 }, 0)]
    [InlineData(YatzeeCategory.FullHouse, new[] { 2, 5, 2, 5, 5 }, 25)]
    [InlineData(YatzeeCategory.FullHouse, new[] { 5, 5, 5, 5, 5 }, 0)]
    [InlineData(YatzeeCategory.SmallStraight, new[] { 4, 1, 3, 2, 4 }, 30)]
    [InlineData(YatzeeCategory.SmallStraight, new[] { 6, 5, 3, 4, 1 }, 30)]
    [InlineData(YatzeeCategory.SmallStraight, new[] { 1, 2, 3, 5, 6 }, 0)]
    [InlineData(YatzeeCategory.LargeStraight, new[] { 2, 3, 4, 5, 6 }, 40)]
    [InlineData(YatzeeCategory.LargeStraight, new[] { 1, 2, 3, 4, 6 }, 0)]
    [InlineData(YatzeeCategory.Yatzee, new[] { 6, 6, 6, 6, 6 }, 50)]
    [InlineData(YatzeeCategory.Yatzee, new[] { 6, 6, 6, 6, 5 }, 0)]
    [InlineData(YatzeeCategory.Chance, new[] { 1, 3, 4, 6, 6 }, 20)]
    public void Each_box_scores_the_dice_by_the_classic_rules(YatzeeCategory category, int[] dice, int expected)
    {
        Assert.Equal(expected, YatzeeScoring.Score(category, dice));
    }

    [Fact]
    public void The_upper_section_earns_35_at_63_or_more()
    {
        var (game, cup) = Solo();
        foreach (var n in new[] { 1, 2, 3, 4, 5 })
        {
            Play(game, cup, 0, (YatzeeCategory)(n - 1), n, n, n, 6, 6);
        }

        Assert.Equal(45, game.Scorecards[0].UpperTotal);
        Assert.Equal(0, game.Scorecards[0].UpperBonus);
        Play(game, cup, 0, YatzeeCategory.Sixes, 6, 6, 6, 1, 1);
        Assert.Equal((63, 35), (game.Scorecards[0].UpperTotal, game.Scorecards[0].UpperBonus));
        Assert.Equal(98, game.Scorecards[0].GrandTotal);
    }

    // Turns

    [Fact]
    public void A_turn_has_up_to_three_rolls_and_held_dice_are_not_rolled_again()
    {
        var (game, cup) = Solo();
        Assert.False(game.TryHold(0, 0, true, out var error));
        Assert.Equal("Roll the dice first.", error);
        Assert.False(game.TryScore(0, YatzeeCategory.Chance, out error));
        Assert.Equal("Roll the dice first.", error);
        Assert.All(game.PossibleScores, Assert.Null);

        cup.Add(6, 2, 6, 3, 1);
        Assert.True(game.TryRoll(0, out error), error);
        Assert.True(game.TryHold(0, 0, true, out error), error);
        Assert.True(game.TryHold(0, 2, true, out error), error);
        Assert.False(game.TryHold(0, 5, true, out error));
        Assert.Equal("There is no such die.", error);

        cup.Add(6, 4, 4);
        Assert.True(game.TryRoll(0, out error), error);
        Assert.Equal([6, 6, 6, 4, 4], game.Dice);
        Assert.True(game.TryHold(0, 2, false, out error), error);
        cup.Add(6, 6, 6, 6);
        Assert.True(game.TryRoll(0, out error), error);
        Assert.Equal([6, 6, 6, 6, 6], game.Dice);
        Assert.Equal((3, 3), (game.RollsUsed, game.Rolls));

        Assert.False(game.TryRoll(0, out error));
        Assert.Equal("You have no rolls left. Choose a box to score.", error);
        Assert.False(game.TryHold(0, 1, true, out error));
        Assert.Equal("You have no rolls left. Choose a box to score.", error);
        Assert.Equal(50, game.PossibleScores[(int)YatzeeCategory.Yatzee]);
        Assert.Equal(30, game.PossibleScores[(int)YatzeeCategory.Sixes]);
    }

    [Fact]
    public void Scoring_ends_the_turn_and_a_round_ends_after_the_last_player()
    {
        var cup = new Cup();
        var game = new YatzeeGame(2, cup.Roll);
        cup.Add(1, 2, 3, 4, 6);
        Assert.False(game.TryRoll(1, out var error));
        Assert.Equal("It is not your turn.", error);
        Assert.True(game.TryRoll(0, out error), error);
        Assert.True(game.TryHold(0, 0, true, out error), error);
        Assert.True(game.TryScore(0, YatzeeCategory.SmallStraight, out error), error);

        var last = game.LastTurn!;
        Assert.Equal((0, YatzeeCategory.SmallStraight, 30, false), (last.Player, last.Category, last.Points, last.Bonus));
        Assert.Equal([1, 2, 3, 4, 6], last.Dice);
        Assert.Equal(30, game.Scorecards[0][YatzeeCategory.SmallStraight]);
        Assert.Equal((1, 1, 0), (game.Turn, game.Round, game.RollsUsed));
        Assert.Equal([0, 0, 0, 0, 0], game.Dice);
        Assert.All(game.Held, Assert.False);

        Play(game, cup, 1, YatzeeCategory.Chance, 6, 6, 5, 5, 4);
        Assert.Equal((0, 2), (game.Turn, game.Round));
        cup.Add(1, 1, 1, 1, 2);
        game.TryRoll(0, out _);
        Assert.False(game.TryScore(0, YatzeeCategory.SmallStraight, out error));
        Assert.Equal("That box is already filled.", error);
        Assert.Null(game.PossibleScores[(int)YatzeeCategory.SmallStraight]);
    }

    // Extra Yatzees: the bonus and the joker rule

    [Fact]
    public void An_extra_Yatzee_earns_100_and_must_go_in_its_upper_box_then_in_a_lower_box_at_full_value()
    {
        var (game, cup) = Solo();
        Play(game, cup, 0, YatzeeCategory.Yatzee, 5, 5, 5, 5, 5);

        cup.Add(5, 5, 5, 5, 5);
        game.TryRoll(0, out _);
        Assert.Equal([null, null, null, null, 25, null, null, null, null, null, null, null, null], game.PossibleScores);
        Assert.False(game.TryScore(0, YatzeeCategory.FullHouse, out var error));
        Assert.Equal("This Yatzee must be scored in the upper box of its number.", error);
        Assert.True(game.TryScore(0, YatzeeCategory.Fives, out error), error);
        Assert.True(game.LastTurn!.Bonus);

        cup.Add(5, 5, 5, 5, 5);
        game.TryRoll(0, out _);
        Assert.Equal([null, null, null, null, null, null, 25, 25, 25, 30, 40, null, 25], game.PossibleScores);
        Assert.False(game.TryScore(0, YatzeeCategory.Ones, out error));
        Assert.Equal("This Yatzee must be scored in an open lower box.", error);
        Assert.True(game.TryScore(0, YatzeeCategory.LargeStraight, out error), error);

        var card = game.Scorecards[0];
        Assert.Equal((2, 200), (card.YatzeeBonuses, card.YatzeeBonus));
        Assert.Equal(50 + 25 + 40 + 200, card.GrandTotal);
    }

    [Fact]
    public void A_Yatzee_after_a_zero_in_the_Yatzee_box_follows_the_joker_rule_without_a_bonus()
    {
        var (game, cup) = Solo();
        Play(game, cup, 0, YatzeeCategory.Yatzee, 1, 2, 3, 4, 6);

        cup.Add(6, 6, 6, 6, 6);
        game.TryRoll(0, out _);
        Assert.Equal(30, game.PossibleScores[(int)YatzeeCategory.Sixes]);
        Assert.Null(game.PossibleScores[(int)YatzeeCategory.Chance]);
        Assert.True(game.TryScore(0, YatzeeCategory.Sixes, out var error), error);

        Assert.False(game.LastTurn!.Bonus);
        Assert.Equal(0, game.Scorecards[0].YatzeeBonuses);
        Assert.Equal(30, game.Scorecards[0].GrandTotal);
    }

    [Fact]
    public void A_joker_with_its_upper_box_and_the_lower_section_filled_scores_0_in_an_open_upper_box()
    {
        var (game, cup) = Solo();
        Play(game, cup, 0, YatzeeCategory.Yatzee, 5, 5, 5, 5, 5);
        Play(game, cup, 0, YatzeeCategory.ThreeOfAKind, 1, 1, 1, 2, 3);
        Play(game, cup, 0, YatzeeCategory.FourOfAKind, 2, 2, 2, 2, 1);
        Play(game, cup, 0, YatzeeCategory.FullHouse, 3, 3, 4, 4, 4);
        Play(game, cup, 0, YatzeeCategory.SmallStraight, 1, 2, 3, 4, 1);
        Play(game, cup, 0, YatzeeCategory.LargeStraight, 1, 2, 3, 4, 5);
        Play(game, cup, 0, YatzeeCategory.Chance, 6, 6, 6, 6, 5);
        Play(game, cup, 0, YatzeeCategory.Fives, 5, 5, 1, 2, 3);

        cup.Add(5, 5, 5, 5, 5);
        game.TryRoll(0, out _);
        Assert.Equal([0, 0, 0, 0, null, 0, null, null, null, null, null, null, null], game.PossibleScores);
        Assert.True(game.TryScore(0, YatzeeCategory.Twos, out var error), error);
        Assert.Equal((0, true), (game.LastTurn!.Points, game.LastTurn.Bonus));
    }

    // The end of the game

    [Fact]
    public void The_game_ends_after_13_rounds_and_the_highest_totals_win()
    {
        var cup = new Cup();
        var game = new YatzeeGame(2, cup.Roll);
        foreach (var category in Enum.GetValues<YatzeeCategory>())
        {
            Assert.Empty(game.Winners);
            Play(game, cup, 0, category, 1, 2, 3, 4, 6);
            Play(game, cup, 1, category, 6, 4, 3, 2, 1);
        }

        Assert.True(game.IsOver);
        Assert.Equal(13, game.Round);
        Assert.Equal([0, 1], game.Winners);
        Assert.All(game.PossibleScores, Assert.Null);
        Assert.False(game.TryRoll(0, out var error));
        Assert.Equal("The game is over.", error);

        var solo = Solo();
        foreach (var category in Enum.GetValues<YatzeeCategory>())
        {
            Play(solo.Game, solo.Cup, 0, category, 2, 2, 2, 3, 3);
        }

        Assert.Equal([0], solo.Game.Winners);
        Assert.True(solo.Game.Scorecards[0].IsComplete);
    }

    private static (YatzeeGame Game, Cup Cup) Solo()
    {
        var cup = new Cup();
        return (new YatzeeGame(1, cup.Roll), cup);
    }

    // Rolls the given dice for the player and scores them in the box.
    private static void Play(YatzeeGame game, Cup cup, int player, YatzeeCategory category, params int[] dice)
    {
        cup.Add(dice);
        Assert.True(game.TryRoll(player, out var error), error);
        Assert.True(game.TryScore(player, category, out error), error);
    }

    // Dice that roll the numbers added, in order.
    private sealed class Cup
    {
        private readonly Queue<int> _dice = new();

        public Func<int> Roll => _dice.Dequeue;

        public void Add(params int[] dice)
        {
            foreach (var die in dice)
            {
                _dice.Enqueue(die);
            }
        }
    }
}
