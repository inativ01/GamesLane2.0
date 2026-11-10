using System.Security.Claims;

namespace GamesLane.Server.Auth;

/// <summary>
/// The saved users who may open the admin page, by user ID, from the <c>Admin:UserIds</c> setting (in
/// <c>appsettings.json</c>). Their accounts are never deleted for inactivity.
/// </summary>
public class AdminUsers(IEnumerable<Guid> ids)
{
    /// <summary>
    /// The admins' user IDs.
    /// </summary>
    public IReadOnlySet<Guid> Ids { get; } = ids.ToHashSet();

    /// <summary>
    /// The admins from configuration. IDs that are not GUIDs are ignored.
    /// </summary>
    public static AdminUsers From(IConfiguration configuration) =>
        new(configuration.GetSection("Admin:UserIds").Get<string[]>()?
            .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
            .OfType<Guid>() ?? []);

    /// <summary>
    /// Whether the logged-in user is an admin.
    /// </summary>
    public bool IsAdmin(ClaimsPrincipal? user) => AuthSetup.GetSavedUserId(user) is { } id && Ids.Contains(id);
}
