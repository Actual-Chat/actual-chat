# Walkie-Talkie iOS Push to Talk — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** iOS wake-to-hear: with walkie-talkie mode armed, a `SpeechStarted`
event reaches the iOS user as an Apple Push to Talk push (direct APNs), and
the app — even from a killed state — plays the utterance from its first word
through the shared walkie-talkie playback core. Receive-only v1.

**Architecture:** Server side, `SendWalkieTalkieWake` gains an iOS branch: a
hand-rolled `ApnsClient` (HTTP/2 + cached ES256 JWT) sends
`apns-push-type: pushtotalk` pushes to PTT device tokens stored as a new
`DeviceType.iOSPttApp`. Client side, B's Android wake core is extracted into
a platform-neutral `WalkieTalkieSession` (Android refactored onto it, no
behavior change); a process-level `IosPushToTalk` owns the single aggregate
"Voxt" PTT channel (join = armed, restoration survives kill/reboot,
`ListenOnly` transmission mode) and routes incoming pushes into the shared
core after the system activates the audio session. An
"externally activated" flag stops `AudioSession` from fighting the
PTT-managed AVAudioSession.

**Tech Stack:** .NET 10; `System.Security.Cryptography.ECDsa` +
`HttpClient` HTTP/2 (no new dependencies); Microsoft.iOS `PushToTalk`
bindings (`PTChannelManager`, `PTChannelManagerDelegate`,
`PTChannelRestorationDelegate`, `PTPushResult`, `PTParticipant`,
`PTChannelDescriptor`, `PTTransmissionMode`); xUnit.

**Spec:** `docs/superpowers/specs/2026-07-13-walkie-talkie-ios-design.md`

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any code: no `Async` suffix;
  no member XML docs (type-level 3-line summary only when justified);
  Allman braces for classes/methods, K&R for everything else; max 120
  chars/line; `.ConfigureAwait(false)` in service code; comments only for
  non-obvious "why"; tests PascalCase, AAA with lowercase comments.
- Branch: `feat/walkie-talkie-push` (checked out; do NOT create branches).
  Stage explicit paths only.
- **Build limitations on this machine:** `net10.0-android` cannot compile
  (pre-existing toolchain gap) and `net10.0-ios` doesn't even exist as a
  target off-macOS — so NO App.Maui code compiles here. Verification for
  App.Maui tasks = careful transcription + `dotnet build ActualChat.CI.slnf`
  (guards shared projects) + reviewer static symbol verification. Server
  and UI.Blazor.App tasks build and test normally.
- Exact values from the spec: `DeviceType.iOSPttApp = 4` (appended);
  APNs headers `apns-push-type=pushtotalk`,
  `apns-topic=<ApplePushBundleId>.voip-ptt`, `apns-priority=10`,
  `apns-expiration=now+60s`; JWT ES256 cached ~50 min; payload keys
  `kind`, `chatId`, `timestamp` (epoch ms) + `chatTitle`;
  channel name `"Voxt"`; transmission mode `ListenOnly`;
  entitlement `com.apple.developer.push-to-talk`; background mode
  `push-to-talk`.
- The PTT server branch and Android FCM branch share A's gates verbatim
  (feature flag, member cap, armed check, wake-pending TTL) — no gate is
  duplicated or moved.
- Integration tests need localhost infra (PostgreSQL/Redis/NATS) — running;
  `dotnet test` timeouts up to 10 min.

---

### Task 1: DeviceType.iOSPttApp + push-path exclusions + APNs settings

**Files:**
- Modify: `src/dotnet/Api/Notifications/DeviceType.cs`
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs:838-841,861-864` (OnPush / OnPushDismissal)
- Modify: `src/dotnet/Notifications.Service/Module/NotificationsSettings.cs`
- Test: `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs` (extend)

**Interfaces:**
- Consumes: `ListDevices(userId, sessionHash, minActiveAt, ct)` returns
  `IReadOnlyList<Device>` with `Device.DeviceType`;
  `NotificationsBackend_RegisterDevice(UserId, Symbol, DeviceType, Symbol)`.
- Produces: `DeviceType.iOSPttApp` (= 4); `NotificationsSettings`
  properties `ApplePushKeyId`, `ApplePushTeamId`, `ApplePushBundleId`,
  `ApplePushPrivateKeyPath` (all `string`, default `""`),
  `ApplePushUseSandbox` (`bool`, default false); OnPush/OnPushDismissal
  never hand `iOSPttApp` tokens to FCM.

- [ ] **Step 1: Write the failing test**

In `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs`, add:

```csharp
    [Fact]
    public async Task PttDeviceIsExcludedFromMessagePushes()
    {
        // arrange
        var (chatId, alice, _, _) = await CreateChatWithAliceAndBob("WT ptt-excluded");
        var pttDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSPttApp);
        var fcmDeviceId = await RegisterDevice(alice.Id, DeviceType.WebBrowser);
        Sink.Clear();

        // act: a normal message notification for alice
        await Tester.CreateTextEntry(chatId, "Hi Alice");

        // assert: the FCM push reaches the web device but never the PTT token
        await WaitFor(() => Sink.Messages.Any(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId)),
            WakeTimeout);
        Sink.Messages.Should().Contain(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId));
        Sink.Messages.Should().NotContain(m => m.DeviceIds.Contains(pttDeviceId));
    }
```

Note: `CreateChatWithAliceAndBob` signs in bob last, and `CreateTextEntry`
posts as the current user — bob — so alice is the notified recipient.
`RegisterDevice`/`WaitFor`/`Sink` already exist in this file.

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~PttDeviceIsExcludedFromMessagePushes" 2>&1 | tail -5
```
Expected: build FAILURE — `DeviceType.iOSPttApp` doesn't exist. That's the
red signal.

- [ ] **Step 3: Add the enum member**

In `src/dotnet/Api/Notifications/DeviceType.cs`, after `AndroidApp = 3,`:

```csharp
    // Apple Push to Talk token (ephemeral, from PTChannelManager) - direct APNs only,
    // must never be handed to FCM.
    iOSPttApp = 4,
```

- [ ] **Step 4: Exclude PTT tokens from the FCM push paths**

In `src/dotnet/Notifications.Service/NotificationsBackend.cs`:

(a) In `OnPush`, replace:

```csharp
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        if (devices.Count == 0) {
```

with:

```csharp
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        devices = devices.Where(d => d.DeviceType != DeviceType.iOSPttApp).ToList();
        if (devices.Count == 0) {
```

(b) In `OnPushDismissal`, replace:

```csharp
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        if (devices.Count == 0)
            return;
```

with:

```csharp
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        devices = devices.Where(d => d.DeviceType != DeviceType.iOSPttApp).ToList();
        if (devices.Count == 0)
            return;
```

- [ ] **Step 5: Add the APNs settings**

In `src/dotnet/Notifications.Service/Module/NotificationsSettings.cs`, add
after `WalkieTalkieMaxChatMembers`:

