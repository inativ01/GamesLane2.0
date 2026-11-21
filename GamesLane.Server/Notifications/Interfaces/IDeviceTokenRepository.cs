using GamesLane.Server.Data;

namespace GamesLane.Server.Notifications;

/// <summary>
/// Where the phones that get turn notifications are kept (the database).
/// </summary>
public interface IDeviceTokenRepository
{
    /// <summary>
    /// Saves <paramref name="token"/> as <paramref name="userKey"/>'s device, replacing whoever had it before, and marks
    /// it used at <paramref name="now"/>.
    /// </summary>
    Task SaveAsync(string token, DevicePlatform platform, string userKey, bool isSandbox, string language, DateTimeOffset now);

    /// <summary>
    /// The devices of <paramref name="userKey"/>.
    /// </summary>
    Task<IReadOnlyList<DeviceToken>> GetForUserAsync(string userKey);

    /// <summary>
    /// Forgets a device, e.g. once its notification service says the app was removed from it.
    /// </summary>
    Task DeleteAsync(string token);

    /// <summary>
    /// Forgets the devices of <paramref name="userKey"/>, e.g. when they delete their account.
    /// </summary>
    Task DeleteForUserAsync(string userKey);

    /// <summary>
    /// Forgets the devices not used since <paramref name="before"/>, and returns how many.
    /// </summary>
    Task<int> DeleteUnusedAsync(DateTimeOffset before);
}
