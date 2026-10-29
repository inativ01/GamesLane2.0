using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tests;

public class GameSessionStoreTests
{
    [Fact]
    public void Creates_six_character_codes_without_ambiguous_characters()
    {
        var store = new GameSessionStore();

        var id = store.Create(GameType.TicTacToe).Id;

        Assert.Equal(6, id.Length);
        Assert.All(id, c => Assert.Contains(c, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"));
    }

    [Fact]
    public void Codes_are_unique()
    {
        var store = new GameSessionStore();

        var ids = Enumerable.Range(0, 1000).Select(_ => store.Create(GameType.TicTacToe).Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Creates_a_session_of_the_requested_game_type()
    {
        var store = new GameSessionStore();

        Assert.IsType<TicTacToeSession>(store.Create(GameType.TicTacToe));
        Assert.IsType<RockPaperScissorsSession>(store.Create(GameType.RockPaperScissors));
    }

    [Fact]
    public void Finds_games_regardless_of_case()
    {
        var store = new GameSessionStore();
        var session = store.Create(GameType.RockPaperScissors);

        Assert.Same(session, store.Get(session.Id.ToLowerInvariant()));
    }

    [Fact]
    public void Returns_null_for_unknown_games()
    {
        var store = new GameSessionStore();

        Assert.Null(store.Get("NOPE00"));
    }

    [Fact]
    public void Removed_sessions_can_no_longer_be_found()
    {
        var store = new GameSessionStore();
        var session = store.Create(GameType.TicTacToe);

        store.Remove(session.Id);

        Assert.Null(store.Get(session.Id));
        Assert.DoesNotContain(session, store.All);
    }
}
