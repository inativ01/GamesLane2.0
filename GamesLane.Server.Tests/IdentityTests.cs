using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GamesLane.Server.Auth;
using GamesLane.Server.Data;
using GamesLane.Server.Hubs;
using GamesLane.Server.Notifications;
using GamesLane.Server.Sessions;
using GamesLane.Server.Tv;
using GamesLane.Server.Users;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
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
    [InlineData("80351110224678912", "8342729096ea3675442027381ff50dfe", "https://cdn.discordapp.com/avatars/80351110224678912/8342729096ea3675442027381ff50dfe.png?size=128")]
    [InlineData("80351110224678912", null, null)]
    [InlineData("80351110224678912", "", null)]
    [InlineData(null, "8342729096ea3675442027381ff50dfe", null)]
    public void Discord_avatars_are_downloaded_from_the_discord_cdn(string? userId, string? avatarHash, string? expected)
    {
        Assert.Equal(expected, AuthSetup.DiscordAvatarUrl(userId, avatarHash));
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

    // An example verifier and its challenge from RFC 7636 (PKCE), appendix B.
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public void The_iPhone_apps_challenge_is_the_SHA_256_hash_of_its_verifier()
    {
        Assert.Equal(Challenge, AuthSetup.AppSignInChallenge(Verifier));
        Assert.True(AuthSetup.IsAppSignInChallenge(Challenge));
        Assert.False(AuthSetup.IsAppSignInChallenge(Challenge[..^1]));
        Assert.False(AuthSetup.IsAppSignInChallenge(Challenge[..^1] + "+"));
        Assert.False(AuthSetup.IsAppSignInChallenge(null));
    }

    [Fact]
    public void The_iPhone_apps_sign_in_code_brings_back_the_external_account_once_with_its_verifier()
    {
        var account = new ExternalAccount(AuthSetup.Google, "1234567890", "Ada Lovelace", "https://example.com/ada.png");

        var context = TokenContext(new TestTime { Now = DateTimeOffset.UtcNow });
        var code = AuthSetup.CreateAppSignInCode(context, account, Challenge);
        Assert.Null(AuthSetup.ReadAppSignInCode(context, code, "another-verifier-that-only-another-app-knows"));
        Assert.Null(AuthSetup.ReadAppSignInCode(context, code, null));
        Assert.Equal(account, AuthSetup.ReadAppSignInCode(context, code, Verifier));
        Assert.Null(AuthSetup.ReadAppSignInCode(context, code, Verifier));

        var other = TokenContext(new TestTime { Now = DateTimeOffset.UtcNow });
        var otherCode = AuthSetup.CreateAppSignInCode(other, account, Challenge);
        Assert.Null(AuthSetup.ReadAppSignInCode(other, otherCode[..^2] + "xx", Verifier));
        Assert.Null(AuthSetup.ReadAppSignInCode(other, "not-a-code", Verifier));
        Assert.Null(AuthSetup.ReadAppSignInCode(other, "", Verifier));
        Assert.Null(AuthSetup.ReadAppSignInCode(other, null, Verifier));
    }

    [Fact]
    public void The_iPhone_apps_sign_in_code_expires_after_a_few_minutes()
    {
        var time = new TestTime { Now = DateTimeOffset.UtcNow - AuthSetup.AppSignInCodeLifetime - TimeSpan.FromSeconds(1) };
        var context = TokenContext(time);

        var code = AuthSetup.CreateAppSignInCode(context, new ExternalAccount(AuthSetup.Google, "1234567890", null, null), Challenge);

        Assert.Null(AuthSetup.ReadAppSignInCode(context, code, Verifier));
    }

    [Fact]
    public void Used_sign_in_codes_are_forgotten_once_they_expire()
    {
        var time = new TestTime { Now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero) };
        var used = new UsedAppSignInCodes(time);

        Assert.True(used.TryUse("code", time.Now.AddMinutes(5)));
        Assert.False(used.TryUse("code", time.Now.AddMinutes(5)));

        time.Now = time.Now.AddMinutes(6);
        Assert.True(used.TryUse("code", time.Now.AddMinutes(5)));
    }

    // TVs (the TV apps): their login, pairing codes and QR codes, and Watch on TV

    private static readonly Guid OtherId = Guid.Parse("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f");
    private static readonly DateTimeOffset TvStart = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_tvs_login_token_does_not_expire_and_has_no_profile_to_play_with()
    {
        var time = new TestTime { Now = TvStart };
        var context = TokenContext(time);
        var token = AuthSetup.IssueToken(context, AuthSetup.CreateUser("tv:0123", "Living Room", TvEndpoints.Icon, AuthSetup.TvKind));

        time.Now = time.Now.AddYears(50);
        var tv = AuthSetup.ReadToken(context, token);
        Assert.Equal("tv:0123", AuthSetup.GetUserId(tv));
        Assert.Equal(AuthSetup.TvKind, AuthSetup.GetKind(tv));
        Assert.Null(AuthSetup.GetProfile(tv));
        Assert.Null(AuthSetup.GetSavedUserId(tv));
    }

    [Theory]
    [InlineData("  Living \n  Room ", "Living Room")]
    [InlineData("", "TV")]
    [InlineData(null, "TV")]
    [InlineData("A very long name for the TV in the living room", "A very long name for the TV in")]
    public void A_tv_is_named_on_one_short_line(string? name, string expected) =>
        Assert.Equal(expected, TvEndpoints.TvName(name));

    // The standard's example: "HELLO WORLD" in version 1-M, and its 10 error correction codewords.
    [Fact]
    public void A_qr_codes_error_correction_matches_the_standard()
    {
        byte[] data = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];

        Assert.Equal([196, 35, 39, 119, 235, 215, 231, 226, 93, 23], QrCode.ReedSolomonRemainder(data, QrCode.ReedSolomonDivisor(10)));
        Assert.Equal(0b101010000010010, QrCode.FormatBits(0));
        Assert.Equal(0b101000100100101, QrCode.FormatBits(1));
    }

    [Fact]
    public void A_pairing_address_makes_the_smallest_qr_code_with_its_fixed_patterns()
    {
        var qr = QrCode.Encode("https://gameslane.onrender.com/?tv=ABCDEF")!;

        Assert.Equal(3, qr.Version);
        Assert.Equal(29, qr.Size);
        foreach (var (x, y) in new[] { (0, 0), (qr.Size - 7, 0), (0, qr.Size - 7) })
        {
            // A finder: a dark ring, a light ring, a dark 3 × 3 middle, and a light separator around it.
            Assert.True(qr[x, y] && qr[x + 6, y + 6] && qr[x + 2, y + 2] && qr[x + 4, y + 3]);
            Assert.False(qr[x + 1, y + 1] || qr[x + 5, y + 1]);
        }

        Assert.False(qr[7, 7]);
        Assert.True(qr[8, qr.Size - 8]);
        Assert.All(Enumerable.Range(8, qr.Size - 16), i => Assert.Equal(i % 2 == 0, qr[i, 6]));
        // The alignment pattern of version 3, at (22, 22).
        Assert.True(qr[22, 22] && qr[20, 20] && !qr[21, 21]);
        // The format bits, around the top left finder, then split between the other two.
        var bits = QrCode.FormatBits(qr.Mask);
        Assert.All(Enumerable.Range(0, 6), i => Assert.Equal(((bits >> i) & 1) != 0, qr[8, i]));
        Assert.All(Enumerable.Range(0, 8), i => Assert.Equal(((bits >> i) & 1) != 0, qr[qr.Size - 1 - i, 8]));
    }

    [Fact]
    public void Qr_codes_hold_up_to_213_bytes_and_the_endpoint_refuses_longer_texts()
    {
        Assert.Equal(1, QrCode.Encode("GamesLane")!.Version);
        Assert.Equal(10, QrCode.Encode(new string('a', 200))!.Version);
        Assert.Equal(57, QrCode.Encode(new string('a', 213))!.Size);
        Assert.Null(QrCode.Encode(new string('a', 214)));

        var svg = TvEndpoints.QrSvg("https://gameslane.onrender.com/?tv=ABCDEF")!;
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 37 37\"", svg);
        Assert.Null(TvEndpoints.QrSvg(""));
        Assert.Null(TvEndpoints.QrSvg(null));
        Assert.Null(TvEndpoints.QrSvg(new string('a', TvEndpoints.MaxQrTextLength + 1)));
    }

    [Fact]
    public void A_tv_pairing_code_works_once_while_the_tv_is_open_and_until_it_expires()
    {
        var time = new TestTime { Now = TvStart };
        var links = new TvLinks(time);
        var code = links.ConnectTv("tv:1", "Living Room", "tv-conn").Code;

        Assert.Equal(TvLinks.CodeLength, code.Length);
        Assert.Null(links.RedeemCode("WRONG1"));
        Assert.Null(links.RedeemCode(null));
        Assert.Equal(("tv:1", "Living Room"), links.RedeemCode($" {code[..3].ToLowerInvariant()}-{code[3..]} ")!.Value);
        Assert.Null(links.RedeemCode(code));

        var next = links.Find("tv:1")!.Code;
        Assert.NotEqual(code, next);
        time.Now += TvLinks.CodeLifetime;
        Assert.Null(links.RedeemCode(next));
        Assert.NotEqual(next, links.Find("tv:1")!.Code);

        var shown = links.Find("tv:1")!.Code;
        links.Disconnect("tv-conn", time.Now);
        Assert.Null(links.RedeemCode(shown));
        Assert.Null(links.Find("tv:1"));
    }

    [Fact]
    public void A_tv_shows_one_users_games_at_a_time_and_a_users_games_show_on_one_tv_at_a_time()
    {
        var links = new TvLinks(new TestTime { Now = TvStart });
        links.ConnectTv("tv:1", "Living Room", "tv1");
        links.ConnectTv("tv:2", "Kitchen", "tv2");
        Assert.Equal(TvAvailability.Offline, links.AvailabilityFor("tv:3", SavedId));
        Assert.False(links.TryLink("tv:3", Viewer(SavedId), "ada", out _, out var error));
        Assert.Equal("This TV is not open. Open GamesLane on the TV first.", error);

        Assert.True(links.TryLink("tv:1", Viewer(SavedId, "ABC123"), "ada", out var change, out error), error);
        Assert.Equal(["tv:1"], change.TvIds);
        Assert.Equal("ABC123", links.Find("tv:1")!.Link!.SessionId);
        Assert.Equal(TvAvailability.Watching, links.AvailabilityFor("tv:1", SavedId));
        Assert.Equal(TvAvailability.InUse, links.AvailabilityFor("tv:1", OtherId));
        Assert.Equal(TvAvailability.Available, links.AvailabilityFor("tv:2", OtherId));
        Assert.False(links.TryLink("tv:1", Viewer(OtherId), "ben", out _, out error));
        Assert.Equal("This TV is showing another player's games.", error);

        Assert.True(links.TryLink("tv:2", Viewer(SavedId), "ada-tab2", out change, out error), error);
        Assert.Equal(["tv:2", "tv:1"], change.TvIds);
        Assert.Equal(["ada"], change.EndedConnections);
        Assert.Null(links.Find("tv:1")!.Link);
        Assert.Equal(TvAvailability.Available, links.AvailabilityFor("tv:1", OtherId));

        change = links.Unlink(SavedId);
        Assert.Equal(["tv:2"], change.TvIds);
        Assert.Equal(["ada-tab2"], change.EndedConnections);
        Assert.Same(TvChange.None, links.Unlink(SavedId));
    }

    [Fact]
    public void Watch_on_tv_follows_the_user_from_page_to_page_and_ends_once_the_user_or_the_tv_is_gone()
    {
        var time = new TestTime { Now = TvStart };
        var links = new TvLinks(time);
        links.ConnectTv("tv:1", "Living Room", "tv1");
        Assert.True(links.TryLink("tv:1", Viewer(SavedId), "page1", out _, out var error), error);

        // Moving to another page: the old page's connection closes, and the new page links again with its game.
        links.Disconnect("page1", time.Now);
        time.Now += TvLinks.ReconnectWindow - TimeSpan.FromSeconds(1);
        Assert.Empty(links.Expire(time.Now).TvIds);
        Assert.True(links.TryLink("tv:1", Viewer(SavedId, "ABC123"), "page2", out _, out error), error);
        time.Now += TvLinks.ReconnectWindow;
        Assert.Empty(links.Expire(time.Now).TvIds);
        Assert.Equal("ABC123", links.Find("tv:1")!.Link!.SessionId);

        // All the user's pages closed: the TV goes back to waiting.
        links.Disconnect("page2", time.Now);
        time.Now += TvLinks.ReconnectWindow;
        Assert.Equal(["tv:1"], links.Expire(time.Now).TvIds);
        Assert.Null(links.Find("tv:1")!.Link);

        // The TV closed: the user's pages are told, and the TV is forgotten.
        Assert.True(links.TryLink("tv:1", Viewer(SavedId), "page3", out _, out error), error);
        links.Disconnect("tv1", time.Now);
        Assert.Equal(TvAvailability.Offline, links.AvailabilityFor("tv:1", SavedId));
        time.Now += TvLinks.ReconnectWindow;
        Assert.Equal(["page3"], links.Expire(time.Now).EndedConnections);
        Assert.Null(links.Find("tv:1"));
        Assert.False(links.TryLink("tv:1", Viewer(SavedId), "page3", out _, out _));
    }

    [Fact]
    public async Task Pairing_and_watching_send_the_tv_its_status_and_unpairing_ends_watch_on_tv()
    {
        var time = new TestTime { Now = TvStart };
        var pairings = new MemoryTvPairings { Names = { [SavedId] = "Ada" } };
        var hub = new RecordingHub();
        var texts = new NotificationTexts(new Dictionary<string, IReadOnlyDictionary<string, string>> { ["he"] = new Dictionary<string, string>() });
        var tvs = new TvService(new TvLinks(time), pairings, new OneSessionStore(new TicTacToeSession("ABC123")), hub, texts, time);

        var status = await tvs.ConnectAsync("tv:1", "Living Room", "tv1");
        Assert.Empty(status.PairedWith);
        Assert.Null(status.Viewer);
        Assert.Null(await tvs.PairAsync(SavedId, "WRONG1"));
        Assert.Equal("Living Room", await tvs.PairAsync(SavedId, status.Code));
        var sent = Assert.Single(hub.Sent);
        Assert.Equal((TvService.TvUpdated, "tv1"), (sent.Method, Assert.Single(sent.Connections)));
        Assert.Equal(["Ada"], ((TvStatus)sent.Args[0]!).PairedWith);
        Assert.Equal([new PairedTv("tv:1", "Living Room", TvAvailability.Available)], await tvs.ListAsync(SavedId));

        var ada = new PlayerProfile("Ada", "🦊");
        Assert.Equal("This TV is not paired with your account.", await tvs.WatchAsync(OtherId, new("Ben", "🐼"), "he", "tv:1", null, "ben"));
        time.Now += TimeSpan.FromDays(1);
        Assert.Null(await tvs.WatchAsync(SavedId, ada, "he", "tv:1", "abc123", "ada"));
        Assert.Equal(new TvViewer("Ada", "🦊", "he", "ABC123", GameType.TicTacToe), ((TvStatus)hub.Sent[^1].Args[0]!).Viewer);
        Assert.Equal(time.Now, pairings.Pairings.Single().LastActiveAt);
        Assert.Equal(TvAvailability.Watching, (await tvs.ListAsync(SavedId)).Single().Status);
        Assert.Null(await tvs.WatchAsync(SavedId, ada, "xx", "tv:1", null, "ada"));
        Assert.Equal(new TvViewer("Ada", "🦊", "en", null, null), ((TvStatus)hub.Sent[^1].Args[0]!).Viewer);

        await tvs.UnpairAsync(SavedId, "tv:1");
        Assert.Empty(await tvs.ListAsync(SavedId));
        Assert.Contains(hub.Sent, s => s.Method == TvService.WatchOnTvEnded && s.Connections.SequenceEqual(["ada"]));
        var last = (TvStatus)hub.Sent[^1].Args[0]!;
        Assert.Null(last.Viewer);
        Assert.Empty(last.PairedWith);
    }

    private static TvLink Viewer(Guid userId, string? sessionId = null) =>
        new(userId, "Ada", "🦊", "en", sessionId, sessionId is null ? null : GameType.TicTacToe);

    // Deleting a user (Delete account, and the 30-day cleanup) deletes their external accounts, icons, game options,
    // saved games and paired TVs.
    [Fact]
    public void Everything_linked_to_a_user_is_deleted_with_them()
    {
        var options = new DbContextOptionsBuilder<GamesLaneDbContext>().UseNpgsql("Host=localhost").Options;
        using var db = new GamesLaneDbContext(options);

        var linked = db.Model.FindEntityType(typeof(User))!.GetReferencingForeignKeys().ToList();

        Assert.Equal(
            new[] { typeof(ExternalLogin), typeof(SavedGame), typeof(TvPairing), typeof(UserGameOptions), typeof(UserIcon) }.Select(t => t.Name),
            linked.Select(key => key.DeclaringEntityType.ClrType.Name).Order());
        Assert.All(linked, key => Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior));
    }

    // The database refuses to delete a session that a user saved.
    [Fact]
    public void A_saved_game_keeps_its_session_from_being_deleted()
    {
        var options = new DbContextOptionsBuilder<GamesLaneDbContext>().UseNpgsql("Host=localhost").Options;
        using var db = new GamesLaneDbContext(options);

        var key = Assert.Single(db.Model.FindEntityType(typeof(SavedSession))!.GetReferencingForeignKeys());

        Assert.Equal(typeof(SavedGame), key.DeclaringEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
    }

    // Every user, existing (the database's default for the column) and new, may keep 2 saved games.
    [Fact]
    public void Every_user_has_a_saved_games_limit_of_2()
    {
        var options = new DbContextOptionsBuilder<GamesLaneDbContext>().UseNpgsql("Host=localhost").Options;
        using var db = new GamesLaneDbContext(options);

        var limit = db.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.SavedGameLimit))!;

        Assert.Equal(2, limit.GetDefaultValue());
        Assert.Equal(2, new User { DisplayName = "Ada", Icon = "🦊" }.SavedGameLimit);
    }

    [Fact]
    public void An_external_sign_in_returns_to_the_iPhone_app_with_its_code_or_an_error()
    {
        Assert.Equal("/auth/app-return?code=a%2Bb%2Fc", AuthSetup.AppReturnUrl("a+b/c", null));
        Assert.Equal("/auth/app-return?error=failed", AuthSetup.AppReturnUrl(null, "failed"));
    }

    [Fact]
    public async Task The_end_of_an_external_sign_in_redirects_the_iPhone_app_sign_in_window()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddGamesLaneAuth();
        builder.Services.AddScoped<IUserService, UserService>();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.MapExternalSignInEndpoints();
        await app.StartAsync();

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.First()) };
        var response = await client.GetAsync(AuthSetup.AppSignInPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/app-return?error=failed", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("""{"name":{"firstName":"Ada","lastName":"Lovelace"},"email":"ada@example.com"}""", "Ada Lovelace")]
    [InlineData("""{"name":{"firstName":"Ada"}}""", "Ada")]
    [InlineData("""{"name":{"firstName":" ","lastName":""}}""", null)]
    [InlineData("""{"email":"ada@example.com"}""", null)]
    [InlineData("not json", null)]
    [InlineData(null, null)]
    public void Sign_in_with_Apple_on_the_website_takes_the_name_Apple_posts_the_first_time(string? user, string? expected)
    {
        Assert.Equal(expected, AuthSetup.AppleUserName(user));
    }

    [Fact]
    public void The_site_association_lets_join_links_and_the_sign_in_window_open_the_iPhone_app()
    {
        var apple = new AppleSettings("7Z69JFQYCV", "com.gameslane.app", "com.gameslane.web", null, null);

        var json = System.Text.Json.JsonSerializer.Serialize(apple.SiteAssociation());

        Assert.Equal(
            """{"applinks":{"details":[{"appIDs":["7Z69JFQYCV.com.gameslane.app"],"components":[{"/":"/join/*"}]}]},"webcredentials":{"apps":["7Z69JFQYCV.com.gameslane.app"]}}""",
            json);
        Assert.False(apple.CanSignInOnWebsite);
        Assert.True((apple with { KeyId = "ABC123DEFG", PrivateKey = "-----BEGIN PRIVATE KEY-----" }).CanSignInOnWebsite);
    }

    [Fact]
    public void The_asset_links_let_join_links_and_the_sign_in_tab_open_the_Android_app_once_its_certificates_are_set()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Android:PackageName"] = "com.gameslane.app",
            ["Android:CertificateFingerprints"] = "aa:bb:cc, DD:EE:FF,aa:bb:cc",
        }).Build();

        var android = AndroidSettings.From(configuration);
        var json = System.Text.Json.JsonSerializer.Serialize(android.AssetLinks());

        Assert.Equal(
            """[{"relation":["delegate_permission/common.handle_all_urls","delegate_permission/common.get_login_creds"],"target":{"namespace":"android_app","package_name":"com.gameslane.app","sha256_cert_fingerprints":["AA:BB:CC","DD:EE:FF"]}}]""",
            json);
        Assert.Empty((android with { CertificateFingerprints = [] }).AssetLinks());
    }

    // Sign in with Apple in the iPhone app: an identity token signs in only if Apple signed it for the app, it has not
    // expired, it carries the hash of the page's nonce, and it was not used before.
    private const string AppleSubject = "001234.abcdef0123456789.1234";
    private static readonly AppleSettings Apple = new("7Z69JFQYCV", "com.gameslane.app", "com.gameslane.web", null, null);
    private readonly ECDsaSecurityKey _appleKey = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "apple-key" };

    [Fact]
    public async Task An_identity_token_Apple_signed_for_the_app_and_the_nonce_signs_in_once()
    {
        var tokens = AppleTokens();
        var token = AppleToken();

        Assert.Equal(AppleSubject, await tokens.ValidateAsync(token, Verifier));
        Assert.Null(await tokens.ValidateAsync(token, Verifier));
    }

    [Fact]
    public async Task An_identity_token_for_another_nonce_app_or_issuer_or_expired_is_refused()
    {
        Assert.Null(await AppleTokens().ValidateAsync(AppleToken(), "another-nonce"));
        Assert.Null(await AppleTokens().ValidateAsync(AppleToken(), null));
        Assert.Null(await AppleTokens().ValidateAsync(AppleToken(audience: "com.example.other"), Verifier));
        Assert.Null(await AppleTokens().ValidateAsync(AppleToken(issuer: "https://evil.example"), Verifier));
        Assert.Null(await AppleTokens().ValidateAsync(AppleToken(expires: DateTime.UtcNow.AddMinutes(-10)), Verifier));
    }

    [Fact]
    public async Task An_identity_token_not_signed_by_Apple_is_refused()
    {
        var otherKey = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "apple-key" };

        Assert.Null(await AppleTokens().ValidateAsync(AppleToken(key: otherKey), Verifier));
        Assert.Null(await AppleTokens().ValidateAsync("not-a-token", Verifier));
        Assert.Null(await AppleTokens().ValidateAsync(null, Verifier));
    }

    [Fact]
    public async Task Apples_keys_are_fetched_again_once_when_a_token_has_a_new_key()
    {
        var oldKey = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "old-key" };
        var keys = new AppleKeys(oldKey, _appleKey);
        var tokens = new AppleIdentityTokens(Apple, new UsedAppSignInCodes(TimeProvider.System), keys);

        Assert.Equal(AppleSubject, await tokens.ValidateAsync(AppleToken(), Verifier));
        Assert.Equal(1, keys.Refreshes);
    }

    [Fact]
    public async Task An_identity_token_is_refused_when_Apples_keys_cannot_be_fetched()
    {
        var tokens = new AppleIdentityTokens(Apple, new UsedAppSignInCodes(TimeProvider.System), new AppleKeys(null));

        Assert.Null(await tokens.ValidateAsync(AppleToken(), Verifier));
    }

    [Fact]
    public void The_client_secret_for_Apple_is_signed_with_the_teams_key_for_the_client()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var apple = Apple with { KeyId = "ABC123DEFG", PrivateKey = ecdsa.ExportPkcs8PrivateKeyPem() };
        var tokens = new AppleAccountTokens(apple, null!, new EphemeralDataProtectionProvider(), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AppleAccountTokens>.Instance);

        var secret = new JsonWebToken(tokens.ClientSecret("com.gameslane.app"));

        Assert.Equal(("ABC123DEFG", "ES256"), (secret.Kid, secret.Alg));
        Assert.Equal(("7Z69JFQYCV", "com.gameslane.app"), (secret.Issuer, secret.Subject));
        Assert.Equal([AppleIdentityTokens.Issuer], secret.Audiences);
        Assert.True(secret.ValidTo > DateTime.UtcNow && secret.ValidTo <= DateTime.UtcNow.AddMinutes(6));
        Assert.True(new JsonWebTokenHandler().ValidateTokenAsync(secret.EncodedToken, new TokenValidationParameters
        {
            ValidIssuer = "7Z69JFQYCV",
            ValidAudience = AppleIdentityTokens.Issuer,
            IssuerSigningKey = new ECDsaSecurityKey(ecdsa),
        }).Result.IsValid);
    }

    [Fact]
    public async Task Deleting_an_account_revokes_only_kept_Apple_tokens_and_without_a_key_reports_it()
    {
        var tokens = new AppleAccountTokens(Apple, null!, new EphemeralDataProtectionProvider(), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AppleAccountTokens>.Instance);
        var kept = tokens.Protect(Apple.BundleId, "refresh-token");

        Assert.NotEqual("refresh-token", kept.RefreshToken);
        Assert.True(await tokens.RevokeAsync(new ExternalLogin { Provider = AuthSetup.Google, ProviderKey = "1" }));
        Assert.True(await tokens.RevokeAsync(new ExternalLogin { Provider = AuthSetup.Apple, ProviderKey = "1" }));
        Assert.False(await tokens.RevokeAsync(new ExternalLogin
        {
            Provider = AuthSetup.Apple, ProviderKey = "1", TokenClientId = kept.ClientId, RefreshToken = kept.RefreshToken,
        }));
        Assert.Null(await tokens.RedeemAppCodeAsync("code"));
    }

    // Apple's keys: the old ones until refreshed, then the new ones; or failing to be fetched (null).
    private sealed class AppleKeys(SecurityKey? before, SecurityKey? after = null) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Refreshes { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var key = Refreshes == 0 ? before : after;
            if (key is null)
            {
                throw new InvalidOperationException("Apple could not be reached.");
            }

            var configuration = new OpenIdConnectConfiguration { Issuer = AppleIdentityTokens.Issuer };
            configuration.SigningKeys.Add(key);
            return Task.FromResult(configuration);
        }

        public void RequestRefresh() => Refreshes++;
    }

    private AppleIdentityTokens AppleTokens()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = AppleIdentityTokens.Issuer };
        configuration.SigningKeys.Add(_appleKey);
        return new AppleIdentityTokens(Apple, new UsedAppSignInCodes(TimeProvider.System),
            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
    }

    private string AppleToken(string? audience = null, string? issuer = null, DateTime? expires = null, SecurityKey? key = null)
    {
        var until = expires ?? DateTime.UtcNow.AddMinutes(10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? AppleIdentityTokens.Issuer,
            Audience = audience ?? Apple.BundleId,
            Subject = new ClaimsIdentity([new Claim("sub", AppleSubject)]),
            Claims = new Dictionary<string, object> { ["nonce"] = Challenge },
            NotBefore = until.AddMinutes(-20),
            IssuedAt = until.AddMinutes(-20),
            Expires = until,
            SigningCredentials = new SigningCredentials(key ?? _appleKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    private static HttpContext TokenContext(TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(time);
        services.AddSingleton(new UsedAppSignInCodes(time));
        services.AddAuthentication().AddBearerToken(BearerTokenDefaults.AuthenticationScheme, o => o.BearerTokenExpiration = TimeSpan.FromDays(30));
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // The TV pairings, in memory, with the paired users' display names from Names.
    private sealed class MemoryTvPairings : ITvPairingRepository
    {
        public List<TvPairing> Pairings { get; } = [];

        public Dictionary<Guid, string> Names { get; } = [];

        public Task SaveAsync(Guid userId, string tvId, string tvName, DateTimeOffset now)
        {
            Pairings.RemoveAll(p => p.UserId == userId && p.TvId == tvId);
            Pairings.Add(new TvPairing { UserId = userId, TvId = tvId, TvName = tvName, PairedAt = now, LastActiveAt = now });
            return Task.CompletedTask;
        }

        public Task<bool> TouchAsync(Guid userId, string tvId, DateTimeOffset now)
        {
            var pairing = Pairings.Find(p => p.UserId == userId && p.TvId == tvId);
            pairing?.LastActiveAt = now;
            return Task.FromResult(pairing is not null);
        }

        public Task<IReadOnlyList<TvPairing>> GetForUserAsync(Guid userId) =>
            Task.FromResult<IReadOnlyList<TvPairing>>([.. Pairings.Where(p => p.UserId == userId)]);

        public Task<IReadOnlyList<string>> GetUserNamesAsync(string tvId) =>
            Task.FromResult<IReadOnlyList<string>>([.. Pairings.Where(p => p.TvId == tvId).Select(p => Names[p.UserId]).Order()]);

        public Task DeleteAsync(Guid userId, string tvId)
        {
            Pairings.RemoveAll(p => p.UserId == userId && p.TvId == tvId);
            return Task.CompletedTask;
        }

        public Task<int> DeleteUnusedAsync(DateTimeOffset before) => Task.FromResult(Pairings.RemoveAll(p => p.LastActiveAt < before));
    }

    // A session store that only finds one session.
    private sealed class OneSessionStore(IGameSession session) : IGameSessionStore
    {
        public Task<IGameSession?> GetAsync(string sessionId) =>
            Task.FromResult(string.Equals(sessionId, session.Id, StringComparison.OrdinalIgnoreCase) ? session : null);

        public Task<IGameSession> CreateAsync(GameType type, Action<IGameSession>? prepare = null) => throw new NotSupportedException();

        public Task<IGameSession?> PeekAsync(string sessionId) => GetAsync(sessionId);

        public Task SaveAsync(IGameSession session) => Task.CompletedTask;

        public Task UnloadAsync(IGameSession session) => Task.CompletedTask;

        public IEnumerable<IGameSession> All => [session];

        public DateTimeOffset LastActivity(IGameSession session) => DateTimeOffset.MinValue;
    }

    // The hub's messages to connections, recorded in the order sent.
    private sealed class RecordingHub : IHubContext<GameHub>
    {
        public List<(IReadOnlyList<string> Connections, string Method, object?[] Args)> Sent { get; } = [];

        public IHubClients Clients => new RecordingClients(this);

        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class RecordingClients(RecordingHub hub) : IHubClients
        {
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new Proxy(hub, connectionIds);

            public IClientProxy Client(string connectionId) => new Proxy(hub, [connectionId]);

            public IClientProxy All => throw new NotSupportedException();

            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

            public IClientProxy Group(string groupName) => throw new NotSupportedException();

            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

            public IClientProxy User(string userId) => throw new NotSupportedException();

            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class Proxy(RecordingHub hub, IReadOnlyList<string> connections) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                hub.Sent.Add((connections, method, args));
                return Task.CompletedTask;
            }
        }
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
    [InlineData("Backgammon PokerHeist TicTacToe Wazoo", true)]
    [InlineData("PokerHeist", true)]
    [InlineData("", true)]
    [InlineData("PokerHeist Chess", false)]
    [InlineData("pokerheist", false)]
    [InlineData("2", false)]
    [InlineData("PokerHeist PokerHeist", false)]
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
        Assert.Equal(["Backgammon", "PokerHeist"], UserRules.ParseGameOrder("Backgammon,Chess,PokerHeist,Backgammon"));
        Assert.Empty(UserRules.ParseGameOrder(null));
    }

    [Fact]
    public void Only_saved_users_listed_in_the_admin_setting_are_admins()
    {
        var other = Guid.Parse("1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b");
        var admins = AdminUsers.From(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:UserIds:0"] = SavedId.ToString(),
                ["Admin:UserIds:1"] = "not-a-guid",
            })
            .Build());

        Assert.Equal([SavedId], admins.Ids);
        Assert.True(admins.IsAdmin(AuthSetup.CreateUser(SavedId.ToString(), "Ada", "🦊", AuthSetup.RegisteredKind)));
        Assert.False(admins.IsAdmin(AuthSetup.CreateUser(other.ToString(), "Bob", "🐼", AuthSetup.Google)));
        Assert.False(admins.IsAdmin(AuthSetup.CreateUser($"guest:{SavedId:N}", "Guest", "🐼", AuthSetup.GuestKind)));
        Assert.False(admins.IsAdmin(null));
        Assert.Empty(AdminUsers.From(new ConfigurationBuilder().Build()).Ids);
    }

    [Fact]
    public void Only_admins_can_start_Spades_and_Dominoes()
    {
        var admins = new AdminUsers([SavedId]);
        var admin = AuthSetup.CreateUser(SavedId.ToString(), "Ada", "🦊", AuthSetup.RegisteredKind);
        var other = AuthSetup.CreateUser(Guid.Parse("1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b").ToString(), "Bob", "🐼", AuthSetup.Google);
        var guest = AuthSetup.CreateUser($"guest:{SavedId:N}", "Guest", "🐼", AuthSetup.GuestKind);

        Assert.Equal([GameType.Spades, GameType.Dominoes], AdminUsers.AdminOnlyGames.Order());
        foreach (var gameType in AdminUsers.AdminOnlyGames)
        {
            Assert.True(admins.CanStart(admin, gameType));
            Assert.False(admins.CanStart(other, gameType));
            Assert.False(admins.CanStart(guest, gameType));
        }

        Assert.True(admins.CanStart(other, GameType.FinalCard));
        Assert.True(admins.CanStart(guest, GameType.Hearts));
    }

    // accountReady runs completeExternalSignIn as common.js loads; until its first await, it may read only constants
    // declared before accountReady (others are not initialized yet, and reading them throws).
    [Fact]
    public void Finishing_a_sign_in_reads_only_constants_declared_before_the_page_loads_the_account()
    {
        var script = File.ReadAllText(Path.Combine(FindWwwroot(), "common.js"));
        var accountReady = script.IndexOf("const accountReady = ", StringComparison.Ordinal);
        var start = script.IndexOf("async function completeExternalSignIn()", StringComparison.Ordinal);
        var beforeAwait = script[start..script.IndexOf("await ", start, StringComparison.Ordinal)];
        var constants = Regex.Matches(beforeAwait, @"\b[A-Z][A-Z0-9_]+_KEY\b").Select(match => match.Value).Distinct().ToList();

        Assert.True(accountReady > 0 && start > 0);
        Assert.NotEmpty(constants);
        Assert.All(constants, name =>
        {
            var declared = script.IndexOf($"const {name} = ", StringComparison.Ordinal);
            Assert.True(declared >= 0 && declared < accountReady, $"{name} is declared after accountReady.");
        });
    }

    private static string FindWwwroot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var wwwroot = Path.Combine(dir.FullName, "GamesLane.Server", "wwwroot");
            if (Directory.Exists(wwwroot))
            {
                return wwwroot;
            }
        }
        throw new DirectoryNotFoundException("GamesLane.Server/wwwroot was not found.");
    }
}
