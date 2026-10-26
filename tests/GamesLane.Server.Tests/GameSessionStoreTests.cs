using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tests;

public class GameSessionStoreTests
{
    [Fact]
    public void Creates_six_character_codes_without_ambiguous_characters()
    {
        var store = new GameSessionStore();

        var id = store.Create().Id;

        Assert.Equal(6, id.Length);
        Assert.All(id, c => Assert.Contains(c, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"));
    }

    [Fact]
    public void Codes_are_unique()
    {
        var store = new GameSessionStore();

        var ids = Enumerable.Range(0, 1000).Select(_ => store.Create().Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Finds_games_regardless_of_case()
    {
        var store = new GameSessionStore();
        var session = store.Create();

        Assert.Same(session, store.Get(session.Id.ToLowerInvariant()));
    }

    [Fact]
    public void Returns_null_for_unknown_games()
    {
        var store = new GameSessionStore();

        Assert.Null(store.Get("NOPE00"));
    }
}
