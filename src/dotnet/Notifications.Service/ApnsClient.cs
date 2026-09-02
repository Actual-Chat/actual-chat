using System.Net;
using System.Security.Cryptography;
using System.Text;
using ActualChat.Notifications.Module;

namespace ActualChat.Notifications;

/// <summary>
/// Minimal direct-APNs sender for Push to Talk wakes and call rings (FCM cannot deliver
/// apns-push-type=pushtotalk or voip); ES256 token auth with a cached JWT.
/// </summary>
public class ApnsClient(
    NotificationsSettings settings,
    IHttpClientFactory httpClientFactory,
    ICommander commander,
    ILogger<ApnsClient> log) : IApnsClient
{
    public const string HttpClientName = "apns";
    private static readonly TimeSpan JwtLifetime = TimeSpan.FromMinutes(50);
    private static readonly TimeSpan Expiration = TimeSpan.FromSeconds(60);

    private readonly Lock _jwtLock = new();
    private (string Token, DateTimeOffset IssuedAt)? _jwt;
    private volatile bool _isPttConfigWarningLogged;
    private volatile bool _isVoipConfigWarningLogged;

    private NotificationsSettings Settings { get; } = settings;
    private IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    private ICommander Commander { get; } = commander;
    private ILogger Log { get; } = log;

    public bool IsConfigured
        => !Settings.ApplePushKeyId.IsNullOrEmpty()
        && !Settings.ApplePushTeamId.IsNullOrEmpty()
        && !Settings.ApplePushBundleId.IsNullOrEmpty()
        && !Settings.ApplePushPrivateKeyPath.IsNullOrEmpty();

    public async Task SendPttWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        if (!IsConfigured) {
            if (!_isPttConfigWarningLogged) {
                _isPttConfigWarningLogged = true;
                Log.LogWarning("ApplePush settings are not configured - iOS PTT wakes are disabled");
            }
            return;
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object> {
            { "aps", new Dictionary<string, object>() },
            { Constants.Notification.MessageDataKeys.Kind, NotificationKind.SpeechStarted.ToString() },
            { Constants.Notification.MessageDataKeys.ChatId, chatId.Value },
            { Constants.Notification.MessageDataKeys.Timestamp, (long)startedAt.EpochOffset.TotalMilliseconds },
            { "chatTitle", chatTitle },
        });
        var jwt = GetJwt();
        var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        foreach (var deviceId in deviceIds)
            try {
                await SendOne(httpClient, jwt, deviceId, payload, PushKind.Ptt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "APNs PTT push failed for device '{DeviceId}'", deviceId);
            }
    }

    public async Task SendCallRing(
        ConversationId conversationId,
        AuthorId caller,
        string callerName,
        bool hasVideo,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        if (!IsConfigured) {
            if (!_isVoipConfigWarningLogged) {
                _isVoipConfigWarningLogged = true;
                Log.LogWarning("ApplePush settings are not configured - iOS call rings are disabled");
            }
            return;
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object> {
            { "aps", new Dictionary<string, object>() },
            { Constants.Notification.MessageDataKeys.Kind, NotificationKind.IncomingCall.ToString() },
            { Constants.Notification.MessageDataKeys.ConversationId, conversationId.Value },
            { Constants.Notification.MessageDataKeys.ChatId, conversationId.ChatId.Value },
            { Constants.Notification.MessageDataKeys.AuthorId, caller.Value },
            { Constants.Notification.MessageDataKeys.CallerName, callerName },
            { Constants.Notification.MessageDataKeys.HasVideo, hasVideo },
        });
        var jwt = GetJwt();
        var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        foreach (var deviceId in deviceIds)
            try {
                await SendOne(httpClient, jwt, deviceId, payload, PushKind.Voip, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "APNs call ring failed for device '{DeviceId}'", deviceId);
            }
    }

    public static string CreateJwt(string privateKeyPem, string keyId, string teamId, DateTimeOffset now)
    {
        var header = JsonSerializer.Serialize(new Dictionary<string, string> {
            { "alg", "ES256" },
            { "kid", keyId },
        });
        var claims = JsonSerializer.Serialize(new Dictionary<string, object> {
            { "iss", teamId },
            { "iat", now.ToUnixTimeSeconds() },
        });
        var signingInput =
            $"{Base64Url(Encoding.UTF8.GetBytes(header))}.{Base64Url(Encoding.UTF8.GetBytes(claims))}";
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        var signature = key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    public static bool IsDeadTokenResponse(HttpStatusCode statusCode, string body)
        => statusCode == HttpStatusCode.Gone
            || (statusCode == HttpStatusCode.BadRequest && body.Contains("BadDeviceToken"));

    // Private methods

    private async Task SendOne(
        HttpClient httpClient,
        string jwt,
        Symbol deviceId,
        string payload,
        PushKind pushKind,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/3/device/{deviceId.Value}") {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        var (pushType, topicSuffix, expiration) = pushKind switch {
            PushKind.Voip => ("voip", ".voip", Constants.Call.RingTimeout),
            PushKind.Ptt => ("pushtotalk", ".voip-ptt", Expiration),
            _ => throw new ArgumentOutOfRangeException(nameof(pushKind), pushKind, null),
        };
        request.Headers.TryAddWithoutValidation("authorization", $"bearer {jwt}");
        request.Headers.TryAddWithoutValidation("apns-push-type", pushType);
        request.Headers.TryAddWithoutValidation("apns-topic", $"{Settings.ApplePushBundleId}{topicSuffix}");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Headers.TryAddWithoutValidation("apns-expiration",
            (DateTimeOffset.UtcNow + expiration).ToUnixTimeSeconds().ToString());

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (IsDeadTokenResponse(response.StatusCode, body)) {
            Log.LogInformation("APNs reports dead {PushKind} token '{DeviceId}', removing", pushKind, deviceId);
            _ = Commander.Start(new NotificationsBackend_RemoveDevices([deviceId]), true, CancellationToken.None);
            return;
        }

        Log.LogError("APNs {PushKind} push rejected: {StatusCode} {Body}", pushKind, (int)response.StatusCode, body);
    }

    private string GetJwt()
    {
        lock (_jwtLock) {
            var now = DateTimeOffset.UtcNow;
            if (_jwt is { } jwt && now - jwt.IssuedAt < JwtLifetime)
                return jwt.Token;

            var token = CreateJwt(
                File.ReadAllText(Settings.ApplePushPrivateKeyPath),
                Settings.ApplePushKeyId, Settings.ApplePushTeamId, now);
            _jwt = (token, now);
            return token;
        }
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Nested types

    private enum PushKind { Ptt, Voip }
}
