using GamesLane.Server.Games.Backgammon;
using GamesLane.Server.Games.RichManPoorMan;
using GamesLane.Server.Games.RockPaperScissors;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.TicTacToe;
using GamesLane.Server.Games.Uno;
using GamesLane.Server.Games.Yatzee;
using GamesLane.Server.Hubs;
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
        Assert.Empty(state.AbsentSeats);
    }

    [Fact]
    public void Taking_all_seats_does_not_start_the_game()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.Equal(["Alice", "Bob"], state.Seats.Select(s => s!.Name));
        Assert.Equal(["🦊", "🐼"], state.Seats.Select(s => s!.Icon));
        Assert.All(state.Seats, s => Assert.Equal(SeatStatus.Present, s!.Status));
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
    public void In_the_lobby_a_disconnected_users_seat_is_kept_for_the_reconnect_window_then_freed()
    {
        var session = new TicTacToeSession("ABC123");
        Assert.True(session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _));
        Assert.True(session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _));
        Assert.Equal(["Bob", "Alice"], session.GetPublicState().Seats.Select(s => s!.Name));

        Assert.True(session.Disconnect("conn-a", Now));
        Assert.Equal(SeatStatus.Reconnecting, session.GetPublicState().Seats[1]!.Status);
        Assert.False(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out var error));
        Assert.Equal("All seats are taken.", error);
        Assert.False(session.UpdateAwaySeats(Now.AddSeconds(59)));

        Assert.True(session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow)));
        Assert.True(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out error), error);
        Assert.Equal(["Bob", "Carol"], session.GetPublicState().Seats.Select(s => s!.Name));
    }

    [Fact]
    public void In_the_lobby_the_seats_after_a_freed_seat_move_up()
    {
        var session = TheGang();
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);
        session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out _);

        Assert.False(session.TryLeaveSeat(Bob, 0, out var error));
        Assert.Equal("That is not your seat.", error);
        Assert.True(session.TryLeaveSeat(Alice, 0, out _));
        Assert.Equal(["Bob", "Carol", null], session.GetPublicState().Seats.Take(3).Select(s => s?.Name));

        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        Assert.Equal(["Carol", null], session.GetPublicState().Seats.Take(2).Select(s => s?.Name));
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

    // Logging in or out (seats move to the new user)

    private const string NewAlice = "user:guest:alice2";
    private static readonly PlayerProfile NewAliceProfile = new("Guest-042", "🐸");

    [Fact]
    public void Logging_in_or_out_gives_the_users_seats_to_the_new_user_who_plays_on()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        var playerId = session.Enter(Alice, AliceProfile, "conn-a");
        session.Disconnect("conn-a", Now);

        Assert.True(session.TransferSeats(Alice, NewAlice, NewAliceProfile, Now.AddSeconds(5)));
        var seat = session.GetPublicState().Seats[0]!;
        Assert.Equal(("Guest-042", "🐸", playerId, SeatStatus.Reconnecting), (seat.Name, seat.Icon, seat.PlayerId, seat.Status));

        Assert.Equal(playerId, session.Enter(NewAlice, NewAliceProfile, "conn-a2"));
        Assert.Equal(SeatStatus.Present, session.GetPublicState().Seats[0]!.Status);
        Assert.False(session.TryMove(Alice, 0, 4, out var error));
        Assert.Equal("Only players in this game can make moves.", error);
        Assert.True(session.TryMove(NewAlice, 0, 4, out error), error);
    }

    [Fact]
    public void An_away_seat_stays_away_after_moving_to_the_new_user_until_they_come_back()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-a", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        Assert.Equal(SessionPhase.Paused, session.GetPublicState().Phase);

        Assert.True(session.TransferSeats(Alice, NewAlice, NewAliceProfile, Now.AddMinutes(5)));
        Assert.Equal(SeatStatus.Away, session.GetPublicState().Seats[0]!.Status);

        session.Enter(NewAlice, NewAliceProfile, "conn-a2");
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
    }

    [Fact]
    public void A_new_user_who_does_not_come_to_the_session_becomes_away()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.True(session.TransferSeats(Alice, NewAlice, NewAliceProfile, Now));
        Assert.Equal(SeatStatus.Reconnecting, session.GetPublicState().Seats[0]!.Status);

        Assert.True(session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow)));
        Assert.Equal(SeatStatus.Away, session.GetPublicState().Seats[0]!.Status);
    }

    [Fact]
    public void Nothing_moves_for_a_user_without_seats_or_to_the_same_user()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.False(session.TransferSeats(Carol, NewAlice, NewAliceProfile, Now));
        Assert.False(session.TransferSeats(Alice, Alice, AliceProfile, Now));
        Assert.Equal(["Alice", "Bob"], session.GetPublicState().Seats.Select(s => s!.Name));
    }

    // Public games

    [Fact]
    public void In_a_public_game_only_the_creator_chooses_the_options_and_starts_it()
    {
        var session = Public(new TheGangSession("ABC123"));
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);
        session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out _);
        var state = session.GetPublicState();
        Assert.True(state.IsPublic);
        Assert.Equal(state.Seats[0]!.PlayerId, state.HostPlayerId);

        Assert.False(session.TrySetOptions(Bob, null, out var error));
        Assert.Equal("Only the game's creator can change the options.", error);
        Assert.True(session.TrySetOptions(Alice, null, out error), error);
        Assert.False(session.TryNewGame(Bob, 0, out error));
        Assert.Equal("Only the game's creator can start the game.", error);
        Assert.True(session.TryNewGame(Alice, 0, out error), error);
    }

    [Fact]
    public void A_public_game_with_a_fixed_number_of_players_starts_when_its_seats_are_filled()
    {
        var session = Public(new TicTacToeSession("ABC123"));
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        Assert.True(session.IsOpenToJoin);
        Assert.Equal(SessionPhase.Lobby, session.GetPublicState().Phase);

        Assert.True(session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out var error), error);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.False(session.IsOpenToJoin);
    }

    [Fact]
    public void A_public_game_starts_by_itself_when_a_player_fills_the_last_seat()
    {
        var session = Public(new YatzeeSession("ABC123"));
        for (var i = 0; i < 7; i++)
        {
            session.TryTakeNextSeat($"user:p{i}", new PlayerProfile($"P{i}", "🙂"), $"conn-{i}", out _);
        }

        Assert.Equal(SessionPhase.Lobby, session.GetPublicState().Phase);
        session.TryTakeNextSeat("user:p7", new PlayerProfile("P7", "🙂"), "conn-7", out _);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
    }

    [Fact]
    public void A_public_game_cannot_be_joined_once_started_and_its_absent_seats_cannot_be_taken()
    {
        var session = PublicTicTacToe();
        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        Assert.Equal(SessionPhase.Paused, session.GetPublicState().Phase);

        Assert.False(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out var error));
        Assert.Equal("A public game cannot be joined once it has started.", error);
    }

    [Fact]
    public void A_player_leaving_a_public_game_ends_it_for_everyone()
    {
        var session = PublicTicTacToe();

        Assert.True(session.TryLeaveSeat(Bob, 1, out var error), error);

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.Equal("Bob left the game.", state.Notice);
        Assert.Equal(["Alice"], state.Seats.OfType<SeatInfo>().Select(s => s.Name));
        Assert.True(session.IsOpenToJoin);
    }

    [Fact]
    public void Going_back_to_the_main_screen_from_a_public_game_ends_it_and_the_next_game_clears_the_notice()
    {
        var session = PublicTicTacToe();
        session.LeaveAllSeats(Alice);
        Assert.Equal("Alice left the game.", session.GetPublicState().Notice);

        session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out _);
        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Null(state.Notice);
    }

    [Fact]
    public void When_the_creator_leaves_a_public_lobby_the_next_seated_player_becomes_the_creator()
    {
        var session = Public(new TheGangSession("ABC123"));
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);
        session.LeaveAllSeats(Alice);

        var state = session.GetPublicState();
        Assert.Equal(state.Seats[0]!.PlayerId, state.HostPlayerId);
        Assert.Equal("Bob", state.Seats[0]!.Name);
        Assert.True(session.TrySetOptions(Bob, null, out var error), error);

        session.LeaveAllSeats(Bob);
        Assert.False(session.HasSeatedPlayers);
        Assert.False(session.IsOpenToJoin);
    }

    [Fact]
    public void A_disconnected_player_is_waited_for_until_they_come_back()
    {
        var session = PublicTicTacToe();
        Assert.False(session.IsWaitingFor(Bob));

        session.Disconnect("conn-b", Now);
        Assert.True(session.IsWaitingFor(Bob));
        Assert.False(session.IsWaitingFor(Carol));

        session.Enter(Bob, BobProfile, "conn-b2");
        Assert.False(session.IsWaitingFor(Bob));
    }

    [Fact]
    public void Nobody_can_ask_to_join_a_public_rich_man_poor_man_game()
    {
        var session = RichManPoorMan(6);
        session.MakePublic();

        Assert.False(session.TryRequestSeat(Players[6], new PlayerProfile("P6", "🐙"), out var error));
        Assert.Equal("A public game cannot be joined once it has started.", error);
    }

    // A public Tic-tac-toe game created by Alice and joined by Bob, so it has started.
    private static TicTacToeSession PublicTicTacToe()
    {
        var session = Public(new TicTacToeSession("ABC123"));
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);
        return session;
    }

    private static T Public<T>(T session) where T : GameSession
    {
        session.MakePublic();
        return session;
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
    public void During_a_game_only_its_absent_seats_can_be_taken()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-b", Now);

        Assert.False(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out var error));
        Assert.Equal("That seat is taken.", error);
        Assert.False(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out error));
        Assert.Equal("The game has already started. You can watch it.", error);

        var gang = StartedTheGang();
        Assert.False(gang.TryTakeSeat("user:dan", new PlayerProfile("Dan", "🐻"), "conn-d", 3, out error));
        Assert.Equal("The game has already started. You can watch it.", error);
    }

    // Absent players and pausing

    [Fact]
    public void Leaving_a_game_pauses_it_on_the_game_screen_and_present_players_can_still_play()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.True(session.TryLeaveSeat(Bob, 1, out _));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Paused, state.Phase);
        Assert.Equal([1], state.AbsentSeats);
        Assert.True(session.TryMove(Alice, 0, 0, out var error), error);
        Assert.False(session.TryMove(Alice, 0, 1, out error));
        Assert.Equal("It is not your turn.", error);
    }

    [Fact]
    public void A_disconnected_player_is_away_after_the_reconnect_window_and_the_game_pauses()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-b", Now);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.Equal(SeatStatus.Reconnecting, session.GetPublicState().Seats[1]!.Status);

        Assert.True(session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow)));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Paused, state.Phase);
        Assert.Equal(SeatStatus.Away, state.Seats[1]!.Status);
        Assert.Equal("Bob", state.Seats[1]!.Name);
        Assert.Equal([1], state.AbsentSeats);
        Assert.False(session.UpdateAwaySeats(Now.AddHours(5)));
    }

    [Fact]
    public void An_away_player_gets_their_seat_back_whenever_they_return()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.AddHours(3));

        session.Enter(Bob, BobProfile, "conn-b2");

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal(SeatStatus.Present, state.Seats[1]!.Status);
        Assert.Empty(state.AbsentSeats);
    }

    [Fact]
    public void Anyone_can_take_an_away_seat_and_continue_that_players_game()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));

        Assert.True(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out var error), error);

        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.Equal("Carol", session.GetPublicState().Seats[1]!.Name);
        Assert.True(session.TryMove(Carol, 1, 4, out error), error);
        session.Enter(Bob, BobProfile, "conn-b2");
        Assert.False(session.TryMove(Bob, 1, 5, out _));
        Assert.DoesNotContain(session.GetPublicState().Seats, s => s?.Name == "Bob");
    }

    [Fact]
    public void A_player_can_take_an_empty_seat_of_the_game_as_a_second_seat_and_play_both()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryLeaveSeat(Bob, 1, out _);

        Assert.True(session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out var error), error);

        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.Equal(["Alice", "Alice"], session.GetPublicState().Seats.Select(s => s!.Name));
        Assert.True(session.TryMove(Alice, 0, 0, out error), error);
        Assert.True(session.TryMove(Alice, 1, 4, out error), error);
    }

    [Fact]
    public void A_new_user_in_the_empty_seat_continues_the_paused_game()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.TryLeaveSeat(Bob, 1, out _);

        Assert.True(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out var error), error);

        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.False(session.TryMove(Bob, 1, 4, out _));
        Assert.True(session.TryMove(Carol, 1, 4, out error), error);
        Assert.Equal(Mark.X, session.GetState().Board[0]);
    }

    [Fact]
    public void Any_seated_player_can_end_a_game_in_progress_which_moves_the_seats_up_and_lets_others_join()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.TryLeaveSeat(Alice, 0, out _);
        Assert.False(session.TryEndGame(Carol, out var error));
        Assert.Equal("Only seated players can do that.", error);

        Assert.True(session.TryEndGame(Bob, out error), error);

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.Empty(state.AbsentSeats);
        Assert.Equal(["Bob", null], state.Seats.Select(s => s?.Name));
        Assert.False(session.TryEndGame(Bob, out error));
        Assert.Equal("There is no game in progress to end.", error);
        Assert.True(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out _));
        Assert.True(session.TryNewGame(Carol, 1, out error), error);
        Assert.Equal(2, session.GetPublicState().GameNumber);
        Assert.Equal(Mark.None, session.GetState().Board[0]);
    }

    [Fact]
    public void A_paused_game_can_be_ended_and_replaced_with_fewer_players()
    {
        var session = TheGang();
        foreach (var (user, profile, conn) in new[] { (Alice, AliceProfile, "conn-a"), (Bob, BobProfile, "conn-b"),
                     (Carol, CarolProfile, "conn-c"), ("user:dan", new PlayerProfile("Dan", "🐻"), "conn-d") })
        {
            session.TryTakeNextSeat(user, profile, conn, out _);
        }

        session.TryNewGame(Alice, 0, out _);
        session.TryLeaveSeat(Bob, 1, out _);
        session.TryEndGame(Alice, out _);

        Assert.True(session.TryNewGame(Carol, 1, out var error), error);
        Assert.Equal([0, 1, 2], Gang(session).PlayingSeats);
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
        Assert.Equal(SessionPhase.Paused, state.Phase);
        Assert.Equal([0, 1], state.AbsentSeats);
    }

    // After a game ends

    [Fact]
    public void A_new_game_can_start_while_the_game_is_in_progress_only_after_it_ends()
    {
        var session = Started(new EndableSession("ABC123"));

        Assert.False(session.TryNewGame(Alice, 1, out var error));
        Assert.Equal("The game is not over yet.", error);

        session.End();
        Assert.Equal(SessionPhase.Over, session.GetPublicState().Phase);
        Assert.True(session.TryNewGame(Bob, 1, out _));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal(2, state.GameNumber);
    }

    [Fact]
    public void Standing_up_after_the_game_ends_returns_to_the_lobby_when_too_few_remain()
    {
        var session = Started(new EndableSession("ABC123"));
        session.End();

        session.TryLeaveSeat(Bob, 1, out _);

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Lobby, state.Phase);
        Assert.Empty(state.AbsentSeats);
    }

    [Fact]
    public void Players_can_return_to_the_lobby_only_after_the_game_ends()
    {
        var session = Started(new EndableSession("ABC123"));

        Assert.False(session.TryReturnToLobby(Alice, out var error));
        Assert.Equal("The game is not over yet.", error);

        session.End();
        Assert.True(session.TryReturnToLobby(Bob, out _));
        Assert.Equal(SessionPhase.Lobby, session.GetPublicState().Phase);
        Assert.Equal(["Alice", "Bob"], session.GetPublicState().Seats.Select(s => s!.Name));
    }

    // Disconnecting

    [Fact]
    public void Once_a_game_is_over_away_players_seats_are_freed()
    {
        var session = Started(new EndableSession("ABC123"));
        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        Assert.Equal("Bob", session.GetPublicState().Seats[1]!.Name);

        session.End();
        Assert.True(session.UpdateAwaySeats(Now.AddMinutes(2)));

        var state = session.GetPublicState();
        Assert.Null(state.Seats[1]);
        Assert.Equal(SessionPhase.Lobby, state.Phase);
    }

    [Fact]
    public void Ending_a_game_frees_away_players_seats()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));

        Assert.True(session.TryEndGame(Alice, out var error), error);

        Assert.Equal(["Alice", null], session.GetPublicState().Seats.Select(s => s?.Name));
    }

    [Fact]
    public void Reconnecting_in_time_keeps_the_seat()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-a", Now);

        session.Enter(Alice, AliceProfile, "conn-a2");

        Assert.Equal(SeatStatus.Present, session.GetPublicState().Seats[0]!.Status);
        Assert.False(session.UpdateAwaySeats(Now.AddMinutes(1)));
        Assert.True(session.TryMove(Alice, 0, 0, out var error), error);
    }

    [Fact]
    public void A_user_stays_connected_while_any_of_their_connections_is_open()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Enter(Alice, AliceProfile, "conn-a2");

        Assert.False(session.Disconnect("conn-a", Now));
        Assert.Equal(SeatStatus.Present, session.GetPublicState().Seats[0]!.Status);
        Assert.False(session.Disconnect("spectator", Now));
    }

    [Fact]
    public void The_observer_count_is_the_open_connections_of_users_without_a_seat()
    {
        var session = Seated(new TicTacToeSession("ABC123"));
        session.Enter(Alice, AliceProfile, "conn-a2");
        session.Enter(Carol, CarolProfile, "conn-c");
        session.Enter(Carol, CarolProfile, "conn-c2");
        session.Enter("connection:tv", null, "tv");
        Assert.Equal(3, session.GetPublicState().ObserverCount);

        Assert.True(session.Disconnect("conn-c", Now));
        Assert.Equal(2, session.GetPublicState().ObserverCount);

        Assert.True(session.LeaveAllSeats(Alice));
        Assert.Equal(4, session.GetPublicState().ObserverCount);

        Assert.True(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c2", out var error), error);
        Assert.Equal(3, session.GetPublicState().ObserverCount);
    }

    // Idle sessions (removed from memory, but saved)

    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);

    [Fact]
    public void A_session_nobody_has_open_is_idle_after_the_idle_time_even_with_seats_taken()
    {
        var session = Seated(new TicTacToeSession("ABC123"));
        session.Disconnect("conn-a", Now);
        session.Disconnect("conn-b", Now);

        Assert.False(session.IsIdle(Now, Idle));
        Assert.False(session.IsIdle(Now.AddMinutes(4), Idle));
        Assert.True(session.IsIdle(Now.AddMinutes(5), Idle));
        Assert.False(session.HasStarted);
        Assert.True(session.TryNewGame(Alice, 0, out _));
        Assert.True(session.HasStarted);
    }

    [Fact]
    public void Sessions_nobody_has_open_are_removed_sooner_when_their_game_never_started()
    {
        var empty = new TicTacToeSession("ABC123");
        var privateLobby = Seated(new TicTacToeSession("ABC124"));
        var publicLobby = Public(new YatzeeSession("ABC125"));
        publicLobby.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        var started = Started(new TicTacToeSession("ABC126"));

        Assert.Equal(TimeSpan.FromSeconds(30), DisconnectedSeatsMonitor.IdleLimit(empty));
        Assert.Equal(TimeSpan.FromMinutes(10), DisconnectedSeatsMonitor.IdleLimit(privateLobby));
        Assert.Equal(TimeSpan.FromSeconds(30), DisconnectedSeatsMonitor.IdleLimit(publicLobby));
        Assert.Equal(TimeSpan.FromMinutes(5), DisconnectedSeatsMonitor.IdleLimit(started));
    }

    [Fact]
    public void A_session_with_a_spectator_is_not_idle_and_coming_back_restarts_the_wait()
    {
        var session = new TicTacToeSession("ABC123");
        session.Enter("connection:tv", null, "tv");
        Assert.False(session.IsIdle(Now, Idle));
        Assert.False(session.IsIdle(Now.AddMinutes(10), Idle));

        session.Disconnect("tv", Now.AddMinutes(10));
        Assert.False(session.IsIdle(Now.AddMinutes(10), Idle));
        Assert.True(session.IsIdle(Now.AddMinutes(15), Idle));
    }

    // Chat

    [Fact]
    public void Players_and_watchers_with_the_session_open_can_chat()
    {
        var session = Seated(new TicTacToeSession("ABC123"));
        session.Enter(Carol, CarolProfile, "conn-c");

        Assert.True(session.TrySendChat(Alice, AliceProfile, "  Hello\n  everyone  ", Now, out var first, out _));
        Assert.True(session.TrySendChat(Carol, CarolProfile, "Good luck!", Now.AddSeconds(5), out var second, out _));

        Assert.Equal(new ChatMessage(1, session.Enter(Alice, AliceProfile, "conn-a")!, "Alice", "🦊", "Hello everyone", Now), first);
        Assert.Equal((2, "Carol", "Good luck!"), (second.Number, second.Name, second.Text));
        Assert.Equal([first, second], session.GetChat());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_chat_messages_are_refused(string? text)
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.False(session.TrySendChat(Alice, AliceProfile, text, Now, out _, out var error));
        Assert.Equal("Write a message of 1 to 300 characters.", error);
        Assert.Empty(session.GetChat());
    }

    [Fact]
    public void Chat_messages_are_limited_in_length_and_need_the_session_open()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.True(session.TrySendChat(Alice, AliceProfile, new string('a', GameSession.MaxChatLength), Now, out _, out _));
        Assert.False(session.TrySendChat(Alice, AliceProfile, new string('a', GameSession.MaxChatLength + 1), Now, out _, out var tooLong));
        Assert.Equal("Write a message of 1 to 300 characters.", tooLong);

        Assert.False(session.TrySendChat(Carol, CarolProfile, "Hi", Now, out _, out var notOpen));
        Assert.Equal("Open the game to chat.", notOpen);
        session.Disconnect("conn-a", Now);
        Assert.False(session.TrySendChat(Alice, AliceProfile, "Hi", Now, out _, out _));
    }

    [Fact]
    public void Only_the_latest_chat_messages_are_kept_and_they_are_saved_with_the_session()
    {
        var session = Seated(new TicTacToeSession("ABC123"));
        for (var i = 1; i <= GameSession.ChatHistoryLength + 5; i++)
        {
            session.TrySendChat(Alice, AliceProfile, $"Message {i}", Now, out _, out _);
        }

        var chat = session.GetChat();
        Assert.Equal(GameSession.ChatHistoryLength, chat.Count);
        Assert.Equal((6, "Message 6"), (chat[0].Number, chat[0].Text));
        Assert.Equal(GameSession.ChatHistoryLength + 5, chat[^1].Number);

        var loaded = new TicTacToeSession("ABC123");
        loaded.Restore(session.Save(), Now);
        Assert.Equal(chat, loaded.GetChat());
        loaded.Enter(Bob, BobProfile, "conn-b2");
        Assert.True(loaded.TrySendChat(Bob, BobProfile, "Back again", Now, out var next, out _));
        Assert.Equal(GameSession.ChatHistoryLength + 6, next.Number);
    }

    // Saving and loading

    [Fact]
    public void Every_game_plays_on_the_same_after_being_saved_and_loaded()
    {
        var gang = StartedTheGang();
        gang.TryTakeChip(Alice, 0, 2, out _);
        AssertSameAfterReload(gang, () => new TheGangSession("ABC123"),
            s => s.TryTakeChip(Bob, 1, 3, out var e) ? null : e);

        var backgammon = Started(new BackgammonSession("ABC123", Dice(6, 1, 6, 1)));
        backgammon.TryRoll(Alice, 0, out _);
        backgammon.TryRoll(Bob, 1, out _);
        backgammon.TryRoll(Alice, 0, out _);
        AssertSameAfterReload(backgammon, () => new BackgammonSession("ABC123", Dice(3, 2)),
            s => s.TryMove(Alice, 0, 13, 7, out var e) ? null : e);

        var rps = StartedRpsSession();
        rps.TryPick(Alice, 0, Choice.Rock, out _);
        AssertSameAfterReload(rps, () => new RockPaperScissorsSession("ABC123"),
            s => s.TryPick(Bob, 1, Choice.Paper, out var e) ? null : e);

        var tictactoe = Started(new TicTacToeSession("ABC123"));
        tictactoe.TryMove(Alice, 0, 4, out _);
        AssertSameAfterReload(tictactoe, () => new TicTacToeSession("ABC123"),
            s => s.TryMove(Bob, 1, 0, out var e) ? null : e);

        var rich = RichManPoorMan(6);
        rich.TryNewGame(Players[0], 0, out _);
        rich.TryThrow(Players[0], 0, ["2S"], out _);
        AssertSameAfterReload(rich, () => new RichManPoorManSession("ABC123"),
            s => s.TryThrow(Players[5], 5, ["AS"], out var e) ? null : e);

        var yatzee = Started(new YatzeeSession("ABC123", Dice(6, 6, 2, 6, 3)));
        yatzee.TryRoll(Alice, 0, out _);
        yatzee.TryHold(Alice, 0, 0, true, out _);
        AssertSameAfterReload(yatzee, () => new YatzeeSession("ABC123"),
            s => s.TryScore(Alice, 0, YatzeeCategory.Sixes, out var e) ? null : e);

        var uno = Started(new UnoSession("ABC123", UnshuffledUno));
        uno.TryPlay(Alice, 0, "R1", null, out _);
        AssertSameAfterReload(uno, () => new UnoSession("ABC123", UnshuffledUno),
            s => s.TryPlay(Bob, 1, "R4", null, out var e) ? null : e);
    }

    // Saves the session, loads it into a new one, reconnects its users, and checks both give the same public state
    // (except the observer count, as connections are not saved), and the same result for the next action.
    private static void AssertSameAfterReload<T>(T session, Func<T> create, Func<T, string?> nextAction) where T : GameSession
    {
        var loaded = create();
        loaded.Restore(session.Save(), Now);
        var users = new[] { Alice, Bob, Carol }.Concat(Players).ToArray();
        foreach (var userKey in users)
        {
            loaded.Enter(userKey, null, $"re-{userKey}");
        }

        Assert.Equal(PublicJson(session), PublicJson(loaded));
        Assert.Equal(nextAction(session), nextAction(loaded));
        Assert.Equal(PublicJson(session), PublicJson(loaded));
        Assert.Equal(Json(users.Select(session.GetPrivateState)), Json(users.Select(loaded.GetPrivateState)));
    }

    private static string PublicJson(GameSession session) => Json(session.GetPublicState() with { ObserverCount = 0 });

    private static string Json(object? value) => System.Text.Json.JsonSerializer.Serialize(value);

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
    public void No_moves_after_a_win_until_the_next_round()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));

        Assert.Equal(Mark.X, session.GetState().Winner);
        Assert.False(session.TryMove(Bob, 1, 5, out var error));
        Assert.Equal("The game is over.", error);
    }

    [Fact]
    public void Tic_tac_toe_keeps_score_and_alternates_the_starting_player()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));
        Assert.Equal([1, 0], session.GetState().Scores);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);

        Assert.True(session.TryNextRound(Bob, 1, out var error), error);
        Assert.True(session.TryNextRound(Alice, 1, out error), error);

        var state = session.GetState();
        Assert.Equal(2, state.Round);
        Assert.All(state.Board, cell => Assert.Equal(Mark.None, cell));
        Assert.Equal(Mark.O, state.CurrentTurn);
        Assert.Equal([1, 0], state.Scores);
        Assert.False(session.TryMove(Alice, 0, 0, out error));
        Assert.Equal("It is not your turn.", error);
        Assert.True(session.TryMove(Bob, 1, 0, out error), error);
    }

    [Fact]
    public void Tic_tac_toe_counts_draws_and_x_starts_again_in_round_three()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));
        session.TryNextRound(Alice, 1, out _);

        // O X O
        // O X X
        // X O O
        var players = new[] { (Bob, 1), (Alice, 0) };
        int[] cells = [0, 1, 2, 4, 3, 5, 7, 6, 8];
        for (var i = 0; i < cells.Length; i++)
        {
            var (user, seat) = players[i % 2];
            Assert.True(session.TryMove(user, seat, cells[i], out var error), error);
        }

        Assert.True(session.GetState().IsDraw);
        Assert.Equal(1, session.GetState().Draws);
        Assert.Equal([1, 0], session.GetState().Scores);

        session.TryNextRound(Bob, 2, out _);
        Assert.Equal(3, session.GetState().Round);
        Assert.Equal(Mark.X, session.GetState().CurrentTurn);
    }

    [Fact]
    public void Tic_tac_toe_next_round_requires_a_finished_board_and_a_player()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);

        Assert.False(session.TryNextRound(Alice, 1, out var error));
        Assert.Equal("This round is not finished yet.", error);
        Assert.False(session.TryNextRound(Carol, 1, out _));
        Assert.Equal(1, session.GetState().Round);
    }

    [Fact]
    public void A_new_tic_tac_toe_game_resets_the_score()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));
        session.TryLeaveSeat(Bob, 1, out _);
        session.TryEndGame(Alice, out _);
        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);

        Assert.True(session.TryNewGame(Alice, 1, out var error), error);

        var state = session.GetState();
        Assert.Equal((1, 0), (state.Round, state.Draws));
        Assert.Equal([0, 0], state.Scores);
        Assert.Equal(Mark.X, state.CurrentTurn);
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
        Assert.Equal(SessionPhase.Paused, session.GetPublicState().Phase);
        session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out _);

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
        Assert.Equal((3, 8), (state.MinSeats, state.MaxSeats));
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
    public void With_the_option_players_see_their_own_hand_ranking()
    {
        var plain = StartedTheGang();
        Assert.Null(Assert.IsType<TheGangPrivateState>(plain.GetPrivateState(Carol)).Hands.Single().Ranking);

        var session = TheGang();
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Bob, BobProfile, "conn-b", 1, out _);
        session.TryTakeSeat(Carol, CarolProfile, "conn-c", 2, out _);
        Assert.True(session.TrySetOptions(Alice, new Dictionary<string, string>
        {
            [TheGangOptions.ShowHandRanking] = TheGangOptions.Yes,
        }, out var error), error);
        session.TryNewGame(Alice, 0, out _);

        Assert.Equal("Pair", Assert.IsType<TheGangPrivateState>(session.GetPrivateState(Carol)).Hands.Single().Ranking);
        Assert.Equal("High card", Assert.IsType<TheGangPrivateState>(session.GetPrivateState(Alice)).Hands.Single().Ranking);
    }

    [Fact]
    public void A_user_with_two_seats_sees_both_hands()
    {
        var session = TheGang();
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        session.TryNewGame(Alice, 0, out _);

        var hands = Assert.IsType<TheGangPrivateState>(session.GetPrivateState(Alice)).Hands;
        Assert.Equal([0, 2], hands.Select(h => h.Seat));
        Assert.Equal([0, 1, 2], Gang(session).PlayingSeats);
    }

    [Fact]
    public void No_private_information_is_sent_in_the_lobby()
    {
        var session = StartedTheGang();
        session.TryLeaveSeat(Bob, 1, out _);
        Assert.NotNull(session.GetPrivateState(Alice));
        session.TryEndGame(Alice, out _);

        Assert.Null(session.GetPrivateState(Alice));
        Assert.Empty(session.GetPrivateUpdates());
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
        Assert.Equal("High card: K, J, 9, 7, 4", revealed.Name);

        session.TryRevealNext(Alice, 1, out _);
        session.TryRevealNext(Alice, 2, out _);
        var state = Gang(session);
        Assert.Equal(TheGangPhase.HeistOver, state.Phase);
        Assert.Equal(1, state.Vaults);
        Assert.Equal(["Pair: 9, 9, K, J, 7", "Three of a kind: K, K, K, J, 9"], state.Revealed.Skip(1).Select(h => h.Name));
        Assert.True(session.TryNextHeist(Carol, 1, out error), error);
        Assert.Equal(2, Gang(session).Heist);
    }

    [Fact]
    public void Leaving_during_a_heist_pauses_the_game_and_a_new_player_continues_that_hand()
    {
        var session = StartedTheGang();
        session.TryTakeChip(Alice, 0, 2, out _);
        session.TryLeaveSeat(Alice, 0, out _);

        Assert.Equal(SessionPhase.Paused, session.GetPublicState().Phase);
        Assert.True(session.TryTakeSeat("user:dan", new PlayerProfile("Dan", "🐢"), "conn-d", 0, out var error), error);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);

        Assert.Equal(2, Gang(session).Chips[0][0]);
        var dan = Assert.IsType<TheGangPrivateState>(session.GetPrivateState("user:dan")).Hands.Single();
        Assert.Equal(["3♦", "4♥"], dan.Cards);
        Assert.Null(session.GetPrivateState(Alice));
    }

    // Backgammon

    [Fact]
    public void Backgammon_seat_0_plays_white_and_seat_1_black_with_the_chosen_options()
    {
        var session = Seated(new BackgammonSession("ABC123", Dice(6, 1, 6, 1)));
        session.TrySetOptions(Alice, new Dictionary<string, string> { [BackgammonOptions.MatchLength] = "1", [BackgammonOptions.Cube] = "No" }, out _);
        session.TryNewGame(Alice, 0, out _);
        Assert.Equal(BackgammonPhase.Opening, Backgammon(session).Phase);
        Assert.False(session.TryRoll("connection:tv", 0, out _));
        Assert.True(session.TryRoll(Alice, 0, out var error), error);
        Assert.True(session.TryRoll(Bob, 1, out error), error);

        var state = Backgammon(session);
        Assert.Equal((1, false), (state.MatchLength, state.CubeEnabled));
        Assert.Equal((0, BackgammonPhase.Rolling), (state.Turn, state.Phase));
        Assert.Equal([6, 1], state.OpeningDice);
        Assert.Equal([6, 1], state.OpeningRoll);
        Assert.Equal(2, state.Rolls);

        Assert.True(session.TryRoll(Alice, 0, out error), error);
        state = Backgammon(session);
        Assert.Equal((0, BackgammonPhase.Moving), (state.Turn, state.Phase));
        Assert.Equal([6, 1], state.Dice);
        Assert.Null(state.OpeningRoll);
        Assert.Contains(new BackgammonMoveState(13, 7), state.LegalMoves);
        Assert.False(session.TryMove(Bob, 0, 13, 7, out error));
        Assert.Equal("Only players in this game can make moves.", error);
        Assert.False(session.TryMove(Bob, 1, 13, 7, out error));
        Assert.Equal("It is not your turn.", error);

        Assert.True(session.TryMove(Alice, 0, 13, 7, out error), error);
        Assert.True(session.TryMove(Alice, 0, 8, 7, out error), error);
        Assert.True(session.TryEndTurn(Alice, 0, out error), error);
        state = Backgammon(session);
        Assert.Equal((1, BackgammonPhase.Rolling), (state.Turn, state.Phase));
        Assert.Equal(2, state.Points[6]);
        Assert.Equal([new BackgammonMoveState(13, 7), new BackgammonMoveState(8, 7)], state.LastTurn!.Moves);
    }

    [Fact]
    public void Backgammon_players_start_the_next_game_of_the_match()
    {
        var session = Started(new BackgammonSession("ABC123", Dice(3, 1, 3, 1, 6, 1)));
        session.TryRoll(Alice, 0, out _);
        session.TryRoll(Bob, 1, out _);
        session.TryRoll(Alice, 0, out _);
        session.TryMove(Alice, 0, 8, 5, out _);
        session.TryMove(Alice, 0, 6, 5, out _);
        session.TryEndTurn(Alice, 0, out _);
        Assert.True(session.TryDouble(Bob, 1, out var error), error);
        Assert.True(session.TryDrop(Alice, 0, out error), error);

        var state = Backgammon(session);
        Assert.Equal(BackgammonPhase.GameOver, state.Phase);
        Assert.Equal(new BackgammonResultState(1, 1, BackgammonResultKind.Dropped), state.Result);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.False(session.TryNextRound("connection:tv", 1, out _));
        Assert.True(session.TryNextRound(Alice, 1, out error), error);
        Assert.True(session.TryNextRound(Bob, 1, out error), error);

        state = Backgammon(session);
        Assert.Equal((2, BackgammonPhase.Opening), (state.GameNumber, state.Phase));
        Assert.Equal([0, 1], state.Scores);
        Assert.True(session.TryRoll(Bob, 1, out error), error);
        Assert.True(session.TryRoll(Alice, 0, out error), error);
        Assert.Equal([1, 6], Backgammon(session).OpeningRoll);
        Assert.Equal(1, Backgammon(session).Turn);
    }

    private static BackgammonState Backgammon(BackgammonSession session) =>
        Assert.IsType<BackgammonState>(session.GetPublicState().Game);

    // A die that rolls the given numbers in order.
    private static Func<int> Dice(params int[] numbers)
    {
        var queue = new Queue<int>(numbers);
        return () => queue.Dequeue();
    }

    // Yatzee

    [Fact]
    public void Yatzee_seats_take_turns_in_seat_order()
    {
        var session = Started(new YatzeeSession("ABC123", Dice(6, 6, 2, 6, 3, 6, 1)));
        Assert.False(session.TryRoll(Bob, 1, out var error));
        Assert.Equal("It is not your turn.", error);
        Assert.False(session.TryRoll(Bob, 0, out error));
        Assert.Equal("Only players in this game can make moves.", error);

        Assert.True(session.TryRoll(Alice, 0, out error), error);
        Assert.True(session.TryHold(Alice, 0, 0, true, out error), error);
        Assert.True(session.TryHold(Alice, 0, 1, true, out error), error);
        Assert.True(session.TryHold(Alice, 0, 3, true, out error), error);
        var state = Yatzee(session);
        Assert.Equal([6, 6, 2, 6, 3], state.Dice);
        Assert.Equal([true, true, false, true, false], state.Held);
        Assert.Equal(2, state.RollsLeft);
        Assert.Equal(18, state.PossibleScores[(int)YatzeeCategory.Sixes]);
        Assert.True(session.TryRoll(Alice, 0, out error), error);
        Assert.True(session.TryScore(Alice, 0, YatzeeCategory.Sixes, out error), error);

        state = Yatzee(session);
        Assert.Equal((1, 1, 3), (state.Turn, state.Round, state.RollsLeft));
        Assert.Equal(new YatzeeTurnState(0, YatzeeCategory.Sixes, 24, false, state.LastTurn!.Dice), state.LastTurn);
        Assert.Equal([6, 6, 6, 6, 1], state.LastTurn.Dice);
        Assert.Equal([0, 1], state.Players.Select(p => p.Seat));
        Assert.Equal([24, 0], state.Players.Select(p => p.Total));
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
    }

    [Fact]
    public void Yatzee_can_be_played_alone_and_played_again_once_over()
    {
        var face = 0;
        var session = new YatzeeSession("ABC123", () => face++ % 6 + 1);
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        Assert.True(session.TryNewGame(Alice, 0, out var error), error);

        foreach (var category in Enum.GetValues<YatzeeCategory>())
        {
            Assert.True(session.TryRoll(Alice, 0, out error), error);
            Assert.True(session.TryScore(Alice, 0, category, out error), error);
        }

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Over, state.Phase);
        Assert.Equal([0], Yatzee(session).Winners);
        Assert.True(Yatzee(session).IsOver);
        Assert.True(session.TryNewGame(Alice, state.GameNumber, out error), error);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.All(Yatzee(session).Players[0].Scores, Assert.Null);
    }

    private static YatzeeState Yatzee(YatzeeSession session) =>
        Assert.IsType<YatzeeState>(session.GetPublicState().Game);

    // Uno

    // Leaves the deck in order: seat 0 gets R0 R1 R1 R2 R2 R3 R3, seat 1 gets R4 R4 R5 R5 R6 R6 R7, and R7 is turned up.
    private static void UnshuffledUno(UnoCard[] deck)
    {
    }

    [Fact]
    public void Uno_seats_play_their_own_cards_which_only_they_see()
    {
        var session = Started(new UnoSession("ABC123", UnshuffledUno));
        Assert.False(session.TryPlay(Bob, 0, "R1", null, out var error));
        Assert.Equal("Only players in this game can make moves.", error);

        var hand = Assert.IsType<UnoPrivateState>(session.GetPrivateState(Alice)).Hands.Single();
        Assert.Equal(0, hand.Seat);
        Assert.Equal(["R0", "R1", "R1", "R2", "R2", "R3", "R3"], hand.Cards);
        Assert.Equal(["R0", "R1", "R2", "R3"], hand.Playable);
        Assert.Empty(Assert.IsType<UnoPrivateState>(session.GetPrivateState(Bob)).Hands.Single().Playable);
        Assert.False(session.TryPlay(Alice, 0, "XX", null, out error));
        Assert.Equal("Unknown card.", error);

        Assert.True(session.TryPlay(Alice, 0, "R1", null, out error), error);
        var state = Uno(session);
        Assert.Equal(("R1", 1, UnoColor.Red), (state.TopCard, state.Turn, state.Color));
        Assert.Equal([6, 7], state.Players.Select(p => p.CardCount));
        Assert.Equal(new UnoEventState(0, UnoEventKind.Played, "R1", 0, null, null), state.Events.Single());
        Assert.False(session.TryCatchUno(Bob, 1, 5, out error));
        Assert.Equal("There is no such player.", error);
        Assert.False(session.TryNextRound(Alice, 1, out error));
        Assert.Equal("This hand is not finished yet.", error);
    }

    [Fact]
    public void Uno_starts_with_the_chosen_options()
    {
        var session = Seated(new UnoSession("ABC123", UnshuffledUno));
        Assert.True(session.TrySetOptions(Alice, new Dictionary<string, string>
        {
            [UnoOptions.End] = "Pyramid", [UnoOptions.Stacking] = "Yes", [UnoOptions.Draw] = "UntilPlayable",
        }, out var error), error);
        Assert.True(session.TryNewGame(Alice, 0, out error), error);

        var state = Uno(session);
        Assert.Equal((UnoEndMode.Pyramid, true, true), (state.End, state.Stacking, state.DrawUntilPlayable));
        Assert.All(state.Players, p => Assert.Equal(7, p.HandSize));
    }

    private static UnoState Uno(UnoSession session) =>
        Assert.IsType<UnoState>(session.GetPublicState().Game);

    // Rich man, poor man

    private static readonly string[] Players = [.. Enumerable.Range(0, 9).Select(i => $"user:p{i}")];

    [Fact]
    public void Rich_man_poor_man_needs_6_seats_and_a_player_who_leaves_passes_automatically_until_someone_takes_their_seat()
    {
        var session = RichManPoorMan(5);
        Assert.False(session.TryNewGame(Players[0], 0, out var error));
        Assert.Equal("At least 6 players must be seated.", error);
        Sit(session, 5);
        Assert.True(session.TryNewGame(Players[0], 0, out error), error);
        Assert.Equal(0, Rmpm(session).Turn);
        Assert.True(session.TryThrow(Players[0], 0, ["2S"], out error), error);
        Assert.Equal(5, Rmpm(session).Turn);

        Assert.True(session.TryLeaveSeat(Players[5], 5, out error), error);
        var left = Rmpm(session).Players.Single(p => p.Seat == 5);
        Assert.Equal((false, 13), (left.IsActive, left.CardCount));
        Assert.Equal(4, Rmpm(session).Turn);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);

        Sit(session, 6);
        Assert.True(Rmpm(session).Players.Single(p => p.Seat == 5).IsActive);
        var hand = Assert.IsType<RichManPoorManPrivateState>(session.GetPrivateState(Players[6])).Hands.Single();
        Assert.Equal((5, 13), (hand.Seat, hand.Cards.Count));
        Assert.False(session.TryTakeNextSeat(Players[7], new PlayerProfile("P7", "🙂"), "conn-7", out error));
        Assert.Equal("The game has already started. You can watch it.", error);
        Assert.False(session.TryThrow(Players[4], 4, ["XX"], out error));
        Assert.Equal("Unknown card.", error);
    }

    [Fact]
    public void Rich_man_poor_man_never_pauses_an_away_player_passes_and_is_dropped_at_the_exchange()
    {
        var session = RichManPoorMan(7);
        session.TryNewGame(Players[0], 0, out _);
        session.Disconnect("conn-3", Now);
        Assert.True(Rmpm(session).Players.Single(p => p.Seat == 3).IsActive);

        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.False(Rmpm(session).Players.Single(p => p.Seat == 3).IsActive);
        session.Enter(Players[3], new PlayerProfile("P3", "🙂"), "conn-3b");
        Assert.True(Rmpm(session).Players.Single(p => p.Seat == 3).IsActive);

        session.Disconnect("conn-3b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        PlayOut(session);
        Assert.True(session.TryNextRound(Players[0], 1, out var error), error);

        Assert.Equal(6, Rmpm(session).Players.Count);
        Assert.Null(session.GetPublicState().Seats[3]);
    }

    [Fact]
    public void Rich_man_poor_man_drops_players_who_left_at_the_exchange_and_goes_back_to_the_lobby_with_fewer_than_6()
    {
        var session = RichManPoorMan(7);
        session.TryNewGame(Players[0], 0, out _);
        PlayOut(session);
        Assert.Equal(7, Rmpm(session).Ranking.Count);
        Assert.True(session.TryLeaveSeat(Players[6], 6, out var error), error);

        Assert.True(session.TryNextRound(Players[0], 1, out error), error);
        var state = Rmpm(session);
        Assert.Equal((2, RichManPoorManPhase.Exchanging, 6), (state.GameNumber, state.Phase, state.Players.Count));
        Assert.DoesNotContain(state.Players, p => p.Seat == 6);
        Assert.Equal([3, 2, 1, 1, 2, 3], state.Players.Select(p => p.ExchangeCount));
        Assert.False(session.TryTakeNextSeat(Players[7], new PlayerProfile("P7", "🙂"), "conn-7", out error));
        Assert.Equal("Ask to join: a player will choose your place.", error);

        Assert.True(session.TryLeaveSeat(Players[5], 5, out error), error);
        Assert.Equal(SessionPhase.Lobby, session.GetPublicState().Phase);
        Assert.Empty(session.GetPublicState().AbsentSeats);
    }

    [Fact]
    public void Rich_man_poor_man_first_game_can_start_with_an_exchange_by_seat_order()
    {
        var session = RichManPoorMan(6);
        Assert.True(session.TrySetOptions(Players[0],
            new Dictionary<string, string> { [RichManPoorManOptions.FirstGame] = RichManPoorManOptions.Exchange }, out var error), error);
        Assert.True(session.TryNewGame(Players[0], 0, out error), error);

        var state = Rmpm(session);
        Assert.Equal((1, RichManPoorManPhase.Exchanging), (state.GameNumber, state.Phase));
        Assert.Equal([0, 1, 2, 3, 4, 5], state.Players.Select(p => p.Seat));
        Assert.Equal([3, 2, 1, 1, 2, 3], state.Players.Select(p => p.ExchangeCount));
        foreach (var player in state.Players)
        {
            Give(session, Players[player.Seat], player.Seat);
        }

        Assert.Equal((RichManPoorManPhase.Playing, 5), (Rmpm(session).Phase, Rmpm(session).Turn));
    }

    [Fact]
    public void Rich_man_poor_man_hands_can_be_read_after_the_exchange_when_a_poorer_player_has_fewer_cards_than_they_gave()
    {
        var session = RichManPoorMan(6);
        session.TrySetOptions(Players[0],
            new Dictionary<string, string> { [RichManPoorManOptions.FirstGame] = RichManPoorManOptions.Exchange }, out _);
        session.TryNewGame(Players[0], 0, out _);
        foreach (var player in Rmpm(session).Players)
        {
            Give(session, Players[player.Seat], player.Seat);
        }

        PlayOut(session);

        foreach (var player in Rmpm(session).Players)
        {
            var hand = Assert.IsType<RichManPoorManPrivateState>(session.GetPrivateState(Players[player.Seat])).Hands.Single();
            Assert.Null(hand.RequiredGift);
        }
    }

    [Fact]
    public void Rich_man_poor_man_exchange_waits_for_an_away_player_until_they_come_back()
    {
        var session = RichManPoorMan(6);
        session.TryNewGame(Players[0], 0, out _);
        PlayOut(session);
        session.TryNextRound(Players[0], 1, out _);
        var away = Rmpm(session).Players[^1].Seat;
        session.Disconnect($"conn-{away}", Now);
        Assert.True(session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow)));

        var state = Rmpm(session);
        Assert.Equal((RichManPoorManPhase.Exchanging, 6), (state.Phase, state.Players.Count));
        Assert.False(state.Players.Single(p => p.Seat == away).IsActive);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.Equal([away], session.GetPublicState().AbsentSeats);
        foreach (var player in state.Players.Where(p => p.Seat != away))
        {
            Give(session, Players[player.Seat], player.Seat);
        }

        Assert.Equal(RichManPoorManPhase.Exchanging, Rmpm(session).Phase);
        session.Enter(Players[away], new PlayerProfile($"P{away}", "🙂"), $"conn-{away}b");
        Assert.True(Rmpm(session).Players.Single(p => p.Seat == away).IsActive);
        Give(session, Players[away], away);
        Assert.Equal(RichManPoorManPhase.Playing, Rmpm(session).Phase);
    }

    [Fact]
    public void Rich_man_poor_man_someone_can_take_an_away_seat_in_the_exchange_and_give_in_their_place()
    {
        var session = RichManPoorMan(6);
        session.TryNewGame(Players[0], 0, out _);
        PlayOut(session);
        session.TryNextRound(Players[0], 1, out _);
        var away = Rmpm(session).Players[^1].Seat;
        session.Disconnect($"conn-{away}", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));

        Sit(session, 7);
        Assert.Equal("P7", session.GetPublicState().Seats[away]!.Name);
        Assert.True(Rmpm(session).Players.Single(p => p.Seat == away).IsActive);
        foreach (var player in Rmpm(session).Players)
        {
            Give(session, player.Seat == away ? Players[7] : Players[player.Seat], player.Seat);
        }

        Assert.Equal(RichManPoorManPhase.Playing, Rmpm(session).Phase);
    }

    [Fact]
    public void Rich_man_poor_man_players_accept_a_request_to_join_at_a_place_and_the_cards_are_dealt_again()
    {
        var session = RichManPoorMan(6);
        session.TryNewGame(Players[0], 0, out _);
        session.Enter(Players[6], new PlayerProfile("P6", "🐙"), "conn-6");
        session.Enter(Players[7], new PlayerProfile("P7", "🐼"), "conn-7");
        Assert.False(session.TryRequestSeat(Players[6], new PlayerProfile("P6", "🐙"), out var error));
        Assert.Equal("You can ask to join during the card exchange, between games.", error);
        PlayOut(session);
        session.TryNextRound(Players[0], 1, out _);

        Assert.True(session.TryRequestSeat(Players[6], new PlayerProfile("P6", "🐙"), out error), error);
        Assert.True(session.TryRequestSeat(Players[7], new PlayerProfile("P7", "🐼"), out error), error);
        var requests = Rmpm(session).JoinRequests;
        Assert.Equal(["P6", "P7"], requests.Select(r => r.Name));
        Assert.False(session.TryAcceptSeat(Players[8], requests[0].PlayerId, 1, out error));
        Assert.Equal("Only players in this game can do that.", error);
        Assert.True(session.TryDeclineSeat(Players[1], requests[1].PlayerId, out error), error);

        Assert.True(session.TryAcceptSeat(Players[1], requests[0].PlayerId, 1, out error), error);
        var state = Rmpm(session);
        Assert.Equal(7, state.Players.Count);
        Assert.Equal(6, state.Players[0].Seat);
        Assert.All(state.Players, p => Assert.Equal(13, p.CardCount));
        Assert.Empty(state.JoinRequests);
        Assert.Equal("P6", session.GetPublicState().Seats[6]!.Name);
        Assert.Equal(13, Assert.IsType<RichManPoorManPrivateState>(session.GetPrivateState(Players[6])).Hands.Single().Cards.Count);
    }

    [Fact]
    public void Rich_man_poor_man_request_to_join_moves_to_the_new_user_after_logging_in()
    {
        var session = RichManPoorMan(6);
        session.TryNewGame(Players[0], 0, out _);
        PlayOut(session);
        session.TryNextRound(Players[0], 1, out _);
        session.Enter(Players[6], new PlayerProfile("P6", "🐙"), "conn-6");
        session.TryRequestSeat(Players[6], new PlayerProfile("P6", "🐙"), out _);
        var playerId = Rmpm(session).JoinRequests.Single().PlayerId;

        var ada = new PlayerProfile("Ada", "🦉");
        Assert.True(session.TransferSeats(Players[6], Players[8], ada, Now));
        session.Enter(Players[8], ada, "conn-8");
        var request = Rmpm(session).JoinRequests.Single();
        Assert.Equal(("Ada", playerId), (request.Name, request.PlayerId));

        Assert.True(session.TryAcceptSeat(Players[1], playerId, 1, out var error), error);
        Assert.Equal("Ada", session.GetPublicState().Seats[6]!.Name);
        Assert.NotNull(session.GetPrivateState(Players[8]));
    }

    // Seats players 0 to count - 1, each in their own seat; the deck is dealt from low to high (#1 gets the lowest cards).
    private static RichManPoorManSession RichManPoorMan(int count)
    {
        var session = new RichManPoorManSession("ABC123", deck => deck.OrderBy(c => c.Rank).ToArray().CopyTo(deck, 0));
        for (var i = 0; i < count; i++)
        {
            Sit(session, i);
        }

        return session;
    }

    private static void Sit(RichManPoorManSession session, int player)
    {
        var profile = new PlayerProfile($"P{player}", "🙂");
        session.Enter(Players[player], profile, $"conn-{player}");
        Assert.True(session.TryTakeNextSeat(Players[player], profile, $"conn-{player}", out var error), error);
    }

    // Gives the seat's required cards in the exchange, or else its lowest cards.
    private static void Give(RichManPoorManSession session, string user, int seat)
    {
        var hand = Assert.IsType<RichManPoorManPrivateState>(session.GetPrivateState(user)).Hands.Single(h => h.Seat == seat);
        var count = Rmpm(session).Players.Single(p => p.Seat == seat).ExchangeCount;
        Assert.True(session.TryGive(user, seat, [.. hand.RequiredGift ?? hand.Cards.Take(count)], out var error), error);
    }

    // Plays the game to its end: each round's starter throws all their cards of their lowest number, and everyone passes.
    // Seat i must be held by player i.
    private static void PlayOut(RichManPoorManSession session)
    {
        for (var moves = 0; moves < 1000 && Rmpm(session).Phase == RichManPoorManPhase.Playing; moves++)
        {
            var seat = Rmpm(session).Turn;
            string? error;
            if (Rmpm(session).CurrentThrow is null)
            {
                var cards = Assert.IsType<RichManPoorManPrivateState>(session.GetPrivateState(Players[seat])).Hands
                    .Single(h => h.Seat == seat).Cards;
                Assert.True(session.TryThrow(Players[seat], seat, [.. cards.Where(c => c[..^1] == cards[0][..^1])], out error), error);
            }
            else
            {
                Assert.True(session.TryPass(Players[seat], seat, out error), error);
            }
        }

        Assert.Equal(RichManPoorManPhase.GameOver, Rmpm(session).Phase);
    }

    private static RichManPoorManState Rmpm(RichManPoorManSession session) =>
        Assert.IsType<RichManPoorManState>(session.GetPublicState().Game);

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

    /// <summary>
    /// A two-seat game that ends only when the test calls <see cref="End"/>, for testing what happens after a game ends.
    /// </summary>
    private sealed class EndableSession(string id) : GameSession(id)
    {
        private bool _isOver;

        public override GameType Type => GameType.TicTacToe;

        protected override int MinSeats => 2;

        protected override int MaxSeats => 2;

        protected override bool IsGameOver => _isOver;

        public void End()
        {
            lock (Sync)
            {
                _isOver = true;
            }
        }

        protected override void StartNewGame(IReadOnlyList<int> seats) => _isOver = false;

        protected override object GetGameState() => new { IsOver = _isOver };
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
