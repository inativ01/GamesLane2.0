using GamesLane.Server.Games.Backgammon;

namespace GamesLane.Server.Tests;

public class BackgammonGameTests
{
    private const int White = 0;
    private const int Black = 1;
    private const int Bar = BackgammonPosition.Bar;
    private const int Off = BackgammonPosition.Off;

    // The position and the opening roll

    [Fact]
    public void The_game_starts_with_fifteen_checkers_each_and_167_pips()
    {
        var position = BackgammonPosition.Start;

        Assert.All(new[] { White, Black }, p => Assert.Equal(167, position.Pips(p)));
        Assert.Equal(15, position.Points.Where(n => n > 0).Sum());
        Assert.Equal(-15, position.Points.Where(n => n < 0).Sum());
        Assert.Equal(2, position.Points[23]);
        Assert.Equal(-2, position.Points[0]);
    }

    [Fact]
    public void Each_player_rolls_one_die_to_start_again_on_a_tie_and_the_higher_die_rolls_first()
    {
        var game = new BackgammonGame(rollDie: Dice(4, 4, 5, 2, 6, 1));
        Assert.Equal(BackgammonPhase.Opening, game.Phase);
        Assert.False(game.TryMove(White, 13, 9, out var error));
        Assert.Equal("First roll your die to see who starts.", error);

        Assert.True(game.TryRoll(White, out error), error);
        Assert.False(game.TryRoll(White, out error));
        Assert.Equal("You have rolled your die. Waiting for the other player.", error);
        Assert.True(game.TryRoll(Black, out error), error);
        Assert.Equal((BackgammonPhase.Opening, 4, 4), (game.Phase, game.OpeningDice[0], game.OpeningDice[1]));

        Assert.True(game.TryRoll(Black, out error), error);
        Assert.Equal([null, 5], game.OpeningDice);
        Assert.True(game.TryRoll(White, out error), error);

        Assert.Equal((Black, BackgammonPhase.Rolling), (game.Turn, game.Phase));
        Assert.Empty(game.Dice);
        Assert.Equal([2, 5], game.OpeningRoll);
        Assert.Equal(4, game.Rolls);
        Assert.False(game.CanDouble);
        Assert.False(game.TryDouble(Black, out error));
        Assert.Equal("Nobody may double before the first turn.", error);
        Assert.False(game.TryRoll(White, out error));
        Assert.Equal("It is not your turn.", error);

        Assert.True(game.TryRoll(Black, out error), error);
        Assert.Equal((Black, BackgammonPhase.Moving), (game.Turn, game.Phase));
        Assert.Equal([6, 1], game.Dice);
        Assert.Null(game.OpeningRoll);
        Assert.Equal(5, game.Rolls);
    }

    [Fact]
    public void Checkers_cannot_land_on_a_point_with_two_opponent_checkers()
    {
        var game = Opened(rollDie: Dice(5, 2));

        Assert.Contains(game.LegalMoves, m => (m.From, m.To) == (13, 8));
        Assert.Contains(game.LegalMoves, m => (m.From, m.To) == (24, 22));
        Assert.DoesNotContain(game.LegalMoves, m => (m.From, m.To) == (6, 1));
    }

    // Turns

    [Fact]
    public void A_turn_ends_once_both_dice_are_used_and_the_other_player_rolls_next()
    {
        var game = Opened(rollDie: Dice(6, 1));

        Assert.True(game.TryMove(White, 13, 7, out var error), error);
        Assert.False(game.TryEndTurn(White, out _));
        Assert.True(game.TryMove(White, 8, 7, out error), error);
        Assert.True(game.TryEndTurn(White, out error), error);

        Assert.Equal((Black, BackgammonPhase.Rolling), (game.Turn, game.Phase));
        Assert.Equal(2, game.Position.Count(White, 7));
        Assert.Equal([new BackgammonStep(13, 7, 6), new BackgammonStep(8, 7, 1)], game.LastTurn!.Steps);
        Assert.Null(game.OpeningRoll);
    }

