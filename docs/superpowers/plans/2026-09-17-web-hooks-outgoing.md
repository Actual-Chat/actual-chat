# Web hooks — phase 1 (outgoing) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Outgoing web hooks at chat, place and personal scope: registration + secrets, event
fan-in into a durable outbox, signed ordered delivery with retries and auto-disable, a delivery
log, and the settings UI.

**Architecture:** Everything lives in existing projects. `Api` / `Api.Contracts` hold the models
and `IWebHooks`; `Chat.Contracts` holds `IWebHooksBackend`; `Chat.Service` holds the backend,
the event handlers, the `WebHookDeliverer` (sign + POST) and the per-hook `WebHookDeliveryFlow`;
`UI.Blazor.App` holds the tiles. `EgressGuard` moves to `Core.Server` so both link crawling and
hooks use one SSRF guard.

**Tech Stack:** .NET 11 / C# 15, ActualLab.Fusion (compute methods, commands, events on NATS,
Flows), EF Core + PostgreSQL, ASP.NET Data Protection, Blazor.

**Spec:** `docs/superpowers/specs/2026-09-17-web-hooks-design.md`

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#/razor. Notable: no `Async` suffix; `//`
  comments only when non-obvious; mixed brace style (types/methods Allman, everything else K&R);
  control-flow statements on their own line + blank line after; member ordering; `sealed` by
  default except Fusion-proxied services; `.ConfigureAwait(false)` in services.
- Serialization: `[DataContract, MessagePackObject]` + `[DataMember, Key(N)]` on every RPC/API
  record; never MemoryPack; exclude computed members with all four ignore attributes.
- Tests: FluentAssertions, `Should` naming, `// arrange / act / assert` comments.
- UI strings only via `L.*` (`LocalizedStringsLocalizerExt`), keys in all 22 hand-written
  catalogs + derive scripts (`scripts/derive-bcms.cmd`, `scripts/derive-max.cmd`).
- Style hook runs on every `.cs/.razor/.css` edit; fix everything it reports.
- Build a test project, not the CI solution filter (it is stale locally): e.g.
  `dotnet build tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj`.
- Migrations: build `Chat.Service.Migration` first, then
  `./ef-migrations.cmd Chat.Service add <Name>` (runs `dotnet ef migrations --no-build …`).
- Commit after every task; never push unless asked.
- Deferred to phase 2 (deviation from the spec, agreed to keep phase 1 tractable): lifecycle
  system entries in the chat/place (needs a new `SystemEntry` union member across `ChatEntry`,
  `DbChatEntry`, legacy mapping, markup builders) and the owner push notification on
  auto-disable (needs a new `Notification` union member). Phase 1 surfaces the disabled state
  and its reason in the hook list instead. `McpChatMessage` → `ExternalMessage` switch for MCP
  is the last task and may be split out if it grows.

## File structure

| File | Responsibility |
|---|---|
| `src/dotnet/Core.Server/Net/EgressGuard.cs`, `SpecialAddresses.cs`, `HostWildcard.cs` | moved from `Media.Service`; settings from `CoreServerSettings.Egress*` |
| `src/dotnet/Core.Server/Net/EgressHttpClientExt.cs` | `AddEgressHttpClient` moved here |
| `src/dotnet/Core.Server/Security/StandardWebhookSigner.cs` | `whsec_` secrets, Standard Webhooks signature header |
| `src/dotnet/Api/WebHooks/WebHookId.cs`, `WebHook.cs`, `WebHookDiff.cs`, `WebHookEnums.cs`, `WebHookDelivery.cs`, `WebHookResults.cs` | client-visible models |
| `src/dotnet/Api/External/ExternalMessage.cs` | shared external message/author/attachment JSON models |
| `src/dotnet/Api.Contracts/WebHooks/IWebHooks.cs` | frontend contract + `WebHooks_*` commands |
| `src/dotnet/Chat.Contracts/IWebHooksBackend.cs` | backend contract + `WebHooksBackend_*` commands |
| `src/dotnet/Backend/Events/UserNotifiedEvent.cs` | new event from `NotificationsBackend.OnNotify` |
| `src/dotnet/Chat.Service/Db/DbWebHook.cs`, `DbWebHookDelivery.cs` | tables `WebHooks`, `WebHookDeliveries` |
| `src/dotnet/Chat.Service/WebHooks/WebHooks.cs` | frontend service (permissions) |
| `src/dotnet/Chat.Service/WebHooks/WebHooksBackend.cs` | CRUD, secrets, outbox commands |
| `src/dotnet/Chat.Service/WebHooks/WebHooksBackend.Events.cs` | event handlers → enqueue |
| `src/dotnet/Chat.Service/WebHooks/WebHookPayloads.cs` | envelope + `data` JSON per event |
| `src/dotnet/Chat.Service/WebHooks/WebHookDeliverer.cs` | drain one hook: guard, sign, POST, record |
| `src/dotnet/Chat.Service/WebHooks/WebHookDeliveryPruner.cs` | 30-day log retention |
| `src/dotnet/Chat.Service/Flows/WebHookDeliveryFlow.cs` | per-hook durable retry loop |
| `src/dotnet/UI.Blazor.App/Components/WebHooks/*.razor`, `web-hooks.css` | list, create pages, reveal, detail |

---

### Task 1: Move `EgressGuard` to `Core.Server`

**Files:**
- Move: `src/dotnet/Media.Service/EgressGuard.cs` → `src/dotnet/Core.Server/Net/EgressGuard.cs`
- Move: `src/dotnet/Media.Service/SpecialAddresses.cs` → `src/dotnet/Core.Server/Net/SpecialAddresses.cs`
- Move: `src/dotnet/Media.Service/HostWildcard.cs` → `src/dotnet/Core.Server/Net/HostWildcard.cs`
- Create: `src/dotnet/Core.Server/Net/EgressHttpClientExt.cs` (from `MediaServiceCollectionExt.AddEgressHttpClient`)
- Modify: `src/dotnet/Core.Server/Module/CoreServerSettings.cs`, `CoreServerModule.cs`
- Modify: `src/dotnet/Media.Service/Module/MediaSettings.cs` (drop `Crawling*`), `MediaServiceModule.cs` (drop `AddSingleton<EgressGuard>` and the ext class)
- Move: `tests/Media.UnitTests/EgressGuardTest.cs` → `tests/Core.Server.UnitTests/EgressGuardTest.cs`
- Modify: `tests/Mcp.IntegrationTests/Collections/McpCollection.cs`, `tests/Media.IntegrationTests/Collections/MediaCollection.cs`

**Interfaces:**
- Produces: `ActualChat.EgressGuard` (`Core.Server`, namespace `ActualChat`) with the unchanged
  public surface `IsAllowed(host, ct)`, `IsAllowedUri(uri)`, `IsAllowedAddress(host, address)`;
  `IServiceCollection.AddEgressHttpClient(name, maxResponseContentLength?)`;
  `CoreServerSettings.EgressCidrDenylist / EgressDomainDenylist / EgressHostAllowList`.

- [ ] **Step 1: Move the three files** with `git mv`, change namespaces to `ActualChat` (match
  `EgressHttpHandler`), replace the `MediaSettings settings` constructor parameter with
  `CoreServerSettings settings` and the three property reads with `settings.EgressCidrDenylist`,
  `settings.EgressDomainDenylist`, `settings.EgressHostAllowList`. Drop the `DebugLog` line's
  `Constants.DebugMode.TranscriptionTranslation` dependency: use `log.IfEnabled(LogLevel.Debug)`.

- [ ] **Step 2: Settings + registration**

```csharp
// CoreServerSettings — add after RpcProbeCountries
public string[] EgressCidrDenylist { get; set; } = [];
public string[] EgressDomainDenylist { get; set; } = [];
public string[] EgressHostAllowList { get; set; } = [];
```

In `CoreServerModule.InjectServices`, after `services.AddSingleton(_ => new RpcProbePolicy(Settings));`:
`services.AddSingleton<EgressGuard>();`

`Core.Server/Net/EgressHttpClientExt.cs`:

```csharp
namespace ActualChat;

public static class EgressHttpClientExt
{
    public static IHttpClientBuilder AddEgressHttpClient(
        this IServiceCollection services, string name, long? maxResponseContentLength = null)
        => services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(c => {
                var guard = c.GetRequiredService<EgressGuard>();
                var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress);
                if (maxResponseContentLength is { } max)
                    options = options with { MaxResponseContentLength = max };
                return new EgressHttpHandler(options);
            });
}
```

Delete `MediaServiceCollectionExt` and `services.AddSingleton<EgressGuard>()` from
`MediaServiceModule`; delete the three `Crawling*` properties from `MediaSettings`.

- [ ] **Step 3: Tests** — move `EgressGuardTest.cs`, fix its `using`s and its `NewGuard()`
  helper to build `CoreServerSettings`. Update the two collection fixtures:
  `cfg.AddInMemory<CoreServerSettings>((x => x.EgressHostAllowList, "localhost"))` (Mcp) and
  `"domain*.some"` (Media). Grep `Crawling` across `src`, `tests`, config files — must be zero hits.

- [ ] **Step 4: Build and run**

Run: `dotnet build tests/Core.Server.UnitTests/Core.Server.UnitTests.csproj && dotnet test tests/Core.Server.UnitTests --filter EgressGuardTest`
Then: `dotnet build tests/Media.IntegrationTests/Media.IntegrationTests.csproj && dotnet build tests/Mcp.IntegrationTests/Mcp.IntegrationTests.csproj`
Expected: builds clean, EgressGuard tests pass.

- [ ] **Step 5: Commit** — `refactor(core): move EgressGuard to Core.Server`

---

### Task 2: `StandardWebhookSigner`

**Files:**
- Create: `src/dotnet/Core.Server/Security/StandardWebhookSigner.cs`
- Test: `tests/Core.Server.UnitTests/StandardWebhookSignerTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class StandardWebhookSigner {
      public const string SecretPrefix = "whsec_";
      public static string NewSecret();                       // "whsec_" + base64(32 random bytes)
      public static string Sign(string secret, string id, long unixTimestamp, string body); // "v1,<base64>"
      public static string SignatureHeader(string id, long ts, string body, params ReadOnlySpan<string> secrets); // space-joined
      public static bool Verify(string secret, string id, long unixTimestamp, string body, string header, TimeSpan tolerance, Moment now);
  }
  ```

- [ ] **Step 1: Failing tests**

