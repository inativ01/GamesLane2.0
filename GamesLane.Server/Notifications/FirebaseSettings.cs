using System.Text.Json;

namespace GamesLane.Server.Notifications;

/// <summary>
/// The Firebase settings (the <c>Firebase</c> configuration section): the service account key that sends the Android
/// app's turn notifications, a JSON file at <c>KeyPath</c> (on Render a secret file), as Firebase downloads it. Holds the
/// project, the account's email, its private key and Google's token address; all empty if there is no key.
/// </summary>
public record FirebaseSettings(string ProjectId, string ClientEmail, string PrivateKey, string TokenUri)
{
    /// <summary>Google's token address, if the key does not name one.</summary>
    public const string DefaultTokenUri = "https://oauth2.googleapis.com/token";

    /// <summary>No key: notifications are not sent to Android phones.</summary>
    public static readonly FirebaseSettings None = new("", "", "", DefaultTokenUri);

    /// <summary>
    /// Reads the key file, if it exists.
    /// </summary>
    public static FirebaseSettings From(IConfiguration configuration, ILogger? logger = null)
    {
        var keyPath = configuration["Firebase:KeyPath"];
        if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath))
        {
            return None;
        }

        try
        {
            return Parse(File.ReadAllText(keyPath));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger?.LogWarning("The Firebase key at {KeyPath} could not be read.", keyPath);
            return None;
        }
    }

    /// <summary>
    /// Reads a service account key (JSON).
    /// </summary>
    public static FirebaseSettings Parse(string json)
    {
        using var key = JsonDocument.Parse(json);
        var root = key.RootElement;
        return new FirebaseSettings(
            root.GetProperty("project_id").GetString() ?? "",
            root.GetProperty("client_email").GetString() ?? "",
            root.GetProperty("private_key").GetString() ?? "",
            root.TryGetProperty("token_uri", out var tokenUri) ? tokenUri.GetString() ?? DefaultTokenUri : DefaultTokenUri);
    }

    /// <summary>
    /// Whether the server can send notifications through Firebase.
    /// </summary>
    public bool HasKey => ProjectId.Length > 0 && ClientEmail.Length > 0 && !string.IsNullOrWhiteSpace(PrivateKey);
}
