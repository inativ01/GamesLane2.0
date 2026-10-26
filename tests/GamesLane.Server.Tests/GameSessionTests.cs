using GamesLane.Server.Games.TicTacToe;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tests;

public class GameSessionTests
{
    [Fact]
    public void First_player_is_X_and_second_is_O()
    {
        var session = new GameSession("ABC123");

        Assert.True(session.TryAddPlayer("Alice", "conn-a", out var alice, out _));
        Assert.True(session.TryAddPlayer("Bob", "conn-b", out var bob, out _));

        Assert.Equal(Mark.X, alice.Mark);
        Assert.Equal(Mark.O, bob.Mark);
        Assert.NotEqual(alice.Id, bob.Id);
    }

    [Fact]
    public void Game_starts_when_both_players_have_joined()
    {
        var session = new GameSession("ABC123");
        Assert.Equal(GameStatus.WaitingForPlayers, session.GetState().Status);

        session.TryAddPlayer("Alice", "conn-a", out _, out _);
        Assert.Equal(GameStatus.WaitingForPlayers, session.GetState().Status);

        session.TryAddPlayer("Bob", "conn-b", out _, out _);
        Assert.Equal(GameStatus.InProgress, session.GetState().Status);
    }

    [Fact]
    public void Rejects_a_third_player()
    {
        var session = StartedSession();

        Assert.False(session.TryAddPlayer("Carol", "conn-c", out _, out var error));
        Assert.Equal("The game is full.", error);
    }

    [Fact]
    public void Rejects_the_same_connection_joining_twice()
    {
        var session = new GameSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _);

        Assert.False(session.TryAddPlayer("Alice again", "conn-a", out _, out var error));
        Assert.Equal("You have already joined this game.", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_player_name(string name)
    {
        var session = new GameSession("ABC123");

        Assert.False(session.TryAddPlayer(name, "conn-a", out _, out var error));
        Assert.Equal("Player name is required.", error);
    }

    [Fact]
    public void Trims_player_names()
    {
        var session = new GameSession("ABC123");

        session.TryAddPlayer("  Alice  ", "conn-a", out var player, out _);

        Assert.Equal("Alice", player!.Name);
        Assert.Equal("Alice", session.GetState().Players[0].Name);
    }

    [Fact]
    public void No_moves_before_both_players_join()
    {
        var session = new GameSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _);

        Assert.False(session.TryMove("conn-a", 0, out var error));
        Assert.Equal("The game is not in progress.", error);
    }

    [Fact]
    public void Only_players_can_move()
    {
        var session = StartedSession();

        Assert.False(session.TryMove("spectator", 0, out var error));
        Assert.Equal("Only players in this game can make moves.", error);
    }

    [Fact]
    public void Players_move_with_their_own_mark()
    {
        var session = StartedSession();

        Assert.False(session.TryMove("conn-b", 0, out var error));
        Assert.Equal("It is not your turn.", error);

        Assert.True(session.TryMove("conn-a", 0, out _));
        Assert.True(session.TryMove("conn-b", 4, out _));

        var board = session.GetState().Board;
        Assert.Equal(Mark.X, board[0]);
        Assert.Equal(Mark.O, board[4]);
    }

    [Fact]
    public void Game_is_finished_after_a_win()
    {
        var session = StartedSession();
        foreach (var (connection, cell) in new[] { ("conn-a", 0), ("conn-b", 3), ("conn-a", 1), ("conn-b", 4), ("conn-a", 2) })
        {
            Assert.True(session.TryMove(connection, cell, out var moveError), moveError);
        }

        var state = session.GetState();
        Assert.Equal(GameStatus.Finished, state.Status);
        Assert.Equal(Mark.X, state.Winner);
        Assert.False(session.TryMove("conn-b", 5, out var error));
        Assert.Equal("The game is not in progress.", error);
    }

    [Fact]
    public void State_is_a_snapshot()
    {
        var session = StartedSession();
        var before = session.GetState();

        session.TryMove("conn-a", 0, out _);

        Assert.Equal(Mark.None, before.Board[0]);
        Assert.Equal(Mark.X, session.GetState().Board[0]);
    }

    private static GameSession StartedSession()
    {
        var session = new GameSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _);
        session.TryAddPlayer("Bob", "conn-b", out _, out _);
        return session;
    }
}
