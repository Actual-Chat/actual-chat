# Walkie-Talkie Server-Side Speech-Start Push Trigger — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a user starts streaming speech into a chat, send a high-priority
data-only FCM push to Android devices of chat members who have walkie-talkie
("Keep listening") mode on but are not currently listening.

**Architecture:** `LiveSessionsBackend.OnStreamRegistered` (the earliest server
hook with ChatId + AuthorId, fired per utterance) enqueues a new
`SpeechStartedEvent` (unbound `EventCommand` over NATS/in-memory queues → fans
out to all `[EventHandler]`s). A new handler on `NotificationsBackend` gates on
a server feature flag, resolves armed recipients from existing per-user KVAS
settings, excludes the speaker / active live-session participants /
wake-pending entries, and sends a data-only high-priority FCM message via a new
`IFirebaseMessagingClient.SendSpeechStartedWake` method. Nothing is persisted.

**Tech Stack:** .NET 10, ActualLab Fusion (compute services, `EventCommand`,
`RecentlySeenMap`), FirebaseAdmin SDK, xUnit + FluentAssertions integration
tests against the shared notification test app host.

**Spec:** `docs/superpowers/specs/2026-07-13-walkie-talkie-server-trigger-design.md`

## Global Constraints

- Read `docs/CODING_STYLE.md` first. Highlights that WILL bite you:
  - **No `Async` suffix** on async methods.
  - **No XML docs on members**, ever. Type-level `/// <summary>` only when the
    name isn't self-explanatory, 3 lines ideal. Default to **no comments**.
  - Braces: classes/methods Allman (next line); everything else K&R (same line).
  - Max 120 chars/line; blank line after any block-escaping statement
    (`return`, `continue`, …) unless last in its block.
  - `.ConfigureAwait(false)` on every await in service-layer code.
  - No `StringComparison.Ordinal` / `CultureInfo.InvariantCulture` (invariant
    globalization is on).
  - Test names PascalCase without underscores; AAA pattern with lowercase
    `// arrange` / `// act` / `// assert` comments (arrange comment optional
    when trivial).
- Settings defaults (from spec, verbatim): `EnableWalkieTalkiePush = true`,
  `WalkieTalkieWakeTtl = 30 s`, `WalkieTalkieMaxChatMembers = 100`.
- FCM wake message: `Priority.High`, `TimeToLive = 60 s`, per-chat collapse key,
  Android devices only. iOS/Web/Windows devices are skipped (sub-project C
  adds iOS).
- Wake-pending map key is `(UserId, ChatId)`; an entry suppresses re-sends for
  `WalkieTalkieWakeTtl`. Invariant recorded in the spec: this TTL must stay
  **shorter** than the client's post-wake keep-listening window (≥ 60 s).
- Integration tests require host infra (PostgreSQL/Redis/NATS on localhost) —
  available per project setup; tests run from repo root.
- Commit after every task; branch is `feat/walkie-talkie-push` (already
  checked out). Do NOT commit the pre-existing unrelated onboarding changes
  (`OnboardingModal.razor`, `PermissionStepModel.cs`, `PermissionsStep.razor`)
  — always `git add` explicit paths, never `git add -A`.

---

### Task 1: Notifications settings + server feature flag

**Files:**
- Create: `src/dotnet/Notifications.Service/Module/NotificationsSettings.cs`
- Modify: `src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs:11-12`
- Create: `src/dotnet/Notifications.Service/Features_EnableWalkieTalkiePush.cs`

**Interfaces:**
- Consumes: `HostModule<TSettings>` (auto-binds the settings class from the
  `NotificationsSettings` config section and registers it as a DI singleton —
  see `src/dotnet/Core/Hosting/HostModule.cs:54-69`), `FeatureDef<bool>` /
  `IServerFeatureDef` (`src/dotnet/Core/Features/FeatureDef.cs`; defs are
  lazily instantiated by type via `ServerFeatureDefRegistry`, no registration
  needed).