```csharp
    // Direct-APNs auth for Push to Talk wakes (FCM can't deliver pushtotalk pushes).
    // The .p8 must be an APNs-enabled key - the Apple Sign-In key won't work.
    public string ApplePushKeyId { get; set; } = "";
    public string ApplePushTeamId { get; set; } = "";
    public string ApplePushBundleId { get; set; } = "";
    public string ApplePushPrivateKeyPath { get; set; } = "";
    public bool ApplePushUseSandbox { get; set; }
```

- [ ] **Step 6: Run the test to verify it passes**

Run the Step 2 command. Expected: PASS.
Then run the whole file to catch regressions:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -4
```
Expected: 8 passed (7 existing + 1 new).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Notifications/DeviceType.cs \
        src/dotnet/Notifications.Service/NotificationsBackend.cs \
        src/dotnet/Notifications.Service/Module/NotificationsSettings.cs \
        tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs
git commit -m "feat(notifications): iOSPttApp device type, FCM-path exclusion, APNs settings

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: ApnsClient (direct APNs, ES256 JWT, HTTP/2)

**Files:**
- Create: `src/dotnet/Notifications.Service/IApnsClient.cs`
- Create: `src/dotnet/Notifications.Service/ApnsClient.cs`
- Modify: `src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs` (registration)
- Test: `tests/Notifications.IntegrationTests/ApnsClientTest.cs` (create; plain unit tests, no app host)

**Interfaces:**
- Consumes: `NotificationsSettings.ApplePush*` (Task 1),
  `Constants.Notification.MessageDataKeys.{Kind,ChatId,Timestamp}`,
  `NotificationKind.SpeechStarted`, `NotificationsBackend_RemoveDevices(Symbol[])`.
- Produces:
  `interface IApnsClient { Task SendPushToTalkWake(ChatId chatId, Moment startedAt, string chatTitle, IReadOnlyCollection<Symbol> deviceIds, CancellationToken cancellationToken); }`
  plus public statics used by tests: `ApnsClient.CreateJwt(string privateKeyPem, string keyId, string teamId, DateTimeOffset now)` and
  `ApnsClient.IsDeadTokenResponse(HttpStatusCode statusCode, string body)`;
  DI: `IApnsClient` singleton + named `HttpClient` `ApnsClient.HttpClientName`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Notifications.IntegrationTests/ApnsClientTest.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActualChat.Notifications;
using ActualChat.Notifications.Module;

namespace ActualChat.Notifications.IntegrationTests;

public class ApnsClientTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void CreateJwtProducesVerifiableES256Token()
    {
        // arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_780_000_000);

        // act
        var jwt = ApnsClient.CreateJwt(pem, "KEY123", "TEAM456", now);

        // assert
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);
        var header = JsonSerializer.Deserialize<Dictionary<string, string>>(FromBase64Url(parts[0]))!;
        header["alg"].Should().Be("ES256");
        header["kid"].Should().Be("KEY123");
        var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FromBase64Url(parts[1]))!;
        claims["iss"].GetString().Should().Be("TEAM456");
        claims["iat"].GetInt64().Should().Be(1_780_000_000);
        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = FromBase64UrlBytes(parts[2]);
        key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }

    [Fact]
    public void DeadTokenResponsesAreRecognized()
    {
        // act + assert
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.Gone, """{"reason":"Unregistered"}""").Should().BeTrue();
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.BadRequest, """{"reason":"BadDeviceToken"}""").Should().BeTrue();
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.BadRequest, """{"reason":"BadTopic"}""").Should().BeFalse();
        ApnsClient.IsDeadTokenResponse(HttpStatusCode.InternalServerError, "").Should().BeFalse();
    }

    [Fact]
    public async Task SendPushToTalkWakeSendsCorrectRequest()
    {
        // arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyPath = Path.Combine(Path.GetTempPath(), $"apns-test-{Guid.NewGuid():N}.p8");
        await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());
        try {
            var settings = new NotificationsSettings {
                ApplePushKeyId = "KEY123",
                ApplePushTeamId = "TEAM456",
                ApplePushBundleId = "chat.actual.app",
                ApplePushPrivateKeyPath = keyPath,
            };
            var handler = new RecordingHandler();
            var client = new ApnsClient(settings, new FakeHttpClientFactory(handler), null!, NullLogger<ApnsClient>.Instance);
            var chatId = ChatId.Parse("testchatid1234567890");
            var startedAt = Moment.EpochStart + TimeSpan.FromDays(20_000);

            // act
            await client.SendPushToTalkWake(chatId, startedAt, "My Chat", [new Symbol("aabbccdd")], CancellationToken.None);

            // assert
            var request = handler.Requests.Should().ContainSingle().Subject;
            request.RequestUri!.AbsolutePath.Should().Be("/3/device/aabbccdd");
            request.Headers.GetValues("apns-push-type").Single().Should().Be("pushtotalk");
            request.Headers.GetValues("apns-topic").Single().Should().Be("chat.actual.app.voip-ptt");
            request.Headers.GetValues("apns-priority").Single().Should().Be("10");
            request.Headers.GetValues("authorization").Single().Should().StartWith("bearer ");
            var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(handler.Bodies.Single())!;
            body["kind"].GetString().Should().Be("SpeechStarted");
            body["chatId"].GetString().Should().Be(chatId.Value);
            body["chatTitle"].GetString().Should().Be("My Chat");
            body["timestamp"].GetInt64().Should().Be((long)startedAt.EpochOffset.TotalMilliseconds);
        }
        finally {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public async Task UnconfiguredClientSilentlySkips()
    {
        // arrange
        var handler = new RecordingHandler();
        var client = new ApnsClient(
            new NotificationsSettings(), new FakeHttpClientFactory(handler), null!, NullLogger<ApnsClient>.Instance);

        // act
        await client.SendPushToTalkWake(
            ChatId.Parse("testchatid1234567890"), Moment.EpochStart, "T", [new Symbol("x")], CancellationToken.None);

        // assert
        handler.Requests.Should().BeEmpty();
    }

    // Private methods

    private static string FromBase64Url(string s)
        => Encoding.UTF8.GetString(FromBase64UrlBytes(s));

    private static byte[] FromBase64UrlBytes(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    // Nested types

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.push.apple.com") };
    }
}
```

(If `TestBase(@out)` or `NullLogger` don't resolve, match the constructor
pattern of the sibling `NotificationSerializationTests.cs` in the same
project and add `using Microsoft.Extensions.Logging.Abstractions;` — note
any such mechanical adjustment in the report. Likewise, if
`ChatId.Parse("testchatid1234567890")` throws a format error, substitute any
≥6-char lowercase-alphanumeric literal that parses as a group chat id and
note it.)

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~ApnsClientTest" 2>&1 | tail -5
```
Expected: build FAILURE — `IApnsClient`/`ApnsClient` don't exist.

- [ ] **Step 3: Create the interface**

`src/dotnet/Notifications.Service/IApnsClient.cs`:

```csharp
namespace ActualChat.Notifications;