    [Fact]
    public void Moves_can_be_undone_before_the_turn_ends()
    {
        var game = Opened(rollDie: Dice(6, 1));
        game.TryMove(White, 13, 7, out _);

        Assert.True(game.TryUndo(White, out var error), error);

        Assert.Equal(BackgammonPosition.Start.Key, game.Position.Key);
        Assert.Equal([6, 1], game.DiceLeft);
        Assert.False(game.TryUndo(White, out _));
    }

    [Fact]
    public void Only_the_player_to_move_can_act_and_only_in_the_right_order()
    {
        var game = Opened(rollDie: Dice(6, 1, 3, 3));

        Assert.False(game.TryMove(Black, 13, 7, out var error));
        Assert.Equal("It is not your turn.", error);
        Assert.False(game.TryRoll(White, out error));
        Assert.Equal("You have already rolled.", error);
        game.TryMove(White, 13, 7, out _);
        game.TryMove(White, 8, 7, out _);
        Assert.False(game.TryMove(White, 6, 5, out error));
        Assert.Equal("You have used your dice. End your turn.", error);
        game.TryEndTurn(White, out _);
        Assert.False(game.TryMove(Black, 13, 10, out error));
        Assert.Equal("Roll the dice first.", error);
    }

    [Fact]
    public void Doubles_are_played_four_times()
    {
        var game = Opened(rollDie: Dice(6, 1, 3, 3));
        PlayOpening(game);

        Assert.True(game.TryRoll(Black, out var error), error);

        Assert.Equal([3, 3], game.Dice);
        Assert.Equal([3, 3, 3, 3], game.DiceLeft);
    }

    // The bar

    [Fact]
    public void A_hit_checker_goes_to_the_bar_and_must_enter_before_any_other_move()
    {
        // White: 10, 10, 13, 13, 13. Black: a blot on white's 7 (black's 18) and five on its 6-point.
        var start = BackgammonPosition.Create(
            new Dictionary<int, int> { [10] = 2, [13] = 3 },
            new Dictionary<int, int> { [18] = 1, [6] = 5 });
        var game = Opened(rollDie: Dice(3, 1, 4, 2), start: start);

        Assert.True(game.TryMove(White, 10, 7, out var error), error);
        Assert.Equal(1, game.Position.Count(Black, Bar));
        game.TryMove(White, 13, 12, out _);
        game.TryEndTurn(White, out _);
        game.TryRoll(Black, out _);

        Assert.NotEmpty(game.LegalMoves);
        Assert.All(game.LegalMoves, m => Assert.Equal(Bar, m.From));
    }

    [Fact]
    public void A_player_who_cannot_enter_from_the_bar_loses_the_turn()
    {
        // White has closed its home board; black has a checker on the bar and rolls 2 and 1 at the start.
        var start = BackgammonPosition.Create(
            new Dictionary<int, int> { [1] = 2, [2] = 2, [3] = 2, [4] = 2, [5] = 2, [6] = 2, [13] = 3 },
            new Dictionary<int, int> { [6] = 5 },
            blackBar: 1);
        var game = Opened(rollDie: Dice(1, 2), start: start, starter: Black);

        Assert.Equal((White, BackgammonPhase.Rolling), (game.Turn, game.Phase));
        Assert.Equal(Black, game.LastTurn!.Player);
        Assert.Empty(game.LastTurn.Steps);
        Assert.Equal([1, 2], game.LastTurn.Dice);
    }

    // Combined moves

    [Fact]
    public void A_checker_can_move_by_both_dice_at_once_and_the_turn_can_then_end()
    {
        var game = Opened(rollDie: Dice(5, 3));

        var combined = Assert.Single(game.CombinedMoves, m => (m.From, m.To) == (13, 5));
        Assert.Equal(2, combined.Steps.Count);
        Assert.True(game.TryMove(White, 13, 5, out var error), error);

        Assert.Equal(1, game.Position.Count(White, 5));
        Assert.Equal(2, game.Played.Count);
        Assert.True(game.CanEndTurn);
        Assert.Empty(game.CombinedMoves);
    }

