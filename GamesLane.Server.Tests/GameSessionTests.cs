using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.TicTacToe;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tests;

public class GameSessionTests
{
    private const string Alice = "user:alice";
    private const string Bob = "user:bob";
    private const string Carol = "user:carol";

    private static readonly PlayerProfile AliceProfile = new("Alice", "🦊");
    private static readonly PlayerProfile BobProfile = new("Bob", "🐼");
    private static readonly PlayerProfile CarolProfile = new("Carol", "🐙");

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    // Seats

    [Fact]
    public void A_new_session_is_in_the_lobby_with_empty_seats()
    {
        var state = new TicTacToeSession("ABC123").GetPublicState();

        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.Equal((2, 2), (state.MinSeats, state.MaxSeats));
        Assert.All(state.Seats, Assert.Null);
        Assert.Equal(0, state.GameNumber);
        Assert.Null(state.Game);
        Assert.False(state.CanResume);
    }

    [Fact]
    public void Taking_all_seats_does_not_start_the_game()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.Equal(["Alice", "Bob"], state.Seats.Select(s => s!.Name));
        Assert.Equal(["🦊", "🐼"], state.Seats.Select(s => s!.Icon));
        Assert.All(state.Seats, s => Assert.True(s!.IsConnected));
    }

    [Fact]
    public void Seats_show_a_player_id_per_user()
    {
        var session = new TicTacToeSession("ABC123");
        var alicePlayerId = session.Enter(Alice, AliceProfile, "conn-a");
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Bob, BobProfile, "conn-b", 1, out _);

        var seats = session.GetPublicState().Seats;
        Assert.Equal(alicePlayerId, seats[0]!.PlayerId);
        Assert.NotEqual(seats[0]!.PlayerId, seats[1]!.PlayerId);
        Assert.Equal(alicePlayerId, session.Enter(Alice, AliceProfile, "conn-a2"));
        Assert.Null(session.Enter("connection:x", null, "x"));
    }

    [Fact]
    public void A_user_can_take_several_seats_and_play_them_all()
    {
        var session = new TicTacToeSession("ABC123");
        Assert.True(session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _));
        Assert.True(session.TryTakeSeat(Alice, AliceProfile, "conn-a", 1, out _));
        Assert.True(session.TryNewGame(Alice, 0, out _));

        Assert.True(session.TryMove(Alice, 0, 0, out var error), error);
        Assert.True(session.TryMove(Alice, 1, 4, out error), error);
        Assert.Equal([Mark.X, Mark.O], new[] { session.GetState().Board[0], session.GetState().Board[4] });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Rejects_seats_that_do_not_exist(int seat)
    {
        var session = new TicTacToeSession("ABC123");

        Assert.False(session.TryTakeSeat(Alice, AliceProfile, "conn-a", seat, out var error));
        Assert.Equal("There is no such seat.", error);
    }

    [Fact]
    public void Cannot_take_the_seat_of_a_connected_user()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.False(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 0, out var error));
        Assert.Equal("That seat is taken.", error);
        Assert.False(session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out error));
        Assert.Equal("You are already in this seat.", error);
    }

    [Fact]
    public void Can_take_the_seat_of_a_disconnected_user()
    {
        var session = Seated(new TicTacToeSession("ABC123"));
        Assert.True(session.Disconnect("conn-a", Now));

        Assert.True(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 0, out var error), error);
        Assert.Equal("Carol", session.GetPublicState().Seats[0]!.Name);
    }

    [Fact]
    public void Leaving_a_seat_empties_it()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.False(session.TryLeaveSeat(Bob, 0, out var error));
        Assert.Equal("That is not your seat.", error);
        Assert.True(session.TryLeaveSeat(Alice, 0, out _));
        Assert.Null(session.GetPublicState().Seats[0]);
    }

    [Fact]
    public void Entering_again_updates_the_name_and_icon_on_the_users_seats()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        session.Enter(Alice, new PlayerProfile("Ada", "🚀"), "conn-a2");

        var seats = session.GetPublicState().Seats;
        Assert.Equal(["Ada", "Bob"], seats.Select(s => s!.Name));
        Assert.Equal(["🚀", "🐼"], seats.Select(s => s!.Icon));
    }

    // Starting

    [Fact]
    public void Starting_needs_the_minimum_number_of_seated_players()
    {
        var session = new TicTacToeSession("ABC123");
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);

        Assert.False(session.TryNewGame(Alice, 0, out var error));
        Assert.Equal("At least 2 players must be seated.", error);
    }

    [Fact]
    public void Only_seated_users_can_start()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.False(session.TryNewGame(Carol, 0, out var error));
        Assert.Equal("Only seated players can do that.", error);
        Assert.Equal(SessionPhase.Lobby, session.GetPublicState().Phase);
    }

    [Fact]
    public void Any_seated_user_can_start_and_a_second_press_changes_nothing()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.True(session.TryNewGame(Bob, 0, out _));
        session.TryMove(Alice, 0, 4, out _);
        Assert.True(session.TryNewGame(Alice, 0, out _));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal(1, state.GameNumber);
        Assert.Equal(Mark.X, session.GetState().Board[4]);
    }

    [Fact]
    public void Seats_cannot_be_taken_during_a_game()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-b", Now);

        Assert.False(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out var error));
        Assert.Equal("The game has already started. You can watch it.", error);
    }

    // Pausing and resuming

    [Fact]
    public void Leaving_an_unfinished_game_pauses_it_in_the_lobby()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);

        Assert.True(session.TryLeaveSeat(Bob, 1, out _));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.True(state.CanResume);
        Assert.False(session.TryMove(Alice, 0, 1, out var error));
        Assert.Equal("The game is not in progress.", error);
    }

    [Fact]
    public void A_new_user_in_the_empty_seat_continues_the_paused_game()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.TryLeaveSeat(Bob, 1, out _);

        Assert.False(session.TryResume(Alice, out var error));
        Assert.Equal("Every player's seat must be taken to resume.", error);

        session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out _);
        Assert.True(session.TryResume(Carol, out error), error);

        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.False(session.TryMove(Bob, 1, 4, out _));
        Assert.True(session.TryMove(Carol, 1, 4, out error), error);
        Assert.Equal(Mark.X, session.GetState().Board[0]);
    }

    [Fact]
    public void A_new_game_can_replace_the_paused_game()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.TryLeaveSeat(Bob, 1, out _);
        session.TryTakeSeat(Bob, BobProfile, "conn-b", 1, out _);

        Assert.True(session.TryNewGame(Bob, 1, out _));

        var state = session.GetPublicState();
        Assert.Equal(2, state.GameNumber);
        Assert.False(state.CanResume);
        Assert.Equal(Mark.None, session.GetState().Board[0]);
    }

    [Fact]
    public void Leaving_all_seats_frees_them_and_pauses_the_game()
    {
        var session = new TicTacToeSession("ABC123");
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 1, out _);
        session.TryNewGame(Alice, 0, out _);

        Assert.True(session.LeaveAllSeats(Alice));
        Assert.False(session.LeaveAllSeats(Alice));

        var state = session.GetPublicState();
        Assert.All(state.Seats, Assert.Null);
        Assert.Equal(SessionPhase.Lobby, state.Phase);
    }

    // After a game ends

    [Fact]
    public void A_new_game_can_start_while_the_game_is_in_progress_only_after_it_ends()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.False(session.TryNewGame(Alice, 1, out var error));
        Assert.Equal("The game is not over yet.", error);

        Finish(session);
        Assert.True(session.GetPublicState().IsGameOver);
        Assert.True(session.TryNewGame(Bob, 1, out _));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal(2, state.GameNumber);
        Assert.False(state.IsGameOver);
        Assert.True(session.TryMove(Alice, 0, 4, out error), error);
    }

    [Fact]
    public void Standing_up_after_the_game_ends_returns_to_the_lobby_when_too_few_remain()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));

        session.TryLeaveSeat(Bob, 1, out _);

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.False(state.CanResume);
    }

    [Fact]
    public void Players_can_return_to_the_lobby_only_after_the_game_ends()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.False(session.TryReturnToLobby(Alice, out var error));
        Assert.Equal("The game is not over yet.", error);

        Finish(session);
        Assert.True(session.TryReturnToLobby(Bob, out _));
        Assert.Equal(SessionPhase.Lobby, session.GetPublicState().Phase);
        Assert.Equal(["Alice", "Bob"], session.GetPublicState().Seats.Select(s => s!.Name));
    }

    // Disconnecting

    [Fact]
    public void A_disconnected_player_keeps_their_seat_until_released()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.True(session.Disconnect("conn-a", Now));
        Assert.False(session.GetPublicState().Seats[0]!.IsConnected);
        Assert.False(session.ReleaseDisconnectedSeats(Now.AddSeconds(-1)));
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);

        Assert.True(session.ReleaseDisconnectedSeats(Now));
        var state = session.GetPublicState();
        Assert.Null(state.Seats[0]);
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.True(state.CanResume);
    }

    [Fact]
    public void Reconnecting_in_time_keeps_the_seat()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-a", Now);

        session.Enter(Alice, AliceProfile, "conn-a2");

        Assert.True(session.GetPublicState().Seats[0]!.IsConnected);
        Assert.False(session.ReleaseDisconnectedSeats(Now.AddMinutes(1)));
        Assert.True(session.TryMove(Alice, 0, 0, out var error), error);
    }

    [Fact]
    public void A_user_stays_connected_while_any_of_their_connections_is_open()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Enter(Alice, AliceProfile, "conn-a2");

        Assert.False(session.Disconnect("conn-a", Now));
        Assert.True(session.GetPublicState().Seats[0]!.IsConnected);
        Assert.False(session.Disconnect("spectator", Now));
    }

    // Abandoned sessions

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    [Fact]
    public void A_session_nobody_opens_is_abandoned_after_the_grace_period()
    {
        var session = new TicTacToeSession("ABC123");

        Assert.False(session.IsAbandoned(Now, Grace));
        Assert.False(session.IsAbandoned(Now.AddSeconds(29), Grace));
        Assert.True(session.IsAbandoned(Now.AddSeconds(30), Grace));
    }

    [Fact]
    public void A_session_with_a_spectator_is_not_abandoned()
    {
        var session = new TicTacToeSession("ABC123");
        session.Enter("connection:tv", null, "tv");

        Assert.False(session.IsAbandoned(Now, Grace));
        Assert.False(session.IsAbandoned(Now.AddMinutes(5), Grace));
    }

    [Fact]
    public void A_disconnected_player_keeps_the_session_until_their_seat_is_freed()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-a", Now);
        session.Disconnect("conn-b", Now);

        Assert.False(session.IsAbandoned(Now.AddMinutes(5), Grace));

        session.ReleaseDisconnectedSeats(Now);
        Assert.False(session.IsAbandoned(Now.AddMinutes(5), Grace));
        Assert.True(session.IsAbandoned(Now.AddMinutes(5).AddSeconds(30), Grace));
    }

    [Fact]
    public void Coming_back_restarts_the_wait()
    {
        var session = new TicTacToeSession("ABC123");
        session.IsAbandoned(Now, Grace);
        session.Enter("connection:x", null, "x");
        Assert.False(session.IsAbandoned(Now.AddSeconds(20), Grace));
        session.Disconnect("x", Now.AddSeconds(20));

        Assert.False(session.IsAbandoned(Now.AddSeconds(40), Grace));
        Assert.True(session.IsAbandoned(Now.AddSeconds(70), Grace));
    }

    // Tic-tac-toe

    [Fact]
    public void Players_move_with_their_seats_mark()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.False(session.TryMove(Bob, 1, 0, out var error));
        Assert.Equal("It is not your turn.", error);
        Assert.True(session.TryMove(Alice, 0, 0, out _));
        Assert.True(session.TryMove(Bob, 1, 4, out _));

        var board = session.GetState().Board;
        Assert.Equal(Mark.X, board[0]);
        Assert.Equal(Mark.O, board[4]);
    }

    [Fact]
    public void Players_cannot_move_for_a_seat_they_do_not_hold()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.False(session.TryMove(Bob, 0, 0, out var error));
        Assert.Equal("Only players in this game can make moves.", error);
        Assert.False(session.TryMove(Carol, 0, 0, out error));
        Assert.Equal("Only players in this game can make moves.", error);
    }

    [Fact]
    public void No_moves_after_a_win()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));

        Assert.Equal(Mark.X, session.GetState().Winner);
        Assert.False(session.TryMove(Bob, 1, 5, out var error));
        Assert.Equal("The game is not in progress.", error);
    }

    [Fact]
    public void State_is_a_snapshot()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        var before = session.GetState();

        session.TryMove(Alice, 0, 0, out _);

        Assert.Equal(Mark.None, before.Board[0]);
        Assert.Equal(Mark.X, session.GetState().Board[0]);
    }

    // Rock-paper-scissors

    [Fact]
    public void Rock_paper_scissors_pick_survives_a_reconnect()
    {
        var session = Started(new RockPaperScissorsSession("ABC123"));
        session.TryPick(Alice, 0, Choice.Rock, out _);

        session.Disconnect("conn-a", Now);
        session.Enter(Alice, AliceProfile, "conn-a2");

        Assert.True(session.GetState().Hands[0].HasPicked);
        Assert.False(session.TryPick(Alice, 0, Choice.Paper, out var error));
        Assert.Equal("You have already picked this round.", error);
    }

    [Fact]
    public void Rock_paper_scissors_scores_survive_a_pause()
    {
        var session = Started(new RockPaperScissorsSession("ABC123"));
        session.TryPick(Alice, 0, Choice.Rock, out _);
        session.TryPick(Bob, 1, Choice.Scissors, out _);

        session.TryLeaveSeat(Bob, 1, out _);
        session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out _);
        session.TryResume(Alice, out _);

        Assert.Equal([1, 0], session.GetState().Hands.Select(h => h.Score));
        Assert.True(session.TryNextRound(Carol, 1, out var error), error);
    }

    [Fact]
    public void Picks_are_hidden_until_both_players_have_picked()
    {
        var session = StartedRpsSession();

        Assert.True(session.TryPick(Alice, 0, Choice.Rock, out _));
        var hidden = session.GetState();
        Assert.False(hidden.IsRoundComplete);
        Assert.True(hidden.Hands[0].HasPicked);
        Assert.False(hidden.Hands[1].HasPicked);
        Assert.All(hidden.Hands, h => Assert.Equal(Choice.None, h.Pick));

        Assert.True(session.TryPick(Bob, 1, Choice.Paper, out _));
        var revealed = session.GetState();
        Assert.True(revealed.IsRoundComplete);
        Assert.Equal([Choice.Rock, Choice.Paper], revealed.Hands.Select(h => h.Pick));
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

        session.TryPick(Alice, 0, first, out _);
        session.TryPick(Bob, 1, second, out _);

        var state = session.GetState();
        Assert.Equal(winner, state.RoundWinner);
        Assert.Equal(winner == 0 ? 1 : 0, state.Hands[0].Score);
        Assert.Equal(winner == 1 ? 1 : 0, state.Hands[1].Score);
        Assert.Equal(winner is null ? 1 : 0, state.Draws);
    }

    [Fact]
    public void Cannot_pick_twice_in_a_round()
    {
        var session = StartedRpsSession();
        session.TryPick(Alice, 0, Choice.Rock, out _);

        Assert.False(session.TryPick(Alice, 0, Choice.Paper, out var error));
        Assert.Equal("You have already picked this round.", error);
    }

    [Fact]
    public void Cannot_pick_after_the_round_is_decided()
    {
        var session = StartedRpsSession();
        session.TryPick(Alice, 0, Choice.Rock, out _);
        session.TryPick(Bob, 1, Choice.Rock, out _);

        Assert.False(session.TryPick(Alice, 0, Choice.Paper, out var error));
        Assert.Equal("This round is over. Start the next round.", error);
    }

    [Theory]
    [InlineData(Choice.None)]
    [InlineData((Choice)42)]
    public void Rejects_invalid_picks(Choice choice)
    {
        var session = StartedRpsSession();

        Assert.False(session.TryPick(Alice, 0, choice, out var error));
        Assert.Equal("Pick rock, paper or scissors.", error);
    }

    [Fact]
    public void Only_players_can_pick_and_only_once_the_game_has_started()
    {
        var session = Seated(new RockPaperScissorsSession("ABC123"));

        Assert.False(session.TryPick(Alice, 0, Choice.Rock, out var error));
        Assert.Equal("The game is not in progress.", error);

        session.TryNewGame(Alice, 0, out _);
        Assert.False(session.TryPick(Carol, 0, Choice.Rock, out error));
        Assert.Equal("Only players in this game can make moves.", error);
    }

    [Fact]
    public void A_user_with_both_seats_picks_for_each()
    {
        var session = new RockPaperScissorsSession("ABC123");
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 1, out _);
        session.TryNewGame(Alice, 0, out _);

        Assert.True(session.TryPick(Alice, 0, Choice.Rock, out _));
        Assert.True(session.TryPick(Alice, 1, Choice.Paper, out _));
        Assert.Equal(1, session.GetState().RoundWinner);
    }

    [Fact]
    public void Next_round_clears_picks_and_keeps_scores()
    {
        var session = StartedRpsSession();
        session.TryPick(Alice, 0, Choice.Rock, out _);
        session.TryPick(Bob, 1, Choice.Scissors, out _);

        Assert.True(session.TryNextRound(Bob, 1, out _));

        var state = session.GetState();
        Assert.Equal(2, state.Round);
        Assert.False(state.IsRoundComplete);
        Assert.Null(state.RoundWinner);
        Assert.All(state.Hands, h => Assert.False(h.HasPicked));
        Assert.Equal([1, 0], state.Hands.Select(h => h.Score));
    }

    [Fact]
    public void Next_round_requires_both_picks()
    {
        var session = StartedRpsSession();
        session.TryPick(Alice, 0, Choice.Rock, out _);

        Assert.False(session.TryNextRound(Alice, 1, out var error));
        Assert.Equal("This round is not finished yet.", error);
    }

    [Fact]
    public void Next_round_pressed_by_both_players_advances_once()
    {
        var session = StartedRpsSession();
        session.TryPick(Alice, 0, Choice.Rock, out _);
        session.TryPick(Bob, 1, Choice.Rock, out _);

        Assert.True(session.TryNextRound(Alice, 1, out _));
        Assert.True(session.TryNextRound(Bob, 1, out _));

        Assert.Equal(2, session.GetState().Round);
    }

    [Fact]
    public void Spectators_cannot_start_the_next_round()
    {
        var session = StartedRpsSession();
        session.TryPick(Alice, 0, Choice.Rock, out _);
        session.TryPick(Bob, 1, Choice.Rock, out _);

        Assert.False(session.TryNextRound(Carol, 1, out _));
        Assert.Equal(1, session.GetState().Round);
    }

    // The Gang

    // Seat 0: high card king; seat 1: pair of nines; seat 2: three kings.
    private static TheGangSession TheGang() =>
        new("ABC123", () => "3D 4H 9C 5S KD KH 2C 7D 9H JS KC".Split(' ').Select(Card.Parse).ToArray());

    private static TheGangSession StartedTheGang()
    {
        var session = TheGang();
        session.Enter(Alice, AliceProfile, "conn-a");
        session.Enter(Bob, BobProfile, "conn-b");
        session.Enter(Carol, CarolProfile, "conn-c");
        session.Enter("connection:tv", null, "tv");
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Bob, BobProfile, "conn-b", 1, out _);
        session.TryTakeSeat(Carol, CarolProfile, "conn-c", 2, out _);
        Assert.True(session.TryNewGame(Alice, 0, out var error), error);
        return session;
    }

    private static TheGangState Gang(TheGangSession session) => Assert.IsType<TheGangState>(session.GetPublicState().Game);

    [Fact]
    public void The_gang_needs_at_least_3_of_its_6_seats()
    {
        var session = TheGang();
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Bob, BobProfile, "conn-b", 4, out _);

        var state = session.GetPublicState();
        Assert.Equal((3, 6), (state.MinSeats, state.MaxSeats));
        Assert.False(session.TryNewGame(Alice, 0, out var error));
        Assert.Equal("At least 3 players must be seated.", error);
    }

    [Fact]
    public void Each_player_sees_only_their_own_cards()
    {
        var session = StartedTheGang();

        var updates = session.GetPrivateUpdates().ToDictionary(u => u.ConnectionId, u => Assert.IsType<TheGangPrivateState>(u.State));
        Assert.Equal(["conn-a", "conn-b", "conn-c"], updates.Keys.Order());
        Assert.Equal(["3♦", "4♥"], updates["conn-a"].Hands.Single().Cards);
        Assert.Equal(["K♦", "K♥"], updates["conn-c"].Hands.Single().Cards);
        Assert.Null(session.GetPrivateState("connection:tv"));
        Assert.Empty(Gang(session).Revealed);
    }

    [Fact]
    public void A_user_with_two_seats_sees_both_hands()
    {
        var session = TheGang();
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 3, out _);
        session.TryTakeSeat(Bob, BobProfile, "conn-b", 5, out _);
        session.TryNewGame(Alice, 0, out _);

        var hands = Assert.IsType<TheGangPrivateState>(session.GetPrivateState(Alice)).Hands;
        Assert.Equal([0, 3], hands.Select(h => h.Seat));
        Assert.Equal([0, 3, 5], Gang(session).PlayingSeats);
    }

    [Fact]
    public void Players_only_take_chips_for_their_own_seats()
    {
        var session = StartedTheGang();

        Assert.False(session.TryTakeChip(Bob, 0, 1, out var error));
        Assert.Equal("Only players in this game can make moves.", error);
        Assert.True(session.TryTakeChip(Alice, 0, 1, out error), error);
        Assert.Equal(1, Gang(session).Chips[0][0]);
    }

    [Fact]
    public void A_full_heist_reveals_hands_to_everyone_one_at_a_time()
    {
        var session = StartedTheGang();
        for (var stage = 0; stage < TheGangGame.StageCount; stage++)
        {
            session.TryTakeChip(Alice, 0, 1, out _);
            session.TryTakeChip(Bob, 1, 2, out _);
            session.TryTakeChip(Carol, 2, 3, out _);
            session.TrySetReady(Alice, 0, true, out _);
            session.TrySetReady(Bob, 1, true, out _);
            session.TrySetReady(Carol, 2, true, out _);
        }

        Assert.Equal(TheGangPhase.Reveal, Gang(session).Phase);
        Assert.False(session.TryRevealNext("connection:tv", 0, out _));
        Assert.True(session.TryRevealNext(Bob, 0, out var error), error);

        var revealed = Assert.Single(Gang(session).Revealed);
        Assert.Equal(0, revealed.Seat);
        Assert.Equal("High card, king", revealed.Name);

        session.TryRevealNext(Alice, 1, out _);
        session.TryRevealNext(Alice, 2, out _);
        var state = Gang(session);
        Assert.Equal(TheGangPhase.HeistOver, state.Phase);
        Assert.Equal(1, state.Vaults);
        Assert.Equal(["Pair of nines", "Three kings"], state.Revealed.Skip(1).Select(h => h.Name));
        Assert.True(session.TryNextHeist(Carol, 1, out error), error);
        Assert.Equal(2, Gang(session).Heist);
    }

    [Fact]
    public void Leaving_during_a_heist_pauses_the_game_and_a_new_player_continues_that_hand()
    {
        var session = StartedTheGang();
        session.TryTakeChip(Alice, 0, 2, out _);
        session.TryLeaveSeat(Alice, 0, out _);

        Assert.Equal(SessionPhase.Lobby, session.GetPublicState().Phase);
        session.TryTakeSeat("user:dan", new PlayerProfile("Dan", "🐢"), "conn-d", 0, out _);
        Assert.True(session.TryResume(Bob, out var error), error);

        Assert.Equal(2, Gang(session).Chips[0][0]);
        var dan = Assert.IsType<TheGangPrivateState>(session.GetPrivateState("user:dan")).Hands.Single();
        Assert.Equal(["3♦", "4♥"], dan.Cards);
        Assert.Null(session.GetPrivateState(Alice));
    }

    private static RockPaperScissorsSession StartedRpsSession() => Started(new RockPaperScissorsSession("ABC123"));

    private static T Seated<T>(T session) where T : GameSession
    {
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Bob, BobProfile, "conn-b", 1, out _);
        return session;
    }

    private static T Started<T>(T session) where T : GameSession
    {
        Seated(session).TryNewGame(Alice, 0, out _);
        return session;
    }

    private static TicTacToeSession Finish(TicTacToeSession session)
    {
        foreach (var (user, seat, cell) in new[] { (Alice, 0, 0), (Bob, 1, 3), (Alice, 0, 1), (Bob, 1, 4), (Alice, 0, 2) })
        {
            Assert.True(session.TryMove(user, seat, cell, out var error), error);
        }

        return session;
    }
}

/// <summary>
/// Reads the game-specific state the way clients receive it, from <see cref="SessionState.Game"/>.
/// </summary>
internal static class GameStateExtensions
{
    public static TicTacToeState GetState(this TicTacToeSession session) =>
        Assert.IsType<TicTacToeState>(session.GetPublicState().Game);

    public static RockPaperScissorsState GetState(this RockPaperScissorsSession session) =>
        Assert.IsType<RockPaperScissorsState>(session.GetPublicState().Game);
}