public interface IApnsClient
{
    Task SendPushToTalkWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Create the client**

`src/dotnet/Notifications.Service/ApnsClient.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActualChat.Notifications.Module;

namespace ActualChat.Notifications;

/// <summary>
/// Minimal direct-APNs sender for Push to Talk wakes (FCM cannot deliver
/// apns-push-type=pushtotalk); ES256 token auth with a cached JWT.
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
    private volatile bool _isConfigWarningLogged;

    private NotificationsSettings Settings { get; } = settings;
    private IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    private ICommander Commander { get; } = commander;
    private ILogger Log { get; } = log;

    public bool IsConfigured
        => !Settings.ApplePushKeyId.IsNullOrEmpty()
        && !Settings.ApplePushTeamId.IsNullOrEmpty()
        && !Settings.ApplePushBundleId.IsNullOrEmpty()
        && !Settings.ApplePushPrivateKeyPath.IsNullOrEmpty();

    public async Task SendPushToTalkWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        if (!IsConfigured) {
            if (!_isConfigWarningLogged) {
                _isConfigWarningLogged = true;
                Log.LogWarning("ApplePush settings are not configured - iOS PTT wakes are disabled");
            }
            return;
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object> {
            { Constants.Notification.MessageDataKeys.Kind, NotificationKind.SpeechStarted.ToString() },
            { Constants.Notification.MessageDataKeys.ChatId, chatId.Value },
            { Constants.Notification.MessageDataKeys.Timestamp, (long)startedAt.EpochOffset.TotalMilliseconds },
            { "chatTitle", chatTitle },
        });
        var jwt = GetJwt();
        var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        foreach (var deviceId in deviceIds)
            try {
                await SendOne(httpClient, jwt, deviceId, payload, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e, "APNs PTT push failed for device '{DeviceId}'", deviceId);
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
        HttpClient httpClient, string jwt, Symbol deviceId, string payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/3/device/{deviceId.Value}") {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("authorization", $"bearer {jwt}");
        request.Headers.TryAddWithoutValidation("apns-push-type", "pushtotalk");
        request.Headers.TryAddWithoutValidation("apns-topic", $"{Settings.ApplePushBundleId}.voip-ptt");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Headers.TryAddWithoutValidation("apns-expiration",
            (DateTimeOffset.UtcNow + Expiration).ToUnixTimeSeconds().ToString());

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (IsDeadTokenResponse(response.StatusCode, body)) {
            Log.LogInformation("APNs reports dead PTT token '{DeviceId}', removing", deviceId);
            _ = Commander.Start(new NotificationsBackend_RemoveDevices([deviceId]), true, CancellationToken.None);
            return;
        }

        Log.LogWarning("APNs PTT push rejected: {StatusCode} {Body}", (int)response.StatusCode, body);
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
}
```

- [ ] **Step 5: Register in DI**

In `src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs`,
after the `services.AddSingleton<IFirebaseMessagingClient, FirebaseMessagingClient>();` line, add:

```csharp
        // Direct APNs - Push to Talk wakes only (FCM can't deliver pushtotalk pushes)
        services.AddSingleton<IApnsClient, ApnsClient>();
        services.AddHttpClient(ApnsClient.HttpClientName)
            .ConfigureHttpClient((c, httpClient) => {
                var s = c.GetRequiredService<NotificationsSettings>();
                httpClient.BaseAddress = new Uri(s.ApplePushUseSandbox
                    ? "https://api.sandbox.push.apple.com"
                    : "https://api.push.apple.com");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                EnableMultipleHttp2Connections = true,
            });
```

`ApnsClient`'s constructor resolves `NotificationsSettings` (Task 1 module
singleton), `IHttpClientFactory` (added by `AddHttpClient`), `ICommander`,
and `ILogger<ApnsClient>` — all registered. Add
`using System.Net.Http;`-related usings only if the compiler asks.

- [ ] **Step 6: Run the tests to verify they pass**

Run the Step 2 command. Expected: 4 passed.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Notifications.Service/IApnsClient.cs \
        src/dotnet/Notifications.Service/ApnsClient.cs \
        src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs \
        tests/Notifications.IntegrationTests/ApnsClientTest.cs
git commit -m "feat(notifications): direct-APNs client for Push to Talk wakes

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Wake-sender iOS branch + APNs test sink

**Files:**
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs:987-1009` (SendWalkieTalkieWake) + DI property
- Create: `tests/Testing.Host/ApnsTestSink.cs`
- Modify: `tests/Testing.Host/TestAppHostFactory.cs:100-102` (register the sink)
- Test: `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs` (extend)

**Interfaces:**
- Consumes: `IApnsClient` (Task 2), `DeviceType.iOSPttApp` (Task 1),
  `ChatsBackend.Get(chatId, ct)` → `Chat?` with `.Title` (already a DI
  property on NotificationsBackend).
- Produces: `SendWalkieTalkieWake` fan-out to both transports;
  `ApnsTestSink` with `IReadOnlyList<ApnsPttWakeMessage> Wakes`
  (`ApnsPttWakeMessage(ChatId ChatId, Moment StartedAt, string ChatTitle, IReadOnlyList<Symbol> DeviceIds)`)
  and `Clear()`.

- [ ] **Step 1: Create the test sink**

`tests/Testing.Host/ApnsTestSink.cs`:

```csharp
using System.Collections.Concurrent;
using ActualChat.Notifications;

namespace ActualChat.Testing.Host;

public sealed record ApnsPttWakeMessage(
    ChatId ChatId,
    Moment StartedAt,
    string ChatTitle,
    IReadOnlyList<Symbol> DeviceIds);

// Replaces IApnsClient in test hosts: records every PTT wake instead of hitting APNs.
public sealed class ApnsTestSink(ILogger<ApnsTestSink> log) : IApnsClient
{
    private readonly ConcurrentQueue<ApnsPttWakeMessage> _wakes = new();

    public IReadOnlyList<ApnsPttWakeMessage> Wakes => _wakes.ToArray();

    public void Clear()
        => _wakes.Clear();

