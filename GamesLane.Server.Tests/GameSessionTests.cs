using GamesLane.Server.Games.Backgammon;
using GamesLane.Server.Games.RichManPoorMan;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.TicTacToe;
using GamesLane.Server.Games.FinalCard;
using GamesLane.Server.Games.Spades;
using GamesLane.Server.Games.Yaniv;
using GamesLane.Server.Games.Wazoo;
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
        Assert.Equal([0], session.GetPublicState().WaitingSeats);

        Assert.True(session.TransferSeats(Alice, NewAlice, NewAliceProfile, Now.AddMinutes(5)));
        Assert.Equal(SeatStatus.Away, session.GetPublicState().Seats[0]!.Status);

        session.Enter(NewAlice, NewAliceProfile, "conn-a2");
        Assert.Empty(session.GetPublicState().WaitingSeats);
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
        var session = Public(new WazooSession("ABC123"));
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
        session.Enter(Carol, CarolProfile, "conn-c");
        Assert.False(session.HasSeatOffer(Carol));

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
    public void During_a_game_only_a_user_offered_a_seat_can_take_an_absent_seat()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        Assert.False(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 0, out var error));
        Assert.Equal("That seat is taken.", error);
        session.Disconnect("conn-b", Now);

        Assert.False(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out error));
        Assert.Equal("Only a user opening the game without a seat can take a disconnected player's seat.", error);
        Assert.False(session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out error));
        Assert.Equal("Only a user opening the game without a seat can take a disconnected player's seat.", error);

        var gang = StartedTheGang();
        Assert.False(gang.TryTakeSeat("user:dan", new PlayerProfile("Dan", "🐻"), "conn-d", 3, out error));
        Assert.Equal("The game has already started. You can watch it.", error);
    }

    [Fact]
    public void A_user_without_a_seat_is_offered_the_absent_seats_each_time_they_open_or_reconnect_and_can_watch_instead()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.Enter(Carol, CarolProfile, "conn-c");
        Assert.False(session.HasSeatOffer(Carol));
        session.Disconnect("conn-b", Now);
        Assert.False(session.HasSeatOffer(Carol));
        session.Enter(Carol, CarolProfile, "conn-c2");
        Assert.True(session.HasSeatOffer(Carol));

        const string Dan = "user:dan";
        const string Eve = "user:eve";
        session.Enter(Dan, new PlayerProfile("Dan", "🐻"), "conn-d");
        session.Enter(Eve, new PlayerProfile("Eve", "🦉"), "conn-e");
        Assert.True(session.HasSeatOffer(Dan));
        Assert.True(session.HasSeatOffer(Eve));
        Assert.Null(session.Enter("connection:tv", null, "tv"));
        Assert.False(session.HasSeatOffer("connection:tv"));

        Assert.True(session.DeclineSeatOffer(Eve));
        Assert.False(session.HasSeatOffer(Eve));
        Assert.False(session.TryTakeSeat(Eve, new PlayerProfile("Eve", "🦉"), "conn-e", 1, out var error));
        Assert.Equal("Only a user opening the game without a seat can take a disconnected player's seat.", error);
        session.Enter(Eve, new PlayerProfile("Eve", "🦉"), "conn-e2");
        Assert.True(session.HasSeatOffer(Eve));

        Assert.True(session.TryTakeSeat(Dan, new PlayerProfile("Dan", "🐻"), "conn-d", 1, out error), error);
        Assert.False(session.HasSeatOffer(Dan));
        session.Enter(Bob, BobProfile, "conn-b2");
        Assert.False(session.HasSeatOffer(Bob));
        session.Enter(Eve, new PlayerProfile("Eve", "🦉"), "conn-e3");
        Assert.False(session.HasSeatOffer(Eve));
    }

    [Fact]
    public void Only_disconnected_seats_are_offered_and_the_list_follows_players_coming_back()
    {
        var session = StartedTheGang();
        session.Disconnect("conn-b", Now);
        session.Disconnect("conn-c", Now);
        session.Enter("user:dan", new PlayerProfile("Dan", "🐻"), "conn-d");
        Assert.True(session.HasSeatOffer("user:dan"));
        Assert.Equal([1, 2], session.GetPublicState().AbsentSeats);
        Assert.False(session.TryTakeSeat("user:dan", new PlayerProfile("Dan", "🐻"), "conn-d", 0, out var error));
        Assert.Equal("That seat is taken.", error);

        session.Enter(Bob, BobProfile, "conn-b2");
        Assert.Equal([2], session.GetPublicState().AbsentSeats);
        Assert.False(session.TryTakeSeat("user:dan", new PlayerProfile("Dan", "🐻"), "conn-d", 1, out error));
        Assert.Equal("That seat is taken.", error);
        session.Enter(Carol, CarolProfile, "conn-c2");
        Assert.Empty(session.GetPublicState().AbsentSeats);
    }

    // Absent players and waiting

    [Fact]
    public void Leaving_a_game_leaves_an_absent_seat_and_the_game_waits_for_it_on_its_turn()
    {
        var session = Started(new TicTacToeSession("ABC123"));

        Assert.True(session.TryLeaveSeat(Bob, 1, out _));

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal([1], state.AbsentSeats);
        Assert.Empty(state.WaitingSeats);
        Assert.True(session.TryMove(Alice, 0, 0, out var error), error);
        Assert.False(session.TryMove(Alice, 0, 1, out error));
        Assert.Equal("It is not your turn.", error);

        state = session.GetPublicState();
        AssertWaiting(session, [1], canSkip: false);
        Assert.False(session.TrySkipPlayer(Alice, 1, out error));
        Assert.Equal("This player cannot be skipped. You can end the game.", error);
        Assert.True(session.TryEndGame(Alice, out error), error);
    }

    [Fact]
    public void A_disconnected_player_is_absent_at_once_and_away_after_the_reconnect_window()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.Disconnect("conn-b", Now);
        var state = session.GetPublicState();
        Assert.Equal(SeatStatus.Reconnecting, state.Seats[1]!.Status);
        Assert.Equal([1], state.AbsentSeats);
        AssertWaiting(session, [1], canSkip: false);

        Assert.True(session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow)));

        state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal(SeatStatus.Away, state.Seats[1]!.Status);
        Assert.Equal("Bob", state.Seats[1]!.Name);
        Assert.Equal([1], state.WaitingSeats);
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
    public void A_newcomer_can_take_an_away_seat_and_continue_that_players_game()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        session.Enter(Carol, CarolProfile, "conn-c");

        Assert.True(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out var error), error);

        Assert.Empty(session.GetPublicState().WaitingSeats);
        Assert.Equal("Carol", session.GetPublicState().Seats[1]!.Name);
        Assert.True(session.TryMove(Carol, 1, 4, out error), error);
        session.Enter(Bob, BobProfile, "conn-b2");
        Assert.False(session.TryMove(Bob, 1, 5, out _));
        Assert.DoesNotContain(session.GetPublicState().Seats, s => s?.Name == "Bob");
    }

    [Fact]
    public void A_player_cannot_take_an_empty_seat_of_the_game_as_a_second_seat()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryLeaveSeat(Bob, 1, out _);

        Assert.False(session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out var error));
        Assert.Equal("Only a user opening the game without a seat can take a disconnected player's seat.", error);
        session.Enter(Alice, AliceProfile, "conn-a2");
        Assert.False(session.HasSeatOffer(Alice));
        session.Enter(Bob, BobProfile, "conn-b2");
        Assert.True(session.HasSeatOffer(Bob));
    }

    [Fact]
    public void A_new_user_in_the_empty_seat_continues_the_game()
    {
        var session = Started(new TicTacToeSession("ABC123"));
        session.TryMove(Alice, 0, 0, out _);
        session.TryLeaveSeat(Bob, 1, out _);
        session.Enter(Carol, CarolProfile, "conn-c");

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
    public void A_game_with_an_empty_seat_can_be_ended_and_replaced_with_fewer_players()
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
    public void Leaving_all_seats_frees_them_and_the_game_waits()
    {
        var session = new TicTacToeSession("ABC123");
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 1, out _);
        session.TryNewGame(Alice, 0, out _);

        Assert.True(session.LeaveAllSeats(Alice));
        Assert.False(session.LeaveAllSeats(Alice));

        var state = session.GetPublicState();
        Assert.All(state.Seats, Assert.Null);
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal([0, 1], state.AbsentSeats);
        Assert.Equal([0], state.WaitingSeats);
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
        var publicLobby = Public(new WazooSession("ABC125"));
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

        var tictactoe = Started(new TicTacToeSession("ABC123"));
        tictactoe.TryMove(Alice, 0, 4, out _);
        AssertSameAfterReload(tictactoe, () => new TicTacToeSession("ABC123"),
            s => s.TryMove(Bob, 1, 0, out var e) ? null : e);

        var rich = RichManPoorMan(6);
        rich.TryNewGame(Players[0], 0, out _);
        rich.TryThrow(Players[0], 0, ["2S"], out _);
        AssertSameAfterReload(rich, () => new RichManPoorManSession("ABC123"),
            s => s.TryThrow(Players[5], 5, ["AS"], out var e) ? null : e);

        var wazoo = Started(new WazooSession("ABC123", Dice(6, 6, 2, 6, 3)));
        wazoo.TryRoll(Alice, 0, out _);
        wazoo.TryHold(Alice, 0, 0, true, out _);
        AssertSameAfterReload(wazoo, () => new WazooSession("ABC123"),
            s => s.TryScore(Alice, 0, WazooCategory.Sixes, out var e) ? null : e);

        var finalCard = Started(new FinalCardSession("ABC123", UnshuffledFinalCard));
        finalCard.TryPlay(Alice, 0, "R1", null, out _);
        AssertSameAfterReload(finalCard, () => new FinalCardSession("ABC123", UnshuffledFinalCard),
            s => s.TryPlay(Bob, 1, "R4", null, out var e) ? null : e);

        var yaniv = Started(new YanivSession("ABC123", UnshuffledYaniv));
        yaniv.TryDiscard(Alice, 0, ["2♣", "3♣", "4♣"], false, out _);
        AssertSameAfterReload(yaniv, () => new YanivSession("ABC123", UnshuffledYaniv),
            s => s.TryDiscard(Bob, 1, ["7♣"], true, out var e) ? null : e);

        var spades = StartedSpades();
        spades.TryBid(Players[0], 0, 4, false, out _);
        AssertSameAfterReload(spades, () => new SpadesSession("ABC123", _ => { }),
            s => s.TryBid(Players[1], 1, 2, false, out var e) ? null : e);
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

    [Fact]
    public void Tic_tac_toe_scores_survive_a_new_player_and_the_game_waits_for_a_missing_move()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));

        session.TryLeaveSeat(Bob, 1, out _);
        session.Enter(Carol, CarolProfile, "conn-c");
        session.TryTakeSeat(Carol, CarolProfile, "conn-c", 1, out _);
        Assert.True(session.TryNextRound(Alice, 1, out var error), error);
        Assert.True(session.TryMove(Carol, 1, 4, out error), error);
        session.Disconnect("conn-c", Now);
        Assert.Empty(session.GetPublicState().WaitingSeats);
        Assert.True(session.TryMove(Alice, 0, 0, out error), error);
        Assert.Equal([1], session.GetPublicState().WaitingSeats);
        session.Enter(Carol, CarolProfile, "conn-c2");

        Assert.Equal([1, 0], session.GetState().Scores);
        Assert.True(session.TryMove(Carol, 1, 8, out error), error);
    }

    [Fact]
    public void Tic_tac_toe_moves_only_once_the_game_has_started()
    {
        var session = Seated(new TicTacToeSession("ABC123"));

        Assert.False(session.TryMove(Alice, 0, 0, out var error));
        Assert.Equal("The game is not in progress.", error);

        session.TryNewGame(Alice, 0, out _);
        Assert.True(session.TryMove(Alice, 0, 0, out error), error);
    }

    [Fact]
    public void Spectators_cannot_start_the_next_round()
    {
        var session = Finish(Started(new TicTacToeSession("ABC123")));

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
    public void Leaving_during_a_heist_the_game_waits_once_the_others_are_ready_and_a_new_player_continues_that_hand()
    {
        var session = StartedTheGang();
        session.TryTakeChip(Alice, 0, 2, out _);
        session.TryLeaveSeat(Alice, 0, out _);

        Assert.Empty(session.GetPublicState().WaitingSeats);
        session.TryTakeChip(Bob, 1, 1, out _);
        session.TryTakeChip(Carol, 2, 3, out _);
        session.TrySetReady(Bob, 1, true, out _);
        session.TrySetReady(Carol, 2, true, out _);
        AssertWaiting(session, [0], canSkip: false);
        session.Enter("user:dan", new PlayerProfile("Dan", "🐢"), "conn-d");
        Assert.True(session.TryTakeSeat("user:dan", new PlayerProfile("Dan", "🐢"), "conn-d", 0, out var error), error);
        Assert.Empty(session.GetPublicState().WaitingSeats);

        Assert.Equal(2, Gang(session).Chips[0][0]);
        var dan = Assert.IsType<TheGangPrivateState>(session.GetPrivateState("user:dan")).Hands.Single();
        Assert.Equal(["3♦", "4♥"], dan.Cards);
        Assert.Null(session.GetPrivateState(Alice));
    }

    // Backgammon

    [Fact]
    public void Backgammon_waits_for_an_absent_seat_only_once_the_present_one_has_done_its_part()
    {
        var session = Started(new BackgammonSession("ABC123", Dice(6, 1, 6, 1)));
        session.Disconnect("conn-b", Now);
        Assert.Empty(session.GetPublicState().WaitingSeats);

        Assert.True(session.TryRoll(Alice, 0, out var error), error);
        AssertWaiting(session, [1], canSkip: false);
        session.Enter(Bob, BobProfile, "conn-b2");
        Assert.True(session.TryRoll(Bob, 1, out error), error);
        Assert.Empty(session.GetPublicState().WaitingSeats);
        session.Disconnect("conn-a", Now);
        AssertWaiting(session, [0], canSkip: false);
    }

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

    // Wazoo

    [Fact]
    public void Wazoo_seats_take_turns_in_seat_order()
    {
        var session = Started(new WazooSession("ABC123", Dice(6, 6, 2, 6, 3, 6, 1)));
        Assert.False(session.TryRoll(Bob, 1, out var error));
        Assert.Equal("It is not your turn.", error);
        Assert.False(session.TryRoll(Bob, 0, out error));
        Assert.Equal("Only players in this game can make moves.", error);

        Assert.True(session.TryRoll(Alice, 0, out error), error);
        Assert.True(session.TryHold(Alice, 0, 0, true, out error), error);
        Assert.True(session.TryHold(Alice, 0, 1, true, out error), error);
        Assert.True(session.TryHold(Alice, 0, 3, true, out error), error);
        var state = Wazoo(session);
        Assert.Equal([6, 6, 2, 6, 3], state.Dice);
        Assert.Equal([true, true, false, true, false], state.Held);
        Assert.Equal(2, state.RollsLeft);
        Assert.Equal(18, state.PossibleScores[(int)WazooCategory.Sixes]);
        Assert.True(session.TryRoll(Alice, 0, out error), error);
        Assert.True(session.TryScore(Alice, 0, WazooCategory.Sixes, out error), error);

        state = Wazoo(session);
        Assert.Equal((1, 1, 3), (state.Turn, state.Round, state.RollsLeft));
        Assert.Equal(new WazooTurnState(0, WazooCategory.Sixes, 24, false, state.LastTurn!.Dice), state.LastTurn);
        Assert.Equal([6, 6, 6, 6, 1], state.LastTurn.Dice);
        Assert.Equal([0, 1], state.Players.Select(p => p.Seat));
        Assert.Equal([24, 0], state.Players.Select(p => p.Total));
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
    }

    [Fact]
    public void Wazoo_can_be_played_alone_and_played_again_once_over()
    {
        var face = 0;
        var session = new WazooSession("ABC123", () => face++ % 6 + 1);
        session.TryTakeSeat(Alice, AliceProfile, "conn-a", 0, out _);
        Assert.True(session.TryNewGame(Alice, 0, out var error), error);

        foreach (var category in Enum.GetValues<WazooCategory>())
        {
            Assert.True(session.TryRoll(Alice, 0, out error), error);
            Assert.True(session.TryScore(Alice, 0, category, out error), error);
        }

        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Over, state.Phase);
        Assert.Equal([0], Wazoo(session).Winners);
        Assert.True(Wazoo(session).IsOver);
        Assert.True(session.TryNewGame(Alice, state.GameNumber, out error), error);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.All(Wazoo(session).Players[0].Scores, Assert.Null);
    }

    private static WazooState Wazoo(WazooSession session) =>
        Assert.IsType<WazooState>(session.GetPublicState().Game);

    // Final Card

    // Leaves the deck in order: seat 0 gets R0 R1 R1 R2 R2 R3 R3, seat 1 gets R4 R4 R5 R5 R6 R6 R7, and R7 is turned up.
    private static void UnshuffledFinalCard(FinalCardCard[] deck)
    {
    }

    [Fact]
    public void Final_card_seats_play_their_own_cards_which_only_they_see()
    {
        var session = Started(new FinalCardSession("ABC123", UnshuffledFinalCard));
        Assert.False(session.TryPlay(Bob, 0, "R1", null, out var error));
        Assert.Equal("Only players in this game can make moves.", error);

        var hand = Assert.IsType<FinalCardPrivateState>(session.GetPrivateState(Alice)).Hands.Single();
        Assert.Equal(0, hand.Seat);
        Assert.Equal(["R0", "R1", "R1", "R2", "R2", "R3", "R3"], hand.Cards);
        Assert.Equal(["R0", "R1", "R2", "R3"], hand.Playable);
        Assert.Empty(Assert.IsType<FinalCardPrivateState>(session.GetPrivateState(Bob)).Hands.Single().Playable);
        Assert.False(session.TryPlay(Alice, 0, "XX", null, out error));
        Assert.Equal("Unknown card.", error);

        Assert.True(session.TryPlay(Alice, 0, "R1", null, out error), error);
        var state = FinalCard(session);
        Assert.Equal(("R1", 1, FinalCardColor.Red), (state.TopCard, state.Turn, state.Color));
        Assert.Equal([6, 7], state.Players.Select(p => p.CardCount));
        Assert.Equal(new FinalCardEventState(0, FinalCardEventKind.Played, "R1", 0, null, null), state.Events.Single());
        Assert.False(session.TryCallFinalCard(Bob, 1, out error));
        Assert.Equal("Nobody to catch.", error);
        Assert.False(session.TryNextRound(Alice, 1, out error));
        Assert.Equal("This hand is not finished yet.", error);
    }

    [Fact]
    public void Final_card_starts_with_the_chosen_options()
    {
        var session = Seated(new FinalCardSession("ABC123", UnshuffledFinalCard));
        Assert.True(session.TrySetOptions(Alice, new Dictionary<string, string>
        {
            [FinalCardOptions.End] = "Pyramid", [FinalCardOptions.Stacking] = "FourOnTwo", [FinalCardOptions.Draw] = "UntilPlayable",
            [FinalCardOptions.WildDrawFour] = "NoColor",
        }, out var error), error);
        Assert.True(session.TryNewGame(Alice, 0, out error), error);

        var state = FinalCard(session);
        Assert.Equal((FinalCardEndMode.Pyramid, FinalCardStacking.FourOnTwo, true, FinalCardWildDrawFour.NoColor),
            (state.End, state.Stacking, state.DrawUntilPlayable, state.WildDrawFour));
        Assert.All(state.Players, p => Assert.Equal(7, p.HandSize));
    }

    [Fact]
    public void Final_card_defaults_to_the_official_rules()
    {
        var state = FinalCard(Started(new FinalCardSession("ABC123", UnshuffledFinalCard)));

        Assert.Equal((FinalCardStyle.US, FinalCardEndMode.Score, 500, FinalCardStacking.None, false, FinalCardWildDrawFour.Challenge),
            (state.Style, state.End, state.Target, state.Stacking, state.DrawUntilPlayable, state.WildDrawFour));
    }

    [Fact]
    public void Final_card_in_israeli_style_keeps_only_the_israeli_game_end_of_the_options()
    {
        Assert.Equal(FinalCardOptions.Style, FinalCardOptions.All[0].Key);
        var session = Seated(new FinalCardSession("ABC123", UnshuffledFinalCard));
        Assert.True(session.TrySetOptions(Alice, new Dictionary<string, string>
        {
            [FinalCardOptions.Style] = "Israeli", [FinalCardOptions.IsraeliEnd] = "Pyramid", [FinalCardOptions.End] = "Score",
            [FinalCardOptions.Stacking] = "None", [FinalCardOptions.Draw] = "UntilPlayable",
        }, out var error), error);
        Assert.True(session.TryNewGame(Alice, 0, out error), error);

        var state = FinalCard(session);
        Assert.Equal((FinalCardStyle.Israeli, FinalCardEndMode.Pyramid, FinalCardStacking.TwoOnTwo, false),
            (state.Style, state.End, state.Stacking, state.DrawUntilPlayable));
        Assert.All(state.Players, p => Assert.Equal((8, 8), (p.HandSize, p.CardCount)));
        Assert.Equal((null, null, false), (state.TakiColor, state.ThreeBy, state.AnyCard));
    }

    private static FinalCardState FinalCard(FinalCardSession session) =>
        Assert.IsType<FinalCardState>(session.GetPublicState().Game);

    // Yaniv

    // Leaves the deck in order: seat 0 gets 2♣ 3♣ 4♣ 5♣ 6♣, seat 1 gets 7♣ 8♣ 9♣ 10♣ J♣, and Q♣ is on top of the closed pile.
    private static void UnshuffledYaniv(Card[] deck)
    {
    }

    [Fact]
    public void Yaniv_seats_play_their_own_cards_which_only_they_see()
    {
        var session = Started(new YanivSession("ABC123", UnshuffledYaniv));
        Assert.False(session.TryDiscard(Bob, 0, ["2♣"], false, out var error));
        Assert.Equal("Only players in this game can make moves.", error);
        Assert.False(session.TryDiscard(Alice, 0, ["XX"], false, out error));
        Assert.Equal("Unknown card.", error);

        var hand = Assert.IsType<YanivPrivateState>(session.GetPrivateState(Alice)).Hands.Single();
        Assert.Equal((0, 20, false), (hand.Seat, hand.HandScore, hand.CanCallYaniv));
        Assert.Equal(["2♣", "3♣", "4♣", "5♣", "6♣"], hand.Cards);
        Assert.True(session.TryDiscard(Alice, 0, ["4♣", "2♣", "3♣"], false, out error), error);

        var state = Yaniv(session);
        Assert.Equal(("4♣", 1, 3), (state.OpenTop, state.Turn, state.OpenPile));
        Assert.Equal((0, false, null, false), (state.LastTurn!.Seat, state.LastTurn.TookOpen, state.LastTurn.Taken, state.LastTurn.Skipped));
        Assert.Equal(["2♣", "3♣", "4♣"], state.LastTurn.Cards);
        Assert.Equal([3, 5], state.Players.Select(p => p.CardCount));
        Assert.True(session.TryDiscard(Bob, 1, ["7♣"], true, out error), error);
        Assert.Equal("4♣", Yaniv(session).LastTurn!.Taken);
        Assert.False(session.TryNextRound(Alice, 1, out error));
        Assert.Equal("This round is not finished yet.", error);
    }

    [Fact]
    public void Yaniv_starts_with_the_chosen_options_and_an_absent_player_can_be_skipped()
    {
        var session = Seated(new YanivSession("ABC123", UnshuffledYaniv));
        Assert.True(session.TrySetOptions(Alice, new Dictionary<string, string>
        {
            [YanivOptions.End] = "Score", [YanivOptions.Target] = "100", [YanivOptions.Bonus] = "No",
        }, out var error), error);
        Assert.True(session.TryNewGame(Alice, 0, out error), error);
        Assert.Equal((YanivEndMode.Score, 100, false), (Yaniv(session).End, Yaniv(session).Target, Yaniv(session).Bonus));

        session.Disconnect("conn-a", Now);
        AssertWaiting(session, [0], canSkip: true);
        Assert.True(session.TrySkipPlayer(Bob, 0, out error), error);
        Assert.Equal(1, Yaniv(session).Turn);
        Assert.True(Yaniv(session).LastTurn!.Skipped);
    }

    [Fact]
    public void A_public_Yaniv_game_skips_away_players_and_goes_on_when_a_player_leaves()
    {
        var session = Public(new YanivSession("ABC123", UnshuffledYaniv));
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);
        session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out _);
        session.TryNewGame(Alice, 0, out _);

        session.Disconnect("conn-b", Now);
        session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow));
        Assert.True(session.TryDiscard(Alice, 0, ["2♣"], false, out var error), error);
        Assert.Equal(2, Yaniv(session).Turn);
        Assert.True(Yaniv(session).LastTurn!.Skipped);

        Assert.True(session.TryLeaveSeat(Carol, 2, out error), error);
        var state = session.GetPublicState();
        Assert.Equal(SessionPhase.Playing, state.Phase);
        Assert.Equal(0, Yaniv(session).Turn);
    }

    private static YanivState Yaniv(YanivSession session) =>
        Assert.IsType<YanivState>(session.GetPublicState().Game);

    // Spades

    // A Spades game with 4 seats, unshuffled: seat 0 holds the clubs, 1 the diamonds, 2 the spades and 3 the hearts.
    private static SpadesSession StartedSpades(Dictionary<string, string>? options = null)
    {
        var session = new SpadesSession("ABC123", _ => { });
        for (var i = 0; i < 4; i++)
        {
            Sit(session, i);
        }

        Assert.True(session.TrySetOptions(Players[0], options, out var error), error);
        Assert.True(session.TryNewGame(Players[0], 0, out error), error);
        return session;
    }

    [Fact]
    public void Spades_seats_bid_in_turn_in_teams_of_partners_across()
    {
        var session = StartedSpades();
        var state = Spades(session);
        Assert.Equal((SpadesPhase.Bidding, 3, 0), (state.Phase, state.Dealer, state.Bidder));
        Assert.Equal([[0, 2], [1, 3]], state.Teams.Select(t => t.Seats));
        Assert.False(session.TryBid(Players[1], 1, 3, false, out var error));
        Assert.Equal("It is not your turn to bid.", error);

        foreach (var (seat, bid) in new[] { (0, 4), (1, 0), (2, 5), (3, 2) })
        {
            Assert.True(session.TryBid(Players[seat], seat, bid, false, out error), error);
        }

        state = Spades(session);
        Assert.Equal((SpadesPhase.Playing, 0), (state.Phase, state.Turn));
        Assert.Equal([4, 0, 5, 2], state.Players.Select(p => p.Bid));
        var hand = Assert.IsType<SpadesPrivateState>(session.GetPrivateState(Players[0])).Hands.Single();
        Assert.Equal(13, hand.Playable.Count);
        Assert.True(session.TryPlayCard(Players[0], 0, "A♣", out error), error);
        Assert.Equal([new TrickCardState(0, "A♣")], Spades(session).Trick.Cards);
    }

    [Fact]
    public void With_blind_nil_a_seat_sees_its_cards_only_once_it_looks_or_bids()
    {
        var session = StartedSpades(new() { [SpadesOptions.BlindNil] = "Yes" });
        Assert.Equal((0, false), (Hand(0).Cards.Count, Hand(0).Looked));

        Assert.True(session.TryBid(Players[0], 0, 0, true, out var error), error);
        Assert.Equal(13, Hand(0).Cards.Count);
        Assert.True(session.TryLook(Players[1], 1, out error), error);
        Assert.True(Hand(1).Looked);
        Assert.True(Spades(session).Players[0].Blind);

        (IReadOnlyList<string> Cards, bool Looked) Hand(int seat)
        {
            var hand = Assert.IsType<SpadesPrivateState>(session.GetPrivateState(Players[seat])).Hands.Single();
            return (hand.Cards, hand.Looked);
        }
    }

    private static SpadesState Spades(SpadesSession session) =>
        Assert.IsType<SpadesState>(session.GetPublicState().Game);

    // Rich man, poor man

    private static readonly string[] Players = [.. Enumerable.Range(0, 9).Select(i => $"user:p{i}")];

    [Fact]
    public void Rich_man_poor_man_needs_4_seats_and_a_player_who_leaves_keeps_their_turn_until_skipped_or_replaced()
    {
        var session = RichManPoorMan(3);
        Assert.False(session.TryNewGame(Players[0], 0, out var error));
        Assert.Equal("At least 4 players must be seated.", error);
        Sit(session, 3);
        Sit(session, 4);
        Sit(session, 5);
        Assert.True(session.TryNewGame(Players[0], 0, out error), error);
        Assert.Equal(0, Rmpm(session).Turn);
        Assert.True(session.TryThrow(Players[0], 0, ["2S"], out error), error);
        Assert.Equal(5, Rmpm(session).Turn);

        Assert.True(session.TryLeaveSeat(Players[5], 5, out error), error);
        var left = Rmpm(session).Players.Single(p => p.Seat == 5);
        Assert.Equal((false, 13), (left.IsActive, left.CardCount));
        Assert.Equal(5, Rmpm(session).Turn);
        AssertWaiting(session, [5], canSkip: true);
        Assert.True(session.TrySkipPlayer(Players[1], 5, out error), error);
        Assert.Equal(4, Rmpm(session).Turn);
        Assert.True(Rmpm(session).Players.Single(p => p.Seat == 5).Passed);
        Assert.Empty(session.GetPublicState().WaitingSeats);

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
    public void Rich_man_poor_man_a_disconnected_players_turn_waits_until_they_come_back_or_are_skipped()
    {
        var session = RichManPoorMan(4);
        session.TryNewGame(Players[0], 0, out _);
        session.TryThrow(Players[0], 0, ["2S"], out _);
        session.Disconnect("conn-3", Now);

        Assert.False(Rmpm(session).Players.Single(p => p.Seat == 3).IsActive);
        AssertWaiting(session, [3], canSkip: true);
        session.Enter(Players[3], new PlayerProfile("P3", "🙂"), "conn-3b");
        Assert.True(Rmpm(session).Players.Single(p => p.Seat == 3).IsActive);
        Assert.Empty(session.GetPublicState().WaitingSeats);
        Assert.True(session.TrySkipPlayer(Players[1], 3, out var error), error);
        Assert.Equal(3, Rmpm(session).Turn);

        session.Disconnect("conn-3b", Now);
        Assert.False(session.TrySkipPlayer("user:someone", 3, out error));
        Assert.Equal("Only players in this game can do that.", error);
        Assert.True(session.TrySkipPlayer(Players[1], 3, out error), error);
        Assert.Equal(2, Rmpm(session).Turn);
    }

    [Fact]
    public void Rich_man_poor_man_ends_when_the_last_connected_player_finishes_and_drops_the_disconnected_players_with_cards()
    {
        var session = RichManPoorMan(6);
        session.TryNewGame(Players[0], 0, out _);
        session.Disconnect("conn-4", Now);
        session.Disconnect("conn-5", Now);

        PlayOut(session, skipAbsent: true);

        var state = Rmpm(session);
        Assert.Equal([0, 1, 2, 3], state.Players.Select(p => p.Seat).Order());
        Assert.Equal(4, state.Ranking.Count);
        Assert.Null(session.GetPublicState().Seats[4]);
        Assert.Null(session.GetPublicState().Seats[5]);
        session.Enter(Players[4], new PlayerProfile("P4", "🙂"), "conn-4b");
        Assert.False(session.HasSeatOffer(Players[4]));
        Assert.True(session.TryNextRound(Players[0], 1, out var error), error);
        Assert.Equal((RichManPoorManPhase.Exchanging, 4), (Rmpm(session).Phase, Rmpm(session).Players.Count));
    }

    [Fact]
    public void Rich_man_poor_man_drops_players_who_left_at_the_exchange_and_goes_back_to_the_lobby_with_fewer_than_4()
    {
        var session = RichManPoorMan(5);
        session.TryNewGame(Players[0], 0, out _);
        PlayOut(session);
        Assert.Equal(5, Rmpm(session).Ranking.Count);
        Assert.True(session.TryLeaveSeat(Players[4], 4, out var error), error);

        Assert.True(session.TryNextRound(Players[0], 1, out error), error);
        var state = Rmpm(session);
        Assert.Equal((2, RichManPoorManPhase.Exchanging, 4), (state.GameNumber, state.Phase, state.Players.Count));
        Assert.DoesNotContain(state.Players, p => p.Seat == 4);
        Assert.Equal([2, 1, 1, 2], state.Players.Select(p => p.ExchangeCount));
        Assert.False(session.TryTakeNextSeat(Players[7], new PlayerProfile("P7", "🙂"), "conn-7", out error));
        Assert.Equal("Ask to join: a player will choose your place.", error);

        Assert.True(session.TryLeaveSeat(Players[3], 3, out error), error);
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
    public void Rich_man_poor_man_a_player_who_finished_then_disconnected_stays_and_the_exchange_waits_for_them()
    {
        var session = RichManPoorMan(6);
        session.TryNewGame(Players[0], 0, out _);
        PlayOut(session);
        var away = Rmpm(session).Ranking[^1];
        session.Disconnect($"conn-{away}", Now);
        Assert.True(session.TryNextRound(Players[0], 1, out var error), error);
        Assert.True(session.UpdateAwaySeats(Now.Add(GameSession.ReconnectWindow)));

        var state = Rmpm(session);
        Assert.Equal((RichManPoorManPhase.Exchanging, 6), (state.Phase, state.Players.Count));
        Assert.False(state.Players.Single(p => p.Seat == away).IsActive);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
        Assert.Equal([away], session.GetPublicState().AbsentSeats);
        Assert.Empty(session.GetPublicState().WaitingSeats);
        foreach (var player in state.Players.Where(p => p.Seat != away))
        {
            Give(session, Players[player.Seat], player.Seat);
        }

        Assert.Equal(RichManPoorManPhase.Exchanging, Rmpm(session).Phase);
        AssertWaiting(session, [away], canSkip: false);
        Assert.False(session.TrySkipPlayer(Players[0], away, out error));
        Assert.Equal("This player cannot be skipped. You can end the game.", error);
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

    // Seats player `player` in the next seat, with the connection "conn-{player}".
    private static void Sit(GameSession session, int player)
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
    // With skipAbsent, player 0 skips the turns the game waits for. Seat i must be held by player i.
    private static void PlayOut(RichManPoorManSession session, bool skipAbsent = false)
    {
        for (var moves = 0; moves < 1000 && Rmpm(session).Phase == RichManPoorManPhase.Playing; moves++)
        {
            var seat = Rmpm(session).Turn;
            string? error;
            if (skipAbsent && session.GetPublicState().WaitingSeats.Contains(seat))
            {
                Assert.True(session.TrySkipPlayer(Players[0], seat, out error), error);
            }
            else if (Rmpm(session).CurrentThrow is null)
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

    // The game waits for these absent seats, and can skip them or not.
    private static void AssertWaiting(GameSession session, int[] seats, bool canSkip)
    {
        var state = session.GetPublicState();
        Assert.Equal(seats, state.WaitingSeats);
        Assert.Equal(canSkip, state.CanSkipWaiting);
    }

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

    // Seat limits by options

    [Fact]
    public void A_game_seats_only_as_many_players_as_its_options_allow()
    {
        var session = new LimitedSession("ABC123");
        Assert.True(session.TrySetOptions(Alice, OneRound, out var error), error);
        Assert.Equal(2, session.GetPublicState().MaxSeats);
        Seated(session);

        Assert.False(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out error));
        Assert.Equal("All seats are taken.", error);
        Assert.False(session.TryTakeSeat(Carol, CarolProfile, "conn-c", 2, out error));
        Assert.Equal("All seats are taken.", error);

        Assert.True(session.TrySetOptions(Alice, null, out error), error);
        Assert.True(session.TryTakeNextSeat(Carol, CarolProfile, "conn-c", out error), error);
        Assert.Equal(4, session.GetPublicState().MaxSeats);
        Assert.False(session.TrySetOptions(Alice, OneRound, out error));
        Assert.Equal("With these options, at most 2 players can play.", error);
    }

    [Fact]
    public void A_public_game_starts_by_itself_once_the_seats_its_options_allow_are_taken()
    {
        var session = Public(new LimitedSession("ABC123"));
        session.TryTakeNextSeat(Alice, AliceProfile, "conn-a", out _);
        Assert.True(session.TrySetOptions(Alice, OneRound, out var error), error);
        Assert.True(session.IsOpenToJoin);

        session.TryTakeNextSeat(Bob, BobProfile, "conn-b", out _);

        Assert.False(session.IsOpenToJoin);
        Assert.Equal(SessionPhase.Playing, session.GetPublicState().Phase);
    }

    private static readonly Dictionary<string, string> OneRound = new() { [YanivOptions.End] = nameof(YanivEndMode.OneRound) };

    /// <summary>
    /// A game of 1 to 4 seats (with Yaniv's options) that seats only 2 players with the One round option.
    /// </summary>
    private sealed class LimitedSession(string id) : GameSession(id)
    {
        public override GameType Type => GameType.Yaniv;

        protected override int MinSeats => 1;

        protected override int MaxSeats => 4;

        protected override bool IsGameOver => false;

        protected override int SeatLimitFor(IReadOnlyDictionary<string, string> options) =>
            options.GetValueOrDefault(YanivOptions.End) == nameof(YanivEndMode.OneRound) ? 2 : 4;

        protected override void StartNewGame(IReadOnlyList<int> seats)
        {
        }

        protected override object GetGameState() => new { };
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
}
