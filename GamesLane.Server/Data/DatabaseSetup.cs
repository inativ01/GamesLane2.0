using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GamesLane.Server.Data;

/// <summary>
/// Connects to the PostgreSQL database given by the <c>ConnectionStrings:GamesLane</c> setting
/// (on Render, the <c>ConnectionStrings__GamesLane</c> environment variable; locally, <c>dotnet user-secrets</c>).
/// </summary>
public static class DatabaseSetup
{
    private const string ConnectionStringName = "GamesLane";

    /// <summary>
    /// Registers the database, and stores the keys that sign login tokens in it.
    /// </summary>
    public static void AddGamesLaneDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The database connection string 'ConnectionStrings:{ConnectionStringName}' is not set. " +
                "Locally, set it with 'dotnet user-secrets'; on Render, set the environment variable 'ConnectionStrings__GamesLane'.");
        }

        string npgsqlConnectionString;
        try
        {
            npgsqlConnectionString = new NpgsqlConnectionStringBuilder(ToNpgsqlConnectionString(connectionString)).ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or UriFormatException)
        {
            // The message never includes the value, which contains the password.
            throw new InvalidOperationException(
                $"The database connection string 'ConnectionStrings:{ConnectionStringName}' could not be read ({ex.Message}). " +
                "Use the .NET format from Neon's Connect dialog (Host=...;Database=...;Username=...;Password=...).");
        }

        builder.Services.AddDbContext<GamesLaneDbContext>(options =>
            options.UseNpgsql(npgsqlConnectionString, npgsql => npgsql.EnableRetryOnFailure()));

        builder.Services.AddDataProtection()
            .SetApplicationName("GamesLane")
            .PersistKeysToDbContext<GamesLaneDbContext>();
    }

    /// <summary>
    /// Creates or updates the database tables to match the code.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GamesLaneDbContext>().Database.MigrateAsync();
    }

    /// <summary>
    /// Accepts either the .NET format (<c>Host=...;Database=...</c>) or the URL format
    /// (<c>postgresql://user:password@host/database?sslmode=require</c>) and returns the .NET format.
    /// Also removes common copy mistakes: surrounding spaces or quotes, and a leading <c>psql</c> command.
    /// </summary>
    public static string ToNpgsqlConnectionString(string connectionString)
    {
        connectionString = connectionString.Trim();
        if (connectionString.StartsWith("psql ", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = connectionString[5..].Trim();
        }

        while (connectionString.Length >= 2 && connectionString[0] is '"' or '\'' && connectionString[^1] == connectionString[0])
        {
            connectionString = connectionString[1..^1].Trim();
        }

        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };
        if (uri.Port > 0)
        {
            builder.Port = uri.Port;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            switch (parts[0].ToLowerInvariant())
            {
                case "sslmode":
                    builder.SslMode = Enum.Parse<SslMode>(value.Replace("-", ""), ignoreCase: true);
                    break;
                case "channel_binding":
                    builder.ChannelBinding = Enum.Parse<ChannelBinding>(value, ignoreCase: true);
                    break;
            }
        }

        return builder.ConnectionString;
    }
}
