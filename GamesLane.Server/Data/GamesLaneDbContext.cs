using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GamesLane.Server.Data;

/// <summary>
/// The GamesLane database: saved users, their external accounts, icons, game options and saved games, saved game
/// sessions, the number of games started per day, the anonymous daily activity counts, the iPhones that get turn
/// notifications, and the keys that sign login tokens.
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
    /// Users' preferred options per game.
    /// </summary>
    public DbSet<UserGameOptions> UserGameOptions => Set<UserGameOptions>();

    /// <summary>
    /// Game sessions, saved after every change.
    /// </summary>
    public DbSet<SavedSession> Sessions => Set<SavedSession>();

    /// <summary>
    /// The private games users keep in their Saved games.
    /// </summary>
    public DbSet<SavedGame> SavedGames => Set<SavedGame>();

    /// <summary>
    /// The games started per day and game.
    /// </summary>
    public DbSet<GamePlay> GamePlays => Set<GamePlay>();

    /// <summary>
    /// The anonymous activity counts per day: visits, games opened, created, joined and finished, and invites opened.
    /// </summary>
    public DbSet<ActivityCount> ActivityCounts => Set<ActivityCount>();

    /// <summary>
    /// The phones that get a notification when it is their user's turn.
    /// </summary>
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    /// <summary>
    /// The keys ASP.NET Core uses to encrypt and sign login tokens, so tokens stay valid after a restart.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Sets keys, unique indexes, lengths, and deletes a user's external accounts, icons, game options and saved games
    /// with the user. A session that a user saved cannot be deleted while it is saved.
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
            user.Property(u => u.GameOrder).HasMaxLength(200);
            user.Property(u => u.CardStyle).HasMaxLength(20);
            user.Property(u => u.SavedGameLimit).HasDefaultValue(User.DefaultSavedGameLimit);
            user.HasIndex(u => u.LastActiveAt);
            user.HasMany(u => u.ExternalLogins).WithOne().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalLogin>(login =>
        {
            login.HasKey(l => new { l.Provider, l.ProviderKey });
            login.Property(l => l.Provider).HasMaxLength(50);
            login.Property(l => l.ProviderKey).HasMaxLength(200);
            login.Property(l => l.TokenClientId).HasMaxLength(100);
            login.Property(l => l.RefreshToken).HasMaxLength(2000);
        });

        modelBuilder.Entity<UserIcon>(icon =>
        {
            icon.Property(i => i.ContentType).HasMaxLength(50);
            icon.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserGameOptions>(options =>
        {
            options.HasKey(o => new { o.UserId, o.GameType });
            options.Property(o => o.GameType).HasMaxLength(50);
            options.Property(o => o.Options).HasMaxLength(4000);
            options.HasOne<User>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedSession>(session =>
        {
            session.Property(s => s.Id).HasMaxLength(6);
            session.Property(s => s.GameType).HasMaxLength(50);
            session.Property(s => s.Version).HasMaxLength(64);
            session.HasIndex(s => s.UpdatedAt);
        });

        modelBuilder.Entity<SavedGame>(saved =>
        {
            saved.HasKey(g => new { g.UserId, g.SessionId });
            saved.Property(g => g.SessionId).HasMaxLength(6);
            saved.HasIndex(g => g.SessionId);
            saved.HasOne<User>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            saved.HasOne<SavedSession>().WithMany().HasForeignKey(g => g.SessionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GamePlay>(play =>
        {
            play.HasKey(p => new { p.Day, p.GameType });
            play.Property(p => p.GameType).HasMaxLength(50);
        });

        modelBuilder.Entity<ActivityCount>(count =>
        {
            count.HasKey(c => new { c.Day, c.Event, c.GameType, c.Detail });
            count.Property(c => c.Event).HasMaxLength(30);
            count.Property(c => c.GameType).HasMaxLength(50);
            count.Property(c => c.Detail).HasMaxLength(50);
        });

        modelBuilder.Entity<DeviceToken>(device =>
        {
            device.HasKey(d => d.Token);
            device.Property(d => d.Token).HasMaxLength(DeviceToken.MaxTokenLength);
            device.Property(d => d.Platform).HasConversion<string>().HasMaxLength(10).HasDefaultValue(DevicePlatform.Ios);
            device.Property(d => d.UserKey).HasMaxLength(100);
            device.Property(d => d.Language).HasMaxLength(10);
            device.HasIndex(d => d.UserKey);
            device.HasIndex(d => d.UpdatedAt);
        });
    }
}