```csharp
public class StandardWebhookSignerTest
{
    [Fact]
    public void SignShouldMatchIndependentHmac()
    {
        // arrange
        var secret = StandardWebhookSigner.NewSecret();
        var key = Convert.FromBase64String(secret[StandardWebhookSigner.SecretPrefix.Length..]);
        var expected = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("msg_1.1758104122.{}")));

        // act
        var signature = StandardWebhookSigner.Sign(secret, "msg_1", 1758104122, "{}");

        // assert
        signature.Should().Be("v1," + expected);
    }

    [Fact]
    public void HeaderShouldCarryOneSignaturePerSecret()
    {
        var a = StandardWebhookSigner.NewSecret();
        var b = StandardWebhookSigner.NewSecret();
        var header = StandardWebhookSigner.SignatureHeader("id", 1, "{}", a, b);
        header.Split(' ').Should().HaveCount(2).And.AllSatisfy(x => x.Should().StartWith("v1,"));
    }

    [Fact]
    public void VerifyShouldRejectStaleTimestamp()
    {
        var secret = StandardWebhookSigner.NewSecret();
        var header = StandardWebhookSigner.SignatureHeader("id", 1000, "{}", secret);
        var now = Moment.EpochStart + TimeSpan.FromSeconds(1000 + 600);
        StandardWebhookSigner.Verify(secret, "id", 1000, "{}", header, TimeSpan.FromMinutes(5), now).Should().BeFalse();
        StandardWebhookSigner.Verify(secret, "id", 1000, "{}", header, TimeSpan.FromMinutes(15), now).Should().BeTrue();
    }

    [Fact]
    public void NewSecretShouldBe32RandomBytes()
        => Convert.FromBase64String(StandardWebhookSigner.NewSecret()["whsec_".Length..]).Should().HaveCount(32);
}
```

- [ ] **Step 2: Run** `dotnet test tests/Core.Server.UnitTests --filter StandardWebhookSignerTest` → FAIL (type missing).

- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ActualChat.Security;

/// <summary>
/// Standard Webhooks (standardwebhooks.com) signing: HMAC-SHA256 over "{id}.{timestamp}.{body}"
/// with a "whsec_"-prefixed base64 secret, rendered as "v1,&lt;base64&gt;".
/// </summary>
public static class StandardWebhookSigner
{
    public const string SecretPrefix = "whsec_";
    private const string Version = "v1,";

    public static string NewSecret()
        => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Sign(string secret, string id, long unixTimestamp, string body)
    {
        var key = Convert.FromBase64String(secret[SecretPrefix.Length..]);
        var payload = Encoding.UTF8.GetBytes($"{id}.{unixTimestamp}.{body}");
        return Version + Convert.ToBase64String(HMACSHA256.HashData(key, payload));
    }

    public static string SignatureHeader(string id, long unixTimestamp, string body, params ReadOnlySpan<string> secrets)
    {
        var parts = new string[secrets.Length];
        for (var i = 0; i < secrets.Length; i++)
            parts[i] = Sign(secrets[i], id, unixTimestamp, body);
        return string.Join(' ', parts);
    }

    public static bool Verify(
        string secret, string id, long unixTimestamp, string body, string header, TimeSpan tolerance, Moment now)
    {
        var age = now - (Moment.EpochStart + TimeSpan.FromSeconds(unixTimestamp));
        if (age.Duration() > tolerance)
            return false;

        var expected = Sign(secret, id, unixTimestamp, body);
        foreach (var part in header.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(part), Encoding.UTF8.GetBytes(expected)))
                return true;

        return false;
    }
}
```

- [ ] **Step 4: Run tests** → PASS.
- [ ] **Step 5: Commit** — `feat(core): Standard Webhooks signer`

---

### Task 3: Api models

**Files:**
- Create: `src/dotnet/Api/WebHooks/WebHookId.cs`, `WebHookEnums.cs`, `WebHook.cs`, `WebHookDiff.cs`, `WebHookDelivery.cs`, `WebHookResults.cs`
- Create: `src/dotnet/Api/External/ExternalMessage.cs`
- Modify: `src/dotnet/Api/Constants.cs` (new `WebHooks` class)
- Test: `tests/Chat.UnitTests/WebHookModelSerializationTest.cs`

**Interfaces (produces):**

`WebHookId` — copy `SharedLocationId.cs` verbatim, rename, `IdGenerator = new(16, Alphabet.AlphaNumeric)`,
doc: "Identifier of a web hook."

`WebHookEnums.cs`:
```csharp
namespace ActualChat.WebHooks;

public enum WebHookScope { Chat = 0, Place = 1, User = 2 }
public enum WebHookKind { Outgoing = 0, Incoming = 1 }
public enum WebHookDisabledReason { None = 0, Manual = 1, DeliveryFailures = 2, UnsafeUrl = 3 }
public enum WebHookDeliveryStatus { Pending = 0, Succeeded = 1, Failed = 2, Abandoned = 3 }

[Flags]
public enum WebHookEvents : long
{
    None = 0,
    MessagePosted = 1 << 0, MessageEdited = 1 << 1, MessageRemoved = 1 << 2,
    ReactionAdded = 1 << 3, ReactionRemoved = 1 << 4,
    MemberJoined = 1 << 5, MemberLeft = 1 << 6,
    ChatUpdated = 1 << 7, ChatCreated = 1 << 8, ChatArchived = 1 << 9,
    PlaceUpdated = 1 << 10, PlaceMemberJoined = 1 << 11, PlaceMemberLeft = 1 << 12,
    Notification = 1 << 13,
    Ping = 1 << 14,
    Messages = MessagePosted | MessageEdited | MessageRemoved,
    Reactions = ReactionAdded | ReactionRemoved,
    Members = MemberJoined | MemberLeft,
    ChatChanges = ChatUpdated | ChatCreated | ChatArchived,
    PlaceChanges = PlaceUpdated | PlaceMemberJoined | PlaceMemberLeft,
}

public static class WebHookEventsExt
{
    // "MessagePosted" -> "message.posted"; "PlaceMemberJoined" -> "place.member.joined"
    public static string ToEventType(this WebHookEvents single)
    {
        var name = single.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++) {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append('.');
            sb.Append(char.ToLower(name[i]));
        }
        return sb.ToString();
    }
}
```

`WebHook.cs`:
```csharp
[DataContract, MessagePackObject]
public sealed partial record WebHook(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] long Version
) : IHasId<WebHookId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public required WebHookScope Scope { get; init; }
    [DataMember, Key(3)] public required string ScopeId { get; init; }
    [DataMember, Key(4)] public WebHookKind Kind { get; init; }
    [DataMember, Key(5)] public string Name { get; init; } = "";
    [DataMember, Key(6)] public UserId CreatedBy { get; init; } = UserId.None;
    [DataMember, Key(7)] public Moment CreatedAt { get; init; }
    [DataMember, Key(8)] public Moment ModifiedAt { get; init; }
    [DataMember, Key(9)] public bool IsEnabled { get; init; } = true;
    [DataMember, Key(10)] public WebHookDisabledReason DisabledReason { get; init; }
    [DataMember, Key(11)] public Moment? LastActivityAt { get; init; }
    [DataMember, Key(12)] public string Url { get; init; } = "";
    [DataMember, Key(13)] public WebHookEvents Events { get; init; }
    [DataMember, Key(14)] public bool IncludeText { get; init; } = true;
    [DataMember, Key(15)] public ApiArray<ChatId> ChatIds { get; init; }
    [DataMember, Key(16)] public bool SubscribeNotifications { get; init; }
    [DataMember, Key(17)] public string? CustomHeaderName { get; init; }
    [DataMember, Key(18)] public int ConsecutiveFailures { get; init; }
    [DataMember, Key(19)] public int? LastStatusCode { get; init; }
    [DataMember, Key(20)] public string? LastError { get; init; }
    // Keys 21..24 reserved for phase 2 (BotLocalId, DisplayName, AvatarMediaId, DefaultChatId)

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsActiveOutgoing => IsEnabled && Kind == WebHookKind.Outgoing;

    public bool Covers(ChatId chatId)
        => Scope switch {
            WebHookScope.Chat => ScopeId == chatId.Value,
            WebHookScope.Place => ChatIds.Count == 0 || ChatIds.Contains(chatId),
            _ => ChatIds.Contains(chatId),
        };
}
```

`WebHookDiff.cs` (all nullable = "unchanged"; `CustomHeaderValue` is write-only):
```csharp
[DataContract, MessagePackObject(true)]
public sealed partial record WebHookDiff : RecordDiff
{
    [DataMember] public string? Name { get; init; }
    [DataMember] public string? Url { get; init; }
    [DataMember] public WebHookEvents? Events { get; init; }
    [DataMember] public bool? IncludeText { get; init; }
    [DataMember] public ApiArray<ChatId>? ChatIds { get; init; }
    [DataMember] public bool? SubscribeNotifications { get; init; }
    [DataMember] public string? CustomHeaderName { get; init; }
    [DataMember] public string? CustomHeaderValue { get; init; }
    [DataMember] public bool? IsEnabled { get; init; }
}
```

`WebHookDelivery.cs`:
```csharp
[DataContract, MessagePackObject]
public sealed partial record WebHookDelivery(
    [property: DataMember, Key(0)] string Id,
    [property: DataMember, Key(1)] WebHookId WebHookId)
{
    [DataMember, Key(2)] public long Seq { get; init; }
    [DataMember, Key(3)] public string EventType { get; init; } = "";
    [DataMember, Key(4)] public WebHookDeliveryStatus Status { get; init; }
    [DataMember, Key(5)] public int Attempts { get; init; }
    [DataMember, Key(6)] public Moment? NextAttemptAt { get; init; }
    [DataMember, Key(7)] public int? LastStatusCode { get; init; }
    [DataMember, Key(8)] public string? LastError { get; init; }
    [DataMember, Key(9)] public int? LastLatencyMs { get; init; }
    [DataMember, Key(10)] public Moment CreatedAt { get; init; }
    [DataMember, Key(11)] public Moment? CompletedAt { get; init; }
}
```

`WebHookResults.cs`:
```csharp
[DataContract, MessagePackObject]
public sealed partial record WebHookChangeResult(
    [property: DataMember, Key(0)] WebHook? WebHook,
    [property: DataMember, Key(1)] string? Secret);   // only on create

[DataContract, MessagePackObject]
public sealed partial record WebHookTestResult(
    [property: DataMember, Key(0)] bool IsSuccess,
    [property: DataMember, Key(1)] int? StatusCode,
    [property: DataMember, Key(2)] string? Error,
    [property: DataMember, Key(3)] int LatencyMs);
