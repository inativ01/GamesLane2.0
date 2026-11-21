using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamesLane.Server.Data;
using Microsoft.IdentityModel.Tokens;

namespace GamesLane.Server.Notifications;

/// <summary>
/// Sends notifications to the Android app through Firebase Cloud Messaging (the HTTP v1 API). Authenticates with an
/// access token from Google, got with a token signed by the service account's key (see <see cref="FirebaseSettings"/>)
/// and reused until shortly before it expires.
/// </summary>
public class FcmPushSender(
    FirebaseSettings settings,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<FcmPushSender> logger) : IPushSender
{
    /// <summary>The name of the <see cref="HttpClient"/> used for Google's servers.</summary>
    public const string HttpClientName = "FCM";

    /// <summary>The permission the access token asks for.</summary>
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    /// <summary>The notification channel the app shows turn notifications in (turn_channel_id in its strings.xml).</summary>
    public const string ChannelId = "turns";

    /// <summary>How long Google keeps trying to deliver a notification to a phone that is off or offline.</summary>
    public static readonly TimeSpan DeliveryLimit = TimeSpan.FromHours(1);

    private static readonly TimeSpan AssertionLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private (string Token, DateTimeOffset ExpiresAt)? _accessToken;

    /// <inheritdoc />
    public bool IsConfigured => settings.HasKey;

    /// <inheritdoc />
    public async Task<PushResult> SendAsync(DeviceToken device, PushNotification notification)
    {
        if (!IsConfigured)
        {
            return PushResult.Failed;
        }

        try
        {
            if (await AccessTokenAsync() is not { } accessToken)
            {
                return PushResult.Failed;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(settings.ProjectId)}/messages:send")
            {
                Content = JsonContent.Create(Message(device.Token, notification)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return PushResult.Sent;
            }

            var errorCode = await ErrorCodeAsync(response);
            if (response.StatusCode == HttpStatusCode.NotFound || errorCode == "UNREGISTERED")
            {
                return PushResult.Unregistered;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _accessToken = null;
            }

            logger.LogWarning("Firebase refused a notification ({Status} {ErrorCode}).", (int)response.StatusCode, errorCode);
            return PushResult.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not reach Firebase Cloud Messaging.");
            return PushResult.Failed;
        }
    }

    /// <summary>
    /// The message for <paramref name="token"/>: the title and body, and the page to open and the session as data. A
    /// newer notification about the same session replaces the older one (<c>tag</c>).
    /// </summary>
    public static object Message(string token, PushNotification notification) => new
    {
        message = new
        {
            token,
            notification = new { title = notification.Title, body = notification.Body },
            data = new Dictionary<string, string> { ["path"] = notification.Path, ["session"] = notification.SessionId },
            android = new
            {
                priority = "HIGH",
                ttl = $"{(int)DeliveryLimit.TotalSeconds}s",
                collapse_key = notification.SessionId,
                notification = new { channel_id = ChannelId, tag = notification.SessionId, sound = "default" },
            },
        },
    };

    /// <summary>
    /// The signed token that asks Google for an access token: a JSON web token from the service account, for
    /// <see cref="Scope"/>, signed with its key (RS256).
    /// </summary>
    public string Assertion()
    {
        var now = timeProvider.GetUtcNow();
        var header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = settings.ClientEmail,
            scope = Scope,
            aud = settings.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = (now + AssertionLifetime).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(settings.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Base64UrlEncoder.Encode(signature)}";
    }

    /// <summary>
    /// The access token for Firebase, from Google in exchange for <see cref="Assertion"/>; reused until
    /// <see cref="RenewBefore"/> before it expires. Null if Google refused.
    /// </summary>
    private async Task<string?> AccessTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_accessToken is { } cached && now < cached.ExpiresAt - RenewBefore)
            {
                return cached.Token;
            }

            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.PostAsync(settings.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = Assertion(),
            }));
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google did not give an access token for Firebase ({Status}).", (int)response.StatusCode);
                return null;
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var token = json.RootElement.GetProperty("access_token").GetString();
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            _accessToken = token is null ? null : (token, now + TimeSpan.FromSeconds(expiresIn));
            return token;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Google's access token for Firebase could not be read.");
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// The error code Firebase gives for refusing a notification (e.g. <c>UNREGISTERED</c>), or null.
    /// </summary>
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (!json.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("errorCode", out var code))
                    {
                        return code.GetString();
                    }
                }
            }

            return error.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