    [Fact]
    public void A_combined_move_is_not_offered_when_the_order_of_the_dice_matters()
    {
        // A black blot on white's 10 (black's 15): 13/10/5 hits it, 13/8/5 does not.
        var start = BackgammonPosition.Create(
            new Dictionary<int, int> { [13] = 1, [6] = 14 },
            new Dictionary<int, int> { [15] = 1, [1] = 14 });
        var game = Opened(rollDie: Dice(5, 3), start: start);

        Assert.DoesNotContain(game.CombinedMoves, m => (m.From, m.To) == (13, 5));
        Assert.False(game.TryMove(White, 13, 5, out var error));
        Assert.Equal("That move is not allowed.", error);
    }

    [Fact]
    public void With_doubles_a_combined_move_uses_the_fewest_dice()
    {
        var game = Opened(rollDie: Dice(2, 2));

        Assert.Equal(2, Assert.Single(game.CombinedMoves, m => (m.From, m.To) == (13, 9)).Steps.Count);
        Assert.Equal(3, Assert.Single(game.CombinedMoves, m => (m.From, m.To) == (13, 7)).Steps.Count);
        Assert.True(game.TryMove(White, 13, 7, out var error), error);
        Assert.Equal([2], game.DiceLeft);
    }

    // Using the dice

    [Fact]
    public void Both_dice_must_be_used_when_possible()
    {
        // White: 10 and 12. Black holds white's 3, 4 and 6. With 6-3, only 10/7, 7/1 uses both dice.
        var position = BackgammonPosition.Create(
            new Dictionary<int, int> { [10] = 1, [12] = 1 },
            new Dictionary<int, int> { [22] = 2, [21] = 2, [19] = 2 });

        var (finals, used) = BackgammonMoves.Turn(position, White, [6, 3]);

        Assert.Equal(2, used);
        Assert.Equal([position.Apply(White, 10, 7).Apply(White, 7, 1).Key], finals);
    }

    [Fact]
    public void When_only_one_die_can_be_used_it_must_be_the_larger()
    {
        // White: 9, and 14 checkers on 24. Black holds white's 18 and 19. Either 9/3 or 9/4 can be played, not both.
        var position = BackgammonPosition.Create(
            new Dictionary<int, int> { [9] = 1, [24] = 14 },
            new Dictionary<int, int> { [7] = 2, [6] = 2 });

        var (finals, used) = BackgammonMoves.Turn(position, White, [6, 5]);

        Assert.Equal(1, used);
        Assert.Equal([position.Apply(White, 9, 3).Key], finals);
    }

    [Fact]
    public void The_only_possible_play_is_made_for_the_player_and_the_turn_ends()
    {
        var start = BackgammonPosition.Create(
            new Dictionary<int, int> { [10] = 1, [12] = 1 },
            new Dictionary<int, int> { [22] = 2, [21] = 2, [19] = 2 });

        var game = Opened(rollDie: Dice(6, 3), start: start);

        Assert.Equal((Black, BackgammonPhase.Rolling), (game.Turn, game.Phase));
        Assert.True(game.LastTurn!.Forced);
        Assert.Equal([new BackgammonStep(10, 7, 3), new BackgammonStep(7, 1, 6)], game.LastTurn.Steps);
    }

    [Theory]
    // All home, a checker on 6: a 5 cannot bear off from 3, a 6 bears off from 6.
    [InlineData("6 3", 5, "6-1 3-")]
    [InlineData("6 3", 6, "6-0 3-")]
    // Nothing higher than 3: a 5 bears off from 3, but not from 2.
    [InlineData("3 2", 5, "3-0")]
    // A checker outside the home board: no bearing off.
    [InlineData("7 3", 3, "7-4 3-")]
    public void Checkers_bear_off_only_from_home_with_the_exact_or_a_higher_number(string points, int die, string expected)
    {
        var white = points.Split(' ').Select(int.Parse).ToDictionary(p => p, _ => 1);
        var position = BackgammonPosition.Create(white, new Dictionary<int, int> { [24] = 1 });

        var steps = BackgammonMoves.Steps(position, White, die).Select(s => $"{s.From}-{s.To}");

        Assert.Equal(expected.Split(' ').Where(s => !s.EndsWith('-')), steps);
    }