```

`Constants.cs` — nested `public static class WebHooks`:
```csharp
public const int MaxNameLength = 64;
public const int MaxPayloadLength = 256 * 1024;
public const int MaxPendingDeliveries = 10_000;
public const int DeliveryListLimit = 20;
public static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);
public static readonly TimeSpan DisableAfter = TimeSpan.FromHours(72);
public static readonly TimeSpan SecretOverlap = TimeSpan.FromHours(24);
public static readonly TimeSpan DeliveryRetention = TimeSpan.FromDays(30);
public static readonly TimeSpan[] RetryDelays = [
    TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
    TimeSpan.FromHours(2), TimeSpan.FromHours(12)];
```

`Api/External/ExternalMessage.cs` (plain STJ records, camelCase via `WebHookJson` later):
```csharp
namespace ActualChat.External;

public sealed record ExternalAuthor(string Id, string Name, string? AvatarUrl);

public sealed record ExternalAttachment(
    string MediaId, string Kind, string FileName, string ContentType, long Length,
    int Width, int Height, string Url, string? PreviewUrl, string? ThumbnailUrl);

public sealed record ExternalOrigin(string Kind, string? WebHookId = null); // "user" | "api" | "webhook" | "bot"

public sealed record ExternalMessage(
    long Id, long Version, long CreatedAt, ExternalAuthor Author,
    bool IsSystem, bool IsStreaming, bool IsTranscribed, bool IsRemoved,
    string? Text, bool? TextTruncated, ExternalAttachment[] Attachments,
    long? RepliedToId, string[] Mentions, string Url, ExternalOrigin Origin);
```

- [ ] **Step 1: Write a round-trip test** in `tests/Chat.UnitTests/WebHookModelSerializationTest.cs`
  following `ChatModelSerializationTest.cs` in the same folder (use its helper to
  MessagePack-serialize and deserialize `WebHook`, `WebHookDiff`, `WebHookDelivery`,
  `WebHookChangeResult`; assert `.Should().Be(original)`), plus:

```csharp
[Theory]
[InlineData(WebHookEvents.MessagePosted, "message.posted")]
[InlineData(WebHookEvents.PlaceMemberJoined, "place.member.joined")]
[InlineData(WebHookEvents.Ping, "ping")]
public void EventTypeShouldBeDottedLowerCase(WebHookEvents e, string expected)
    => e.ToEventType().Should().Be(expected);
```

- [ ] **Step 2: Run** `dotnet test tests/Chat.UnitTests --filter WebHook` → FAIL.
- [ ] **Step 3: Create the files above.** `WebHookId` goes in namespace `ActualChat` like other ids.
- [ ] **Step 4: Run tests** → PASS. Also `dotnet build src/dotnet/Api/Api.csproj`.
- [ ] **Step 5: Commit** — `feat(api): web hook models`

---

### Task 4: Contracts and the new event

**Files:**
- Create: `src/dotnet/Api.Contracts/WebHooks/IWebHooks.cs`
- Create: `src/dotnet/Chat.Contracts/IWebHooksBackend.cs`
- Create: `src/dotnet/Backend/Events/UserNotifiedEvent.cs`
- Modify: `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` (`fusion.AddClient<IWebHooks>();` next to `ISharedLocations`)
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` (`public IWebHooks WebHooks => field ??= Services.GetRequiredService<IWebHooks>();`)

**Interfaces (produces):**

```csharp
// Api.Contracts/WebHooks/IWebHooks.cs
namespace ActualChat.WebHooks;

public interface IWebHooks : IComputeService
{
    [ComputeMethod]
    Task<WebHook?> Get(Session session, WebHookId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHook>> List(Session session, WebHookScope scope, string scopeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHookDelivery>> ListDeliveries(Session session, WebHookId id, CancellationToken cancellationToken);

    [CommandHandler]
    Task<WebHookChangeResult> OnChange(WebHooks_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> OnRotateSecret(WebHooks_RotateSecret command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<WebHookTestResult> OnTest(WebHooks_Test command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRedeliver(WebHooks_Redeliver command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record WebHooks_Change : ApiCommand<WebHookChangeResult>
{
    [DataMember(Order = 2), Key(2)] public required WebHookScope Scope { get; init; }
    [DataMember(Order = 3), Key(3)] public required string ScopeId { get; init; }
    [DataMember(Order = 4), Key(4)] public WebHookId? Id { get; init; }
    [DataMember(Order = 5), Key(5)] public long? ExpectedVersion { get; init; }
    [DataMember(Order = 6), Key(6)] public required Change<WebHookDiff> Change { get; init; }
}
// WebHooks_RotateSecret : ApiCommand<string> { Key(2) WebHookId Id }
// WebHooks_Test : ApiCommand<WebHookTestResult> { Key(2) WebHookId Id }
// WebHooks_Redeliver : ApiCommand<Unit> { Key(2) WebHookId Id; Key(3) string DeliveryId }
```

```csharp
// Chat.Contracts/IWebHooksBackend.cs
namespace ActualChat.Chat;

public interface IWebHooksBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<WebHook?> Get(WebHookId id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListByScope(WebHookScope scope, string scopeId, CancellationToken cancellationToken);
    // Active outgoing hooks whose scope is the chat or the chat's place
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListActiveForChat(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHook>> ListActiveForUser(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<WebHookDelivery>> ListDeliveries(WebHookId id, int limit, CancellationToken cancellationToken);

    [CommandHandler]
    Task<WebHookChangeResult> OnChange(WebHooksBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<string> OnRotateSecret(WebHooksBackend_RotateSecret command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnEnqueue(WebHooksBackend_Enqueue command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRecordDelivery(WebHooksBackend_RecordDelivery command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDisable(WebHooksBackend_Disable command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRedeliver(WebHooksBackend_Redeliver command, CancellationToken cancellationToken);

    [EventHandler] Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler] Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler] Task OnAuthorUpsertedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler] Task OnAuthorsRemovedEvent(AuthorsRemovedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler] Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler] Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler] Task OnPlaceMembershipChangedEvent(PlaceMembershipChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler] Task OnUserNotifiedEvent(UserNotifiedEvent eventCommand, CancellationToken cancellationToken);
}
```

Backend commands, each `[DataContract, MessagePackObject]`, `IBackendCommand, IHasShardKey`
with the four-attribute-ignored `ShardKey`:

| Command | Members | Result | ShardKey |
|---|---|---|---|
| `WebHooksBackend_Change` | `WebHookScope Scope, string ScopeId, WebHookId? Id, long? ExpectedVersion, Change<WebHookDiff> Change, UserId ChangedBy` | `WebHookChangeResult` | `new ShardKey(ScopeId)` (see note) |
| `WebHooksBackend_RotateSecret` | `WebHookId Id, string ScopeId` | `string` | `ScopeId` |
| `WebHooksBackend_Enqueue` | `WebHookId Id, string ScopeId, string DeliveryId, string EventType, string Payload` | `Unit` | `ScopeId` |
| `WebHooksBackend_RecordDelivery` | `WebHookId Id, string ScopeId, string DeliveryId, WebHookDeliveryStatus Status, int? StatusCode, string? Error, int? LatencyMs, Moment? NextAttemptAt` | `Unit` | `ScopeId` |
| `WebHooksBackend_Disable` | `WebHookId Id, string ScopeId, WebHookDisabledReason Reason, string? Error` | `Unit` | `ScopeId` |
| `WebHooksBackend_Redeliver` | `WebHookId Id, string ScopeId, string DeliveryId` | `Unit` | `ScopeId` |

Shard key note: `ScopeId` is a `ChatId`, `PlaceId` or `UserId` string; compute it as
`ShardKey => WebHookScopeIds.ToShardKey(Scope, ScopeId)` where
`WebHookScopeIds` (put it in `Api/WebHooks/WebHookScopeIds.cs`) parses the string into the right
id type and returns its `ShardKey`. Read `ShardKey` in `Api/Identifiers` for the exact
constructor to use.

```csharp
// Backend/Events/UserNotifiedEvent.cs
[DataContract, MessagePackObject(true)]
public sealed partial record UserNotifiedEvent(
    [property: DataMember] Notification Notification
) : EventCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => Notification.UserId.ShardKey;
}
```

- [ ] **Step 1: Create the three files and register** as listed. `Backend` project must reference
  the `Notification` type — it already references `Api` (check `Backend.csproj`).
- [ ] **Step 2: Build** `dotnet build src/dotnet/Chat.Contracts/Chat.Contracts.csproj` and
  `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj` → clean. (No service yet; the
  client registration is enough for the UI project to compile.)
- [ ] **Step 3: Commit** — `feat(contracts): IWebHooks, IWebHooksBackend, UserNotifiedEvent`

---

### Task 5: Database entities and migration

