using GamesLane.Server.Games.Dominoes;

namespace GamesLane.Server.Tests;

public class DominoesGameTests
{
    [Fact]
    public void The_sets_have_28_and_91_tiles_written_low_end_first()
    {
        Assert.Equal(28, DominoesTiles.Set(6).Length);
        Assert.Equal(91, DominoesTiles.Set(12).Length);
        Assert.Equal((new Tile(3, 5), "3-5"), (Tile.Parse("5-3"), Tile.Of(5, 3).ToString()));
        Assert.Throws<FormatException>(() => Tile.Parse("3"));
    }

    [Fact]
    public void The_holder_of_the_highest_double_starts_and_each_play_matches_an_end_of_the_line()
    {
        var game = Game(new(DominoesStyle.Draw), "0-0 0-1 0-2 0-3 0-4 0-5 0-6", "6-6 1-1 1-2 1-3 1-4 1-5 1-6");
        Assert.Equal((1, 7, 14), (game.Turn, game.Hand(0).Count, game.BoneyardCount));
        AssertRefused(game.TryPlay(0, Tile.Parse("0-6"), 0, out var error), error, "It is not your turn.");
        AssertRefused(game.TryPlay(1, Tile.Parse("2-2"), 0, out error), error, "You do not have that tile.");

        Assert.True(game.TryPlay(1, Tile.Parse("6-6"), 0, out error), error);
        Assert.Equal([new DominoesMove(Tile.Parse("0-6"), 0), new DominoesMove(Tile.Parse("0-6"), 1)], game.Moves(0));
        AssertRefused(game.TryPlay(0, Tile.Parse("0-1"), 1, out error), error, "That tile does not match there.");
        Assert.True(game.TryPlay(0, Tile.Parse("0-6"), 1, out error), error);
        Assert.True(game.TryPlay(1, Tile.Parse("1-6"), 0, out error), error);

        Assert.Equal(["1-6", "6-6", "6-0"], game.Line.Select(t => t.ToString()));
        Assert.Equal(new DominoesAction(1, DominoesActionKind.Played, Tile.Parse("1-6"), 0), game.LastAction);
    }

    [Fact]
    public void In_draw_a_player_who_cannot_play_draws_until_they_can()
    {
        var game = Game(new(DominoesStyle.Draw), "6-6 5-5 4-4 3-3 2-2 1-1 0-1", "0-2 0-3 0-4 0-5 1-2 1-3 1-4");
        Assert.True(game.TryPlay(0, Tile.Parse("6-6"), 0, out var error), error);

        Assert.True(game.CanDraw(1));
        AssertRefused(game.TryPass(1, out error), error, "Draw a tile first.");
        Assert.True(game.TryDraw(1, out error), error);
        Assert.True(game.CanDraw(1));
        Assert.True(game.TryDraw(1, out error), error);
        Assert.Equal([Tile.Parse("0-6")], game.Moves(1).Select(m => m.Tile).Distinct());
        AssertRefused(game.TryDraw(1, out error), error, "You can play a tile.");
        Assert.Equal(12, game.BoneyardCount);
    }

    [Fact]
    public void In_block_a_player_who_cannot_play_passes_and_a_blocked_round_goes_to_the_fewest_pips()
    {
        var game = Game(new(DominoesStyle.Block), "6-6 0-6 0-0 0-1 0-2 0-3 0-4", "5-5 4-4 3-3 2-2 1-1 1-2 1-3");
        Assert.True(game.TryPlay(0, Tile.Parse("6-6"), 0, out var error), error);
        AssertRefused(game.TryDraw(1, out error), error, "In Block, a player who cannot play passes.");
        Assert.True(game.TryPass(1, out error), error);
        Assert.Equal(0, game.Turn);

        game = Game(new(DominoesStyle.Block), "6-6 0-0 0-1 0-2 0-3 0-4 0-5", "5-5 4-4 3-3 2-2 1-1 1-2 1-3");
        Assert.True(game.TryPlay(0, Tile.Parse("6-6"), 0, out error), error);

        var result = game.LastResult!;
        Assert.Equal((DominoesPhase.RoundOver, true, null), (game.Phase, result.Blocked, result.Domino));
        Assert.Equal([15, 37], result.Pips);
        Assert.Equal([37, 0], result.Points);
        Assert.True(game.TryNextRound(1, out error), error);
        Assert.Equal((2, 1), (game.RoundNumber, game.Turn));
    }

    [Fact]
    public void In_all_fives_open_ends_that_add_up_to_a_multiple_of_5_score()
    {
        var game = Game(new(DominoesStyle.AllFives), "5-5 0-1 0-2 0-3 0-4 0-6 1-1", "0-0 2-2 1-2 1-3 1-4 2-3 2-4");
        Assert.True(game.TryPlay(0, Tile.Parse("5-5"), 0, out var error), error);

        Assert.Equal(10, game.LastAction!.Points);
        Assert.Equal([10, 0], game.Scores);
    }