    // Winning

    [Theory]
    [InlineData(14, 0, BackgammonResultKind.Single, 1)]
    [InlineData(15, 0, BackgammonResultKind.Gammon, 2)]
    [InlineData(14, 1, BackgammonResultKind.Backgammon, 3)]
    public void Bearing_off_the_last_checker_wins_a_single_game_a_gammon_or_a_backgammon(
        int blackHome, int blackInWhitesHome, BackgammonResultKind kind, int points)
    {
        var black = new Dictionary<int, int> { [6] = blackHome };
        if (blackInWhitesHome > 0)
        {
            black[20] = blackInWhitesHome;
        }

        var game = Opened(rollDie: Dice(2, 1), start: BackgammonPosition.Create(new Dictionary<int, int> { [1] = 1 }, black));

        Assert.Equal(BackgammonPhase.GameOver, game.Phase);
        Assert.Equal(new BackgammonResult(White, points, kind), game.Result);
        Assert.Equal([points, 0], game.Scores);
    }

    [Fact]
    public void Reaching_the_match_length_ends_the_match()
    {
        var start = BackgammonPosition.Create(new Dictionary<int, int> { [1] = 1 }, new Dictionary<int, int> { [6] = 14 });
        var game = Opened(new BackgammonRules(MatchLength: 1), Dice(2, 1), start);

        Assert.True(game.IsMatchOver);
        Assert.False(game.TryNextGame(1, out var error));
        Assert.Equal("The match is over.", error);
    }

    [Fact]
    public void The_next_game_starts_once_from_the_starting_position_with_a_new_opening_roll()
    {
        var start = BackgammonPosition.Create(new Dictionary<int, int> { [1] = 1 }, new Dictionary<int, int> { [6] = 14 });
        var game = Opened(rollDie: Dice(2, 1), start: start);

        Assert.True(game.TryNextGame(1, out var error), error);
        Assert.True(game.TryNextGame(1, out _));

        Assert.Equal((2, BackgammonPhase.Opening), (game.GameNumber, game.Phase));
        Assert.Equal([null, null], game.OpeningDice);
        Assert.Equal(BackgammonPosition.Start.Key, game.Position.Key);
        Assert.Equal([1, 0], game.Scores);
    }

    // The doubling cube

    [Fact]
    public void A_taken_double_doubles_the_cube_and_gives_it_to_the_taker()
    {
        var game = Opened(rollDie: Dice(3, 1));
        PlayOpening(game);

        Assert.True(game.TryDouble(Black, out var error), error);
        Assert.False(game.TryRoll(Black, out _));
        Assert.False(game.TryTake(Black, out _));
        Assert.True(game.TryTake(White, out error), error);

        Assert.Equal((2, White), (game.CubeValue, game.CubeOwner));
        Assert.Equal((Black, BackgammonPhase.Rolling), (game.Turn, game.Phase));
        Assert.False(game.CanDouble);
        Assert.False(game.TryDouble(Black, out error));
        Assert.Equal("The other player owns the cube.", error);
    }

    [Fact]
    public void A_dropped_double_loses_the_game_at_the_cubes_value()
    {
        var game = Opened(rollDie: Dice(3, 1));
        PlayOpening(game);

        game.TryDouble(Black, out _);
        Assert.True(game.TryDrop(White, out var error), error);

        Assert.Equal(new BackgammonResult(Black, 1, BackgammonResultKind.Dropped), game.Result);
        Assert.Equal([0, 1], game.Scores);
        Assert.Equal(BackgammonPhase.GameOver, game.Phase);
    }

