using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Games.TicTacToe;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tests;

public class GameSessionTests
{
    [Fact]
    public void First_player_is_X_and_second_is_O()
    {
        var session = new TicTacToeSession("ABC123");

        Assert.True(session.TryAddPlayer("Alice", "conn-a", out var alice, out _));
        Assert.True(session.TryAddPlayer("Bob", "conn-b", out var bob, out _));

        Assert.Equal(0, alice.Seat);
        Assert.Equal(1, bob.Seat);
        Assert.NotEqual(alice.Id, bob.Id);
        Assert.Equal([Mark.X, Mark.O], session.GetState().Players.Select(p => p.Mark));
    }

    [Fact]
    public void Game_starts_when_both_players_have_joined()
    {
        var session = new TicTacToeSession("ABC123");
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
        var session = new TicTacToeSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _);

        Assert.False(session.TryAddPlayer("Alice again", "conn-a", out _, out var error));
        Assert.Equal("You have already joined this game.", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_player_name(string name)
    {
        var session = new TicTacToeSession("ABC123");

        Assert.False(session.TryAddPlayer(name, "conn-a", out _, out var error));
        Assert.Equal("Player name is required.", error);
    }

    [Fact]
    public void Trims_player_names()
    {
        var session = new TicTacToeSession("ABC123");

        session.TryAddPlayer("  Alice  ", "conn-a", out var player, out _);

        Assert.Equal("Alice", player!.Name);
        Assert.Equal("Alice", session.GetState().Players[0].Name);
    }

    [Fact]
    public void No_moves_before_both_players_join()
    {
        var session = new TicTacToeSession("ABC123");
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

    private static TicTacToeSession StartedSession() => Started(new TicTacToeSession("ABC123"));

    // Rejoining

    [Fact]
    public void Player_rejoins_their_seat_from_a_new_connection()
    {
        var session = new TicTacToeSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _, "guest:alice");
        session.TryAddPlayer("Bob", "conn-b", out _, out _, "guest:bob");

        Assert.True(session.Disconnect("conn-a"));
        Assert.False(session.GetState().Players[0].IsConnected);

        Assert.True(session.TryRejoin("guest:alice", "conn-a2", out var player));
        Assert.Equal(0, player.Seat);
        Assert.True(session.GetState().Players[0].IsConnected);

        Assert.False(session.TryMove("conn-a", 0, out _));
        Assert.True(session.TryMove("conn-a2", 0, out var error), error);
    }

    [Fact]
    public void Unknown_user_cannot_rejoin()
    {
        var session = StartedSession();

        Assert.False(session.TryRejoin("guest:stranger", "conn-x", out _));
    }

    [Fact]
    public void Same_user_cannot_take_two_seats()
    {
        var session = new TicTacToeSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _, "user:alice");

        Assert.False(session.TryAddPlayer("Alice", "conn-a2", out _, out var error, "user:alice"));
        Assert.Equal("You have already joined this game.", error);
    }

    [Fact]
    public void Disconnect_ignores_unknown_and_already_disconnected_connections()
    {
        var session = StartedSession();

        Assert.False(session.Disconnect("spectator"));
        Assert.True(session.Disconnect("conn-a"));
        Assert.False(session.Disconnect("conn-a"));
    }

    [Fact]
    public void Rock_paper_scissors_pick_survives_a_reconnect()
    {
        var session = new RockPaperScissorsSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _, "guest:alice");
        session.TryAddPlayer("Bob", "conn-b", out _, out _, "guest:bob");
        session.TryPick("conn-a", Choice.Rock, out _);

        session.Disconnect("conn-a");
        session.TryRejoin("guest:alice", "conn-a2", out _);

