using GamesLane.Server.Data;

namespace GamesLane.Server.Users;

/// <summary>
/// The result of creating or changing a user: the user, or an error message to show.
/// </summary>
public record UserResult(User? User, string? Error)
{
    /// <summary>
    /// A successful result.
    /// </summary>
    public static UserResult Ok(User user) => new(user, null);

    /// <summary>
    /// A refused change, with the reason.
    /// </summary>
    public static UserResult Fail(string error) => new(null, error);
}

/// <summary>
/// The result of signing in with an external account. <see cref="IsNew"/> is true the first time.
/// </summary>
public record ExternalSignInResult(User User, bool IsNew);

/// <summary>
/// Saved users: registration, logging in, external accounts, profiles and icons.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Creates a registered user with a unique username and a password.
    /// </summary>
    Task<UserResult> RegisterAsync(string? username, string? password, string? displayName, string? icon);

    /// <summary>
    /// The registered user with this username and password, or null if they do not match.
    /// </summary>
    Task<User?> LogInAsync(string? username, string? password);

    /// <summary>
    /// The user linked to an external account, created on first sign-in with the account's name and picture as defaults.
    /// </summary>
    Task<ExternalSignInResult> SignInExternalAsync(string provider, string providerKey, string? name, string? pictureUrl);

    /// <summary>
    /// The user with this ID, or null if they no longer exist. Records the user as active.
    /// </summary>
    Task<User?> GetActiveUserAsync(Guid userId);

    /// <summary>
    /// Changes the display name and icon. The icon is a built-in icon or the user's uploaded icon.
    /// </summary>
    Task<UserResult> UpdateProfileAsync(Guid userId, string? displayName, string? icon);

    /// <summary>
    /// Replaces the user's uploaded icon (PNG, JPEG or WebP) and makes it their icon.
    /// </summary>
    Task<UserResult> SetUploadedIconAsync(Guid userId, byte[] data);

    /// <summary>
    /// The address of the user's uploaded icon, or null if they have none.
    /// </summary>
    Task<string?> GetUploadedIconPathAsync(Guid userId);

    /// <summary>
    /// An uploaded icon, or null if there is none with this ID.
    /// </summary>
    Task<UserIcon?> GetIconAsync(Guid iconId);

    /// <summary>
    /// Deletes users (with their external accounts and icons) last active before <paramref name="cutoff"/>. Returns how many.
    /// </summary>
    Task<int> DeleteInactiveUsersAsync(DateTimeOffset cutoff);
}