- Produces: `NotificationsSettings` (DI singleton) with
  `bool EnableWalkieTalkiePush`, `TimeSpan WalkieTalkieWakeTtl`,
  `int WalkieTalkieMaxChatMembers`; `Features_EnableWalkieTalkiePush`
  (queried via `IServerFeatures.Get<Features_EnableWalkieTalkiePush>(ct)`).

No dedicated test — pure config plumbing, exercised end-to-end by Task 4's
integration tests.

- [ ] **Step 1: Create the settings class**

`src/dotnet/Notifications.Service/Module/NotificationsSettings.cs`:

```csharp
namespace ActualChat.Notifications.Module;

public sealed class NotificationsSettings
{
    public bool EnableWalkieTalkiePush { get; set; } = true;
    public TimeSpan WalkieTalkieWakeTtl { get; set; } = TimeSpan.FromSeconds(30);
    public int WalkieTalkieMaxChatMembers { get; set; } = 100;
}
```

- [ ] **Step 2: Bind it in the module**

In `src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs`, change:

```csharp
public sealed class NotificationServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
```

to:

```csharp
public sealed class NotificationServiceModule(IServiceProvider moduleServices)
    : HostModule<NotificationsSettings>(moduleServices), IServerModule
```

(`HostModule<TSettings>` registers `Settings` as a singleton in
`InjectServices` — nothing else to add.)

- [ ] **Step 3: Create the feature def**

`src/dotnet/Notifications.Service/Features_EnableWalkieTalkiePush.cs`:

```csharp
using ActualChat.Notifications.Module;

namespace ActualChat.Notifications;

// ReSharper disable once InconsistentNaming
public sealed class Features_EnableWalkieTalkiePush : FeatureDef<bool>, IServerFeatureDef
{
    public override Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
        => Task.FromResult(services.GetRequiredService<NotificationsSettings>().EnableWalkieTalkiePush);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/dotnet/Notifications.Service/Notifications.Service.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Notifications.Service/Module/NotificationsSettings.cs \
        src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs \
        src/dotnet/Notifications.Service/Features_EnableWalkieTalkiePush.cs
git commit -m "feat(notifications): walkie-talkie push settings + server feature flag

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: SpeechStartedEvent + emission from OnStreamRegistered

**Files:**
- Create: `src/dotnet/Backend/Events/SpeechStartedEvent.cs`
- Modify: `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs:214-222`

**Interfaces:**
- Consumes: `EventCommand` / `IHasShardKey<ChatId>` (template:
  `src/dotnet/Backend/Events/ChatEntryChangedEvent.cs`), `Services.Queues()
  .Enqueue(...)` (already used in `LiveSessionsBackend.EnqueueLiveNotification`).
  An `EventCommand` enqueued without a chain id is an *unbound event* — the
  queue processor fans it out to every `[EventHandler]` for its type
  (`src/dotnet/Core.Server/Queues/Internal/ShardQueueProcessor.cs:181`).
  The `Backend` project is referenced transitively by every `*.Contracts`
  project, so both Streaming.Service and Notifications.Service already see it —
  no csproj changes.
- Produces: `SpeechStartedEvent(ChatId ChatId, AuthorId AuthorId, Moment
  StartedAt)`, enqueued once per registered audio stream (≈ per utterance).

No isolated test — an event without a handler is inert; the emission is
asserted end-to-end in Task 4's `ArmedByAlwaysListenedChatGetsWake` test,
which drives `OnStreamRegistered` directly.

- [ ] **Step 1: Create the event**

`src/dotnet/Backend/Events/SpeechStartedEvent.cs`:

```csharp
namespace ActualChat;

/// <summary>
/// Fired when an author starts streaming live audio into a chat —
/// per utterance, before any transcript or chat entry exists.
/// </summary>
[DataContract, MessagePackObject(true)]
public partial record SpeechStartedEvent(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] AuthorId AuthorId,
    [property: DataMember] Moment StartedAt
) : EventCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}
```

- [ ] **Step 2: Emit it from OnStreamRegistered**

In `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`, the method
currently starts (line 214):

```csharp
    public virtual async Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        CancellationToken cancellationToken)
    {
        using var _ = Computed.BeginIsolation();
        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);
