using System.Security.Claims;
using GamesLane.Server.Auth;
using GamesLane.Server.Hubs;

namespace GamesLane.Server.Tests;

public class IdentityTests
{
    private const string GuestId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Signed_in_users_are_identified_by_provider_and_account()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "12345", ClaimValueTypes.String, "Google")],
            "Cookies"));

        Assert.Equal("user:Google:12345", GameHub.GetUserKey(user, GuestId, "conn"));
    }

    [Fact]
    public void Guests_are_identified_by_their_guest_id()
    {
        Assert.Equal($"guest:{GuestId}", GameHub.GetUserKey(new ClaimsPrincipal(), GuestId, "conn"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("not-hex-but-long-enough-with-dashes")]
    public void Invalid_guest_ids_fall_back_to_the_connection(string? guestId)
    {
        Assert.Equal("connection:conn", GameHub.GetUserKey(null, guestId, "conn"));
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
}
