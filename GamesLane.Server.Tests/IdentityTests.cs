using System.Security.Claims;
using GamesLane.Server.Auth;
using GamesLane.Server.Data;
using GamesLane.Server.Hubs;
using GamesLane.Server.Sessions;
using GamesLane.Server.Users;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GamesLane.Server.Tests;

public class IdentityTests
{
    private static readonly Guid SavedId = Guid.Parse("8b0e6a3e-2f1c-4d7a-9c55-0d1b2a3c4e5f");

    [Fact]
    public void Logged_in_users_are_identified_by_their_user_id()
    {
        var user = AuthSetup.CreateUser(SavedId.ToString(), "Ada", "🦊", AuthSetup.Google);

        Assert.Equal($"user:{SavedId}", GameHub.GetUserKey(user, "conn"));
        Assert.Equal(new PlayerProfile("Ada", "🦊"), AuthSetup.GetProfile(user));
        Assert.Equal(AuthSetup.Google, AuthSetup.GetKind(user));
        Assert.Equal(SavedId, AuthSetup.GetSavedUserId(user));
    }

    [Fact]
    public void Guests_are_not_saved_users()
    {
        var guest = AuthSetup.CreateUser("guest:0123456789abcdef", "Bob", "🐼", AuthSetup.GuestKind);

        Assert.Equal("user:guest:0123456789abcdef", GameHub.GetUserKey(guest, "conn"));
        Assert.Null(AuthSetup.GetSavedUserId(guest));
        Assert.Equal(AuthSetup.GuestKind, AuthSetup.GetKind(guest));
    }

