using GamesLane.Server.Data;
using GamesLane.Server.Games.Wazoo;
using GamesLane.Server.Sessions;
using GamesLane.Server.Stats;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesLane.Server.Tests;

public class GameSessionStoreTests
{
    private static readonly PlayerProfile Alice = new("Alice", "🦊");
    private static readonly PlayerProfile Bob = new("Bob", "🐼");

    [Fact]
    public async Task Creates_six_character_codes_without_ambiguous_characters_and_saves_them()
    {
        var repository = new MemoryRepository();
        var store = new GameSessionStore(repository);

        var id = (await store.CreateAsync(GameType.TicTacToe)).Id;

        Assert.Equal(6, id.Length);
        Assert.All(id, c => Assert.Contains(c, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"));
        Assert.True(await repository.ExistsAsync(id));
    }

    [Fact]
    public async Task Codes_are_unique()
    {
        var store = new GameSessionStore(new MemoryRepository());

        var ids = new List<string>();
        for (var i = 0; i < 1000; i++)
        {
            ids.Add((await store.CreateAsync(GameType.TicTacToe)).Id);
        }

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Creates_a_session_of_the_requested_game_type()
    {
        var store = new GameSessionStore(new MemoryRepository());

        Assert.IsType<TicTacToeSession>(await store.CreateAsync(GameType.TicTacToe));
        Assert.Equal("/tictactoe.html", GameSessionStore.PageFor(GameType.TicTacToe));
        Assert.IsType<BackgammonSession>(await store.CreateAsync(GameType.Backgammon));
        Assert.Equal("/backgammon.html", GameSessionStore.PageFor(GameType.Backgammon));
        Assert.IsType<RichManPoorManSession>(await store.CreateAsync(GameType.RichManPoorMan));
        Assert.Equal("/rich-man-poor-man.html", GameSessionStore.PageFor(GameType.RichManPoorMan));
        Assert.IsType<WazooSession>(await store.CreateAsync(GameType.Wazoo));
        Assert.Equal("/wazoo.html", GameSessionStore.PageFor(GameType.Wazoo));
        Assert.IsType<FinalCardSession>(await store.CreateAsync(GameType.FinalCard));
        Assert.Equal("/final-card.html", GameSessionStore.PageFor(GameType.FinalCard));
        Assert.IsType<YanivSession>(await store.CreateAsync(GameType.Yaniv));
        Assert.Equal("/yaniv.html", GameSessionStore.PageFor(GameType.Yaniv));
        Assert.IsType<HeartsSession>(await store.CreateAsync(GameType.Hearts));
        Assert.Equal("/hearts.html", GameSessionStore.PageFor(GameType.Hearts));
        Assert.IsType<SpadesSession>(await store.CreateAsync(GameType.Spades));
        Assert.Equal("/spades.html", GameSessionStore.PageFor(GameType.Spades));
    }

    [Fact]
    public async Task Finds_games_regardless_of_case_and_returns_null_for_unknown_ones()
    {
        var store = new GameSessionStore(new MemoryRepository());
        var session = await store.CreateAsync(GameType.TicTacToe);

        Assert.Same(session, await store.GetAsync(session.Id.ToLowerInvariant()));
        Assert.Null(await store.GetAsync("NOPE00"));
    }

    [Fact]
    public async Task A_session_removed_from_memory_is_loaded_again_when_opened_with_its_seats_away()
    {
        var repository = new MemoryRepository();
        var clock = new Clock();
        var store = new GameSessionStore(repository, clock);
        var session = await store.CreateAsync(GameType.TicTacToe);
        session.TryTakeSeat("user:alice", Alice, "conn-a", 0, out _);
        session.TryTakeSeat("user:bob", Bob, "conn-b", 1, out _);
        session.TryNewGame("user:alice", 0, out _);
        ((ITicTacToeSession)session).TryMove("user:alice", 0, 4, out _);

        await store.UnloadAsync(session);
        Assert.DoesNotContain(session, store.All);
        clock.Now += TimeSpan.FromHours(2);

        var loaded = Assert.IsType<TicTacToeSession>(await store.GetAsync(session.Id.ToLowerInvariant()));
        Assert.NotSame(session, loaded);
        Assert.Same(loaded, await store.GetAsync(session.Id));
        var state = loaded.GetPublicState();
        Assert.Equal([1], state.WaitingSeats);
        Assert.All(state.Seats, s => Assert.Equal(SeatStatus.Away, s!.Status));
        Assert.Equal(session.GetPublicState().Seats.Select(s => s!.PlayerId), state.Seats.Select(s => s!.PlayerId));

        loaded.Enter("user:alice", Alice, "conn-a2");
        loaded.Enter("user:bob", Bob, "conn-b2");
        Assert.Empty(loaded.GetPublicState().WaitingSeats);
        Assert.True(loaded.TryMove("user:bob", 1, 0, out var error), error);
    }

    [Fact]
    public async Task A_session_whose_game_never_started_is_deleted_when_removed_from_memory_even_with_seats_taken()
    {
        var repository = new MemoryRepository();
        var store = new GameSessionStore(repository);
        var session = await store.CreateAsync(GameType.TicTacToe);
        session.TryTakeSeat("user:alice", Alice, "conn-a", 0, out _);

        await store.UnloadAsync(session);

        Assert.False(await repository.ExistsAsync(session.Id));
        Assert.Null(await store.GetAsync(session.Id));
    }

    [Fact]
    public async Task A_public_game_with_nobody_seated_is_deleted_even_after_it_started()
    {
        var repository = new MemoryRepository();
        var store = new GameSessionStore(repository);
        var session = await store.CreateAsync(GameType.TicTacToe, s =>
        {
            s.MakePublic();
            s.TryTakeNextSeat("user:alice", Alice, "conn-a", out _);
        });
        session.TryTakeNextSeat("user:bob", Bob, "conn-b", out _);
        Assert.True(session.HasStarted);
        session.LeaveAllSeats("user:alice");
        session.LeaveAllSeats("user:bob");

        await store.UnloadAsync(session);

        Assert.False(await repository.ExistsAsync(session.Id));
    }

    [Fact]
    public async Task Remembers_when_each_session_last_changed()
    {
        var clock = new Clock();
        var store = new GameSessionStore(new MemoryRepository(), clock);
        var session = await store.CreateAsync(GameType.TicTacToe);
        Assert.Equal(clock.Now, store.LastActivity(session));

        clock.Now += TimeSpan.FromMinutes(3);
        await store.SaveAsync(session);
        Assert.Equal(clock.Now, store.LastActivity(session));
    }

    [Fact]
    public async Task A_session_saved_by_another_version_is_not_loaded()
    {
        var repository = new MemoryRepository();
        var store = new GameSessionStore(repository);
        var session = await store.CreateAsync(GameType.TicTacToe);
        session.TryTakeSeat("user:alice", Alice, "conn-a", 0, out _);
        session.TryTakeSeat("user:bob", Bob, "conn-b", 1, out _);
        session.TryNewGame("user:alice", 0, out _);
        await store.UnloadAsync(session);

        var saved = (await repository.FindAsync(session.Id))!;
        saved.Version = "OLD";
        await repository.SaveAsync(saved);

        Assert.Null(await new GameSessionStore(repository).GetAsync(session.Id));
    }

    [Fact]
    public async Task The_cleanup_deletes_sessions_with_no_activity_for_6_hours_but_never_one_in_memory()
    {
        var repository = new MemoryRepository();
        var clock = new Clock();
        var store = new GameSessionStore(repository, clock);
        var old = await Started(store);
        await store.UnloadAsync(old);
        var open = await Started(store);
        clock.Now += TimeSpan.FromHours(4);
        var recent = await Started(store);
        await store.UnloadAsync(recent);
        clock.Now += TimeSpan.FromHours(3);

        await new ExpiredSessionCleanup(repository, store, clock, NullLogger<ExpiredSessionCleanup>.Instance).CleanUpAsync();

        Assert.False(await repository.ExistsAsync(old.Id));
        Assert.True(await repository.ExistsAsync(open.Id));
        Assert.True(await repository.ExistsAsync(recent.Id));
    }

    [Fact]
    public async Task Removing_an_unchanged_session_from_memory_does_not_count_as_activity()
    {
        var repository = new MemoryRepository();
        var clock = new Clock();
        var store = new GameSessionStore(repository, clock);
        var session = await Started(store);
        await store.SaveAsync(session);
        var savedAt = clock.Now;
        clock.Now += TimeSpan.FromHours(1);
        await store.UnloadAsync(session);
        Assert.Equal(savedAt, (await repository.FindAsync(session.Id))!.UpdatedAt);

        clock.Now += TimeSpan.FromHours(1);
        await store.UnloadAsync((await store.GetAsync(session.Id))!);
        Assert.Equal(savedAt, (await repository.FindAsync(session.Id))!.UpdatedAt);

        var reopened = (await store.GetAsync(session.Id))!;
        reopened.Enter("user:alice", Alice, "conn-a2");
        await store.UnloadAsync(reopened);
        Assert.Equal(clock.Now, (await repository.FindAsync(session.Id))!.UpdatedAt);
    }

    [Fact]
    public async Task Each_game_started_is_counted_once_on_its_day_in_utc()
    {
        var plays = new MemoryPlays();
        var clock = new Clock { Now = new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.FromHours(-2)) };
        var store = new GameSessionStore(new MemoryRepository(), clock, null, plays);
        var session = await Started(store);
        await store.SaveAsync(session);
        await store.SaveAsync(session);
        Assert.Equal([(new DateOnly(2026, 11, 5), "TicTacToe", 1)], plays.Counts);

        Assert.True(session.TryEndGame("user:alice", out var error), error);
        Assert.True(session.TryNewGame("user:bob", 1, out error), error);
        await store.SaveAsync(session);
        Assert.Equal(2, plays.Counts.Sum(c => c.Count));
    }

    [Fact]
    public async Task Games_of_a_session_loaded_again_are_not_counted_twice_and_a_lobby_counts_nothing()
    {
        var plays = new MemoryPlays();
        var repository = new MemoryRepository();
        var store = new GameSessionStore(repository, new Clock(), null, plays);
        var session = await Started(store);
        await store.UnloadAsync(session);
        Assert.Equal(1, plays.Counts.Sum(c => c.Count));

        var loaded = (await store.GetAsync(session.Id))!;
        loaded.Enter("user:alice", Alice, "conn-a2");
        await store.SaveAsync(loaded);
        await store.CreateAsync(GameType.Yaniv);

        Assert.Equal(1, plays.Counts.Sum(c => c.Count));
    }

    [Fact]
    public void The_report_has_every_day_and_game_with_zeros_and_each_games_total()
    {
        var today = new DateOnly(2026, 11, 10);
        GamePlay[] kept =
        [
            new() { Day = today, GameType = "Yaniv", Count = 3 },
            new() { Day = today.AddDays(-2), GameType = "Yaniv", Count = 2 },
            new() { Day = today.AddDays(-2), GameType = "TheGang", Count = 1 },
            new() { Day = today.AddDays(-3), GameType = "Yaniv", Count = 9 },
            new() { Day = today, GameType = "Chess", Count = 5 },
        ];

        var report = GamePlayStats.Report(kept, today, 3);

        Assert.Equal([today.AddDays(-2), today.AddDays(-1), today], report.Days);
        Assert.Equal(GamePlayStats.UsualOrder, report.Games.Select(g => g.GameType));
        var yaniv = report.Games.Single(g => g.GameType == GameType.Yaniv);
        Assert.Equal([2, 0, 3], yaniv.Counts);
        Assert.Equal(5, yaniv.Total);
        Assert.Equal(1, report.Games.Single(g => g.GameType == GameType.TheGang).Total);
        Assert.All(report.Games.Where(g => g.GameType is not (GameType.Yaniv or GameType.TheGang)), g => Assert.Equal([0, 0, 0], g.Counts));
        Assert.Equal(Enum.GetValues<GameType>().Order(), GamePlayStats.UsualOrder.Order());
    }

    [Fact]
    public void Popular_games_come_first_and_ties_keep_the_usual_order()
    {
        var today = new DateOnly(2026, 11, 10);
        GamePlay[] kept =
        [
            new() { Day = today, GameType = "Wazoo", Count = 2 },
            new() { Day = today.AddDays(-1), GameType = "Wazoo", Count = 2 },
            new() { Day = today, GameType = "Yaniv", Count = 4 },
            new() { Day = today, GameType = "TicTacToe", Count = 1 },
        ];

        Assert.Equal(
            [GameType.Yaniv, GameType.Wazoo, GameType.TicTacToe, GameType.TheGang, GameType.Backgammon, GameType.RichManPoorMan, GameType.FinalCard,
                GameType.Hearts, GameType.Spades],
            GamePlayStats.PopularOrder(kept));
        Assert.Equal(GamePlayStats.UsualOrder, GamePlayStats.PopularOrder([]));
    }

    [Fact]
    public async Task Each_game_played_to_its_end_is_counted_once_and_a_game_ended_early_is_not()
    {
        var activity = new MemoryActivity();
        var clock = new Clock();
        var store = new GameSessionStore(new MemoryRepository(), clock, null, null, new ActivityCounter(activity, clock));
        var session = await StartedWazoo(store);
        await store.SaveAsync(session);
        Assert.Empty(activity.Counts);

        PlayToTheEnd(session);
        await store.SaveAsync(session);
        await store.SaveAsync(session);
        Assert.Equal([(new DateOnly(2026, 11, 4), "FinishGame", "Wazoo", "", 1)], activity.Counts);

        Assert.True(session.TryNewGame("user:alice", 1, out var error), error);
        Assert.True(session.TryEndGame("user:alice", out error), error);
        await store.SaveAsync(session);
        Assert.Equal(1, activity.Counts.Sum(c => c.Count));
    }

    [Fact]
    public async Task A_finished_game_loaded_again_is_not_counted_twice()
    {
        var activity = new MemoryActivity();
        var clock = new Clock();
        var store = new GameSessionStore(new MemoryRepository(), clock, null, null, new ActivityCounter(activity, clock));
        var session = await StartedWazoo(store);
        PlayToTheEnd(session);
        await store.UnloadAsync(session);
        Assert.Equal(1, activity.Counts.Sum(c => c.Count));

        var loaded = (await store.GetAsync(session.Id))!;
        loaded.Enter("user:alice", Alice, "conn-a2");
        await store.SaveAsync(loaded);

        Assert.Equal(1, activity.Counts.Sum(c => c.Count));
    }

    [Fact]
    public async Task Activity_is_counted_on_its_day_in_utc_and_an_error_does_not_stop_what_is_counted()
    {
        var activity = new MemoryActivity();
        var counter = new ActivityCounter(activity, new Clock { Now = new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.FromHours(-2)) });

        await counter.CountAsync(ActivityEvent.CreateGame, GameType.Yaniv, ActivityCounter.Private);
        await counter.CountAsync(ActivityEvent.CreateGame, GameType.Yaniv, ActivityCounter.Private);
        await counter.CountAsync(ActivityEvent.Visit);

        var day = new DateOnly(2026, 11, 5);
        Assert.Equal([(day, "CreateGame", "Yaniv", "Private", 2), (day, "Visit", "", "", 1)], activity.Counts);
        activity.Fail = true;
        await counter.CountAsync(ActivityEvent.Visit);
    }

    [Fact]
    public void A_first_visit_of_the_day_counts_the_visit_its_source_language_and_return_and_a_game_page_counts_once_a_day()
    {
        (ActivityEvent, GameType?, string)[] first =
        [
            (ActivityEvent.Visit, GameType.Yaniv, ""),
            (ActivityEvent.VisitSource, null, "facebook.com"),
            (ActivityEvent.VisitLanguage, null, "he-IL"),
            (ActivityEvent.VisitReturn, null, VisitRules.After7Days),
            (ActivityEvent.OpenGame, GameType.Yaniv, ""),
        ];
        Assert.Equal(first, VisitRules.Counts(new VisitReport(GameType.Yaniv, true, "WWW.Facebook.com", "he-IL", 9)));

        (ActivityEvent, GameType?, string)[] later = [(ActivityEvent.OpenGame, GameType.Backgammon, "")];
        Assert.Equal(later, VisitRules.Counts(new VisitReport(GameType.Backgammon, false, "ignored", "en", 1)));
        Assert.Empty(VisitRules.Counts(new VisitReport(null, false, null, null, null)));
        Assert.Empty(VisitRules.Counts(new VisitReport((GameType)99, false, null, null, null)));
    }

    [Theory]
    [InlineData(null, "direct")]
    [InlineData(" ", "direct")]
    [InlineData(" Newsletter ", "newsletter")]
    [InlineData("www.google.com", "google.com")]
    [InlineData("spring_sale-2026", "spring_sale-2026")]
    [InlineData("<script>", "other")]
    [InlineData("a b", "other")]
    [InlineData("-leading-dash", "other")]
    public void Sources_are_plain_lower_case_names(string? source, string expected) =>
        Assert.Equal(expected, VisitRules.Source(source));

    [Fact]
    public void Sources_longer_than_50_characters_are_other()
    {
        Assert.Equal(new string('a', 50), VisitRules.Source(new string('a', 50)));
        Assert.Equal(VisitRules.Other, VisitRules.Source(new string('a', 51)));
    }

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("he", "he")]
    [InlineData("zh-Hant-TW", "zh-TW")]
    [InlineData("PT_br", "pt-BR")]
    [InlineData("es-419", "es")]
    [InlineData("", "other")]
    [InlineData(null, "other")]
    [InlineData("english", "other")]
    [InlineData("e1-US", "other")]
    public void Languages_are_a_language_and_its_region(string? tag, string expected) =>
        Assert.Equal(expected, VisitRules.Language(tag));

