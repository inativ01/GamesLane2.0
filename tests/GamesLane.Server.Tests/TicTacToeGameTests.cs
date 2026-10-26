using GamesLane.Server.Games.TicTacToe;

namespace GamesLane.Server.Tests;

public class TicTacToeGameTests
{
    [Fact]
    public void X_moves_first()
    {
        var game = new TicTacToeGame();

        Assert.False(game.TryMove(Mark.O, 0, out var error));
        Assert.Equal("It is not your turn.", error);
        Assert.True(game.TryMove(Mark.X, 0, out _));
    }

    [Fact]
    public void Turns_alternate()
    {
        var game = new TicTacToeGame();

        Assert.True(game.TryMove(Mark.X, 0, out _));
        Assert.Equal(Mark.O, game.CurrentTurn);
        Assert.False(game.TryMove(Mark.X, 1, out _));
        Assert.True(game.TryMove(Mark.O, 1, out _));
        Assert.Equal(Mark.X, game.CurrentTurn);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Rejects_cells_outside_the_board(int cell)
    {
        var game = new TicTacToeGame();

        Assert.False(game.TryMove(Mark.X, cell, out var error));
        Assert.Equal("Cell must be between 0 and 8.", error);
    }

    [Fact]
    public void Rejects_taken_cell()
    {
        var game = new TicTacToeGame();
        game.TryMove(Mark.X, 4, out _);

        Assert.False(game.TryMove(Mark.O, 4, out var error));
        Assert.Equal("That cell is already taken.", error);
        Assert.Equal(Mark.X, game.Board[4]);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(3, 4, 5)]
    [InlineData(6, 7, 8)]
    [InlineData(0, 3, 6)]
    [InlineData(1, 4, 7)]
    [InlineData(2, 5, 8)]
    [InlineData(0, 4, 8)]
    [InlineData(2, 4, 6)]
    public void Detects_every_winning_line(int a, int b, int c)
    {
        var game = new TicTacToeGame();
        int[] line = [a, b, c];
        var others = Enumerable.Range(0, 9).Except(line).ToArray();

        game.TryMove(Mark.X, a, out _);
        game.TryMove(Mark.O, others[0], out _);
        game.TryMove(Mark.X, b, out _);
        game.TryMove(Mark.O, others[1], out _);
        Assert.True(game.TryMove(Mark.X, c, out _));

        Assert.Equal(Mark.X, game.Winner);
        Assert.False(game.IsDraw);
        Assert.True(game.IsOver);
    }

    [Fact]
    public void Full_board_without_a_line_is_a_draw()
    {
        var game = new TicTacToeGame();

        // X O X
        // X O O
        // O X X
        Play(game, 0, 1, 2, 4, 3, 5, 7, 6, 8);

        Assert.True(game.IsDraw);
        Assert.Equal(Mark.None, game.Winner);
        Assert.True(game.IsOver);
    }

    [Fact]
    public void No_moves_after_the_game_is_over()
    {
        var game = new TicTacToeGame();
        Play(game, 0, 3, 1, 4, 2);

        Assert.False(game.TryMove(Mark.O, 5, out var error));
        Assert.Equal("The game is over.", error);
        Assert.Equal(Mark.None, game.Board[5]);
    }

    private static void Play(TicTacToeGame game, params int[] cells)
    {
        var mark = Mark.X;
        foreach (var cell in cells)
        {
            Assert.True(game.TryMove(mark, cell, out var error), error);
            mark = mark == Mark.X ? Mark.O : Mark.X;
        }
    }
}