    [Fact]
    public void Users_who_are_not_logged_in_are_identified_by_their_connection()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal("connection:conn", GameHub.GetUserKey(anonymous, "conn"));
        Assert.Equal("connection:conn", GameHub.GetUserKey(null, "conn"));
        Assert.Null(AuthSetup.GetProfile(anonymous));
        Assert.Null(AuthSetup.GetKind(anonymous));
    }

    [Theory]
    [InlineData("Ada", "Ada")]
    [InlineData("  Ada Lovelace  ", "Ada Lovelace")]
    [InlineData("123456789012345678901234567890", "123456789012345678901234567890")]
    [InlineData("1234567890123456789012345678901", null)]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Display_names_are_trimmed_and_limited_in_length(string? name, string? expected)
    {
        Assert.Equal(expected, UserRules.NormalizeName(name));
    }

    [Theory]
    [InlineData("Ada Lovelace", "Ada Lovelace")]
    [InlineData("  A very long name from a Google account  ", "A very long name from a Google")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void External_account_names_are_shortened_to_fit(string? name, string? expected)
    {
        Assert.Equal(expected, UserRules.FitName(name));
    }

    [Theory]
    [InlineData("ada", true)]
    [InlineData("Ada_Lovelace_1815", true)]
    [InlineData("abcdefghij0123456789", true)]
    [InlineData("ab", false)]
    [InlineData("abcdefghij01234567890", false)]
    [InlineData("ada lovelace", false)]
    [InlineData("ada-l", false)]
    [InlineData("adä", false)]
    [InlineData(null, false)]
    public void Usernames_are_3_to_20_letters_digits_or_underscores(string? username, bool valid)
    {
        Assert.Equal(valid, UserRules.IsValidUsername(username));
    }

    [Fact]
    public void Usernames_compare_regardless_of_case()
    {
        Assert.Equal(UserRules.NormalizeUsername("Ada_L"), UserRules.NormalizeUsername("aDA_l"));
    }

    [Theory]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData(null, false)]
    public void Passwords_need_at_least_8_characters(string? password, bool valid)
    {
        Assert.Equal(valid, UserRules.IsValidPassword(password));
    }

    [Fact]
    public void Only_built_in_icons_can_be_chosen_directly()
    {
        Assert.True(UserRules.IsBuiltInIcon(UserRules.DefaultIcon));
        Assert.True(UserRules.IsBuiltInIcon("🦊"));
        Assert.False(UserRules.IsBuiltInIcon("<img src=x>"));
        Assert.False(UserRules.IsBuiltInIcon(UserRules.IconPath(SavedId)));
        Assert.False(UserRules.IsBuiltInIcon(null));
    }

    [Theory]
    [InlineData("New", true)]
    [InlineData("Classic", true)]
    [InlineData("classic", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_new_and_classic_card_styles_can_be_chosen(string? style, bool valid)
    {
        Assert.Equal(valid, UserRules.IsCardStyle(style));
    }

    [Fact]
    public void Settings_saved_without_a_card_style_get_the_new_cards()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<UserSettings>("""{"chat":true,"sound":false}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        Assert.Equal(new UserSettings(true, false, UserSettings.NewCards), settings);
        Assert.Equal(UserSettings.NewCards, new User { DisplayName = "Ada", Icon = UserRules.DefaultIcon }.CardStyle);
    }

    [Fact]
    public void New_guests_get_a_3_digit_guest_name_and_a_built_in_icon()
    {
        for (var i = 0; i < 200; i++)
        {
            Assert.Matches("^Guest-[0-9]{3}$", UserRules.RandomGuestName());
            Assert.True(UserRules.IsBuiltInIcon(UserRules.RandomIcon()));
        }
    }

    [Fact]
    public void A_saved_users_login_token_is_read_back_until_it_expires_after_30_days()
    {
        var time = new TestTime { Now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero) };
        var context = TokenContext(time);
        var token = AuthSetup.IssueToken(context, AuthSetup.CreateUser(SavedId.ToString(), "Ada", "🦊", AuthSetup.RegisteredKind));

        Assert.Equal(SavedId.ToString(), AuthSetup.GetUserId(AuthSetup.ReadToken(context, token)));
        Assert.Null(AuthSetup.ReadToken(context, "not-a-token"));
        Assert.Null(AuthSetup.ReadToken(context, null));

        time.Now = time.Now.AddDays(31);
        Assert.Null(AuthSetup.ReadToken(context, token));
    }

    [Fact]
    public void A_guests_login_token_does_not_expire()
    {
        var time = new TestTime { Now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero) };
        var context = TokenContext(time);
        var token = AuthSetup.IssueToken(context, AuthSetup.CreateUser("guest:0123456789abcdef", "Guest-042", "🐼", AuthSetup.GuestKind));

        time.Now = time.Now.AddYears(50);
        var guest = AuthSetup.ReadToken(context, token);
        Assert.Equal("guest:0123456789abcdef", AuthSetup.GetUserId(guest));
        Assert.Equal(new PlayerProfile("Guest-042", "🐼"), AuthSetup.GetProfile(guest));
    }

    private static HttpContext TokenContext(TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(time);
        services.AddAuthentication().AddBearerToken(BearerTokenDefaults.AuthenticationScheme, o => o.BearerTokenExpiration = TimeSpan.FromDays(30));
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50, 0x56 }, "image/webp")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]
    [InlineData(new byte[] { }, null)]
    public void Icon_uploads_must_be_png_jpeg_or_webp(byte[] data, string? expected)
    {
        Assert.Equal(expected, UserRules.DetectImageType(data));
    }

    [Fact]
    public void Url_connection_strings_are_converted()
    {
        var converted = DatabaseSetup.ToNpgsqlConnectionString(
            "postgresql://ada:p%40ss@ep-example.us-east-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require");

        var builder = new NpgsqlConnectionStringBuilder(converted);
        Assert.Equal("ep-example.us-east-2.aws.neon.tech", builder.Host);
        Assert.Equal("neondb", builder.Database);
        Assert.Equal("ada", builder.Username);
        Assert.Equal("p@ss", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
        Assert.Equal(ChannelBinding.Require, builder.ChannelBinding);
    }

    [Fact]
    public void Dotnet_connection_strings_are_used_as_they_are()
    {
        const string connectionString = "Host=localhost;Database=neondb;Username=ada;Password=secret";

        Assert.Equal(connectionString, DatabaseSetup.ToNpgsqlConnectionString(connectionString));
    }

    [Theory]
    [InlineData("  Host=localhost;Database=neondb;Username=ada;Password=secret  \n")]
    [InlineData("\"Host=localhost;Database=neondb;Username=ada;Password=secret\"")]
    [InlineData("'Host=localhost;Database=neondb;Username=ada;Password=secret'")]
    [InlineData("psql 'postgresql://ada:secret@localhost/neondb'")]
    public void Common_copy_mistakes_in_connection_strings_are_removed(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseSetup.ToNpgsqlConnectionString(connectionString));

        Assert.Equal("localhost", builder.Host);
        Assert.Equal("neondb", builder.Database);
        Assert.Equal("ada", builder.Username);
        Assert.Equal("secret", builder.Password);
    }

    [Theory]
    [InlineData("/tictactoe.html?game=ABC123", "/tictactoe.html?game=ABC123")]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    public void Sign_in_only_returns_to_pages_on_this_site(string? returnUrl, string expected)
    {
        Assert.Equal(expected, AuthSetup.SafeReturnUrl(returnUrl));
    }

    [Theory]
    [InlineData("Backgammon TheGang TicTacToe RockPaperScissors", true)]
    [InlineData("TheGang", true)]
    [InlineData("", true)]
    [InlineData("TheGang Chess", false)]
    [InlineData("thegang", false)]
    [InlineData("2", false)]
    [InlineData("TheGang TheGang", false)]
    public void A_game_order_lists_existing_games_once_each(string games, bool valid)
    {
        var list = games.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(valid, UserRules.TryNormalizeGameOrder(list, out var stored, out var error));
        Assert.Equal(valid ? string.Join(",", list) : "", stored);
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void A_saved_game_order_keeps_only_games_that_still_exist()
    {
        Assert.Equal(["Backgammon", "TheGang"], UserRules.ParseGameOrder("Backgammon,Chess,TheGang,Backgammon"));
        Assert.Empty(UserRules.ParseGameOrder(null));
    }
}