    [Theory]
    [InlineData(null, VisitRules.New)]
    [InlineData(-3, VisitRules.New)]
    [InlineData(1, VisitRules.Within7Days)]
    [InlineData(6, VisitRules.Within7Days)]
    [InlineData(7, VisitRules.After7Days)]
    [InlineData(400, VisitRules.After7Days)]
    public void Visits_are_new_back_within_7_days_or_back_after_7_days_or_more(int? days, string expected) =>
        Assert.Equal(expected, VisitRules.ReturnBucket(days));

    [Fact]
    public void The_activity_report_has_the_visits_per_day_each_games_activity_and_the_visits_by_return_source_and_language()
    {
        var today = new DateOnly(2026, 11, 10);
        static ActivityCount C(DateOnly day, ActivityEvent activity, string game, string detail, int count) =>
            new() { Day = day, Event = activity.ToString(), GameType = game, Detail = detail, Count = count };
        ActivityCount[] kept =
        [
            C(today, ActivityEvent.Visit, "", "", 4),
            C(today.AddDays(-1), ActivityEvent.Visit, "Yaniv", "", 2),
            C(today.AddDays(-5), ActivityEvent.Visit, "", "", 9),
            C(today, ActivityEvent.VisitSource, "", "direct", 3),
            C(today, ActivityEvent.VisitSource, "", "facebook.com", 1),
            C(today.AddDays(-1), ActivityEvent.VisitSource, "", "facebook.com", 2),
            C(today.AddDays(-5), ActivityEvent.VisitSource, "", "old.example", 9),
            C(today, ActivityEvent.VisitLanguage, "", "he-IL", 4),
            C(today, ActivityEvent.VisitReturn, "", VisitRules.New, 3),
            C(today, ActivityEvent.VisitReturn, "", VisitRules.After7Days, 1),
            C(today, ActivityEvent.OpenGame, "Yaniv", "", 5),
            C(today, ActivityEvent.CreateGame, "Yaniv", ActivityCounter.Public, 1),
            C(today, ActivityEvent.CreateGame, "Yaniv", ActivityCounter.Private, 2),
            C(today, ActivityEvent.JoinGame, "Yaniv", ActivityCounter.Public, 1),
            C(today, ActivityEvent.JoinGame, "Yaniv", ActivityCounter.Private, 3),
            C(today, ActivityEvent.FinishGame, "Yaniv", "", 2),
            C(today, ActivityEvent.OpenInvite, "Yaniv", "", 4),
            C(today, ActivityEvent.OpenGame, "Chess", "", 7),
        ];
        GamePlay[] plays =
        [
            new() { Day = today, GameType = "Yaniv", Count = 3 },
            new() { Day = today.AddDays(-5), GameType = "Yaniv", Count = 8 },
        ];

        var report = ActivityStats.Report(kept, plays, today, 3);

        Assert.Equal([today.AddDays(-2), today.AddDays(-1), today], report.Days);
        Assert.Equal([0, 2, 4], report.Visits);
        Assert.Equal(GamePlayStats.UsualOrder, report.Games.Select(g => g.GameType));
        Assert.Equal(new ActivityGameRow(GameType.Yaniv, 5, 3, 4, 3, 2, 4, 3), report.Games.Single(g => g.GameType == GameType.Yaniv));
        Assert.Equal(new ActivityGameRow(GameType.Wazoo, 0, 0, 0, 0, 0, 0, 0), report.Games.Single(g => g.GameType == GameType.Wazoo));
        Assert.Equal([new NamedCount("New", 3), new NamedCount("Within7Days", 0), new NamedCount("After7Days", 1)], report.Returning);
        Assert.Equal([new NamedCount("direct", 3), new NamedCount("facebook.com", 3)], report.Sources);
        Assert.Equal([new NamedCount("he-IL", 4)], report.Languages);
    }