**Files:**
- Create: `src/dotnet/Chat.Service/Db/DbWebHook.cs`, `src/dotnet/Chat.Service/Db/DbWebHookDelivery.cs`
- Modify: `src/dotnet/Chat.Service/Db/ChatDbContext.cs`, `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
- Create (generated): `src/dotnet/Chat.Service.Migration/Migrations/<ts>_Add_WebHooks.cs`
- Test: `tests/Chat.IntegrationTests/DbTest.cs` already checks the model/migration match — read it.

- [ ] **Step 1: `DbWebHook`** — follow `DbSharedLocation` exactly (`[Table("WebHooks")]`,
  `IHasId<string>, IHasVersion<long>, IRequirementTarget`, `[DbKey] Id`, `[ConcurrencyCheck] Version`,
  UTC `DateTime` properties with the `DefaultKind` getters/setters, `ToModel()`, `UpdateFrom(model)`).
  Columns: every `WebHook` member (`Scope`/`Kind`/`DisabledReason`/`Events` stored as their
  enum/long values, `ChatIds` as `string` joined by `,`, `CreatedBy` as string) plus:

```csharp
public string? SecretProtected { get; set; }
public string? PrevSecretProtected { get; set; }
public DateTime? PrevSecretExpiresAt { get; set; }   // UTC accessor pattern
public string? CustomHeaderValueProtected { get; set; }
public string? TokenHash { get; set; }               // phase 2, nullable now so the migration is one
```
Indexes: `[Index(nameof(ScopeId))]`, `[Index(nameof(TokenHash), IsUnique = true)]`.
`ToModel()` must **not** copy any `*Protected` column.

- [ ] **Step 2: `DbWebHookDelivery`** — `[Table("WebHookDeliveries")]`,
  `[Index(nameof(WebHookId), nameof(Seq))]`, `[Index(nameof(Status), nameof(NextAttemptAt))]`,
  `[Index(nameof(CreatedAt))]`. Columns = `WebHookDelivery` members + `string Payload`
  (`Status` as int). `ToModel()` omits `Payload`.

- [ ] **Step 3: `ChatDbContext`** — add `DbSet<DbWebHook> WebHooks` and
  `DbSet<DbWebHookDelivery> WebHookDeliveries`; in `OnModelCreating` add `UseCollation("C")`
  for `Id`, `ScopeId`, `CreatedBy`, `TokenHash` (hooks) and `Id`, `WebHookId` (deliveries),
  next to the `sharedLocation` block. In `ChatServiceModule` add
  `db.AddEntityResolver<string, DbWebHook>();` after `DbSharedLocation`.

- [ ] **Step 4: Migration**

Run: `dotnet build src/dotnet/Chat.Service.Migration/Chat.Service.Migration.csproj && ./ef-migrations.cmd Chat.Service add Add_WebHooks`
Expected: a new migration creating both tables and the four indexes; inspect it.

- [ ] **Step 5: Run** `dotnet test tests/Chat.IntegrationTests --filter DbTest` → PASS.
- [ ] **Step 6: Commit** — `feat(chat): WebHooks and WebHookDeliveries tables`

---

### Task 6: `WebHooksBackend` — CRUD, secrets, outbox commands

**Files:**
- Create: `src/dotnet/Chat.Service/WebHooks/WebHookSecrets.cs`
- Create: `src/dotnet/Chat.Service/WebHooks/WebHooksBackend.cs`
- Create: `src/dotnet/Api/WebHooks/WebHookScopeIds.cs` (if not done in Task 4)
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` (`rpcHost.AddBackend<IWebHooksBackend, WebHooksBackend>();` after `ISharedLocationsBackend`; `services.AddSingleton<WebHookSecrets>();`)
- Test: `tests/Chat.IntegrationTests/WebHooksBackendTest.cs`

**Interfaces:**
- Consumes: Task 3 models, Task 4 contracts, Task 5 entities, `StandardWebhookSigner`.
- Produces:
  ```csharp
  public sealed class WebHookSecrets(IServiceProvider services) {
      public string Protect(string value);
      public string Unprotect(string protectedValue);
      // Both active signing secrets, newest first; empty for an incoming hook
      public string[] GetSigningSecrets(DbWebHook dbWebHook, Moment now);
      public string? GetCustomHeaderValue(DbWebHook dbWebHook);
  }
  ```
  (`IDataProtector` from `services.GetRequiredService<IDataProtectionProvider>().CreateProtector("WebHooks")`.)

- [ ] **Step 1: Failing integration tests** (`[Collection(nameof(ChatCollection))]`, same base
  and Alice/Bob setup as `SharedLocationsTest`):

```csharp
[Fact]
public async Task CreateShouldReturnSecretOnceAndListByScope()
{
    // arrange
    var backend = AppHost.Services.GetRequiredService<IWebHooksBackend>();
    var commander = AppHost.Services.Commander();
    var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks" });
    var alice = await Alice.GetOwnAccount();

    // act
    var result = await commander.Call(new WebHooksBackend_Change(
        WebHookScope.Chat, chatId.Value, null, null,
        Change.Create(new WebHookDiff { Name = "CI", Url = "https://example.com/hook", Events = WebHookEvents.Messages }),
        alice.Id));

    // assert
    result.Secret.Should().StartWith("whsec_");
    result.WebHook!.Kind.Should().Be(WebHookKind.Outgoing);
    result.WebHook.IsEnabled.Should().BeTrue();
    var listed = await ComputedTest.When(async ct => {
        var hooks = await backend.ListByScope(WebHookScope.Chat, chatId.Value, ct);
        hooks.Should().ContainSingle(x => x.Id == result.WebHook.Id);
        return hooks;
    });
    (await backend.Get(result.WebHook.Id, default))!.Name.Should().Be("CI");
    (await backend.ListActiveForChat(chatId, default)).Should().ContainSingle();
}

[Fact]
public async Task UpdateAndRemoveShouldInvalidateLists() { /* update Name + IsEnabled=false → ListActiveForChat empty; Remove → Get null */ }

[Fact]
public async Task PlaceHookShouldBeListedForPlaceChats() { /* CreatePlace, create chat in it, place-scope hook → ListActiveForChat(chatInPlace) contains it */ }

[Fact]
public async Task RotateSecretShouldKeepOldOneForOverlap()
{
    // create hook; rotate; resolve WebHookSecrets + ChatDbContext, load DbWebHook,
    // GetSigningSecrets(db, now) has 2 entries, newest == returned secret;
    // GetSigningSecrets(db, now + 25h) has 1 entry
}

[Fact]
public async Task CreateShouldRejectHttpUrlAndEmptyEvents() { /* two Change.Create calls → each throws (StandardError.Constraint) */ }
```

- [ ] **Step 2: Run** `dotnet test tests/Chat.IntegrationTests --filter WebHooksBackendTest` → FAIL (no backend registered).

- [ ] **Step 3: Implement `WebHooksBackend`** (`DbServiceBase<ChatDbContext>`, `IWebHooksBackend`;
  partial class — event handlers come in Task 9, so declare `public partial class`).

Read methods:
- `Get` via `IDbEntityResolver<string, DbWebHook>`.
- `ListByScope` — `WHERE ScopeId == scopeId AND Scope == (int)scope`, ordered by `CreatedAt desc`.
- `ListActiveForChat(chatId)` — `var chat = await ChatsBackend.Get(chatId)`; union of
  `ListByScope(Chat, chatId)` and, if `chat?.PlaceId is { } placeId`, `ListByScope(Place, placeId)`;
  filter `IsActiveOutgoing && Covers(chatId)`. Calling the two compute methods makes this
  dependent on them, so `OnChange` only needs to invalidate `ListByScope` + `Get`.
- `ListActiveForUser(userId)` — `ListByScope(User, userId)` filtered `IsActiveOutgoing`.
- `ListDeliveries(id, limit)` — `WHERE WebHookId == id ORDER BY Seq DESC LIMIT limit`.

`OnChange`:
```csharp
var (scope, scopeId, id, expectedVersion, change, changedBy) = command;
var context = CommandContext.GetCurrent();
if (Invalidation.IsActive) {
    var invHook = context.Operation.Items.KeylessGet<WebHook>();
    if (invHook is not null) {
        _ = Get(invHook.Id, default);
        _ = ListByScope(invHook.Scope, invHook.ScopeId, default);
    }
    return default!;
}
change.RequireValid();
var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
await using var _ = dbContext.ConfigureAwait(false);
var now = Clocks.SystemClock.Now;
string? secret = null;
DbWebHook? dbWebHook = id is null ? null
    : await dbContext.WebHooks.FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken).ConfigureAwait(false);
if (change.IsCreate(out var createDiff)) {
    var webHook = new WebHook(WebHookId.New(), VersionGenerator.NextVersion()) {
        Scope = scope, ScopeId = scopeId, Kind = WebHookKind.Outgoing,
        CreatedBy = changedBy, CreatedAt = now, ModifiedAt = now,
    }.ApplyDiff(createDiff);
    Validate(webHook);
    secret = StandardWebhookSigner.NewSecret();
    dbWebHook = new DbWebHook(webHook) { SecretProtected = Secrets.Protect(secret) };
    ApplyCustomHeader(dbWebHook, createDiff);
    dbContext.Add(dbWebHook);
}
else if (change.IsUpdate(out var updateDiff)) {
    var webHook = dbWebHook.Require().ToModel().RequireVersion(expectedVersion)
        .ApplyDiff(updateDiff) with { ModifiedAt = now, Version = VersionGenerator.NextVersion(dbWebHook.Version) };
    if (updateDiff.IsEnabled == true) webHook = webHook with { DisabledReason = WebHookDisabledReason.None, ConsecutiveFailures = 0 };
    if (updateDiff.IsEnabled == false) webHook = webHook with { DisabledReason = WebHookDisabledReason.Manual };
    Validate(webHook);
    dbWebHook.UpdateFrom(webHook);
    ApplyCustomHeader(dbWebHook, updateDiff);
}
else {
    dbWebHook.Require();
    dbContext.Remove(dbWebHook);
    // Pending deliveries die with the hook
    await dbContext.WebHookDeliveries.Where(x => x.WebHookId == dbWebHook.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
}
await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
var model = dbWebHook.ToModel();
context.Operation.Items.KeylessSet(model);
return new WebHookChangeResult(change.IsRemove(out _) ? null : model, secret);
```
`WebHook.ApplyDiff(WebHookDiff)` is an extension in `Api/WebHooks/WebHookExt.cs` copying each
non-null diff member. `Validate` throws `StandardError.Constraint(...)` when: name empty or
`> Constants.WebHooks.MaxNameLength`; `Url` not absolute `https://` (or `http://` loopback when
`HostInfo.IsDevelopmentInstance`) — reuse `OAuthApplications.IsLoopback`-style check inline;
`Events == None`; scope `User` with `!SubscribeNotifications && ChatIds.Count == 0`.
`ApplyCustomHeader`: `if (diff.CustomHeaderValue is { } v) db.CustomHeaderValueProtected = v.IsNullOrEmpty() ? null : Secrets.Protect(v);`

`OnRotateSecret`: load, `PrevSecretProtected = SecretProtected; PrevSecretExpiresAt = now + SecretOverlap;
SecretProtected = Protect(new)`, bump version, invalidate `Get`, return the new secret.

`OnEnqueue`: insert `DbWebHookDelivery { Id = DeliveryId, WebHookId, Seq = VersionGenerator.NextVersion(),
EventType, Payload, Status = Pending, CreatedAt = now }` — **ignore a duplicate key**
(`catch (DbUpdateException) when (IsUniqueViolation(e))` → return; check `DbHub`/`ActualChat.Db`
for an existing `IsUniqueViolation` helper before writing one). Enforce the pending cap: count
pending for the hook; if `>= MaxPendingDeliveries`, mark the oldest pending `Abandoned` with
`LastError = "queue overflow"`. Then schedule the flow:
`await FlowHub.NewResumeEvent<WebHookDeliveryFlow>(Id.Value).Schedule(cancellationToken)` —
the flow type arrives in Task 10; until then leave a `// TODO(task 10)` line out and add the
call in Task 10 (this task's tests don't need delivery). Invalidate `ListDeliveries(Id, 20)`
— use the constant `Constants.WebHooks.DeliveryListLimit` everywhere so the key matches.