    [Fact]
    public void Four_players_can_play_as_two_teams_of_partners_across()
    {
        var game = new DominoesGame(4, new DominoesRules(DominoesStyle.Draw, Teams: true));
        Assert.Equal((true, 2, 0, 1), (game.HasTeams, game.Sides, game.SideOf(2), game.SideOf(3)));
        Assert.Equal(5, game.Hand(0).Count);
        Assert.False(new DominoesGame(3, new DominoesRules(DominoesStyle.Draw, Teams: true)).HasTeams);
    }

    [Fact]
    public void In_mexican_train_players_build_their_own_train_or_the_mexican_one_and_a_double_must_be_covered()
    {
        var game = Game(new(DominoesStyle.MexicanTrain),
            "0-12 0-0 1-1 2-2 3-3 4-4 5-5 6-6 7-7 8-8 9-9 10-10 11-11 0-1 0-2", "1-2 1-3 1-4 1-5 1-6 1-7 1-8 1-9 1-10 1-11 2-3 2-4 2-5 2-6 2-7");
        Assert.Equal((12, 15, 90 - 30), (game.Engine, game.Hand(1).Count, game.BoneyardCount));
        Assert.Equal([new DominoesMove(Tile.Parse("0-12"), 0), new DominoesMove(Tile.Parse("0-12"), DominoesGame.MexicanTarget)], game.Moves(0));
        Assert.True(game.TryPlay(0, Tile.Parse("0-12"), 0, out var error), error);

        Assert.True(game.TryDraw(1, out error), error);
        Assert.False(game.CanDraw(1));
        AssertRefused(game.TryDraw(1, out error), error, "You have already drawn. Play the tile you drew, or pass.");
        Assert.True(game.TryPass(1, out error), error);
        Assert.True(game.HasMarker(1));

        Assert.True(game.TryPlay(0, Tile.Parse("0-0"), 0, out error), error);
        Assert.Equal(0, game.Turn);
        Assert.Equal([new DominoesMove(Tile.Parse("0-1"), 0), new DominoesMove(Tile.Parse("0-2"), 0)], game.Moves(0));
        Assert.True(game.TryPlay(0, Tile.Parse("0-1"), 0, out error), error);
        Assert.Equal(["12-0", "0-0", "0-1"], game.Train(0).Select(t => t.ToString()));
        Assert.Equal(1, game.Turn);
    }

    [Theory]
    [InlineData(DominoesStyle.Block, 2, false)]
    [InlineData(DominoesStyle.Draw, 3, false)]
    [InlineData(DominoesStyle.Draw, 4, true)]
    [InlineData(DominoesStyle.AllFives, 2, false)]
    [InlineData(DominoesStyle.MexicanTrain, 6, false)]
    public void A_game_is_played_to_its_end(DominoesStyle style, int players, bool teams)
    {
        var game = new DominoesGame(players, new DominoesRules(style, teams, TrainRounds: 7), new Random(players).Shuffle);
        while (!game.IsOver)
        {
            var p = game.Turn;
            var done = game.Moves(p) is [var move, ..] ? game.TryPlay(p, move.Tile, move.Target, out var error)
                : game.CanDraw(p) ? game.TryDraw(p, out error)
                : game.TryPass(p, out error);
            Assert.True(done, error);
            if (game.Phase == DominoesPhase.RoundOver)
            {
                var result = game.LastResult!;
                if (style != DominoesStyle.MexicanTrain && result.Domino is { } domino)
                {
                    var others = Enumerable.Range(0, players).Where(q => game.SideOf(q) != game.SideOf(domino)).Sum(q => result.Pips[q]);
                    Assert.Equal(style == DominoesStyle.AllFives ? (others + 2) / 5 * 5 : others, result.Points[game.SideOf(domino)]);
                }

                Assert.True(game.TryNextRound(game.RoundNumber, out error), error);
            }
        }

        var best = style == DominoesStyle.MexicanTrain ? game.Scores.Min() : game.Scores.Max();
        Assert.Equal(Enumerable.Range(0, game.Sides).Where(s => game.Scores[s] == best), game.Winners);
        Assert.True(style == DominoesStyle.MexicanTrain ? game.RoundNumber == 7 : best >= 100);
    }

    // A game for as many players as deals, whose first deal gives each player the given tiles (short text, separated by
    // spaces), the rest of the set staying in order.
    private static DominoesGame Game(DominoesRules rules, params string[] hands)
    {
        var dealt = false;
        return new DominoesGame(hands.Length, rules, tiles =>
        {
            if (dealt)
            {
                return;
            }

            dealt = true;
            var first = hands.SelectMany(h => h.Split(' ').Select(Tile.Parse)).ToList();
            var rest = tiles.ToList();
            Assert.All(first, tile => Assert.True(rest.Remove(tile), $"The set has no {tile}."));
            first.Concat(rest).ToArray().CopyTo(tiles, 0);
        });
    }

    private static void AssertRefused(bool done, string? error, string expected)
    {
        Assert.False(done);
        Assert.Equal(expected, error);
    }
}
