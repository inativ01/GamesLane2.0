using GamesLane.Server.Data;
using GamesLane.Server.Sessions;
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
        Assert.IsType<RockPaperScissorsSession>(await store.CreateAsync(GameType.RockPaperScissors));
        Assert.IsType<BackgammonSession>(await store.CreateAsync(GameType.Backgammon));
        Assert.Equal("/backgammon.html", GameSessionStore.PageFor(GameType.Backgammon));
        Assert.IsType<RichManPoorManSession>(await store.CreateAsync(GameType.RichManPoorMan));
        Assert.Equal("/rich-man-poor-man.html", GameSessionStore.PageFor(GameType.RichManPoorMan));
        Assert.IsType<YatzeeSession>(await store.CreateAsync(GameType.Yatzee));
        Assert.Equal("/yatzee.html", GameSessionStore.PageFor(GameType.Yatzee));
        Assert.IsType<UnoSession>(await store.CreateAsync(GameType.Uno));
        Assert.Equal("/uno.html", GameSessionStore.PageFor(GameType.Uno));
        Assert.IsType<YanivSession>(await store.CreateAsync(GameType.Yaniv));
        Assert.Equal("/yaniv.html", GameSessionStore.PageFor(GameType.Yaniv));
    }

    [Fact]
    public async Task Finds_games_regardless_of_case_and_returns_null_for_unknown_ones()
    {
        var store = new GameSessionStore(new MemoryRepository());
        var session = await store.CreateAsync(GameType.RockPaperScissors);

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
