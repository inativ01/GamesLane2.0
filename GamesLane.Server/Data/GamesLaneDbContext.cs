using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GamesLane.Server.Data;

/// <summary>
/// The GamesLane database: saved users, their external accounts and icons, and the keys that sign login tokens.
/// </summary>
public class GamesLaneDbContext(DbContextOptions<GamesLaneDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Registered users and users who sign in with an external account.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// External accounts linked to users.
    /// </summary>
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    /// <summary>
    /// Uploaded user icons.
    /// </summary>
    public DbSet<UserIcon> UserIcons => Set<UserIcon>();

    /// <summary>
    /// The keys ASP.NET Core uses to encrypt and sign login tokens, so tokens stay valid after a restart.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Sets keys, unique indexes, lengths, and deletes a user's external accounts and icons with the user.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Username).HasMaxLength(20);
            user.Property(u => u.NormalizedUsername).HasMaxLength(20);
            user.HasIndex(u => u.NormalizedUsername).IsUnique();
            user.Property(u => u.DisplayName).HasMaxLength(30);
            user.Property(u => u.Icon).HasMaxLength(100);
            user.HasIndex(u => u.LastActiveAt);
            user.HasMany(u => u.ExternalLogins).WithOne().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalLogin>(login =>
        {
            login.HasKey(l => new { l.Provider, l.ProviderKey });
            login.Property(l => l.Provider).HasMaxLength(50);
            login.Property(l => l.ProviderKey).HasMaxLength(200);
        });

        modelBuilder.Entity<UserIcon>(icon =>
        {
            icon.Property(i => i.ContentType).HasMaxLength(50);
            icon.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