    [Fact]
    public void Without_the_cube_nobody_can_double()
    {
        var game = Opened(new BackgammonRules(Cube: false), Dice(3, 1));
        PlayOpening(game);

        Assert.False(game.CanDouble);
        Assert.False(game.TryDouble(Black, out var error));
        Assert.Equal("This match is played without the doubling cube.", error);
    }

    [Fact]
    public void Nobody_may_double_in_the_game_after_a_player_first_comes_within_one_point()
    {
        var game = Opened(new BackgammonRules(MatchLength: 3), Dice(3, 1, 2, 1, 3, 1, 2, 1, 3, 1));
        for (var g = 1; g <= 2; g++)
        {
            Assert.False(game.IsCrawford);
            PlayOpening(game);
            game.TryDouble(Black, out _);
            game.TryDrop(White, out _);
            game.TryNextGame(g, out _);
            Open(game);
        }

        Assert.Equal([0, 2], game.Scores);
        Assert.True(game.IsCrawford);
        PlayOpening(game);
        Assert.False(game.TryDouble(Black, out var error));
        Assert.Equal("Nobody may double in the Crawford game.", error);
    }

    [Fact]
    public void Random_games_always_reach_the_end_with_every_checker_accounted_for()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var random = new Random(seed);
            var game = new BackgammonGame(new BackgammonRules(MatchLength: 1, Cube: false), () => random.Next(1, 7));
            for (var actions = 0; !game.IsMatchOver; actions++)
            {
                Assert.True(actions < 5000, $"Game {seed} did not end.");
                var player = game.Turn;
                if (game.Phase == BackgammonPhase.Opening)
                {
                    Open(game);
                }
                else if (game.Phase == BackgammonPhase.Rolling)
                {
                    Assert.True(game.TryRoll(player, out var error), error);
                }
                else if (game.LegalMoves.Count == 0)
                {
                    Assert.True(game.TryEndTurn(player, out var error), error);
                }
                else
                {
                    var move = game.LegalMoves[random.Next(game.LegalMoves.Count)];
                    Assert.True(game.TryMove(player, move.From, move.To, out var error), error);
                }

                Assert.All(new[] { White, Black }, p => Assert.Equal(15,
                    game.Position.Count(p, Bar) + game.Position.Count(p, Off) + Enumerable.Range(1, 24).Sum(q => game.Position.Count(p, q))));
            }

            Assert.Equal(game.Result!.Winner, game.Turn);
        }
    }

    // A new match in which the opening roll made `starter` start, and they rolled `rollDie`'s first two numbers.
    private static BackgammonGame Opened(BackgammonRules? rules = null, Func<int>? rollDie = null, BackgammonPosition? start = null,
        int starter = White)
    {
        rollDie ??= () => Random.Shared.Next(1, 7);
        var opening = new Queue<int>(starter == White ? [2, 1] : [1, 2]);
        var game = new BackgammonGame(rules, () => opening.Count > 0 ? opening.Dequeue() : rollDie(), start);
        Open(game);
        return game;
    }

    // Rolls the opening dice, white first (and again after a tie), until a player starts; then they roll.
    private static void Open(BackgammonGame game)
    {
        while (game.Phase == BackgammonPhase.Opening)
        {
            var player = game.OpeningDice[0] is null || game.OpeningDice[1] is not null ? White : Black;
            Assert.True(game.TryRoll(player, out var error), error);
        }

        Assert.True(game.TryRoll(game.Turn, out var rollError), rollError);
    }

    // White opens with 3-1 (or 6-1) and makes a point; black is then to roll.
    private static void PlayOpening(BackgammonGame game)
    {
        var (from1, from2, to) = game.Dice.SequenceEqual([3, 1]) ? (8, 6, 5) : (13, 8, 7);
        Assert.True(game.TryMove(White, from1, to, out var error), error);
        Assert.True(game.TryMove(White, from2, to, out error), error);
        Assert.True(game.TryEndTurn(White, out error), error);
    }

    // A die that rolls the given numbers in order.
    private static Func<int> Dice(params int[] numbers)
    {
        var queue = new Queue<int>(numbers);
        return () => queue.Dequeue();
    }
}