```

Insert the enqueue between `BeginIsolation` and the lock (it must run before
the method's `AuthorIds` early-return so every utterance fires, not just the
first per author):

```csharp
        using var _ = Computed.BeginIsolation();

        // Before the dedup/early-return below: the walkie-talkie wake trigger fires per utterance.
        await Services.Queues()
            .Enqueue(new SpeechStartedEvent(chatId, authorId, Clocks.SystemClock.Now), cancellationToken)
            .ConfigureAwait(false);

        using var lockHolder = await _changeLocks.Lock(chatId, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/Backend/Events/SpeechStartedEvent.cs \
        src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs
git commit -m "feat(streaming): emit SpeechStartedEvent per registered audio stream

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: FCM speech-start wake sender

**Files:**
- Modify: `src/dotnet/Api/Constants.cs:267-291` (MessageDataKeys)
- Modify: `src/dotnet/Api/Identifiers/NotificationKind.cs`
- Modify: `src/dotnet/Notifications.Service/IFirebaseMessagingClient.cs`
- Modify: `src/dotnet/Notifications.Service/FirebaseMessagingClient.cs`
- Modify: `tests/Testing.Host/FirebaseMessagingTestSink.cs`

**Interfaces:**
- Consumes: `FirebaseMessaging.SendEachForMulticastAsync` + the private
  `HandleBatchResponse` (stale-token pruning) already in
  `FirebaseMessagingClient`; `SendDismissal` is the data-only template.
- Produces:
  `Task SendSpeechStartedWake(ChatId chatId, AuthorId authorId, Moment
  startedAt, IReadOnlyCollection<Symbol> deviceIds, CancellationToken
  cancellationToken)` on `IFirebaseMessagingClient`;
  `NotificationKind.SpeechStarted`;
  `Constants.Notification.MessageDataKeys.AuthorId = "authorId"`;
  test sink record `FirebaseWakeMessage(ChatId ChatId, AuthorId AuthorId,
  Moment StartedAt, IReadOnlyList<Symbol> DeviceIds)` exposed via
  `FirebaseMessagingTestSink.Wakes`.

- [ ] **Step 1: Add the AuthorId data key**

In `src/dotnet/Api/Constants.cs`, inside `MessageDataKeys` (after line 269
`NotificationId`), add:

```csharp
            public const string AuthorId = "authorId";
```

and add `AuthorId` to `ValidKeys` (keep the array's alphabetical-ish order):

```csharp
            public static readonly string[] ValidKeys = {
                AuthorId, Body, ChatId, ChatEntryId, DismissedIds, DismissedTags, LastEntryLocalId, Icon, ImageUrl, Kind, Link, NotificationId, Silent, Tag, Title, Timestamp
            };
```

- [ ] **Step 2: Add NotificationKind.SpeechStarted**

In `src/dotnet/Api/Identifiers/NotificationKind.cs`, insert before `Invalid`
(which must stay last):

```csharp
    IncomingCall,
    SpeechStarted,
    Invalid, // Must be the very last entry here - it is used in NotificationId parsing logic
```

- [ ] **Step 3: Extend IFirebaseMessagingClient**

In `src/dotnet/Notifications.Service/IFirebaseMessagingClient.cs`, add after
`SendDismissal`:

```csharp
    Task SendSpeechStartedWake(
        ChatId chatId,
        AuthorId authorId,
        Moment startedAt,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement it**

In `src/dotnet/Notifications.Service/FirebaseMessagingClient.cs`, add after
`SendDismissal` (before `// Private methods` / `HandleBatchResponse`):

```csharp
    public async Task SendSpeechStartedWake(
        ChatId chatId,
        AuthorId authorId,
        Moment startedAt,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        var data = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.Kind, NotificationKind.SpeechStarted.ToString() },
            { Constants.Notification.MessageDataKeys.ChatId, chatId.Value },
            { Constants.Notification.MessageDataKeys.AuthorId, authorId.Value },
            { Constants.Notification.MessageDataKeys.Timestamp, ((long)startedAt.EpochOffset.TotalMilliseconds).ToString() },
        };
        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            Data = data,
            // Android-only data message: a wake for stale speech is useless, so the short
            // TTL + per-chat collapse key keep at most the latest wake queued per device.
            Android = new AndroidConfig {
                Data = data,
                Priority = Priority.High,
                TimeToLive = TimeSpan.FromSeconds(60),
                CollapseKey = $"speech-started-{chatId.Value}",
            },
        };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 5: Extend the test sink**

In `tests/Testing.Host/FirebaseMessagingTestSink.cs`, add after the
`FirebaseSentMessage` record:

```csharp
public sealed record FirebaseWakeMessage(
    ChatId ChatId,
    AuthorId AuthorId,
    Moment StartedAt,
    IReadOnlyList<Symbol> DeviceIds);
```

Inside `FirebaseMessagingTestSink`, add a wake queue + property next to the
existing ones:

```csharp
    private readonly ConcurrentQueue<FirebaseWakeMessage> _wakes = new();

    public IReadOnlyList<FirebaseWakeMessage> Wakes => _wakes.ToArray();
```

update `Clear`:

```csharp
    public void Clear()
    {
        _messages.Clear();
        _wakes.Clear();
    }
```

and implement the new interface method (after `SendDismissal`):

```csharp
    public Task SendSpeechStartedWake(
        ChatId chatId,
        AuthorId authorId,
        Moment startedAt,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        log.LogInformation("SendSpeechStartedWake: chat {ChatId} -> {DeviceCount} device(s)",
            chatId, deviceIds.Count);
        _wakes.Enqueue(new FirebaseWakeMessage(chatId, authorId, startedAt, [..deviceIds]));
        return Task.CompletedTask;
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/dotnet/Notifications.Service/Notifications.Service.csproj && dotnet build tests/Testing.Host/Testing.Host.csproj`
Expected: both build with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Constants.cs \
        src/dotnet/Api/Identifiers/NotificationKind.cs \
        src/dotnet/Notifications.Service/IFirebaseMessagingClient.cs \
        src/dotnet/Notifications.Service/FirebaseMessagingClient.cs \
        tests/Testing.Host/FirebaseMessagingTestSink.cs
git commit -m "feat(notifications): data-only FCM speech-started wake message

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: OnSpeechStartedEvent handler + integration tests (TDD)

**Files:**
- Test: `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs` (create)
- Modify: `src/dotnet/Notifications.Contracts/INotificationsBackend.cs:66-68`
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs`

**Interfaces:**
- Consumes: `SpeechStartedEvent` (Task 2), `SendSpeechStartedWake` +
  `FirebaseMessagingTestSink.Wakes` (Task 3), `NotificationsSettings` +
  `Features_EnableWalkieTalkiePush` (Task 1), plus existing:
  `AuthorsBackend.ListUserIds` / `.Get`, `GetActiveParticipantUserIds`
  (`NotificationsBackend.cs:456`), `ServerKvasBackend.ForUser(userId)
  .UserListeningSettings()` / `.ChatUserSettings(chatId)` (extension methods in
  `src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs`, namespace
  `ActualChat.Users`), `ListDevices(userId, ct)`, `IServerFeatures`,
  `RecentlySeenMap<TKey, TValue>` (ActualLab; NOT thread-safe — guard with
  `lock`, see `InMemoryQueueProcessor.MarkKnown` for the pattern),
  `ListeningMode.Forever` (`ActualChat.Users`), `DeviceType.AndroidApp`.
- Produces: `[EventHandler] Task OnSpeechStartedEvent(SpeechStartedEvent
  eventCommand, CancellationToken cancellationToken)` on `INotificationsBackend`
  and its implementation.

- [ ] **Step 1: Write the failing integration tests**

Create `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs`:

```csharp
using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class WalkieTalkiePushTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoWakeDelay = TimeSpan.FromSeconds(3);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();
    private ILiveSessionsBackend LiveSessionsBackend => AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    private IServerKvasBackend ServerKvasBackend => AppHost.Services.GetRequiredService<IServerKvasBackend>();
    private IAuthors Authors => Tester.AppServices.GetRequiredService<IAuthors>();

    [Fact]
    public async Task ArmedByAlwaysListenedChatGetsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT always-listened");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByAlwaysListened(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await TestExt.When(() => {
            Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, WakeTimeout);
    }

    [Fact]
    public async Task ArmedByForeverListeningModeGetsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT forever-mode");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByForeverListeningMode(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await TestExt.When(() => {
            Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, WakeTimeout);
    }

    [Fact]
    public async Task NotArmedMemberGetsNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT not-armed");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task SpeakerGetsNoWake()
    {
        // arrange
        var (chatId, _, bob, bobAuthor) = await CreateChatWithAliceAndBob("WT speaker-excluded");
        var deviceId = await RegisterDevice(bob.Id, DeviceType.AndroidApp);
        await ArmByAlwaysListened(bob.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task ActiveParticipantGetsNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT active-participant");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByAlwaysListened(alice.Id, chatId);
        await Tester.SignIn(alice);
        var aliceAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        await LiveSessionsBackend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.AudioListen, true, CancellationToken.None);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task SecondUtteranceWithinWakeTtlIsSuppressed()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT wake-ttl");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByAlwaysListened(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);
        await TestExt.When(() => {
            Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
            return Task.CompletedTask;
        }, WakeTimeout);
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Count(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)).Should().Be(1);
    }

    [Fact]
    public async Task NonAndroidDevicesGetNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT web-device");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.WebBrowser);
        await ArmByAlwaysListened(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.DeviceIds.Contains(deviceId));
    }

    // Private methods

    private async Task<(ChatId ChatId, AccountFull Alice, AccountFull Bob, Author BobAuthor)>
        CreateChatWithAliceAndBob(string title)
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, title);
        await Tester.InviteToChat(chatId, alice);
        var bobAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        return (chatId, alice, bob, bobAuthor);
    }

    private Task Speak(ChatId chatId, AuthorId authorId)
        => LiveSessionsBackend.OnStreamRegistered(chatId, authorId, null, false, CancellationToken.None);

    private Task ArmByAlwaysListened(UserId userId, ChatId chatId)
        => ServerKvasBackend.ForUser(userId).UserListeningSettings()
            .Update(x => x.WithAlwaysListeningChat(chatId));

    private Task ArmByForeverListeningMode(UserId userId, ChatId chatId)
        => ServerKvasBackend.ForUser(userId).ChatUserSettings(chatId)
            .Update(x => x with { ListeningMode = ListeningMode.Forever });

    private async Task<Symbol> RegisterDevice(UserId userId, DeviceType deviceType)
    {
        var deviceId = new Symbol($"wt-device-{deviceType}-{userId.Value}");
        await Commander.Call(new NotificationsBackend_RegisterDevice(userId, deviceId, deviceType, Symbol.Empty));
        return deviceId;
    }
}
```

Deliberately not covered (documented, not placeholders): the flag-off and
member-cap paths need per-host config overrides on a shared fixture — both are
single early-return lines; skip automating them in this task. Per-recipient
failure isolation (one recipient throws, others still get wakes) can't be
forced through the recording sink — it's guaranteed structurally by the
try/catch inside the handler's recipient loop.

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -20
```
Expected: FAIL — the two `Armed*GetsWake` and `SecondUtterance*` tests time out
in `TestExt.When` (no handler exists, so no wake is ever recorded); the
negative tests may pass trivially. If the build fails instead because
`OnSpeechStartedEvent` doesn't exist yet — that's fine, same signal.

- [ ] **Step 3: Declare the event handler on the interface**

In `src/dotnet/Notifications.Contracts/INotificationsBackend.cs`, in the
`// Events` section (after `OnReadPositionChangedEvent`, line 66), add:

```csharp
    [EventHandler]
    Task OnSpeechStartedEvent(SpeechStartedEvent eventCommand, CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement the handler**

In `src/dotnet/Notifications.Service/NotificationsBackend.cs`:

Add the using at the top (with the other `ActualChat.*` usings):

```csharp
using ActualChat.Notifications.Module;
```

Add the wake-pending field right after the `_softBuffers` field (line 24):

```csharp
    private const int WakePendingCapacity = 10_000;

    // Suppresses duplicate wakes per (user, chat) while a just-sent wake is presumed in flight.
    // Not thread-safe by itself - always access under lock.
    private readonly RecentlySeenMap<(UserId UserId, ChatId ChatId), Unit> _wakePending = new(
        WakePendingCapacity,
        services.GetRequiredService<NotificationsSettings>().WalkieTalkieWakeTtl);
```

Add the DI properties next to the other injected services (after
`ServerKvasBackend`, line 34):

```csharp
    private NotificationsSettings Settings { get; } = services.GetRequiredService<NotificationsSettings>();
    private IServerFeatures ServerFeatures { get; } = services.GetRequiredService<IServerFeatures>();
```

Add the event handler after `OnSignedOut` (line 698), before
`// Private methods`:

```csharp
    [EventHandler]
    public virtual async Task OnSpeechStartedEvent(SpeechStartedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (chatId, authorId, startedAt) = eventCommand;
        if (!await ServerFeatures.Get<Features_EnableWalkieTalkiePush>(cancellationToken).ConfigureAwait(false))
            return;

        var userIds = await AuthorsBackend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
        if (userIds.Count > Settings.WalkieTalkieMaxChatMembers)
            return;

        var speaker = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false);
        var activeUserIds = await GetActiveParticipantUserIds(chatId, cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            if (userId == speaker?.UserId || activeUserIds.Contains(userId))
                continue;

            try {
                await SendWalkieTalkieWake(userId, chatId, authorId, startedAt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e,
                    "Walkie-talkie wake failed for user '{UserId}' in chat '{ChatId}'", userId, chatId);
            }
        }
    }
```

Add the two private helpers in the `// Private methods` section (after
`GetNotificationMode`, line 942):

```csharp
    private async Task SendWalkieTalkieWake(
        UserId userId, ChatId chatId, AuthorId authorId, Moment startedAt, CancellationToken cancellationToken)
    {
        if (!await IsArmedForWalkieTalkie(userId, chatId, cancellationToken).ConfigureAwait(false))
            return;

        lock (_wakePending) {
            if (!_wakePending.TryAdd((userId, chatId)))
                return;
        }

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
    }

    private async Task<bool> IsArmedForWalkieTalkie(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        var kvas = ServerKvasBackend.ForUser(userId);
        var alwaysListened = await kvas.UserListeningSettings()
            .Get(x => x.AlwaysListenedChatIds, cancellationToken)
            .ConfigureAwait(false);
        if (alwaysListened.Contains(chatId))
            return true;

        var listeningMode = await kvas.ChatUserSettings(chatId)
            .Get(x => x.ListeningMode, cancellationToken)
            .ConfigureAwait(false);
        return listeningMode == ListeningMode.Forever;
    }
```

(If `ListeningMode` is unresolved, add `using ActualChat.Users;` — it is
likely already imported.)

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -20
```
Expected: PASS — 7 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Notifications.Contracts/INotificationsBackend.cs \
        src/dotnet/Notifications.Service/NotificationsBackend.cs \
        tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs
git commit -m "feat(notifications): walkie-talkie wake push on speech start

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build ActualChat.CI.slnf`
Expected: Build succeeded, 0 errors. (Warnings that pre-exist are fine; no new
ones from the touched files.)

- [ ] **Step 2: Full Notifications integration suite**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -10
```
Expected: all tests pass (the pre-existing suite must not regress — the module
base-class change in Task 1 and the new event in Task 2 touch shared paths).

- [ ] **Step 3: Confirm clean tree (only the intended leftovers)**

Run: `git status --short`
Expected: only the three pre-existing onboarding files show as modified;
everything from this plan is committed.