    // A Wazoo session with Alice alone seated and a game started.
    private static async Task<WazooSession> StartedWazoo(GameSessionStore store)
    {
        var session = (WazooSession)await store.CreateAsync(GameType.Wazoo);
        session.TryTakeSeat("user:alice", Alice, "conn-a", 0, out _);
        session.TryNewGame("user:alice", 0, out _);
        return session;
    }

    // Alice scores every category, which ends the game.
    private static void PlayToTheEnd(WazooSession session)
    {
        foreach (var category in Enum.GetValues<WazooCategory>())
        {
            Assert.True(session.TryRoll("user:alice", 0, out var error), error);
            Assert.True(session.TryScore("user:alice", 0, category, out error), error);
        }

        Assert.True(session.HasFinishedGame);
    }

    // A tic-tac-toe session with Alice and Bob seated and a game started.
    private static async Task<IGameSession> Started(GameSessionStore store)
    {
        var session = await store.CreateAsync(GameType.TicTacToe);
        session.TryTakeSeat("user:alice", Alice, "conn-a", 0, out _);
        session.TryTakeSeat("user:bob", Bob, "conn-b", 1, out _);
        session.TryNewGame("user:alice", 0, out _);
        return session;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // The games-played counts, in memory.
    private sealed class MemoryPlays : IGamePlayRepository
    {
        public List<(DateOnly Day, string GameType, int Count)> Counts { get; } = [];

        public Task AddAsync(GameType type, DateOnly day, int count)
        {
            var index = Counts.FindIndex(c => c.Day == day && c.GameType == type.ToString());
            if (index < 0)
            {
                Counts.Add((day, type.ToString(), count));
            }
            else
            {
                Counts[index] = (day, type.ToString(), Counts[index].Count + count);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GamePlay>> GetAsync(DateOnly from, DateOnly to) =>
            Task.FromResult<IReadOnlyList<GamePlay>>([.. Counts.Where(c => c.Day >= from && c.Day <= to)
                .Select(c => new GamePlay { Day = c.Day, GameType = c.GameType, Count = c.Count })]);
    }

    // The activity counts, in memory; adding fails while Fail is set.
    private sealed class MemoryActivity : IActivityRepository
    {
        public List<(DateOnly Day, string Event, string GameType, string Detail, int Count)> Counts { get; } = [];

        public bool Fail { get; set; }

        public Task AddAsync(DateOnly day, ActivityEvent activity, string gameType, string detail, int count)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The database cannot be reached.");
            }

            var index = Counts.FindIndex(c => c.Day == day && c.Event == activity.ToString() && c.GameType == gameType && c.Detail == detail);
            if (index < 0)
            {
                Counts.Add((day, activity.ToString(), gameType, detail, count));
            }
            else
            {
                Counts[index] = Counts[index] with { Count = Counts[index].Count + count };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ActivityCount>> GetAsync(DateOnly from, DateOnly to) =>
            Task.FromResult<IReadOnlyList<ActivityCount>>([.. Counts.Where(c => c.Day >= from && c.Day <= to)
                .Select(c => new ActivityCount { Day = c.Day, Event = c.Event, GameType = c.GameType, Detail = c.Detail, Count = c.Count })]);
    }

    // The database, in memory.
    private sealed class MemoryRepository : ISessionRepository
    {
        private readonly Dictionary<string, SavedSession> _saved = [];

        public Task<SavedSession?> FindAsync(string id) => Task.FromResult(_saved.TryGetValue(id, out var s) ? Copy(s) : null);

        public Task<bool> ExistsAsync(string id) => Task.FromResult(_saved.ContainsKey(id));

        public Task SaveAsync(SavedSession session)
        {
            _saved[session.Id] = Copy(session);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            _saved.Remove(id);
            return Task.CompletedTask;
        }

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff)
        {
            var old = _saved.Values.Where(s => s.UpdatedAt < cutoff).Select(s => s.Id).ToList();
            old.ForEach(id => _saved.Remove(id));
            return Task.FromResult(old.Count);
        }

        private static SavedSession Copy(SavedSession s) =>
            new() { Id = s.Id, GameType = s.GameType, Version = s.Version, Data = s.Data, UpdatedAt = s.UpdatedAt };
    }
}
