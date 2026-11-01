using GamesLane.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GamesLane.Server.Users;

/// <summary>
/// Saved users, stored in the database. Passwords are kept only as salted hashes.
/// </summary>
public class UserService(
    GamesLaneDbContext db,
    TimeProvider timeProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<UserService> logger) : IUserService
{
    private static readonly TimeSpan ActivityInterval = TimeSpan.FromDays(1);
    private static readonly PasswordHasher<User> Hasher = new();

    // Checked when the username is unknown, so a wrong username takes as long as a wrong password.
    private static readonly string DummyHash = Hasher.HashPassword(null!, "not-a-real-password");

    /// <inheritdoc />
    public async Task<UserResult> RegisterAsync(string? username, string? password, string? displayName, string? icon)
    {
        if (!UserRules.IsValidUsername(username))
        {
            return UserResult.Fail("Choose a username of 3 to 20 letters, digits or underscores.");
        }

        if (!UserRules.IsValidPassword(password))
        {
            return UserResult.Fail($"Choose a password of at least {UserRules.MinPasswordLength} characters.");
        }

        if (UserRules.NormalizeName(displayName) is not { } name)
        {
            return UserResult.Fail($"Enter a display name of 1 to {UserRules.MaxNameLength} characters.");
        }

        var normalized = UserRules.NormalizeUsername(username!);
        if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized))
        {
            return UserResult.Fail("That username is taken.");
        }

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = normalized,
            DisplayName = name,
            Icon = UserRules.IsBuiltInIcon(icon) ? icon! : UserRules.DefaultIcon,
            CreatedAt = now,
            LastActiveAt = now,
        };
        user.PasswordHash = Hasher.HashPassword(user, password!);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return UserResult.Fail("That username is taken.");
        }

        return UserResult.Ok(user);
    }

    /// <inheritdoc />
    public async Task<User?> LogInAsync(string? username, string? password)
    {
        if (!UserRules.IsValidUsername(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var normalized = UserRules.NormalizeUsername(username!);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUsername == normalized);
        if (user?.PasswordHash is null)
        {
            Hasher.VerifyHashedPassword(null!, DummyHash, password);
            return null;
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
        }

        user.LastActiveAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync();
        return user;
    }

    /// <inheritdoc />
    public async Task<ExternalSignInResult> SignInExternalAsync(string provider, string providerKey, string? name, string? pictureUrl)
    {
        var now = timeProvider.GetUtcNow();
        var login = await db.ExternalLogins.SingleOrDefaultAsync(l => l.Provider == provider && l.ProviderKey == providerKey);
        if (login is not null && await db.Users.FindAsync(login.UserId) is { } existing)
        {
            existing.LastActiveAt = now;
            await db.SaveChangesAsync();
            return new ExternalSignInResult(existing, false);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = UserRules.FitName(name) ?? "Player",
            Icon = UserRules.DefaultIcon,
            CreatedAt = now,
            LastActiveAt = now,
        };
        user.ExternalLogins.Add(new ExternalLogin { Provider = provider, ProviderKey = providerKey });
        db.Users.Add(user);

        if (await DownloadPictureAsync(pictureUrl) is { } picture)
        {
            var icon = NewIcon(user.Id, picture.Data, picture.ContentType);
            db.UserIcons.Add(icon);
            user.Icon = UserRules.IconPath(icon.Id);
        }

        await db.SaveChangesAsync();
        return new ExternalSignInResult(user, true);
    }

    /// <inheritdoc />
    public async Task<User?> GetActiveUserAsync(Guid userId)
    {
        var user = await db.Users.FindAsync(userId);
        var now = timeProvider.GetUtcNow();
        if (user is not null && now - user.LastActiveAt >= ActivityInterval)
        {
            user.LastActiveAt = now;
            await db.SaveChangesAsync();
        }

        return user;
    }

    /// <inheritdoc />
    public async Task<UserResult> UpdateProfileAsync(Guid userId, string? displayName, string? icon)
    {
        if (UserRules.NormalizeName(displayName) is not { } name)
        {
            return UserResult.Fail($"Enter a display name of 1 to {UserRules.MaxNameLength} characters.");
        }

        if (await db.Users.FindAsync(userId) is not { } user)
        {
            return UserResult.Fail("Your account no longer exists. Log in again.");
        }

        if (!UserRules.IsBuiltInIcon(icon) && (icon is null || icon != await GetUploadedIconPathAsync(userId)))
        {
            return UserResult.Fail("Choose one of the icons.");
        }

        user.DisplayName = name;
        user.Icon = icon;
        await db.SaveChangesAsync();
        return UserResult.Ok(user);
    }

    /// <inheritdoc />
    public async Task<UserResult> SetUploadedIconAsync(Guid userId, byte[] data)
    {
        if (data.Length == 0 || data.Length > UserRules.MaxIconBytes)
        {
            return UserResult.Fail($"The picture must be smaller than {UserRules.MaxIconBytes / 1024} KB.");
        }

        if (UserRules.DetectImageType(data) is not { } contentType)
        {
            return UserResult.Fail("Upload a PNG, JPEG or WebP picture.");
        }

        if (await db.Users.FindAsync(userId) is not { } user)
        {
            return UserResult.Fail("Your account no longer exists. Log in again.");
        }

        await db.UserIcons.Where(i => i.UserId == userId).ExecuteDeleteAsync();
        var icon = NewIcon(userId, data, contentType);
        db.UserIcons.Add(icon);
        user.Icon = UserRules.IconPath(icon.Id);
        await db.SaveChangesAsync();
        return UserResult.Ok(user);
    }

    /// <inheritdoc />
    public async Task<string?> GetUploadedIconPathAsync(Guid userId)
    {
        var iconId = await db.UserIcons.Where(i => i.UserId == userId).Select(i => (Guid?)i.Id).FirstOrDefaultAsync();
        return iconId is { } id ? UserRules.IconPath(id) : null;
    }

    /// <inheritdoc />
    public Task<UserIcon?> GetIconAsync(Guid iconId) => db.UserIcons.AsNoTracking().SingleOrDefaultAsync(i => i.Id == iconId);

    /// <inheritdoc />
    public Task<int> DeleteInactiveUsersAsync(DateTimeOffset cutoff) =>
        db.Users.Where(u => u.LastActiveAt < cutoff).ExecuteDeleteAsync();

    /// <summary>
    /// A new icon record with a new ID.
    /// </summary>
    private UserIcon NewIcon(Guid userId, byte[] data, string contentType) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ContentType = contentType,
        Data = data,
        CreatedAt = timeProvider.GetUtcNow(),
    };

    /// <summary>
    /// Downloads an external account's picture (HTTPS only, PNG/JPEG/WebP, size-limited), or returns null if that fails.
    /// </summary>
    private async Task<(byte[] Data, string ContentType)?> DownloadPictureAsync(string? pictureUrl)
    {
        if (!Uri.TryCreate(pictureUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = UserRules.MaxIconBytes;
            var data = await client.GetByteArrayAsync(uri);
            return UserRules.DetectImageType(data) is { } contentType ? (data, contentType) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not download the account picture.");
            return null;
        }
    }
}
