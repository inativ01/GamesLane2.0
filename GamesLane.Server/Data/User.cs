namespace GamesLane.Server.Data;

/// <summary>
/// A saved user: a registered user (username and password) or a user who signs in with an external account.
/// Guests are not saved. Users who are inactive for 30 days are deleted.
/// </summary>
public class User
{
    /// <summary>
    /// The user's ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The username a registered user logs in with, as they typed it. Null for external accounts.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// <see cref="Username"/> in upper case, so usernames are unique regardless of case.
    /// </summary>
    public string? NormalizedUsername { get; set; }

    /// <summary>
    /// A salted hash of a registered user's password. Null for external accounts.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// The name shown to other players (1 to 30 characters).
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// A built-in icon (an emoji), or the address of the user's uploaded icon (<c>/api/icons/{id}</c>).
    /// </summary>
    public required string Icon { get; set; }

    /// <summary>
    /// When the user was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the user last logged in or opened the site, updated at most once a day.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>
    /// The external accounts (e.g. Google) the user signs in with.
    /// </summary>
    public List<ExternalLogin> ExternalLogins { get; set; } = [];
}

/// <summary>
/// Links an external account (a provider such as Google, and the account's ID there) to a user.
/// </summary>
public class ExternalLogin
{
    /// <summary>
    /// The provider, e.g. <c>Google</c>.
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// The account's ID at the provider.
    /// </summary>
    public required string ProviderKey { get; set; }

    /// <summary>
    /// The user the account belongs to.
    /// </summary>
    public Guid UserId { get; set; }
}

/// <summary>
/// An icon uploaded by a user (or copied from their Google picture). A new upload replaces it with a new ID,
/// so its address never changes content and browsers can keep it.
/// </summary>
public class UserIcon
{
    /// <summary>
    /// The icon's ID, used in its address <c>/api/icons/{id}</c>.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The user the icon belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// <c>image/png</c>, <c>image/jpeg</c> or <c>image/webp</c>.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// The image file.
    /// </summary>
    public required byte[] Data { get; set; }

    /// <summary>
    /// When the icon was uploaded.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