`OnRecordDelivery`: update the row (`Status`, `Attempts++`, `LastStatusCode`, `LastError`,
`LastLatencyMs`, `NextAttemptAt`, `CompletedAt = now` when terminal); update the hook's
`LastActivityAt`, `LastStatusCode`, `LastError`, and `ConsecutiveFailures` (0 on success,
`+1` on a terminal failure or retry). Invalidate `Get(Id)`, `ListByScope`, `ListDeliveries`.

`OnDisable`: set `IsEnabled = false`, `DisabledReason`, `LastError`; mark every `Pending`
delivery `Abandoned`; invalidate as above.

`OnRedeliver`: clone the row to `Id = $"{DeliveryId}:r{Attempts}"`, `Status = Pending`,
`Seq = NextVersion()`, `Attempts = 0`, `CreatedAt = now`; schedule the flow (Task 10).

- [ ] **Step 4: Run tests** → PASS.
- [ ] **Step 5: Commit** — `feat(chat): WebHooksBackend CRUD, secrets and outbox commands`

---

### Task 7: `WebHooks` frontend service and permissions

**Files:**
- Create: `src/dotnet/Chat.Service/WebHooks/WebHooks.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` (`rpcHost.AddApi<IWebHooks, WebHooks>();`)
- Test: `tests/Chat.IntegrationTests/WebHooksPermissionsTest.cs`

**Interfaces:**
- Consumes: `IWebHooksBackend`, `IChats.GetRules`, `IPlaces.GetRules`, `IAccounts.GetOwn`.
- Produces: `IWebHooks` implementation; `OnTest`/`OnRedeliver` bodies are completed in Task 10
  (this task returns `throw StandardError.NotSupported("...")` placeholders replaced there).

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public async Task NonModeratorShouldNotSeeOrCreateChatHooks()
{
    // arrange: Alice creates a public chat, Bob joins via invite (see ChatOperations.JoinChat)
    // act/assert: Bob's IWebHooks.List(scope Chat) throws Unauthorized; Bob's WebHooks_Change Create throws;
    // Alice's Create succeeds; Alice's List has 1
}

[Fact]
public async Task PersonalHooksShouldBeInvisibleToOthers()
{
    // Alice creates scope=User hook (ScopeId = alice.Id, SubscribeNotifications = true)
    // Bob: List(User, alice.Id) throws Unauthorized; Bob: Get(hookId) returns null
}