        Assert.True(session.GetState().Players[0].HasPicked);
        Assert.False(session.TryPick("conn-a2", Choice.Paper, out var error));
        Assert.Equal("You have already picked this round.", error);
    }

    // Rock-paper-scissors

    [Fact]
    public void Picks_are_hidden_until_both_players_have_picked()
    {
        var session = StartedRpsSession();

        Assert.True(session.TryPick("conn-a", Choice.Rock, out _));
        var hidden = session.GetState();
        Assert.False(hidden.IsRoundComplete);
        Assert.True(hidden.Players[0].HasPicked);
        Assert.False(hidden.Players[1].HasPicked);
        Assert.All(hidden.Players, p => Assert.Equal(Choice.None, p.Pick));

        Assert.True(session.TryPick("conn-b", Choice.Paper, out _));
        var revealed = session.GetState();
        Assert.True(revealed.IsRoundComplete);
        Assert.Equal([Choice.Rock, Choice.Paper], revealed.Players.Select(p => p.Pick));
    }

    [Theory]
    [InlineData(Choice.Rock, Choice.Scissors, 0)]
    [InlineData(Choice.Paper, Choice.Rock, 0)]
    [InlineData(Choice.Scissors, Choice.Paper, 0)]
    [InlineData(Choice.Scissors, Choice.Rock, 1)]
    [InlineData(Choice.Rock, Choice.Paper, 1)]
    [InlineData(Choice.Paper, Choice.Scissors, 1)]
    [InlineData(Choice.Rock, Choice.Rock, null)]
    [InlineData(Choice.Paper, Choice.Paper, null)]
    [InlineData(Choice.Scissors, Choice.Scissors, null)]
    public void Decides_and_scores_the_round(Choice first, Choice second, int? winner)
    {
        var session = StartedRpsSession();

        session.TryPick("conn-a", first, out _);
        session.TryPick("conn-b", second, out _);

        var state = session.GetState();
        Assert.Equal(winner, state.RoundWinner);
        Assert.Equal(winner == 0 ? 1 : 0, state.Players[0].Score);
        Assert.Equal(winner == 1 ? 1 : 0, state.Players[1].Score);
        Assert.Equal(winner is null ? 1 : 0, state.Draws);
    }

    [Fact]
    public void Cannot_pick_twice_in_a_round()
    {
        var session = StartedRpsSession();
        session.TryPick("conn-a", Choice.Rock, out _);

        Assert.False(session.TryPick("conn-a", Choice.Paper, out var error));
        Assert.Equal("You have already picked this round.", error);
    }

    [Fact]
    public void Cannot_pick_after_the_round_is_decided()
    {
        var session = StartedRpsSession();
        session.TryPick("conn-a", Choice.Rock, out _);
        session.TryPick("conn-b", Choice.Rock, out _);

        Assert.False(session.TryPick("conn-a", Choice.Paper, out var error));
        Assert.Equal("This round is over. Start the next round.", error);
    }

    [Theory]
    [InlineData(Choice.None)]
    [InlineData((Choice)42)]
    public void Rejects_invalid_picks(Choice choice)
    {
        var session = StartedRpsSession();

        Assert.False(session.TryPick("conn-a", choice, out var error));
        Assert.Equal("Pick rock, paper or scissors.", error);
    }

    [Fact]
    public void Only_players_can_pick_and_only_once_both_have_joined()
    {
        var session = new RockPaperScissorsSession("ABC123");
        session.TryAddPlayer("Alice", "conn-a", out _, out _);

        Assert.False(session.TryPick("conn-a", Choice.Rock, out var error));
        Assert.Equal("The game is not in progress.", error);

        session.TryAddPlayer("Bob", "conn-b", out _, out _);
        Assert.False(session.TryPick("spectator", Choice.Rock, out error));
        Assert.Equal("Only players in this game can make moves.", error);
    }

    [Fact]
    public void Next_round_clears_picks_and_keeps_scores()
    {
        var session = StartedRpsSession();
        session.TryPick("conn-a", Choice.Rock, out _);
        session.TryPick("conn-b", Choice.Scissors, out _);

        Assert.True(session.TryNextRound("conn-b", 1, out _));

        var state = session.GetState();
        Assert.Equal(2, state.Round);
        Assert.False(state.IsRoundComplete);
        Assert.Null(state.RoundWinner);
        Assert.All(state.Players, p => Assert.False(p.HasPicked));
        Assert.Equal([1, 0], state.Players.Select(p => p.Score));
    }

    [Fact]
    public void Next_round_requires_both_picks()
    {
        var session = StartedRpsSession();
        session.TryPick("conn-a", Choice.Rock, out _);

        Assert.False(session.TryNextRound("conn-a", 1, out var error));
        Assert.Equal("This round is not finished yet.", error);
    }

    [Fact]
    public void Next_round_pressed_by_both_players_advances_once()
    {
        var session = StartedRpsSession();
        session.TryPick("conn-a", Choice.Rock, out _);
        session.TryPick("conn-b", Choice.Rock, out _);

        Assert.True(session.TryNextRound("conn-a", 1, out _));
        Assert.True(session.TryNextRound("conn-b", 1, out _));

        Assert.Equal(2, session.GetState().Round);
    }

    [Fact]
    public void Spectators_cannot_start_the_next_round()
    {
        var session = StartedRpsSession();
        session.TryPick("conn-a", Choice.Rock, out _);
        session.TryPick("conn-b", Choice.Rock, out _);

        Assert.False(session.TryNextRound("spectator", 1, out _));
        Assert.Equal(1, session.GetState().Round);
    }

    private static RockPaperScissorsSession StartedRpsSession() => Started(new RockPaperScissorsSession("ABC123"));

    private static T Started<T>(T session) where T : GameSession
    {
        session.TryAddPlayer("Alice", "conn-a", out _, out _);
        session.TryAddPlayer("Bob", "conn-b", out _, out _);
        return session;
    }
}
