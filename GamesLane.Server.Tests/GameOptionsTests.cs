using GamesLane.Server.Games;
using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Tests;

public class GameOptionsTests
{
    private const string Alice = "user:alice";
    private const string Bob = "user:bob";
    private const string Carol = "user:carol";

    private static Dictionary<string, string> Advanced => new() { [TheGangOptions.Mode] = "Advanced" };

    [Fact]
    public void The_gang_defaults_to_basic_and_other_games_have_no_options()
    {
        Assert.Equal(new Dictionary<string, string> { [TheGangOptions.Mode] = "Basic" }, GameOptions.Defaults(GameType.TheGang));
        Assert.Empty(GameOptions.For(GameType.TicTacToe));
        Assert.Empty(GameOptions.Defaults(GameType.RockPaperScissors));
    }

    [Fact]
    public void Validation_refuses_unknown_options_and_values()
    {
        Assert.False(GameOptions.TryValidate(GameType.TheGang, new Dictionary<string, string> { ["speed"] = "Fast" }, out _, out var error));
        Assert.Equal("This game has no option 'speed'.", error);
        Assert.False(GameOptions.TryValidate(GameType.TheGang, new Dictionary<string, string> { [TheGangOptions.Mode] = "Expert" }, out _, out error));
        Assert.Equal("'Expert' is not a choice for Mode.", error);
    }

    [Fact]
    public void Validation_fills_in_defaults_for_options_left_out()
    {
        Assert.True(GameOptions.TryValidate(GameType.TheGang, new Dictionary<string, string>(), out var normalized, out _));
        Assert.Equal("Basic", normalized[TheGangOptions.Mode]);
    }

    [Fact]
    public void Saved_options_keep_valid_values_and_drop_the_rest()
    {
        var saved = new Dictionary<string, string> { [TheGangOptions.Mode] = "Advanced", ["removed"] = "x" };
        Assert.Equal(Advanced, GameOptions.Normalize(GameType.TheGang, saved));

        var outdated = new Dictionary<string, string> { [TheGangOptions.Mode] = "Expert" };
        Assert.Equal("Basic", GameOptions.Normalize(GameType.TheGang, outdated)[TheGangOptions.Mode]);
    }

    [Fact]
    public void Seated_players_change_options_in_the_lobby_and_everyone_sees_them()
    {
        var session = Seated(new TheGangSession("ABC123"));

        Assert.False(session.TrySetOptions(Carol, Advanced, out var error));
        Assert.Equal("Only seated players can do that.", error);
        Assert.True(session.TrySetOptions(Bob, Advanced, out error), error);

        var state = session.GetPublicState();
        Assert.Equal("Advanced", state.Options[TheGangOptions.Mode]);
        Assert.Equal(TheGangOptions.Mode, Assert.Single(state.OptionList).Key);
    }

    [Fact]
    public void Options_cannot_change_during_a_game_and_the_game_uses_the_chosen_mode()
    {
        var session = Seated(new TheGangSession("ABC123"));
        session.TrySetOptions(Alice, Advanced, out _);
        session.TryNewGame(Alice, 0, out _);

        Assert.Equal(TheGangMode.Advanced, Assert.IsType<TheGangState>(session.GetPublicState().Game).Mode);
        Assert.False(session.TrySetOptions(Alice, new Dictionary<string, string>(), out var error));
        Assert.Equal("Options can only be changed in the lobby.", error);
    }

    [Fact]
    public void Saved_preferences_become_the_sessions_options()
    {
        var session = new TheGangSession("ABC123");
        session.UseSavedOptions(Advanced);
        Assert.Equal("Advanced", session.GetPublicState().Options[TheGangOptions.Mode]);

        session.UseSavedOptions(null);
        Assert.Equal("Basic", session.GetPublicState().Options[TheGangOptions.Mode]);
    }

    [Fact]
    public void Games_without_options_accept_only_no_options()
    {
        var session = new TicTacToeSession("ABC123");
        session.TryTakeSeat(Alice, new PlayerProfile("Alice", "🦊"), "conn-a", 0, out _);

        Assert.True(session.TrySetOptions(Alice, null, out _));
        Assert.False(session.TrySetOptions(Alice, Advanced, out _));
        Assert.Empty(session.GetPublicState().Options);
    }

    private static TheGangSession Seated(TheGangSession session)
    {
        session.TryTakeSeat(Alice, new PlayerProfile("Alice", "🦊"), "conn-a", 0, out _);
        session.TryTakeSeat(Bob, new PlayerProfile("Bob", "🐼"), "conn-b", 1, out _);
        session.TryTakeSeat(Bob, new PlayerProfile("Bob", "🐼"), "conn-b", 2, out _);
        return session;
    }
}