    public Task SendPushToTalkWake(
        ChatId chatId,
        Moment startedAt,
        string chatTitle,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendPushToTalkWake: chat {ChatId} -> {DeviceCount} device(s)", chatId, deviceIds.Count);
        _wakes.Enqueue(new ApnsPttWakeMessage(chatId, startedAt, chatTitle, [..deviceIds]));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Register the sink in the test host**

In `tests/Testing.Host/TestAppHostFactory.cs`, right after the existing
`FirebaseMessagingTestSink` registration (lines 100-102), add:

```csharp
                services.AddSingleton<ApnsTestSink>();
                services.AddSingleton<IApnsClient>(
                    c => c.GetRequiredService<ApnsTestSink>());
```

(add `using ActualChat.Notifications;` if not already imported).

- [ ] **Step 3: Write the failing tests**

In `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs`, add a
sink property next to `Sink`:

```csharp
    private ApnsTestSink ApnsSink => AppHost.Services.GetRequiredService<ApnsTestSink>();
```

and the tests:

```csharp
    [Fact]
    public async Task ArmedIosPttDeviceGetsApnsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT ios-ptt");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.iOSPttApp);
        await ArmByAlwaysListened(alice.Id, chatId);
        ApnsSink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => ApnsSink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)),
            WakeTimeout);
        var wake = ApnsSink.Wakes.Should()
            .Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)).Subject;
        wake.ChatTitle.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DualDeviceUserGetsBothTransports()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT dual-device");
        var androidDeviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        var pttDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSPttApp);
        await ArmByAlwaysListened(alice.Id, chatId);
        Sink.Clear();
        ApnsSink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(androidDeviceId))
            && ApnsSink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(pttDeviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(androidDeviceId));
        ApnsSink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(pttDeviceId));
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~ArmedIosPttDeviceGetsApnsWake|FullyQualifiedName~DualDeviceUserGetsBothTransports" 2>&1 | tail -5
```
Expected: FAIL — the direct assertions after `WaitFor` report zero wakes in
`ApnsSink` (no iOS branch exists yet).

- [ ] **Step 5: Add the sender branch**

In `src/dotnet/Notifications.Service/NotificationsBackend.cs`:

(a) Add the DI property next to `FirebaseMessagingClient`:

```csharp
    private IApnsClient ApnsClient { get; } = services.GetRequiredService<IApnsClient>();
```

(b) Replace the device-filter tail of `SendWalkieTalkieWake` — currently:

```csharp
        var devices = await ListDevices(userId, cancellationToken).ConfigureAwait(false);
        var deviceIds = devices
            .Where(d => d.DeviceType == DeviceType.AndroidApp)
            .Select(d => d.DeviceId)
            .ToList();
        if (deviceIds.Count == 0)
            return;

        await FirebaseMessagingClient
            .SendSpeechStartedWake(chatId, authorId, startedAt, deviceIds, cancellationToken)
            .ConfigureAwait(false);
```

with:

```csharp
        var devices = await ListDevices(userId, cancellationToken).ConfigureAwait(false);
        var fcmDeviceIds = devices
            .Where(d => d.DeviceType == DeviceType.AndroidApp)
            .Select(d => d.DeviceId)
            .ToList();
        if (fcmDeviceIds.Count != 0)
            await FirebaseMessagingClient
                .SendSpeechStartedWake(chatId, authorId, startedAt, fcmDeviceIds, cancellationToken)
                .ConfigureAwait(false);

        var pttDeviceIds = devices
            .Where(d => d.DeviceType == DeviceType.iOSPttApp)
            .Select(d => d.DeviceId)
            .ToList();
        if (pttDeviceIds.Count != 0) {
            // The PTT system UI needs a channel/speaker label at push time, before any RPC.
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            await ApnsClient
                .SendPushToTalkWake(chatId, startedAt, chat?.Title ?? "Voxt", pttDeviceIds, cancellationToken)
                .ConfigureAwait(false);
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run the Step 4 command → 2 passed. Then the whole file:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -4
```
Expected: 10 passed.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Notifications.Service/NotificationsBackend.cs \
        tests/Testing.Host/ApnsTestSink.cs \
        tests/Testing.Host/TestAppHostFactory.cs \
        tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs
git commit -m "feat(notifications): iOS Push to Talk branch in the walkie-talkie wake sender

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Extract the shared WalkieTalkieSession (Android refactor, no behavior change)

**Files:**
- Create: `src/dotnet/App.Maui/Services/WalkieTalkiePlatform.cs`
- Create: `src/dotnet/App.Maui/Services/WalkieTalkieSession.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Audio/WalkieTalkieWakeHandler.cs` (becomes a thin shell)

**Interfaces:**
- Consumes: everything the current `WalkieTalkieWakeHandler` consumes
  (read it first — it is the source being extracted):
  `BlazorWebViewApp.WhenAppReady/EnsureStarted`, `TrueSessionResolver.SessionTask`,
  `AppServicesAccessor.TryGetScopedServices`, `HeadlessBlazorScope`,
  `AppUIHub`, `ChatAudioUI` walkie-talkie APIs, `WalkieTalkie.IsStaleWake`,
  `Tune.NotifyOnNewAudioMessageAfterDelay`.
- Produces (Task 6 consumes these — exact signatures):

```csharp
public abstract class WalkieTalkiePlatform
{
    public abstract void OnWakeFailed(ChatId chatId);
    public abstract void OnHeadlessTeardown();
    public virtual Task OnPlaybackStarted(AppUIHub hub, ChatId chatId) => Task.CompletedTask;
    public virtual Task OnForegroundWakeHandled(ChatId chatId) => Task.CompletedTask;
}

public static class WalkieTalkieSession
{
    public static Task HandleWake(ChatId chatId, Moment startedAt, bool isForeground, WalkieTalkiePlatform platform);
    public static void StopHeadless(WalkieTalkiePlatform platform);
}
```

- [ ] **Step 1: Create WalkieTalkiePlatform**

`src/dotnet/App.Maui/Services/WalkieTalkiePlatform.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Platform hooks for <see cref="WalkieTalkieSession"/>: wake failure, playback start,
/// foreground-wake completion, and headless-session teardown.
/// </summary>
public abstract class WalkieTalkiePlatform
{
    public abstract void OnWakeFailed(ChatId chatId);
    public abstract void OnHeadlessTeardown();

    public virtual Task OnPlaybackStarted(AppUIHub hub, ChatId chatId)
        => Task.CompletedTask;

    public virtual Task OnForegroundWakeHandled(ChatId chatId)
        => Task.CompletedTask;
}
```

- [ ] **Step 2: Create WalkieTalkieSession**

Read `src/dotnet/App.Maui/Platforms/Android/Audio/WalkieTalkieWakeHandler.cs`
first — the code below is its portable core, moved verbatim except where a
`platform.` call replaces an Android-specific call.

`src/dotnet/App.Maui/Services/WalkieTalkieSession.cs`:

```csharp
using ActualChat.Security;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Platform-neutral walkie-talkie wake core: scope resolution (live WebView scope vs
/// <see cref="HeadlessBlazorScope"/>), playback start, and headless-session teardown.
/// </summary>
public static class WalkieTalkieSession
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TeardownCheckPeriod = TimeSpan.FromSeconds(5);
    private const int TeardownIdleChecks = 2;
    private static readonly Lock Lock = new();
    private static Task? _teardownWatcher;
    private static ILogger Log => field ??= StaticLog.For(typeof(WalkieTalkieSession));

    public static async Task HandleWake(
        ChatId chatId, Moment startedAt, bool isForeground, WalkieTalkiePlatform platform)
    {
        try {
            var app = await BlazorWebViewApp.WhenAppReady.WaitAsync(StartupTimeout).ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            await sessionResolver.SessionTask.WaitAsync(StartupTimeout).ConfigureAwait(false);

            IServiceProvider scopedServices;
            var isHeadless = false;
            if (AppServicesAccessor.TryGetScopedServices(out var liveScope))
                scopedServices = liveScope;
            else if (HeadlessBlazorScope.GetOrCreate() is { } headless) {
                scopedServices = headless.Services;
                isHeadless = true;
            }
            else if (AppServicesAccessor.TryGetScopedServices(out liveScope!))
                // Lost the creation race to a just-published WebView scope
                scopedServices = liveScope;
            else
                throw StandardError.Internal("No service scope is available.");

            await StartPlayback(scopedServices, chatId, startedAt, isForeground, isHeadless, platform)
                .ConfigureAwait(false);
            if (isHeadless)
                EnsureTeardownWatcher(platform);
        }
        catch (Exception e) {
            Log.LogError(e, "Walkie-talkie wake failed for chat #{ChatId}", chatId);
            platform.OnWakeFailed(chatId);
            await HeadlessBlazorScope.DisposeCurrent("wake failed").ConfigureAwait(false);
        }
    }

    public static void StopHeadless(WalkieTalkiePlatform platform)
        => _ = BackgroundTask.Run(async () => {
            if (HeadlessBlazorScope.Current is not { } headless)
                return;

            var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
            chatAudioUI.StopReplay();
            await chatAudioUI.ClearListeningChats().ConfigureAwait(false);
            platform.OnHeadlessTeardown();
            await HeadlessBlazorScope.DisposeCurrent("stopped by the user").ConfigureAwait(false);
        }, Log, "StopHeadless failed", CancellationToken.None);

    // Private methods

    private static async Task StartPlayback(
        IServiceProvider scopedServices,
        ChatId chatId,
        Moment startedAt,
        bool isForeground,
        bool isHeadless,
        WalkieTalkiePlatform platform)
    {
        var hub = scopedServices.GetRequiredService<AppUIHub>();
        var chatAudioUI = hub.ChatAudioUI;
        if (isHeadless)
            chatAudioUI.IsWalkieTalkieHeadless = true;
        chatAudioUI.Enable();

        if (isForeground) {
            // The user is in the app: don't hijack their state with a forced replay -
            // just make sure the trigger chat is being listened to.
            await chatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
            await platform.OnForegroundWakeHandled(chatId).ConfigureAwait(false);
            return;
        }

        // The replay path bypasses ChatListeningPlayer, which normally plays this cue on
        // stream-start after a long lull - so the wake plays it explicitly.
        _ = hub.TuneUI.Play(Tune.NotifyOnNewAudioMessageAfterDelay);

        // The server gates wakes on the same settings; re-read them for the restore set.
        var restoreSet = await chatAudioUI.GetChatsYouNeedToKeepListeningTo(CancellationToken.None)
            .ConfigureAwait(false);
        if (!restoreSet.Contains(chatId))
            restoreSet = [..restoreSet, chatId];

        if (WalkieTalkie.IsStaleWake(startedAt, hub.Clocks.SystemClock.Now))
            foreach (var armedChatId in restoreSet)
                await chatAudioUI.SetListeningState(armedChatId, true).ConfigureAwait(false);
        else
            await chatAudioUI.StartWalkieTalkieReplay(chatId, startedAt, restoreSet).ConfigureAwait(false);

        _ = platform.OnPlaybackStarted(hub, chatId);
    }

    private static void EnsureTeardownWatcher(WalkieTalkiePlatform platform)
    {
        lock (Lock)
            _teardownWatcher ??= BackgroundTask.Run(
                () => WatchTeardown(platform), Log, "Teardown watcher failed", CancellationToken.None);
    }

    private static async Task WatchTeardown(WalkieTalkiePlatform platform)
    {
        try {
            var idleChecks = 0;
            while (true) {
                await Task.Delay(TeardownCheckPeriod).ConfigureAwait(false);
                if (HeadlessBlazorScope.Current is not { } headless)
                    return; // The WebView scope owns audio now

                var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
                var listeningChatIds = await chatAudioUI.GetListeningChatIds().ConfigureAwait(false);
                if (!listeningChatIds.IsEmpty || chatAudioUI.ReplayState.Value is not null) {
                    idleChecks = 0;
                    continue;
                }

                // Two consecutive idle checks: the replay-ended -> listening-restored transition
                // has a short gap that must not read as "session over".
                if (++idleChecks < TeardownIdleChecks)
                    continue;

                Log.LogInformation("Walkie-talkie: headless session is idle, tearing down");
                platform.OnHeadlessTeardown();
                await HeadlessBlazorScope.DisposeCurrent("armed (idle)").ConfigureAwait(false);
                return;
            }
        }
        finally {
            lock (Lock)
                _teardownWatcher = null;
        }
    }
}
```

- [ ] **Step 3: Refactor the Android handler onto the core**

Rewrite `src/dotnet/App.Maui/Platforms/Android/Audio/WalkieTalkieWakeHandler.cs`
to keep ONLY: `Handle` (payload validation, foreground check, guarded FGS
start, `EnsureStarted`, dispatch), `StopHeadlessSession`, the three FGS
methods, `ShowFallbackNotification`, and a nested platform:

```csharp
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Android shell for walkie-talkie wakes: FGS lifecycle + FCM entry point;
/// the portable core lives in <see cref="WalkieTalkieSession"/>.
/// </summary>
public static class WalkieTalkieWakeHandler
{
    private static ILogger Log => field ??= StaticLog.For(typeof(WalkieTalkieWakeHandler));

    public static void Handle(NotificationData data)
    {
        if (data.ChatId is not { } chatId || data.StartedAt is not { } startedAt) {
            Log.LogWarning("Invalid SpeechStarted push, message #{MessageId}", data.MessageId);
            return;
        }

        var isForeground = AndroidUtils.IsAppForeground() ?? false;
        if (!isForeground)
            try {
                // First and synchronously: FGS start must land inside the FCM high-priority
                // exemption window; the service self-guards the 5s startForeground rule.
                ShowForegroundService(chatId, "Listening…");
            }
            catch (Exception e) {
                // Denied FGS start (OEM restrictions etc.) must not kill the wake:
                // playback is still attempted, and any later failure shows the fallback.
                Log.LogWarning(e, "Couldn't start the audio FGS for chat #{ChatId}", chatId);
            }
        BlazorWebViewApp.EnsureStarted();
        _ = BackgroundTask.Run(
            () => WalkieTalkieSession.HandleWake(chatId, startedAt, isForeground, AndroidPlatform.Instance),
            Log, "SpeechStarted wake failed", CancellationToken.None);
    }

    public static void StopHeadlessSession()
        => WalkieTalkieSession.StopHeadless(AndroidPlatform.Instance);

    // Private methods

    private static async Task UpdateForegroundServiceTitle(AppUIHub hub, ChatId chatId)
    {
        try {
            var chat = await hub.Chats.Get(hub.Session, chatId, CancellationToken.None).ConfigureAwait(false);
            if (chat is not null)
                ShowForegroundService(chatId, chat.Title);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't update the FGS title for chat #{ChatId}", chatId);
        }
    }

    private static void ShowForegroundService(ChatId chatId, string title)
    {
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        intent.SetAction(AndroidAudioWidgetForegroundService.ActionShow);
        intent.PutExtra(IntentExtras.Mode, (int)AudioWidgetMode.Listening);
        intent.PutExtra(IntentExtras.ChatId, chatId.Value);
        intent.PutExtra(IntentExtras.ChatTitle, title);
        intent.PutExtra(IntentExtras.ChatPicUri, "");
        intent.PutExtra(IntentExtras.ExtraChatCount, 0);
        intent.PutExtra(IntentExtras.IsPaused, false);
        context.StartForegroundService(intent);
        AndroidAudioWidget.MarkForegroundServiceShown();
    }

    private static void HideForegroundService()
    {
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        context.StopService(intent);
        AndroidAudioWidget.MarkForegroundServiceHidden();
    }

    private static void ShowFallbackNotification(ChatId chatId)
        => NotificationHelper.ShowChatNotification(
            chatId.Value,
            "Voxt",
            "Someone is talking in a chat you keep listening to",
            null,
            Links.Chat(chatId),
            silent: false);

    // Nested types

    private sealed class AndroidPlatform : WalkieTalkiePlatform
    {
        public static readonly AndroidPlatform Instance = new();

        public override void OnWakeFailed(ChatId chatId)
        {
            ShowFallbackNotification(chatId);
            HideForegroundService();
        }

        public override void OnHeadlessTeardown()
            => HideForegroundService();

        public override Task OnPlaybackStarted(AppUIHub hub, ChatId chatId)
            => UpdateForegroundServiceTitle(hub, chatId);
    }
}
```

Behavioral parity checklist (verify by diffing against the pre-refactor
file): FGS-first ordering, foreground early-return, cue placement,
restore-set logic, stale branch, teardown double-check, `_isShown` marks,
fallback content — all identical; the only changes are indirection through
`WalkieTalkiePlatform` and log source names.

- [ ] **Step 4: Build the shared solution (guard)**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: 0 errors (App.Maui isn't in it; this guards the shared projects).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/Services/WalkieTalkiePlatform.cs \
        src/dotnet/App.Maui/Services/WalkieTalkieSession.cs \
        src/dotnet/App.Maui/Platforms/Android/Audio/WalkieTalkieWakeHandler.cs
git commit -m "refactor(maui): extract platform-neutral WalkieTalkieSession from the Android handler

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Externally-activated audio-session mode + iOS idle-watcher gate

**Files:**
- Modify: `src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs:614-617`

**Interfaces:**
- Produces: `AudioSession.IsExternallyActivated` (public static volatile
  bool) — while true, `AudioSession` configures category/routes but never
  calls `SetActive` (the PTT delegate owns activation). Task 6 sets/clears it.

- [ ] **Step 1: Add the flag and gate SetActive calls**

In `src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs`:

(a) Add the field right after the class declaration line
(`public class AudioSession(AppUIHub hub) : IAsyncDisposable {`):

```csharp
    // Set while an Apple Push to Talk transmission owns AVAudioSession activation:
    // the PTT delegate activates/deactivates the session; we may only configure it.
    public static volatile bool IsExternallyActivated;
```

(b) In `DisposeAsync`, wrap the deactivation:

```csharp
    public ValueTask DisposeAsync()
        => BackgroundTask.Run(() => DispatchToMainThread(() => {
                    if (IsExternallyActivated)
                        return;

                    var session = AVAudioSession.SharedInstance();
                    session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation)
                        .Assert("Failed to deactivate session");
                }),
                Log,
                "Failed to dispose AudioSession")
            .ToValueTask();
```

(c) In `ReactivateUnsafe`, gate the activation block:

```csharp
    private void ReactivateUnsafe(AudioFocusMode mode)
    {
        var session = AVAudioSession.SharedInstance();
        ConfigureUnsafe(session, mode);
        if (IsExternallyActivated)
            return;

        if (!session.SetActive(true, out var error)) {
            Log.LogWarning("Failed to re-activate audio session: {Error}", error.LocalizedDescription);
            // Deactivate and retry
            var deactivateOptions = mode is AudioFocusMode.Tune
                ? AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation
                : 0;
            session.SetActive(false, deactivateOptions, out _);
            session.SetActive(true, out error);
            error.Assert("Failed to re-activate audio session after retry");
        }
    }
```

(d) In `ReconfigureUnsafe`, gate both SetActive calls:

```csharp
    private void ReconfigureUnsafe(AudioFocusMode minMode)
    {
        var session = AVAudioSession.SharedInstance();
        if (IsExternallyActivated) {
            ConfigureUnsafe(session, minMode);
            return;
        }

        var deactivateOptions = minMode is AudioFocusMode.Tune
            ? AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation
            : 0;
        session.SetActive(false, deactivateOptions).Assert("Failed to deactivate session");
        ConfigureUnsafe(session, minMode);
        session.SetActive(true).Assert("Failed to activate session");
    }
```

- [ ] **Step 2: Extend the idle-watcher gate to iOS**

In `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs`, replace:

```csharp
        // Only Android has the FCM wake path that re-arms dropped listening;
        // gate others out until they get one (iOS: sub-project C).
        if (HostInfo.AppKind != AppKind.Android)
            return;
```

with:

```csharp
        // Only platforms with a wake path that re-arms dropped listening:
        // FCM data pushes on Android, Apple Push to Talk on iOS.
        if (HostInfo.AppKind is not (AppKind.Android or AppKind.Ios))
            return;
```

- [ ] **Step 3: Build + test the shared solution**

Run:
```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj 2>&1 | tail -3
```
Expected: 0 errors; all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs \
        src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs
git commit -m "feat(audio): externally-activated AVAudioSession mode; iOS joins the walkie-talkie idle gate

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: iOS Push to Talk integration

**Files:**
- Create: `src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs`
- Create: `src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs`
- Modify: `src/dotnet/App.Maui/MauiProgram.iOS.cs` (register + initialize)
- Modify: `src/dotnet/App.Maui/MauiBlazorApp.cs` (resolve the watcher per scope, `#if IOS`)
- Modify: `src/dotnet/App.Maui/Platforms/iOS/Entitlements.dev.plist`
- Modify: `src/dotnet/App.Maui/Platforms/iOS/Entitlements.prod.plist`
- Modify: `src/dotnet/App.Maui/Platforms/iOS/Info.plist` (UIBackgroundModes)

**Interfaces:**
- Consumes: `WalkieTalkieSession.HandleWake` / `WalkieTalkiePlatform`
  (Task 4), `AudioSession.IsExternallyActivated` (Task 5),
  `MauiNotifications.RefreshNotificationToken(string, DeviceType, ct)`
  (registered in the app container — resolve via
  `BlazorWebViewApp.WhenAppReady`, NOT `IPlatformApplication.Current.Services`),
  `Notifications_DeregisterDevice(Session, Symbol)`,
  `DeviceType.iOSPttApp` (Task 1),
  `Constants.Notification.MessageDataKeys.{Kind,ChatId,Timestamp}`,
  PushToTalk bindings (exact C# names): `PTChannelManager.Create(delegate,
  restorationDelegate, completion)`, `RequestJoinChannel(NSUuid,
  PTChannelDescriptor)`, `LeaveChannel(NSUuid)`, `ActiveChannelUuid`,
  `SetActiveRemoteParticipant(PTParticipant?, NSUuid, Action<NSError>)`,
  `SetTransmissionMode(PTTransmissionMode.ListenOnly, NSUuid, Action<NSError>)`,
  delegate methods `ReceivedEphemeralPushToken(PTChannelManager, NSData)`,
  `IncomingPushResult(PTChannelManager, NSUuid, NSDictionary<NSString,
  NSObject>)` → `PTPushResult`, `DidActivateAudioSession` /
  `DidDeactivateAudioSession(PTChannelManager, AVAudioSession)`,
  `DidJoinChannel` / `DidLeaveChannel(PTChannelManager, NSUuid,
  PTChannelJoin/LeaveReason)`, `PTPushResult.Create(PTParticipant)`,
  `PTChannelRestorationDelegate.Create(NSUuid)` → `PTChannelDescriptor`.
- Produces: `IosPushToTalk.Initialize()`, `.EnsureJoined()`, `.Leave()`,
  `.ClearActiveParticipant()`; scoped `IosPushToTalkUI` armed-set watcher.

- [ ] **Step 1: Add the entitlement to both entitlements files**

In `src/dotnet/App.Maui/Platforms/iOS/Entitlements.dev.plist` AND
`Entitlements.prod.plist`, add inside the top-level `<dict>` (after the
`keychain-access-groups` array):

```xml
	<key>com.apple.developer.push-to-talk</key>
	<true/>
```

- [ ] **Step 2: Add the background mode**

In `src/dotnet/App.Maui/Platforms/iOS/Info.plist`, in the
`UIBackgroundModes` array (after `<string>location</string>`):

```xml
		<string>push-to-talk</string>
```

- [ ] **Step 3: Create IosPushToTalk**

`src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs`
(`Platforms/iOS` compiles only for the iOS TFM — no `#if IOS` needed):

```csharp
using ActualChat.App.Maui.Services;
using ActualChat.Security;
using AVFoundation;
using Foundation;
using PushToTalk;
using UIKit;
using DeviceType = ActualChat.Notifications.DeviceType;

namespace ActualChat.App.Maui;

/// <summary>
/// Process-level Apple Push to Talk integration: one aggregate "Voxt" channel whose join
/// survives app kill/reboot; incoming PTT pushes route into <see cref="WalkieTalkieSession"/>.
/// Receive-only: the channel runs in ListenOnly transmission mode.
/// </summary>
public static class IosPushToTalk
{
    public const string ChannelName = "Voxt";
    private static readonly NSUuid ChannelUuid = new("f3b9a7e2-4c15-4a8e-9f2d-7b6c5d4e3f21");

    private static readonly Lock Lock = new();
    private static PTChannelManager? _manager;
    private static ManagerDelegate? _managerDelegate;
    private static RestorationDelegate? _restorationDelegate;
    private static volatile string _pttToken = "";
    private static volatile PendingWake? _pendingWake;
    private static ILogger Log => field ??= StaticLog.For(typeof(IosPushToTalk));

    public static void Initialize()
    {
        lock (Lock) {
            if (_managerDelegate is not null)
                return;

            _managerDelegate = new ManagerDelegate();
            _restorationDelegate = new RestorationDelegate();
        }
        PTChannelManager.Create(_managerDelegate, _restorationDelegate, (manager, error) => {
            if (error is not null) {
                Log.LogError("PTChannelManager.Create failed: {Error}", error.LocalizedDescription);
                return;
            }

            lock (Lock)
                _manager = manager;
            Log.LogInformation("PTChannelManager ready");
        });
    }

    public static void EnsureJoined()
    {
        var manager = _manager;
        if (manager is null || manager.ActiveChannelUuid is not null)
            return;

        Log.LogInformation("Joining the PTT channel");
        manager.RequestJoinChannel(ChannelUuid, NewDescriptor());
    }

    public static void Leave()
    {
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        Log.LogInformation("Leaving the PTT channel");
        manager.LeaveChannel(ChannelUuid);
    }

    public static void ClearActiveParticipant()
    {
        var manager = _manager;
        if (manager is null)
            return;

        manager.SetActiveRemoteParticipant(null!, ChannelUuid, error => {
            if (error is not null)
                Log.LogWarning("SetActiveRemoteParticipant(null) failed: {Error}", error.LocalizedDescription);
        });
    }

    // Private methods

    private static PTChannelDescriptor NewDescriptor()
        => new(ChannelName, UIImage.FromBundle("AppIcon"));

    private static void RegisterToken(string token)
    {
        _pttToken = token;
        _ = BackgroundTask.Run(async () => {
            var app = await BlazorWebViewApp.WhenAppReady.ConfigureAwait(false);
            // MauiNotifications lives in the app container, not the MAUI root container.
            var mauiNotifications = app.Services.GetRequiredService<MauiNotifications>();
            await mauiNotifications.RefreshNotificationToken(token, DeviceType.iOSPttApp, CancellationToken.None)
                .ConfigureAwait(false);
        }, Log, "PTT token registration failed", CancellationToken.None);
    }

    private static void DeregisterToken()
    {
        var token = _pttToken;
        _pttToken = "";
        if (token.IsNullOrEmpty())
            return;

        _ = BackgroundTask.Run(async () => {
            var app = await BlazorWebViewApp.WhenAppReady.ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            var session = await sessionResolver.SessionTask.ConfigureAwait(false);
            var commander = app.Services.GetRequiredService<ICommander>();
            await commander.Call(new Notifications_DeregisterDevice(session, token), CancellationToken.None)
                .ConfigureAwait(false);
        }, Log, "PTT token deregistration failed", CancellationToken.None);
    }

    private static void OnAudioSessionActivated()
    {
        AudioSession.IsExternallyActivated = true;
        var wake = Interlocked.Exchange(ref _pendingWake, null);
        if (wake is null)
            return;

        BlazorWebViewApp.EnsureStarted();
        _ = BackgroundTask.Run(async () => {
            var isForeground = await AppServicesAccessor
                .DispatchToMainThread(() => UIApplication.SharedApplication.ApplicationState
                    == UIApplicationState.Active)
                .ConfigureAwait(false);
            await WalkieTalkieSession.HandleWake(wake.ChatId, wake.StartedAt, isForeground, IosPlatform.Instance)
                .ConfigureAwait(false);
        }, Log, "PTT wake failed", CancellationToken.None);
    }

    // Nested types

    private sealed record PendingWake(ChatId ChatId, Moment StartedAt);

    private sealed class IosPlatform : WalkieTalkiePlatform
    {
        public static readonly IosPlatform Instance = new();

        public override void OnWakeFailed(ChatId chatId)
            => ClearActiveParticipant();

        public override void OnHeadlessTeardown()
            => ClearActiveParticipant();

        public override Task OnForegroundWakeHandled(ChatId chatId)
        {
            // Foreground: the app manages its own session; end the PTT transmission right away.
            ClearActiveParticipant();
            return Task.CompletedTask;
        }
    }

    private sealed class ManagerDelegate : PTChannelManagerDelegate
    {
        public override void DidJoinChannel(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelJoinReason reason)
        {
            Log.LogInformation("PTT channel joined ({Reason})", reason);
            // Receive-only v1: no transmit button in the system UI.
            channelManager.SetTransmissionMode(PTTransmissionMode.ListenOnly, channelUuid, error => {
                if (error is not null)
                    Log.LogWarning("SetTransmissionMode failed: {Error}", error.LocalizedDescription);
            });
        }

        public override void DidLeaveChannel(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelLeaveReason reason)
        {
            Log.LogInformation("PTT channel left ({Reason})", reason);
            DeregisterToken();
        }

        public override void DidBeginTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        { }

        public override void DidEndTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        { }

        public override void ReceivedEphemeralPushToken(PTChannelManager channelManager, NSData pushToken)
        {
            var token = Convert.ToHexString(pushToken.ToArray()).ToLower();
            Log.LogInformation("PTT push token received ({Length} bytes)", pushToken.Length);
            RegisterToken(token);
        }

        public override PTPushResult IncomingPushResult(
            PTChannelManager channelManager, NSUuid channelUuid, NSDictionary<NSString, NSObject> pushPayload)
        {
            // Must return synchronously and fast; playback starts in DidActivateAudioSession.
            var chatSid = GetString(pushPayload, Constants.Notification.MessageDataKeys.ChatId);
            var sTimestamp = GetString(pushPayload, Constants.Notification.MessageDataKeys.Timestamp);
            var chatTitle = GetString(pushPayload, "chatTitle") ?? ChannelName;
            var chatId = ChatId.TryParse(chatSid, allowNull: true);
            if (chatId is not { } vChatId || vChatId.IsNone || !long.TryParse(sTimestamp, out var epochMs)) {
                Log.LogWarning("Invalid PTT push payload");
                return PTPushResult.Create(new PTParticipant(ChannelName, null!));
            }

            _pendingWake = new PendingWake(vChatId, new Moment(epochMs * 10_000));
            return PTPushResult.Create(new PTParticipant(chatTitle, null!));
        }

        public override void DidActivateAudioSession(PTChannelManager channelManager, AVAudioSession audioSession)
        {
            Log.LogInformation("PTT audio session activated");
            OnAudioSessionActivated();
        }

        public override void DidDeactivateAudioSession(PTChannelManager channelManager, AVAudioSession audioSession)
        {
            Log.LogInformation("PTT audio session deactivated");
            AudioSession.IsExternallyActivated = false;
        }

        private static string? GetString(NSDictionary<NSString, NSObject> dict, string key)
            => dict[new NSString(key)]?.ToString();
    }

    private sealed class RestorationDelegate : PTChannelRestorationDelegate
    {
        public override PTChannelDescriptor Create(NSUuid channelUuid)
            => NewDescriptor();
    }
}
```

Binding-mismatch note for the implementer: the delegate base classes,
member names, and `PTPushResult.Create(PTParticipant)` come from the
dotnet/macios binding definition; if a member name differs on the current
SDK (e.g. a `Failed*` overload signature), adjust mechanically to the real
binding and record it in the report — do NOT change the flow.

- [ ] **Step 4: Create the armed-set watcher**

`src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs`:

```csharp
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Interception;

namespace ActualChat.App.Maui;

/// <summary>
/// Scoped watcher: joins the PTT channel while the user has armed ("Keep listening")
/// chats and leaves it when the last one is disarmed.
/// </summary>
public class IosPushToTalkUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), INotifyInitialized
{
    void INotifyInitialized.Initialized()
        => this.Start();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var chatAudioUI = Hub.ChatAudioUI;
        var cArmedChatIds = await Computed
            .Capture(() => chatAudioUI.GetChatsYouNeedToKeepListeningTo(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await foreach (var change in cArmedChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value.Count != 0)
                IosPushToTalk.EnsureJoined();
            else
                IosPushToTalk.Leave();
        }
    }
}
```

- [ ] **Step 5: Wire registration and initialization**

(a) In `src/dotnet/App.Maui/MauiProgram.iOS.cs`, in
`ConfigureBlazorWebViewAppPlatformServices`, after the
`IosPushNotifications` block, add:

```csharp
        services.AddScoped(c => new IosPushToTalkUI(c.AppUIHub()));
```

and in `ConfigurePlatformLifecycleEvents`'s `FinishedLaunching` lambda,
after `FirebaseCloudMessagingImplementation.Initialize();` (inside the
`#if !HOTRESTART` block), add:

```csharp
            IosPushToTalk.Initialize();
```

(b) In `src/dotnet/App.Maui/MauiBlazorApp.cs`, in `OnInitializedAsync`,
right after the `await _mauiWebView.SetScopedServices(...)` line, add:

```csharp
#if IOS
        _ = Services.GetService<IosPushToTalkUI>();
#endif
```

- [ ] **Step 6: Build the shared solution (guard)**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: 0 errors (no iOS compile is possible here — that's the known,
documented limitation; static verification is the review's job).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs \
        src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs \
        src/dotnet/App.Maui/MauiProgram.iOS.cs \
        src/dotnet/App.Maui/MauiBlazorApp.cs \
        src/dotnet/App.Maui/Platforms/iOS/Entitlements.dev.plist \
        src/dotnet/App.Maui/Platforms/iOS/Entitlements.prod.plist \
        src/dotnet/App.Maui/Platforms/iOS/Info.plist
git commit -m "feat(ios): Apple Push to Talk integration - aggregate channel, wake playback

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full shared build + all affected suites**

Run:
```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj 2>&1 | tail -3
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -4
```
Expected: 0 errors; all pass (the Notifications suite now includes the 3 new
walkie-talkie facts + 4 ApnsClient facts on top of the pre-existing 91).

- [ ] **Step 2: Confirm clean tree**

Run: `git status --short` — expected: empty.

Host-deferred (document in the final summary, not automatable here):
`net10.0-android` build (Task 4's refactor touches the Android handler),
any iOS build (Tasks 4-6), and the on-device manual script from the spec's
Testing section (requires the APNs key + entitlement/provisioning setup).