[Fact]
public async Task PlaceHookShouldRequireOwner() { /* Bob (member, not owner) create → throws; Alice owner → ok */ }
```

- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Pattern from `SharedLocations.cs`. One private helper:

```csharp
private async Task<AccountFull> RequireManager(Session session, WebHookScope scope, string scopeId, CancellationToken cancellationToken)
{
    var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
    account.Require(AccountFull.MustBeActive);
    switch (scope) {
    case WebHookScope.Chat:
        var chatRules = await Chats.GetRules(session, ChatId.Parse(scopeId), cancellationToken).ConfigureAwait(false);
        chatRules.Require(ChatPermissions.Moderate);
        break;
    case WebHookScope.Place:
        var placeRules = await Places.GetRules(session, PlaceId.Parse(scopeId), cancellationToken).ConfigureAwait(false);
        if (!placeRules.IsOwner())
            throw StandardError.Unauthorized("Only place owners can manage its integrations.");
        break;
    default:
        if (account.Id.Value != scopeId)
            throw StandardError.Unauthorized("You can manage only your own web hooks.");
        break;
    }
    return account;
}
```
`Get(session, id)`: backend `Get`, then `RequireManager(hook.Scope, hook.ScopeId)` — return
`null` instead of throwing when unauthorized (use a `try/catch` on the `Unauthorized` error?
No: add a `TryGetManager` variant returning `bool`). `List`, `ListDeliveries`, `OnChange`,
`OnRotateSecret`, `OnRedeliver`, `OnTest` all call `RequireManager` then delegate with
`Commander.Call(backendCommand, true, ct)`; `OnChange` passes `account.Id` as `ChangedBy`.
For `Update`/`Remove` load the hook first and take scope/scopeId from it (the command's
scope must match, else `Constraint`).

- [ ] **Step 4: Run tests** → PASS.
- [ ] **Step 5: Commit** — `feat(chat): IWebHooks frontend service`

---

### Task 8: Payload builder

**Files:**
- Create: `src/dotnet/Chat.Service/WebHooks/WebHookJson.cs` (STJ options: camelCase, `WhenWritingNull`, no indentation)
- Create: `src/dotnet/Chat.Service/WebHooks/WebHookPayloads.cs`
- Test: `tests/Chat.UnitTests/WebHookPayloadsTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class WebHookPayloads(IServiceProvider services) {
      // Envelope with data; returns (eventType, deliveryKey, json)
      public Task<string> Message(WebHook hook, WebHookEvents e, ChatEntry entry, ChatEntry? previous, AuthorFull author, CancellationToken ct);
      public Task<string> Reaction(WebHook hook, WebHookEvents e, Reaction reaction, ChatEntry entry, AuthorFull reactionAuthor, CancellationToken ct);
      public Task<string> Member(WebHook hook, WebHookEvents e, AuthorFull author, CancellationToken ct);
      public Task<string> ChatChanged(WebHook hook, WebHookEvents e, Chat chat, Chat? old, CancellationToken ct);
      public Task<string> PlaceChanged(WebHook hook, WebHookEvents e, Place place, Place? old, CancellationToken ct);
      public Task<string> Notification(WebHook hook, Notification n, ChatEntry? entry, AuthorFull? author, CancellationToken ct);
      public string Ping(WebHook hook, string sentBy);
      public static string DeliveryId(WebHookId hookId, string eventType, string eventKey); // $"{hookId}:{eventType}:{eventKey}"
  }
  ```
  Internally: `ExternalMessage ToExternalMessage(ChatEntry, AuthorFull, bool includeText)`,
  `object ChatBlock(Chat)`, envelope = `new { id, type, timestamp, hook = new { id, scope, scopeId }, chat, data }`.

Rules to encode (from the spec): `type` = `e.ToEventType()`; `timestamp` ISO-8601 UTC of
`Clocks.SystemClock.Now`; `id` = `DeliveryId(...)` with eventKey = `"{entryLid}:{version}"`
(messages), `reaction.Id` (reactions), `"{authorId}:{version}"` (members), `"{chat.Version}"`,
`"{place.Version}"`, `notification.Id` (notification), `RandomStringGenerator.Default.Next()`
(ping). `origin.kind` = `"bot"` if `Bots.IsBot(entry.AuthorId)`, `"api"` if `entry.IsViaApi`,
else `"user"`. `text` omitted when `!hook.IncludeText`; `previous.text` likewise. Mentions:
`MentionExtractor.Instance.GetMentionIds(MarkupParser.Parse(entry.Content))` filtered
`Kind == MentionKind.Author`, `((AuthorId)m.Target).Value`. Attachment URLs via the same
`UrlMapper.ContentUrl` / `ImagePreviewUrl` logic as `McpModelExt.ToMcpMediaRef` — copy it.
Message `url` = `UrlMapper.ToAbsolute(Links.Chat(entry.ChatId, entry.LocalId))`. Chat block:
`{ id, title, kind = chat.Kind.ToString().ToLower(), placeId, url }`. Author block: `{ id, name = avatar.Name, avatarUrl }`
using `avatar.MediaId`→`ContentUrl(media.BlobId)` else `PictureUrl.NullIfEmpty()` (see
`McpModelExt.ToMcpPictureUrl`; needs `IMediaBackend.Get(mediaId)`). Size cap: if the JSON
exceeds `MaxPayloadLength`, rebuild with `text` cut to `MaxPayloadLength / 2` chars and
`textTruncated = true`.

- [ ] **Step 1: Failing unit tests** — construct `WebHookPayloads` against a minimal
  `IServiceProvider` (`new ServiceCollection()` with a fake `UrlMapper` (see how
  `tests/Chat.UnitTests` build one — grep `new UrlMapper(`), `MomentClockSet` with a fixed
  clock, and a stub `IMediaBackend` returning null). Assert with `JsonDocument`:

```csharp
[Fact]
public async Task MessagePostedShouldCarryEnvelopeAndMessage()
{
    // arrange: hook (Chat scope, IncludeText = true), a TextEntry with Content "hi **all** @a:..." , AuthorFull with Avatar.Name "Alexey"
    // act: var json = await payloads.Message(hook, WebHookEvents.MessagePosted, entry, null, author, default);
    // assert: root.type == "message.posted"; root.hook.scope == "chat"; data.message.text == content;
    //         data.message.author.name == "Alexey"; data.message.origin.kind == "user"; no "userId" anywhere (json.Should().NotContain("userId"))
}
[Fact] public async Task IncludeTextFalseShouldOmitText() { /* text and previous.text absent */ }
[Fact] public async Task ViaApiEntryShouldReportApiOrigin() { }
[Fact] public async Task OversizedTextShouldBeTruncated() { /* 300 KB content → textTruncated true, json length < MaxPayloadLength */ }
[Fact] public void PingShouldHaveNoChatBlock() { }
```

- [ ] **Step 2: Run** `dotnet test tests/Chat.UnitTests --filter WebHookPayloadsTest` → FAIL.
- [ ] **Step 3: Implement** `WebHookJson` + `WebHookPayloads` per the rules above. Register
  `services.AddSingleton<WebHookPayloads>();` in `ChatServiceModule`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat(chat): web hook payload builder`

---

### Task 9: Event fan-in → outbox

**Files:**
- Create: `src/dotnet/Chat.Service/WebHooks/WebHooksBackend.Events.cs`
- Test: `tests/Chat.IntegrationTests/WebHooksFanInTest.cs`

**Interfaces:**
- Consumes: `WebHookPayloads` (Task 8), `WebHooksBackend_Enqueue` (Task 6), `IChatsBackend.GetRules(chatId, principalId)`.

Every handler starts with `if (Invalidation.IsActive) return;` and ends by calling
`Enqueue(hooks, e, payloadFactory)`:

```csharp
private async Task Enqueue(
    IEnumerable<WebHook> hooks, WebHookEvents e, string eventKey,
    Func<WebHook, Task<string>> payload, CancellationToken cancellationToken)
{
    var eventType = e.ToEventType();
    foreach (var hook in hooks) {
        if (!hook.Events.HasFlag(e))
            continue;

        var json = await payload(hook).ConfigureAwait(false);
        var command = new WebHooksBackend_Enqueue(
            hook.Id, hook.ScopeId, WebHookPayloads.DeliveryId(hook.Id, eventType, eventKey), eventType, json);
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }
}

// Chat + place hooks for the chat, plus personal "selected chats" hooks that can still read it
private async Task<List<WebHook>> HooksForChat(ChatId chatId, CancellationToken cancellationToken)
{
    var hooks = (await ListActiveForChat(chatId, cancellationToken).ConfigureAwait(false)).ToList();
    var userIds = await AuthorsBackend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
    foreach (var userId in userIds) {
        var personal = await ListActiveForUser(userId, cancellationToken).ConfigureAwait(false);
        foreach (var hook in personal.Where(h => h.ChatIds.Contains(chatId))) {
            var rules = await ChatsBackend.GetRules(chatId, userId, cancellationToken).ConfigureAwait(false);
            if (rules.CanRead())
                hooks.Add(hook);
        }
    }
    return hooks;
}
```
Performance note for the implementer: `ListUserIds` × `ListActiveForUser` are both cached
compute methods; a chat with thousands of members is still one cached array + N cache hits.
Fine for v1; if it shows up in profiles, invert it with a `ListUserIdsWithPersonalHooks`
index later.

Handlers:
- `OnChatEntryChangedEvent`: skip `entry.IsSystemEntry`; skip `Bots.IsBot(entry.AuthorId) && entry.WebHookId is not null` —
  phase 1 has no `WebHookId` yet, so skip only when the flag exists (leave the origin guard as
  `entry.IsViaApi ? … : …` nothing — v1 loop guard is "hook-originated entries", which cannot
  exist before phase 2; write the guard as a one-line comment where it will go). Map:
  `Create && !IsContentStreaming` → `MessagePosted`; `Update && oldEntry.IsContentStreaming && !entry.IsContentStreaming`
  → `MessagePosted`; `Update` (not streaming, `Content`/`Attachments` changed) → `MessageEdited`;
  `Remove` → `MessageRemoved`. eventKey `$"{entry.LocalId}:{entry.Version}"`.
- `OnReactionChangedEvent`: `Create/Update` → `ReactionAdded`, `Remove` → `ReactionRemoved`; key `reaction.Id`.
- `OnAuthorUpsertedEvent`: `OldAuthor is null || (OldAuthor.HasLeft && !Author.HasLeft)` → `MemberJoined`;
  `!OldAuthor.HasLeft && Author.HasLeft` → `MemberLeft`; skip bots; key `$"{author.Id}:{author.Version}"`.
- `OnAuthorsRemovedEvent`: `MemberLeft` per author.
- `OnChatChangedEvent`: `Update` with title/description/picture/kind changed → `ChatUpdated` to
  `HooksForChat`; `Create` → `ChatCreated` and `Remove` → `ChatArchived` to place hooks only
  (`chat.PlaceId is { } p` → `ListByScope(Place, p)` filtered active). Key `chat.Version`.
- `OnPlaceChangedEvent`: `Update` → `PlaceUpdated` to `ListByScope(Place, place.Id)`.
- `OnPlaceMembershipChangedEvent`: `HasLeft ? PlaceMemberLeft : PlaceMemberJoined`; author =
  `AuthorsBackend.Get(placeId.RootChatId, userId-as-principal …)` — use
  `AuthorsBackend.GetByUserId(chatId, userId, …)` if it exists (grep `IAuthorsBackend`), else
  resolve through `ListAuthorIds` + `Get`. Key `$"{userId}:{hasLeft}:{Clocks.SystemClock.Now.EpochOffset.Ticks}"`.
- `OnUserNotifiedEvent`: implemented in Task 11 — leave the method returning `Task.CompletedTask` for now.

- [ ] **Step 1: Failing tests** (Chat collection; helper `WaitForDeliveries(backend, hookId, count)` = `ComputedTest.When(... ListDeliveries(hookId, 20) ...)`):

```csharp
[Fact] public async Task PostedMessageShouldEnqueueOneDelivery()   // create chat hook (Messages) → CreateTextEntry → 1 pending row, EventType "message.posted", payload has the text
[Fact] public async Task EditShouldEnqueueEditedWithPrevious()     // UpdateTextEntry → row "message.edited"
[Fact] public async Task UnsubscribedEventShouldNotEnqueue()       // hook Events = Reactions only → CreateTextEntry → 0 rows
[Fact] public async Task PlaceAllowListShouldFilterChats()         // place hook ChatIds=[chatA]; post in chatB → 0; in chatA → 1
[Fact] public async Task PersonalSelectedChatShouldRequireRead()   // Bob personal hook ChatIds=[aliceChat] where Bob is NOT a member → 0; join → 1
[Fact] public async Task DuplicateEventShouldNotDuplicateRow()     // call OnEnqueue twice with the same DeliveryId → 1 row
```
Reading payloads in tests: resolve `ChatDbContext` via `AppHost.Services.DbHub<ChatDbContext>()`
(grep tests for `CreateDbContext` usage) and read `WebHookDeliveries.Payload`.

- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** the partial class; register nothing new (handlers are discovered from the interface's `[EventHandler]`s).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `feat(chat): fan chat events into the web hook outbox`

---

### Task 10: `WebHookDeliverer` + `WebHookDeliveryFlow` + test/redeliver

**Files:**
- Create: `src/dotnet/Chat.Service/WebHooks/WebHookDeliverer.cs`
- Create: `src/dotnet/Chat.Service/Flows/WebHookDeliveryFlow.cs`
- Modify: `src/dotnet/Chat.Service/WebHooks/WebHooksBackend.cs` (schedule the flow in `OnEnqueue`/`OnRedeliver`)
- Modify: `src/dotnet/Chat.Service/WebHooks/WebHooks.cs` (`OnTest`, `OnRedeliver` bodies)
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` (`services.AddEgressHttpClient("WebHooks", 4 * 1024);`, `services.AddSingleton<WebHookDeliverer>();`, `flows.Add<WebHookDeliveryFlow>()`)
- Test: `tests/Chat.IntegrationTests/WebHookReceiver.cs` (in-test HTTP receiver), `tests/Chat.IntegrationTests/WebHookDeliveryTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class WebHookDeliverer(IServiceProvider services) {
      public sealed record Outcome(bool HasMore, TimeSpan? RetryIn);
      // Delivers the head-of-line pending row of the hook; records the result; returns what the flow should do next
      public Task<Outcome> DeliverNext(WebHookId hookId, CancellationToken ct);
      // In-process ping for "Send test event"; never touches the outbox
      public Task<WebHookTestResult> SendPing(WebHook hook, string sentBy, CancellationToken ct);
  }
  ```

`DeliverNext` algorithm:
1. Load hook row + `SELECT … WHERE WebHookId = @id AND Status = Pending ORDER BY Seq LIMIT 1`. None or hook disabled → `Outcome(false, null)`.
2. If `NextAttemptAt > now` → `Outcome(true, NextAttemptAt - now)`.
3. `Send(row.Id, row.Payload, hook)` → `(statusCode?, error?, latencyMs)`:
   `EgressGuard.IsAllowed(uri.DnsSafeHost)` false → `Disable(UnsafeUrl)`, record `Failed`, return `(false, null)`.
   Build `HttpRequestMessage POST`, headers `webhook-id`, `webhook-timestamp` (unix seconds),
   `webhook-signature` = `StandardWebhookSigner.SignatureHeader(id, ts, body, secrets)`,
   `user-agent: Voxt-Hooks/1`, custom header if set, `content-type: application/json`.
   `HttpClientFactory.CreateClient("WebHooks")`, `CancellationTokenSource` with
   `Constants.WebHooks.DeliveryTimeout`. Catch `HttpRequestException`, `TaskCanceledException`
   (timeout) → error string, no status code. The egress handler throws on redirects to
   disallowed hosts; a 3xx that is allowed is still treated as failure below (we don't follow).
4. Classify: 2xx → `Succeeded`; `410` → `Disable(DeliveryFailures, "410 Gone")` + `Failed`;
   5xx / 429 / no status → retry if `attempts < RetryDelays.Length` else keep retrying at the
   last delay; other 4xx / 3xx → `Failed` terminal. Retry → `RecordDelivery(Pending, NextAttemptAt = now + RetryDelays[min(attempts, len-1)])`.
5. Head-of-line age check: if `Status == Pending && now - row.CreatedAt > DisableAfter` →
   `Disable(DeliveryFailures, lastError)` (which abandons the rest) → `(false, null)`.
6. Return `(true, null)` after a success (drain next immediately), `(true, delay)` after a retry.

`WebHookDeliveryFlow` (`[Flow(DelayQuanta = 1, ResumeTimeout = 120)]`, `Flow<Unit>`, args = hook id):
```csharp
protected override async ValueTask Resume(CancellationToken cancellationToken)
{
    var hookId = WebHookId.Parse(Id.Arguments);
    for (var i = 0; i < MaxDeliveriesPerResume; i++) {          // 50
        var outcome = await Deliverer.DeliverNext(hookId, cancellationToken).ConfigureAwait(false);
        if (!outcome.HasMore)
            return;
        if (outcome.RetryIn is { } delay) {
            Runtime.StageResumeIn(delay);
            return;
        }
    }
    Runtime.StageResume(); // more work; yield so one hook can't monopolize the runner
}
```
Backend: in `OnEnqueue` and `OnRedeliver`, after `SaveChangesAsync`:
`await FlowHub.NewResumeEvent<WebHookDeliveryFlow>(Id.Value).Schedule(cancellationToken).ConfigureAwait(false);`

Frontend: `OnTest` → `RequireManager`, `Deliverer.SendPing(hook, account.Avatar.Name, ct)`;
`OnRedeliver` → `RequireManager`, `Commander.Call(new WebHooksBackend_Redeliver(...))`.

- [ ] **Step 1: The receiver** — copy `tests/OAuth.IntegrationTests/CimdTestServer.cs` into
  `WebHookReceiver` : records every request (`Headers`, `Body`) in a `Channel<Received>`,
  responds with a configurable `Func<int, HttpStatusCode>` by request index (default 200).
  `Task<Received> Next(TimeSpan timeout)`. Because the receiver is `http://127.0.0.1:port`,
  the test host needs `EgressHostAllowList = "127.0.0.1,localhost"` — add
  `cfg.AddInMemory<CoreServerSettings>((x => x.EgressHostAllowList, "localhost,127.0.0.1"))`
  to `ChatCollection.AppHostFixture` (check `TestAppHostOptions` for the `ConfigureHost` hook the
  Mcp collection uses) and, since `IsDevelopmentInstanceBypassEnabled` is off under tests,
  confirm the guard's host allow-list short-circuits the IP check (it does: `AllowedHostWildcards`
  is checked first). The `https://` rule in `Validate` must allow `http://` loopback when
  `HostInfo.IsTested` too.

- [ ] **Step 2: Failing tests**

```csharp
[Fact] public async Task DeliveryShouldBeSignedAndOrdered()
// receiver; hook url = receiver.BaseUri + "/hook"; post 3 messages; receive 3; each has webhook-id/-timestamp/-signature;
// StandardWebhookSigner.Verify(secret, id, ts, body, sig, 5min, now) true; bodies' data.message.id ascending
[Fact] public async Task ServerErrorShouldScheduleRetry()
// receiver returns 500 for first request → delivery row Pending with Attempts 1 and NextAttemptAt ≈ now+1m; hook.ConsecutiveFailures 1
[Fact] public async Task GoneShouldDisableHook()               // 410 → hook IsEnabled false, DisabledReason DeliveryFailures
[Fact] public async Task ClientErrorShouldFailWithoutRetry()   // 404 → Status Failed, CompletedAt set, next message still delivered (ordering continues past terminal failure)
[Fact] public async Task StaleHeadOfLineShouldDisableAndAbandon()
// insert a Pending row with CreatedAt = now - 73h via ChatDbContext, receiver 500 → hook disabled, all Pending → Abandoned
[Fact] public async Task RotatedSecretShouldSendTwoSignatures() // rotate → next delivery header has 2 parts, both verify (old + new)
[Fact] public async Task TestEventShouldNotTouchOutbox()        // IWebHooks.OnTest → receiver gets type "ping"; ListDeliveries empty
[Fact] public async Task RedeliverShouldCloneRow()              // after a Failed row, OnRedeliver → new row id ends with ":r1", delivered
```

- [ ] **Step 3: Run** → FAIL.
- [ ] **Step 4: Implement** deliverer, flow, registrations, frontend bodies.
- [ ] **Step 5: Run** → PASS. Also run `WebHooksBackendTest` and `WebHooksFanInTest` again.
- [ ] **Step 6: Commit** — `feat(chat): signed, ordered web hook delivery with retries`

---

### Task 11: `UserNotifiedEvent` → personal notification hooks

**Files:**
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs` (`OnNotify`)
- Modify: `src/dotnet/Chat.Service/WebHooks/WebHooksBackend.Events.cs` (`OnUserNotifiedEvent`)
- Test: `tests/Chat.IntegrationTests/WebHookNotificationTest.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task MentionShouldDeliverNotificationToPersonalHook()
{
    // arrange: Alice creates chat, Bob joins; Bob creates personal hook { SubscribeNotifications = true, Events = Notification } → receiver
    // act: Alice posts "hi @a:<bobAuthorId>" (see how mention text is built in NotificationsIntegrationTests)
    // assert: receiver.Next(15s) body: type "notification", data.kind "mention", data.message.text contains "hi"
}

[Fact]
public async Task MutedChatShouldNotDeliver() { /* Bob mutes the chat (Chats/ChatNotificationMode helpers in Testing.Host) → plain message → no delivery within 5s */ }
```

- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** In `NotificationsBackend.OnNotify`, right after the `DebugLog` line
  and before the dormant check:
  `await Queues.Enqueue(new UserNotifiedEvent(notification), cancellationToken).ConfigureAwait(false);`
  (`Queues` is already a property there; the event is an `EventCommand`, so every registered
  handler gets it.) In `OnUserNotifiedEvent`: `if (n is not ChatNotification cn) return;`
  hooks = `ListActiveForUser(n.UserId)` where `SubscribeNotifications && Events.HasFlag(Notification)`;
  entry = `cn is ChatEntryNotification en ? await ChatsBackend.GetEntry(en.EntryId)` (grep
  `ChatEntryNotification` for the exact member); author = `entry is null ? null : AuthorsBackend.Get(...)`;
  eventKey `n.Id.Value`; payload `Payloads.Notification(hook, n, entry, author)`.
- [ ] **Step 4: Run** → PASS. Run `tests/Notifications.IntegrationTests` too (the enqueue must not break anything).
- [ ] **Step 5: Commit** — `feat(notifications): UserNotifiedEvent feeds personal web hooks`

---

### Task 12: Delivery log pruner

**Files:**
- Create: `src/dotnet/Chat.Service/WebHooks/WebHookDeliveryPruner.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` (`services.AddSingleton<WebHookDeliveryPruner>(); services.AddHostedService(c => c.GetRequiredService<WebHookDeliveryPruner>());` — check how `OAuthPruner` is registered in `OAuthServiceModule` and copy that)
- Test: `tests/Chat.IntegrationTests/WebHookDeliveryPrunerTest.cs`

- [ ] **Step 1: Failing test** — insert two rows via `ChatDbContext` (one `CreatedAt = now - 31d`,
  one fresh, both terminal), call `pruner.RunOnce(ct)`, assert only the fresh one remains and
  a `Pending` 31-day-old row is *kept* (never prune undelivered work).
- [ ] **Step 2: Implement** — `WorkerBase` shaped like `OAuthPruner`: hourly, first delay 5 min,
  `ExecuteDeleteAsync` on `Status != Pending && CreatedAt < now - DeliveryRetention`, log the count.
- [ ] **Step 3: Run** → PASS. **Commit** — `feat(chat): prune web hook delivery log after 30 days`

---

### Task 13: Localization keys

**Files:**
- Modify: `src/dotnet/Localization/Resources/Strings.en.json` and every hand-written `Strings.<lang>.json` (22 files; `cnr`, `hr`, `sr`, `max` are derived)
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`
- Run: `scripts/derive-bcms.cmd`, `scripts/derive-max.cmd`

Add one group `// Integrations — chat/place settings and Settings → API & Apps: outgoing web hooks` with:

| Key | English |
|---|---|
| `Integrations_Title` | Integrations |
| `Integrations_Add` | Add integration |
| `Integrations_Empty` | No integrations yet. Outgoing webhooks send this chat's events to a URL you choose. |
| `Integrations_PersonalEmpty` | No webhooks yet. A personal webhook sends your notifications and the chats you pick to a URL you choose. |
| `Integrations_Outgoing` | Outgoing webhook |
| `Integrations_OutgoingHint` | Send events to a URL |
| `Integrations_Disabled` | Disabled |
| `Integrations_FailingSince_Format` | Failing since {0} |
| `Integrations_LastDelivery_Format` | Last delivery {0} |
| `Integrations_DisabledAfterFailures` | Disabled after repeated delivery failures |
| `Integrations_DisabledUnsafeUrl` | Disabled: the URL resolves to a private address |
| `Integrations_Hooks_Format` | {0} hook\|{0} hooks |
| `WebHook_Name` | Name |
| `WebHook_Url` | URL |
| `WebHook_Events` | Events |
| `WebHook_Events_Messages` | Messages |
| `WebHook_Events_Reactions` | Reactions |
| `WebHook_Events_Members` | Members |
| `WebHook_Events_Chat` | Chat changes |
| `WebHook_Events_Place` | Place changes |
| `WebHook_Events_Notifications` | My notifications |
| `WebHook_IncludeText` | Include message text |
| `WebHook_Chats` | Chats |
| `WebHook_Chats_All` | All chats |
| `WebHook_Chats_Selected` | Selected chats |
| `WebHook_CustomHeader` | Custom header |
| `WebHook_CustomHeaderName` | Header name |
| `WebHook_CustomHeaderValue` | Header value |
| `WebHook_ShowAdvanced` | Advanced |
| `WebHook_Create` | Create |
| `WebHook_Save` | Save |
| `WebHook_SecretTitle` | Signing secret |
| `WebHook_SecretCopyWarning` | Copy the signing secret now — it is shown only once. |
| `WebHook_SendTest` | Send test event |
| `WebHook_TestSucceeded_Format` | Delivered: {0} in {1} ms |
| `WebHook_TestFailed_Format` | Failed: {0} |
| `WebHook_Enabled` | Enabled |
| `WebHook_RecentDeliveries` | Recent deliveries |
| `WebHook_NoDeliveries` | No deliveries yet |
| `WebHook_Redeliver` | Redeliver |
| `WebHook_RotateSecret` | Rotate secret |
| `WebHook_RotateSecretConfirm` | The current secret keeps working for 24 hours so you can update the receiver. Continue? |
| `WebHook_Delete` | Delete integration |
| `WebHook_DeleteConfirm_Format` | Delete "{0}"? Pending deliveries are dropped. |
| `WebHook_Status_Pending` | Pending |
| `WebHook_Status_Succeeded` | Delivered |
| `WebHook_Status_Failed` | Failed |
| `WebHook_Status_Abandoned` | Abandoned |
| `Settings_Webhooks` | Webhooks |

- [ ] **Step 1: Add keys to `Strings.en.json`** next to the `ApiKeys_*` group with the context comment.
- [ ] **Step 2: Translate into every hand-written catalog** (bg, bs, cs, de, es, fr, hi, id, it, ja, ko, pl, pt, ru, tr, uk, vi, zh — check the exact list with `ls Strings.*.json` minus derived). Keep placeholders and the `|` plural separator.
- [ ] **Step 3: Typed members** in `LocalizedStringsLocalizerExt.cs` (`_Format` keys as methods, plural `Integrations_Hooks_Format(int count, object arg0)` — copy the shape of `Place_MembersLabel`).
- [ ] **Step 4: Run** `scripts/derive-bcms.cmd && scripts/derive-max.cmd` then `dotnet test tests/UI.Blazor.UnitTests --filter AppLocalizationTest` → PASS.
- [ ] **Step 5: Commit** — `feat(l10n): integrations and web hook strings`

---

### Task 14: Settings UI

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/WebHooks/WebHookList.razor`, `WebHookCreateModal.razor`, `WebHookFormPage.razor`, `WebHookRevealPage.razor`, `WebHookDetailModal.razor`, `WebHookDetailPage.razor`, `WebHookDeliveryList.razor`, `WebHookEventsPicker.razor`, `web-hooks.css`
- Modify: `src/dotnet/UI.Blazor.App/styles.css` (import `web-hooks.css`)
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` (modal type map: `.Add<WebHookCreateModal.Model, WebHookCreateModal>()`, `.Add<WebHookDetailModal.Model, WebHookDetailModal>()`)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatSettings/ChatSettingsStartModalPage.razor` (Integrations row)
- Create: `src/dotnet/UI.Blazor.App/Components/ChatSettings/ChatIntegrationsModalPage.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/PlaceSettings/PlaceSettingsStartModalPage.razor` (Integrations tab)
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/ApiAndAppsSettings.razor` (`<WebHookList Scope="User" ScopeId="@account.Id.Value"/>` between the two existing components; get the account like `YourAccount.razor` does)
- Read first: `docs/ui/components.md`, `docs/ui/modals.md`

**Interfaces:**
- `WebHookList` parameters: `WebHookScope Scope`, `string ScopeId`. `ComputedStateComponent<AppUIHub, Model>` computing
  `Hub.WebHooks.List(Session, Scope, ScopeId, ct)` and `Hub.Features.IsIncompleteUIEnabled(ct)`; renders nothing when the flag is off.
- `WebHookCreateModal.Model(WebHookScope Scope, string ScopeId)`; `WebHookDetailModal.Model(WebHookId Id)`.
- `WebHookFormPage` model `(WebHookScope Scope, string ScopeId, WebHook? Existing)` — create and edit share it.
- `WebHookEventsPicker` parameters: `WebHookEvents Value`, `EventCallback<WebHookEvents> ValueChanged`, `WebHookScope Scope` (hides Place group unless place, shows Notifications only for user).

- [ ] **Step 1: `WebHookList`** — clone the structure of `ApiKeySettings.razor`: `TileTopic`
  (`L.Integrations_Title`, or `L.Settings_Webhooks` for user scope), `ButtonTile` → `Hub.ModalUI.Show(new WebHookCreateModal.Model(Scope, ScopeId))`,
  `Tile` of `TileItem`s (`Icon` `icon-call-arrow-out`; `Content` name + `(Disabled)` span; `Caption`:
  host of `Url` · `L.Integrations_LastDelivery_Format(...)` or a red `c-failing` span with
  `L.Integrations_DisabledAfterFailures` / `Integrations_DisabledUnsafeUrl` / `Integrations_FailingSince_Format`
  when `ConsecutiveFailures > 0`; `Click` → `Hub.ModalUI.Show(new WebHookDetailModal.Model(hook.Id))`),
  empty state `TileItem` with `L.Integrations_Empty` / `L.Integrations_PersonalEmpty`.
- [ ] **Step 2: Create flow** — `WebHookCreateModal` = `DiveInDialogFrame` with `StartPage = DiveInDialogPage.New<WebHookFormPage>(model)`
  (kind chooser is phase 2; phase 1 has one kind). `WebHookFormPage` — `Form`/`FormBlock`/`FormSection`/`TextBox`
  for Name and URL, `WebHookEventsPicker`, a `Checkbox`/`Toggle` (grep `Components/Settings` for the toggle component in use) for
  Include text, for place scope a radio All/Selected + `ChatSelector` (grep for the chat picker used by *forward* or *copy chat*, reuse it),
  for user scope the Notifications toggle + selected chats; "Advanced" disclosure with header name/value.
  Submit → `UICommander.Run(new WebHooks_Change { Scope, ScopeId, Change = Change.Create(diff) })`;
  on success `Context.StepIn(DiveInDialogPage.New<WebHookRevealPage>(new WebHookRevealPage.Model(result.WebHook, result.Secret)))`.
  Edit mode (`Existing != null`): submit `Change.Update(diff)` and close.
- [ ] **Step 3: `WebHookRevealPage`** — copy `ApiKeyRevealPage.razor`: warning `L.WebHook_SecretCopyWarning`,
  `<code>` + `CopyToClipboard`, plus a `Button` `L.WebHook_SendTest` calling `UICommander.Run(new WebHooks_Test { Id })`
  and showing `L.WebHook_TestSucceeded_Format(code, ms)` / `L.WebHook_TestFailed_Format(error)` inline.
- [ ] **Step 4: Detail** — `WebHookDetailModal` (DiveIn) → `WebHookDetailPage` (`ComputedStateComponent`, computes `Get` + `ListDeliveries`):
  edit tile (`TileItem` → `Context.StepIn(WebHookFormPage with Existing)`), status tile (enabled toggle → `Change.Update(new WebHookDiff { IsEnabled = x })`,
  last delivery, failures), `WebHookDeliveryList` (rows: time · event type · status chip · `code · ms` · Redeliver `ButtonRound` for non-pending),
  Send test button, danger tiles Rotate (`ConfirmModal` with `L.WebHook_RotateSecretConfirm` → step into `WebHookRevealPage` with the new secret)
  and Delete (`ConfirmModal` `L.WebHook_DeleteConfirm_Format(name)` → `Change.Remove()` → close).
- [ ] **Step 5: Entry points** — chat settings: new `TileItem` (`icon-link-2`, `L.Integrations_Title`, right = `L.Integrations_Hooks_Format(count)` + chevron)
  visible when `m.Chat.Rules.CanModerate()` and incomplete-UI flag on; click → `Context.StepIn(DiveInDialogPage.New<ChatIntegrationsModalPage>(ChatId))`
  where the page sets `Context.Title = L.Integrations_Title` and renders `<WebHookList Scope="WebHookScope.Chat" ScopeId="@ChatId.Value"/>`.
  Place settings: add `new("integrations", L.Integrations_Title) { Content = @<WebHookList Scope="WebHookScope.Place" ScopeId="@PlaceId.Value"/> }`
  to `tabs` when `_isOwner && _enableIncompleteUI`. Settings → API & Apps: insert the user-scope list.
- [ ] **Step 6: CSS** — `web-hooks.css` with `.web-hook-tile .c-failing { color: var(--danger) }` etc.; follow `api-key` styles in the settings CSS for the reveal box.
- [ ] **Step 7: Build** `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj` → clean; then run the server in watch mode and walk the flow
  once with `EnableIncompleteUI` on (see `/debug-ui`): create → reveal → send test against a `https://webhook.site` URL or the local receiver → detail → rotate → delete.
- [ ] **Step 8: Commit** — `feat(ui): integrations settings for outgoing web hooks`

---

### Task 15: Docs, MCP model switch, final verification

**Files:**
- Create: `docs/integrations/web-hooks.md` (living doc: scopes, event catalog, envelope, `data` shapes, headers, signature verification snippet in 3 languages, retry/disable rules, egress IPs placeholder, settings walkthrough)
- Modify: `docs/index.md` (link), `docs/api-index.md` (new types under `Api` / `Chat.Service`)
- Modify: `src/dotnet/Mcp/Models/McpChatMessage.cs` → delete; `McpModelExt.ToMcpModel(ChatEntry…)` returns `ExternalMessage` built by a shared mapper. To avoid duplicating Task 8's mapping, move `ToExternalMessage` from `WebHookPayloads` into `src/dotnet/Chat.Contracts/ExternalMessageExt.cs` (static, takes `UrlMapper` and an author lookup) and call it from both. Update `tests/Mcp.IntegrationTests` for the `author` object.
- Delete: `docs/superpowers/plans/2026-09-17-web-hooks-outgoing.md` is **kept** until phase 2 ships; the spec stays.

- [ ] **Step 1: Write the doc** from the spec's payload section (it is the receiver-facing reference).
- [ ] **Step 2: MCP switch** + run `dotnet test tests/Mcp.IntegrationTests`.
- [ ] **Step 3: Full verification** — `dotnet test tests/Chat.IntegrationTests --filter WebHook`, `tests/Chat.UnitTests --filter WebHook`,
  `tests/Core.Server.UnitTests`, `tests/Notifications.IntegrationTests`, `tests/UI.Blazor.UnitTests --filter AppLocalizationTest`.
- [ ] **Step 4: Commit** — `docs(integrations): outgoing web hooks reference; MCP uses ExternalMessage`

---

## Self-review notes

- Spec coverage: registration/secrets (6, 7), scopes and permissions (7), event catalog (9, 11),
  payload (8), signing/SSRF/retries/auto-disable/abandon (10), rotation overlap (6, 10), delivery
  log + redeliver + test (10, 14), pruning (12), UI (13, 14), docs (15). Not covered by design:
  lifecycle system entries and owner notification (deferred, see Global Constraints);
  `ChatEntry.WebHookId` and the loop guard (phase 2 — no hook-originated entries exist yet).
- Names used across tasks: `WebHooksBackend_Enqueue/RecordDelivery/Disable/Redeliver/RotateSecret/Change`,
  `WebHookDeliverer.DeliverNext/SendPing`, `WebHookPayloads.DeliveryId`, `WebHook.Covers`,
  `WebHook.IsActiveOutgoing`, `ListActiveForChat/ListActiveForUser/ListByScope/ListDeliveries`,
  `Constants.WebHooks.*`, `CoreServerSettings.Egress*`.

## Status (2026-09-18)

All 15 tasks implemented on `feat/web-hooks` (ccb1328409..bcff1ffbef, 30 commits), each task reviewed,
whole-branch review + fix wave done. Deferred to phase 2 / follow-ups (from the reviews):

- Phase 2 as planned: incoming hooks, lifecycle system entries, owner push on auto-disable,
  `ChatEntry.WebHookId` + loop guard, `origin.kind = "webhook"`, empty-state *Learn more* link.
- Ops: rename `MediaSettings:Crawling*` → `CoreSettings:Egress{CidrDenylist,DomainDenylist,HostAllowList}`
  in deployment config in the same release; publish egress IPs (doc placeholder).
- MCP release note: `McpChatMessage` → `ExternalMessage` (`author {id,name,avatarUrl}`, attachment `id`
  dropped, `text` null while streaming, new `mentions`/`url`/`origin`/`textTruncated`).
- Delivery: `previous.text` outside the size-cap loop; `(Status, NextAttemptAt)` index unused (drop via
  migration); pruner should also delete rows whose hook is gone; flow `SetResult` on hook removal;
  outbox `Seq` order across concurrent fan-in handlers is best-effort (derive from lid+version?);
  expired `PrevSecretProtected` never cleared.
- API: `ListDeliveries`/rotate/test/redeliver throw NotFound vs Unauthorized (weak existence oracle);
  form canonicalizes `SubscribeNotifications` from the Notification flag; `IsTranscribed` = `HasAudio`.
- Tests: negative `Verify` cases; pending-cap, version-mismatch, re-enable-clears-failures; same-kind
  mute pair; `Reaction/Member/ChatChanged/PlaceChanged/Notification` payload unit tests.
- Misc: `EgressGuard` not sealed; `RedirectHandlerMock` duplicated in two test projects;
  `EgressGuard.Resolve` logs cancelled lookups at Error; form rejects IP literals while dev instances
  accept them server-side; pl `WebHook_RotateSecretConfirm` masculine form; `.mibc` not regenerated.
