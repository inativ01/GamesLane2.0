using System.Security.Claims;
using GamesLane.Server.Auth;
using GamesLane.Server.Hubs;

namespace GamesLane.Server.Tests;

public class IdentityTests
{
    [Fact]
    public void Logged_in_users_are_identified_by_their_user_id()
    {
        var user = AuthSetup.CreateUser("google:12345", "Ada", AuthSetup.Google);

        Assert.Equal("user:google:12345", GameHub.GetUserKey(user, "conn"));
        Assert.Equal("Ada", AuthSetup.GetUserName(user));
    }

    [Fact]
    public void Users_who_are_not_logged_in_are_identified_by_their_connection()
    {
        Assert.Equal("connection:conn", GameHub.GetUserKey(new ClaimsPrincipal(new ClaimsIdentity()), "conn"));
        Assert.Equal("connection:conn", GameHub.GetUserKey(null, "conn"));
        Assert.Null(AuthSetup.GetUserName(new ClaimsPrincipal(new ClaimsIdentity())));
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
        Assert.Equal(expected, AuthSetup.NormalizeName(name));
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
