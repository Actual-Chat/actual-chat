# Android Incoming-Call UI (Stage B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In-app incoming-call banner + modal on Android (push-triggered, computed-driven), plus a ringer notification with Accept/Decline actions when the app is backgrounded or killed.

**Architecture:** The FCM `IncomingCall` push only wakes the client. A new scoped `IncomingCallUI` service tracks ringing chats and derives the visible ring from the reactive `LiveSessionUI.Get(chatId)` — cancel/timeout/answer-elsewhere all end the ring without any further push. Android platform code contributes the FCM branch, the `incoming_calls` notification channel with Accept/Decline actions, a Decline broadcast receiver that works without Blazor, and an `IIncomingCallsBridge` implementation (looping ringtone + notification bookkeeping + reconciliation).

**Tech Stack:** .NET 10 MAUI (Android head), Blazor Hybrid (`UI.Blazor.App`), ActualLab.Fusion compute services, Firebase Cloud Messaging (data messages), AndroidX NotificationCompat.

**Spec:** `docs/superpowers/specs/2026-07-07-android-incoming-call-ui-design.md`

## Global Constraints

- **Read `docs/CODING_STYLE.md` and `docs/development/ui-components.md` before writing any code.** Non-negotiable specifics used throughout this plan: no `Async` suffix; no XML docs on members (only short type-level `<summary>` where the name isn't obvious); Allman braces for methods/types, K&R for everything else; `.ConfigureAwait(false)` in service code, `.ConfigureAwait(true)` in UI code that touches instance state after `await`; no inline Tailwind in `.razor` (use `c-` classes + `@apply` in CSS); max line length 120.
- **Comments:** default to none. Only keep the comments shown in the code blocks below — they mark non-obvious constraints. Do not add "what this does" comments.
- Server ring timeout is **40 s** (`LiveSessionsBackend.RingTimeout`); the client mirrors it only in `SetTimeoutAfter` on the Android notification.
- No new Android permissions, no `USE_FULL_SCREEN_INTENT`, no `CallStyle`, no foreground service — those are stage A.
- Build verification: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj` for shared code; `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android` for Android code (requires the maui-android workload — present on the host).
- Commit after each task with the message given in the task (the plan was approved with these commit steps).

## Reuse

Existing abstractions this plan builds on (do not re-implement):

| Existing | Used for |
|---|---|
| `LiveSessionUI.Get` / `.AcceptCall` / `.DeclineCall` / `.AmIInLiveConversation` (`src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`) | ring source of truth, call actions |
| `ChatAudioUI.SetRecordingChatId` / `.SetListeningState` | actually joining the call audio |
| `Banner` component + `Banners.razor` always-on slot (like `ReconnectBanner`) | banner rendering |
| `NotificationHelper` (`EnsureAttentionNotificationChannelExist` pattern, `CreateViewIntent`, `RequestCodeProvider`) | call channel + intents |
| `ChatAttentionService` + `AlarmReceiver` action pattern | Decline broadcast receiver |
| `AppServicesAccessor.DispatchToBlazor` / `TryGetScopedServices`, `AndroidUtils.IsAppForeground` | FCM → Blazor bridge |
| `IntentHandler` → `NotificationHandler` → `AppNavigationQueue`, `Links.Chat` | notification tap / accept routing |
| `MauiSession` (secure-storage session) | Session for Blazor-less Decline |
| `AudioRecorder.MicrophonePermission.CheckOrRequest`, `CameraPermissionHandler` | permissions |
| `ChatVideoUI.StartCameraWarmup` / `TryClaimCameraWarmupRecorder` / `StartVideoStreaming`, `JoinVideoCallModal`'s JS module + video-session classes | camera preview & camera-on accept |

New components and placement: `IncomingCallUI`, `IIncomingCallsBridge`, `IncomingCall` → `UI.Blazor.App/Services` (shared — reusable for web/iOS in-app calls later; inert off-Android because nothing triggers it there). `IncomingCallBanner` → `UI.Blazor.App/Components/Banners`. Android-only: `IncomingCallNotifications`, `CallActionReceiver`, `AndroidIncomingCallsBridge` → `App.Maui/Platforms/Android`.

**Deviation from spec (deliberate, simpler):** the banner is an always-on component in `Banners.razor` (like `ReconnectBanner`) driven by `IncomingCallUI` state, NOT a `BannerUI.Show`/`IBannerView` dynamic banner — the dynamic pipeline's dismiss semantics don't fit a state-driven call banner. The spec's `IIncomingCallRinger` is merged into the broader `IIncomingCallsBridge` (ring + notification bookkeeping + reconciliation) — one platform hook instead of two.

---

### Task 1: Server: call-scoped push tag (fixes broken call dismissal on Android)

`CallNotification.SimilarityKey` is a `ConversationId` (`"{chatId}:{lid}"`), which `ChatId.TryParse` rejects, so today `GetPushTag()` falls through to `null` → the ring push goes out with tag `"topic"` and the dismissal push carries **no** tag at all — the Android client can't cancel the call banner. Fix: give calls their own tag.

**Files:**
- Modify: `src/dotnet/Api/Constants.cs` (~line 267, inside `public static class Notification`)
- Modify: `src/dotnet/Api/Notifications/NotificationExt.cs:10-25`
- Test: `tests/Notifications.IntegrationTests/CallNotificationTagTests.cs` (new)

**Interfaces:**
- Produces: `Constants.Notification.CallTagPrefix` (`"call-"`, `const string`) — Task 5's Android code rebuilds the same tag as `CallTagPrefix + chatId.Value`.
- Produces: `CallNotification.GetPushTag()` → `"call-{chatId}"`, `GetChatTag()` → `chatId`.

- [x] **Step 1: Write the failing test**

Create `tests/Notifications.IntegrationTests/CallNotificationTagTests.cs`:

```csharp
namespace ActualChat.Notifications.IntegrationTests;

public class CallNotificationTagTests(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void PushTagIsCallScoped()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var caller = AuthorId.New(TestChatId, 5);
        var ring = CallNotification.New(TestUserId, conversationId, caller, hasVideo: false);

        ring.GetPushTag().Should().Be("call-" + TestChatId.Value);
        ring.GetChatTag().Should().Be(TestChatId.Value);
    }

    [Fact]
    public void DismissalSharesRingTag()
    {
        var conversationId = ConversationId.New(TestChatId, 2067);
        var caller = AuthorId.New(TestChatId, 5);
        var ring = CallNotification.New(TestUserId, conversationId, caller, hasVideo: true);
        var dismissal = new CallNotification(
            NotificationId.New(TestUserId, NotificationKind.IncomingCall, conversationId.Value));

        dismissal.GetPushTag().Should().Be(ring.GetPushTag());
        dismissal.GetPushTag().Should().NotBeNull();
    }
}
```

- [x] **Step 2: Run the test, verify it fails**

Run: `dotnet test tests/Notifications.IntegrationTests --filter CallNotificationTag`
Expected: FAIL — `GetPushTag()` currently returns `null` (falls to `GetChatTag` which can't parse the `ConversationId`-shaped similarity key).

- [x] **Step 3: Implement**

In `src/dotnet/Api/Constants.cs`, inside `public static class Notification`, right above `public static class MessageDataKeys`, add:

```csharp
        public const string CallTagPrefix = "call-";
```

In `src/dotnet/Api/Notifications/NotificationExt.cs`, replace `GetPushTag` and `GetChatTag`:

```csharp
    public static string? GetPushTag(this Notification notification)
        => notification switch {
            ChatEntryNotification n => n.EntryId.Value,
            // A call's ring and its dismissal must collapse onto a banner of their own —
            // the chat-wide tag would make a call dismissal close the chat's message banners too.
            CallNotification n => Constants.Notification.CallTagPrefix + n.ChatId.Value,
            _ => notification.GetChatTag(),
        };

    // The chat a notification belongs to, as a tag (one value per chat). Returns null for
    // non-chat notifications.
    public static string? GetChatTag(this Notification notification)
        => notification switch {
            ConversationNotification n => n.ChatId.Value,
            ChatEntryRelatedNotification n => n.ChatId.Value,
            ChatEntryNotification n => n.ChatId.Value,
            CallNotification n => n.ChatId.Value,
            ChatNotification n when ChatId.TryParse(n.SimilarityKey, out var chatId) => chatId.Value,
            _ => null,
        };
```

(Keep the existing `case` order — `CallNotification` must come before the generic `ChatNotification` arm.)

- [x] **Step 4: Run the test, verify it passes**

Run: `dotnet test tests/Notifications.IntegrationTests --filter CallNotificationTag`
Expected: PASS. Also run the whole class file's neighbors to catch regressions:
`dotnet test tests/Notifications.IntegrationTests --filter NotificationSerializationTests`
Expected: PASS (message/conversation tags unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Constants.cs src/dotnet/Api/Notifications/NotificationExt.cs tests/Notifications.IntegrationTests/CallNotificationTagTests.cs
git commit -m "fix(notifications): call-scoped push tag so call ring/dismissal collapse onto own banner"
```

---

### Task 2: `IncomingCallUI` service + `IIncomingCallsBridge` + registration

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/IIncomingCallsBridge.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs:73` (next to `fusion.AddService<LiveSessionUI>`)
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs:58` (next to `LiveSessionUI` property)
- Test: `tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs` (new)

**Interfaces:**
- Produces: `record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo)` (namespace `ActualChat.UI.Blazor.App.Services`).
- Produces: `IncomingCallUI` members used by later tasks:
  - `void OnRing(ChatId chatId)` — Tasks 5, 7 (FCM foreground signal, reconciliation, accept routing).
  - `[ComputeMethod] Task<IncomingCall?> GetIncomingCall(CancellationToken)` — Task 3 (banner).
  - `[ComputeMethod] Task<IncomingCall?> GetRingingCall(ChatId, CancellationToken)` — Task 4 (modal).
  - `Task Accept(ChatId chatId, bool withCamera = false)`, `Task Decline(ChatId chatId)` — Tasks 3, 4, 7.
  - `static IncomingCall? FindRingingCall(LiveSession? live, AuthorId ownAuthorId)` — pure, unit-tested.
- Produces: `interface IIncomingCallsBridge` — implemented in Task 7 (Android):

```csharp
namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Platform hook for incoming-call rings: the looping ringtone/vibration and the
/// system call-notification bookkeeping. Registered on Android only; when absent,
/// <see cref="IncomingCallUI"/> is inert past its computed state.
/// </summary>
public interface IIncomingCallsBridge
{
    void StartRinging();
    void StopRinging();
    Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken);
    void DismissCallNotification(ChatId chatId);
}
```

- Consumes: `LiveSessionUI.Get/AcceptCall/DeclineCall/AmIInLiveConversation`, `ChatAudioUI.SetRecordingChatId/SetListeningState`, `Hub.Authors.GetOwn`, `Hub.History.NavigateTo(Links.Chat(chatId))`, `Hub.ToastUI.Show`, `Hub.AudioRecorder.MicrophonePermission.CheckOrRequest`.

- [x] **Step 1: Write the failing unit test**

Create `tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs`:

```csharp
using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class IncomingCallUITest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Host = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId Me = AuthorId.New(TestChatId, 2);

    [Fact]
    public void FindsMyRingingInvite()
    {
        var live = NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing });

        var call = IncomingCallUI.FindRingingCall(live, Me);

        call.Should().NotBeNull();
        call!.ChatId.Should().Be(TestChatId);
        call.Caller.Should().Be(Host);
    }

    [Fact]
    public void IgnoresNonRingingStates()
    {
        foreach (var status in new[] { CallInviteStatus.Accepted, CallInviteStatus.Declined, CallInviteStatus.Missed }) {
            var live = NewCall(new CallInvite { InviteeId = Me, Status = status });
            IncomingCallUI.FindRingingCall(live, Me).Should().BeNull();
        }
    }

    [Fact]
    public void IgnoresForeignInviteNullSessionNonCallAndOwnCall()
    {
        var other = AuthorId.New(TestChatId, 3);
        IncomingCallUI.FindRingingCall(null, Me).Should().BeNull();
        IncomingCallUI.FindRingingCall(
            NewCall(new CallInvite { InviteeId = other, Status = CallInviteStatus.Ringing }), Me)
            .Should().BeNull();
        IncomingCallUI.FindRingingCall(
            NewCall(new CallInvite { InviteeId = Me, Status = CallInviteStatus.Ringing })
                with { Kind = LiveSessionKind.Ambient },
            Me)
            .Should().BeNull();
        IncomingCallUI.FindRingingCall(
            NewCall(new CallInvite { InviteeId = Host, Status = CallInviteStatus.Ringing }), Host)
            .Should().BeNull();
    }

    private static LiveSession NewCall(params CallInvite[] invites)
        => new() {
            ChatId = TestChatId,
            Host = Host,
            Kind = LiveSessionKind.Call,
            Invites = invites,
        };
}
```

- [x] **Step 2: Run it, verify it fails to compile**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter IncomingCallUITest`
Expected: build FAILURE — `IncomingCallUI` does not exist yet.

- [x] **Step 3: Create the bridge interface and the service**

Create `src/dotnet/UI.Blazor.App/Services/IIncomingCallsBridge.cs` with the interface shown in **Interfaces** above.

Create `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs`:

```csharp
using System.Collections.Immutable;
using ActualChat.Live;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo);

/// <summary>
/// Client-side incoming-ring state: a push (or notification reconciliation) triggers
/// <see cref="OnRing"/>, but the reactive <see cref="LiveSessionUI.Get"/> is the source
/// of truth — a ring ends itself on cancel, timeout, decline, or accept on another device.
/// </summary>
public class IncomingCallUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private readonly Lock _lock = new();
    private readonly MutableState<ImmutableList<ChatId>> _ringingChatIds;

    private IIncomingCallsBridge? Bridge { get; }
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;

    public IncomingCallUI(AppUIHub hub) : base(hub)
    {
        Bridge = hub.Services.GetService<IIncomingCallsBridge>();
        _ringingChatIds = StateFactory.NewMutable(
            ImmutableList<ChatId>.Empty,
            StateCategories.Get(GetType(), "RingingChatIds"));
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnRing(ChatId chatId)
    {
        if (chatId is null || chatId.Value.IsNullOrEmpty())
            return;

        lock (_lock) {
            var chatIds = _ringingChatIds.Value;
            if (!chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Add(chatId);
        }
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetIncomingCall(CancellationToken cancellationToken)
    {
        var chatIds = await _ringingChatIds.Use(cancellationToken).ConfigureAwait(false);
        for (var i = chatIds.Count - 1; i >= 0; i--) {
            var call = await GetRingingCall(chatIds[i], cancellationToken).ConfigureAwait(false);
            if (call is not null)
                return call;
        }
        return null;
    }

    [ComputeMethod]
    public virtual async Task<IncomingCall?> GetRingingCall(ChatId chatId, CancellationToken cancellationToken)
    {
        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (ownAuthor is null)
            return null;

        var call = FindRingingCall(live, ownAuthor.Id);
        if (call is null)
            return null;

        if (await LiveSessionUI.AmIInLiveConversation(chatId, cancellationToken).ConfigureAwait(false))
            return null;

        return call;
    }

    public static IncomingCall? FindRingingCall(LiveSession? live, AuthorId ownAuthorId)
    {
        if (live is not { Kind: LiveSessionKind.Call })
            return null;
        if (live.Host == ownAuthorId)
            return null;

        var invite = live.Invites.FirstOrDefault(i => i.InviteeId == ownAuthorId);
        if (invite is not { Status: CallInviteStatus.Ringing })
            return null;

        return new IncomingCall(live.ChatId, live.Host, live.Rules.VideoAllowed);
    }

    public async Task Accept(ChatId chatId, bool withCamera = false)
    {
        var call = await GetRingingCall(chatId, default).ConfigureAwait(true);
        EndRing(chatId);
        if (call is null) {
            Hub.ToastUI.Show("Call ended", "icon-phone", ToastDismissDelay.Short);
            return;
        }

        try {
            await LiveSessionUI.AcceptCall(chatId, default).ConfigureAwait(true);
        }
        catch (Exception e) {
            Log.LogWarning(e, "AcceptCall failed for chat #{ChatId}", chatId);
            Hub.ToastUI.Show("Call ended", "icon-phone", ToastDismissDelay.Short);
            return;
        }

        await Hub.History.NavigateTo(Links.Chat(chatId)).ConfigureAwait(true);
        if (await Hub.AudioRecorder.MicrophonePermission.CheckOrRequest(CancellationToken.None).ConfigureAwait(true))
            await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
        else
            // Mic denied: still join the call as a listener.
            await ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(true);
        _ = withCamera; // Camera-on accept is wired in the camera-preview task.
    }

    public async Task Decline(ChatId chatId)
    {
        EndRing(chatId);
        try {
            await LiveSessionUI.DeclineCall(chatId, default).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DeclineCall failed for chat #{ChatId}", chatId);
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SyncRings)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 10), Log)
            .Run(cancellationToken);

    // Private methods

    private async Task SyncRings(CancellationToken cancellationToken)
    {
        if (Bridge is not null)
            // A call push may have landed while the app was killed and the user opened it
            // from the launcher — pick the ring up from the still-active system notification.
            foreach (var chatId in await Bridge.ListActiveCallChatIds(cancellationToken).ConfigureAwait(false))
                OnRing(chatId);

        var cCall = await Computed
            .Capture(() => GetIncomingCall(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var isRinging = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var call = cCall.Value;
                if (call is not null != isRinging) {
                    isRinging = call is not null;
                    if (isRinging)
                        Bridge?.StartRinging();
                    else
                        Bridge?.StopRinging();
                }
                await PruneDeadRings(cancellationToken).ConfigureAwait(false);

                await cCall.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                cCall = await cCall.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            if (isRinging)
                Bridge?.StopRinging();
        }
    }

    private async Task PruneDeadRings(CancellationToken cancellationToken)
    {
        // Dead rings would otherwise accumulate for the whole scope lifetime; a still-live
        // second ring survives the prune and surfaces once the current one ends.
        ImmutableList<ChatId> chatIds;
        lock (_lock)
            chatIds = _ringingChatIds.Value;
        foreach (var chatId in chatIds) {
            if (await GetRingingCall(chatId, cancellationToken).ConfigureAwait(false) is not null)
                continue;

            lock (_lock)
                _ringingChatIds.Value = _ringingChatIds.Value.Remove(chatId);
        }
    }

    private void EndRing(ChatId chatId)
    {
        lock (_lock) {
            var chatIds = _ringingChatIds.Value;
            if (chatIds.Contains(chatId))
                _ringingChatIds.Value = chatIds.Remove(chatId);
        }
        Bridge?.DismissCallNotification(chatId);
    }
}
```

- [x] **Step 4: Register the service**

In `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`, after line 73 (`fusion.AddService<LiveSessionUI>(ServiceLifetime.Scoped);`):

```csharp
        fusion.AddService<IncomingCallUI>(ServiceLifetime.Scoped);
```

In `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs`, after the `LiveSessionUI` property (line 58):

```csharp
    public IncomingCallUI IncomingCallUI => field ??= Services.GetRequiredService<IncomingCallUI>();
```

- [x] **Step 5: Run tests and build**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter IncomingCallUITest`
Expected: PASS (3 tests).
Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: build OK.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs src/dotnet/UI.Blazor.App/Services/IIncomingCallsBridge.cs src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs src/dotnet/UI.Blazor.App/Services/AppUIHub.cs tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs
git commit -m "feat(calls): IncomingCallUI — computed-driven incoming-ring state with platform bridge hook"
```

---

### Task 3: `IncomingCallBanner`

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Banners/IncomingCallBanner.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Banners/Banners.razor:8` (add next to `<ReconnectBanner/>`)
- Modify: `src/dotnet/UI.Blazor.App/Components/Banners/banners.css` (append a section)

**Interfaces:**
- Consumes: `IncomingCallUI.GetIncomingCall/Accept/Decline` (Task 2), `IncomingCallModal.Model(AuthorId CallerId)` (Task 4 — until Task 4 lands, the modal still opens with its old stub content; the banner compiles either way because the `Model` stays a one-`AuthorId` positional record).
- Produces: nothing consumed later.

- [x] **Step 1: Create the component**

`src/dotnet/UI.Blazor.App/Components/Banners/IncomingCallBanner.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, IncomingCallBanner.ComputedModel>
@{
    var m = State.Value;
    if (m.Call is not { } call)
        return;
}

<Banner Class="incoming-call-banner" Severity="BannerSeverity.Info">
    <Body>
        <div class="c-texts" @onclick="@OnExpandClick">
            <AuthorBadge AuthorId="@call.Caller" ShowPresence="true"/>
            <div class="c-subtitle">
                @(call.HasVideo ? "Incoming video call" : "Incoming call")@(m.ChatTitle.IsNullOrEmpty() ? "" : $" · {m.ChatTitle}")
            </div>
        </div>
    </Body>
    <Buttons>
        <Button Class="btn-transparent unhovered" Click="@OnDeclineClick">Decline</Button>
        <Button Class="btn-transparent unhovered on" Click="@OnAcceptClick">Accept</Button>
    </Buttons>
</Banner>

@code {
    private IncomingCallUI IncomingCallUI => Hub.IncomingCallUI;

    protected override ComputedState<ComputedModel>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<ComputedModel>.Options() {
                InitialValue = ComputedModel.None,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = GetStateCategory(t),
            });

    protected override async Task<ComputedModel> ComputeState(CancellationToken cancellationToken) {
        var call = await IncomingCallUI.GetIncomingCall(cancellationToken).ConfigureAwait(false);
        if (call is null)
            return ComputedModel.None;

        var chat = await Hub.Chats.Get(Session, call.ChatId, cancellationToken).ConfigureAwait(false);
        return new ComputedModel {
            Call = call,
            ChatTitle = chat is { Kind: not ChatKind.Peer } ? chat.Title : "",
        };
    }

    private Task OnAcceptClick()
        => State.Value.Call is { } call ? IncomingCallUI.Accept(call.ChatId) : Task.CompletedTask;

    private Task OnDeclineClick()
        => State.Value.Call is { } call ? IncomingCallUI.Decline(call.ChatId) : Task.CompletedTask;

    private Task OnExpandClick() {
        if (State.Value.Call is { } call)
            _ = Hub.ModalUI.Show(new IncomingCallModal.Model(call.Caller));
        return Task.CompletedTask;
    }

    // Nested types

    public sealed record ComputedModel {
        public static readonly ComputedModel None = new();

        public IncomingCall? Call { get; init; }
        public string ChatTitle { get; init; } = "";
    }
}
```

- [x] **Step 2: Render it always-on**

In `Banners.razor`, after `<ReconnectBanner/>` (line 8):

```razor
<ReconnectBanner/>
<IncomingCallBanner/>
```

- [x] **Step 3: Add styles**

Append to `src/dotnet/UI.Blazor.App/Components/Banners/banners.css`:

```css
/* ── Incoming call banner ── */

.incoming-call-banner .c-texts {
    @apply flex-y gap-0.5;
    @apply cursor-pointer;
}
.incoming-call-banner .c-subtitle {
    @apply text-caption-1 text-03;
}
```

- [x] **Step 4: Build and eyeball**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: build OK. (Visual check happens in Task 9's manual matrix; there is no way to ring the banner yet — `OnRing` has no caller until Task 5.)

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Banners/IncomingCallBanner.razor src/dotnet/UI.Blazor.App/Components/Banners/Banners.razor src/dotnet/UI.Blazor.App/Components/Banners/banners.css
git commit -m "feat(calls): incoming-call banner driven by IncomingCallUI state"
```

---

### Task 4: Rework `IncomingCallModal` (wire Accept/Decline, auto-close on ring end)

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModal.razor` (full rewrite below)

**Interfaces:**
- Consumes: `IncomingCallUI.GetRingingCall/Accept/Decline` (Task 2).
- Produces: `IncomingCallModal.Model(AuthorId CallerId)` — positional single-`AuthorId` record, so the existing call sites (`AuthorModalHeader.razor:131`, Task 3's banner) keep compiling. The `AuthorModalHeader` "call" stub button now opens a modal that auto-closes when there is no live ring for that chat — acceptable for the dev stub; do not change `AuthorModalHeader`.

- [x] **Step 1: Rewrite the modal**

Replace the whole `IncomingCallModal.razor` with:

```razor
@using ActualChat.Contacts
@namespace ActualChat.UI.Blazor.App.Components
@implements IModalView<IncomingCallModal.Model>
@inherits ComputedStateComponent<AppUIHub, IncomingCallModal.ComputedModel?>
@{
    var m = State.Value;
    if (m is null)
        return;

    if (m.Author is not { } author || m.Call is not { } call) {
        // The ring ended (cancel / timeout / answered elsewhere) — close ourselves.
        if (!_isAutoClosed) {
            _isAutoClosed = true;
            Modal.Close();
        }
        return;
    }
}

<DialogFrame
    Title="@(call.HasVideo ? "Incoming video call" : "Incoming call")"
    Class="author-modal modal-sm incoming-call-modal wide-dialog-buttons"
    HasCloseButton="true">
    <Body>
    <IncomingCallModalHeader
        Author="author"
        IsOwn="false"/>
    </Body>
    <Buttons>
        <Button Class="btn-modal btn-danger" Click="@OnDeclineClick">
            <Icon><i class="icon-close text-xl"></i></Icon>
            <Title>Decline</Title>
        </Button>
        <Button Class="btn-modal btn-primary" Click="@OnAcceptClick">
            <Icon><i class="icon-phone text-xl"></i></Icon>
            <Title>Accept</Title>
        </Button>
    </Buttons>
</DialogFrame>

@code {
    private bool _isAutoClosed;

    private IAuthors Authors => Hub.Authors;
    private IncomingCallUI IncomingCallUI => Hub.IncomingCallUI;

    private AuthorId CallerId => ModalModel.CallerId;  // Shortcut
    private ChatId ChatId => CallerId.ChatId; // Shortcut

    [CascadingParameter] public Modal Modal { get; set; } = null!;
    [Parameter] public Model ModalModel { get; set; } = null!;

    protected override async Task<ComputedModel?> ComputeState(CancellationToken cancellationToken) {
        var callerId = CallerId;
        var chatId = ChatId;

        var session = Session;
        var author = await Authors.Get(session, chatId, callerId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return ComputedModel.None;

        var call = await IncomingCallUI.GetRingingCall(chatId, cancellationToken).ConfigureAwait(false);
        return new() {
            Author = author,
            Call = call,
        };
    }

    private Task OnAcceptClick() {
        var chatId = ChatId;
        Modal.Close();
        return IncomingCallUI.Accept(chatId);
    }

    private Task OnDeclineClick() {
        var chatId = ChatId;
        Modal.Close();
        return IncomingCallUI.Decline(chatId);
    }

    // Nested types

    public sealed record ComputedModel {
        public static readonly ComputedModel None = new();

        public Author? Author { get; init; }
        public IncomingCall? Call { get; init; }
    }

    public sealed record Model(AuthorId CallerId);
}
```

(`IncomingCallModalHeader.razor` stays as is. The `IsOwnAuthor` path of the old stub is gone — a ring from yourself is filtered out by `IncomingCallUI` anyway.)

- [x] **Step 2: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: build OK (the `AuthorModalHeader.razor:131` positional call site still compiles).

- [ ] **Step 3: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModal.razor
git commit -m "feat(calls): wire IncomingCallModal to IncomingCallUI — working Accept/Decline, auto-close on ring end"
```

---

### Task 5: Android: `incoming_calls` channel + call notification + FCM branch

**Files:**
- Create: `src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs:80` (new branch before the Attention branch)

**Interfaces:**
- Consumes: `Constants.Notification.CallTagPrefix` (Task 1), `IncomingCallUI.OnRing` (Task 2), `NotificationData` (existing), `NotificationHelper.CreateViewIntent/RequestCodeProvider` (existing).
- Produces (used by Tasks 6, 7):
  - `IncomingCallNotifications.ChannelId` (`"incoming_calls"`)
  - `static string DeclineAction` (`"{pkg}.IncomingCall.Decline"`), `static string ChatIdExtraKey`, `static string AcceptExtraKey`
  - `static void Show(NotificationData data)`
  - `static void Dismiss(ChatId chatId)`, `static ChatId[] ListActiveCallChatIds()`
  - `static string CallTag(ChatId chatId)` → `"call-{chatId}"`

- [x] **Step 1: Create `IncomingCallNotifications`**

`src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.Media;
using AndroidX.Core.App;
using Application = Android.App.Application;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public static class IncomingCallNotifications
{
    public const string ChannelId = "incoming_calls";
    // Mirrors the server's LiveSessionsBackend.RingTimeout: the banner self-destructs
    // at ring expiry even when the dismissal push never arrives (offline device).
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(40);
    private static ILogger? _log;

    private static Context Context => Application.Context;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(IncomingCallNotifications));

    public static string DeclineAction => Context.PackageName + ".IncomingCall.Decline";
    public static string AcceptExtraKey => Context.PackageName + ".IncomingCall.Accept";
    public static string ChatIdExtraKey => Context.PackageName + ".IncomingCall.ChatId";

    public static string CallTag(ChatId chatId)
        => Constants.Notification.CallTagPrefix + chatId.Value;

    public static void Show(NotificationData data)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Show: no ChatId, messageId: '{MessageId}'", data.MessageId);
            return;
        }

        EnsureChannelExists();
        var tag = data.Tag ?? CallTag(chatId);
        var link = data.Link ?? (string)Links.Chat(chatId);

        var contentIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        contentIntent.PutExtra(ChatIdExtraKey, chatId.Value);
        var contentPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            contentIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var acceptIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        acceptIntent.PutExtra(ChatIdExtraKey, chatId.Value);
        acceptIntent.PutExtra(AcceptExtraKey, true);
        var acceptPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            acceptIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var declineIntent = new Intent(Context, typeof(CallActionReceiver));
        declineIntent.SetAction(DeclineAction);
        declineIntent.PutExtra(ChatIdExtraKey, chatId.Value);
        var declinePendingIntent = PendingIntent.GetBroadcast(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            declineIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(Context, ChannelId)
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            .SetSmallIcon(Microsoft.Maui.Resource.Drawable.notification_app_icon)!
            .SetColor(0x0036A3)!
            .SetContentTitle(data.Title ?? "Incoming call")!
            .SetContentText(data.Body ?? "Incoming call")!
            .SetContentIntent(contentPendingIntent)!
            .SetCategory(Android.App.Notification.CategoryCall)!
            .SetPriority((int)NotificationPriority.High)!
            .SetAutoCancel(true)!
            .SetTimeoutAfter((long)RingTimeout.TotalMilliseconds)!;
        builder.AddAction(0, "Decline", declinePendingIntent);
        builder.AddAction(0, "Accept", acceptPendingIntent);
        var imageUrl = data.ImageUrl;
        if (!imageUrl.IsNullOrEmpty()) {
            var largeImage = NotificationHelper.GetImage(imageUrl!);
            if (largeImage != null)
                builder.SetLargeIcon(largeImage);
        }
        NotificationManagerCompat.From(Context)!.Notify(tag, 0, builder.Build());
    }

    public static void Dismiss(ChatId chatId)
        => NotificationManagerCompat.From(Context)!.Cancel(CallTag(chatId), 0);

    public static ChatId[] ListActiveCallChatIds()
    {
        var notificationManager = NotificationManagerCompat.From(Context)!;
        var active = notificationManager.ActiveNotifications;
        if (active is null)
            return [];

        return active
            .Select(n => n.Tag)
            .Where(tag => tag != null && tag.StartsWith(Constants.Notification.CallTagPrefix))
            .Select(tag => ChatId.TryParse(tag![Constants.Notification.CallTagPrefix.Length..], allowNull: true))
            .Where(chatId => chatId is not null)
            .Select(chatId => chatId!)
            .ToArray();
    }

    // Private methods

    private static void EnsureChannelExists()
    {
        var notificationManager = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        var channel = notificationManager.GetNotificationChannel(ChannelId);
        if (channel != null)
            return;

        channel = new NotificationChannel(ChannelId, "Incoming calls", NotificationImportance.High);
        var attrs = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.NotificationRingtone)!
            .SetContentType(AudioContentType.Music)!
            .Build();
        var ringtoneUri = Android.Net.Uri.Parse($"android.resource://{Context.PackageName}/"
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            + Microsoft.Maui.Resource.Raw.attention_ringtone);
        channel.SetSound(ringtoneUri, attrs);
        channel.SetVibrationPattern([0, 700, 500, 700, 500, 500]);
        notificationManager.CreateNotificationChannel(channel);
    }
}
```

`CallActionReceiver` is referenced above but implemented in Task 6. To keep this task compilable on its own, create it here as an empty no-op receiver (Task 6 fills it in):

`src/dotnet/App.Maui/Platforms/Android/CallActionReceiver.cs`:

```csharp
using Android.Content;

namespace ActualChat.App.Maui;

[BroadcastReceiver(Exported = false)]
public class CallActionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    { }
}
```

- [x] **Step 2: Branch in `FirebaseMessagingService.OnMessageReceived`**

In `FirebaseMessagingService.cs`, right after the `DismissedTags` block (line 78) and before the `NotificationKind.Attention` check, insert:

```csharp
        if (data.NotificationKind == NotificationKind.IncomingCall) {
            HandleIncomingCall(data);
            return;
        }
```

And add the private method (next to `ShowGetAttentionNotification`):

```csharp
    private static void HandleIncomingCall(NotificationData data)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Can't handle incoming-call push. Invalid ChatId. Ref messageId: '{MessageId}'", data.MessageId);
            return;
        }

        // Foreground with a live Blazor scope: the in-app banner + ringer own the ring,
        // no system notification — otherwise both would ring at once.
        if ((AndroidUtils.IsAppForeground() ?? false) && TryGetScopedServices(out _)) {
            _ = DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().OnRing(chatId),
                "IncomingCallUI.OnRing");
            return;
        }

        IncomingCallNotifications.Show(data);
    }
```

(`IncomingCallUI` is in `ActualChat.UI.Blazor.App.Services`, already a `using` in this file.)

- [x] **Step 3: Build**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
Expected: build OK.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs src/dotnet/App.Maui/Platforms/Android/CallActionReceiver.cs src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs
git commit -m "feat(calls): Android incoming-call push branch — ringer channel + Accept/Decline notification"
```

---

### Task 6: Android: Decline receiver + Accept routing + `MauiSession.ReadStored`

**Files:**
- Modify: `src/dotnet/App.Maui/Services/MauiSession.cs` (add `ReadStored`)
- Modify: `src/dotnet/App.Maui/Platforms/Android/CallActionReceiver.cs` (fill in the Task 5 stub)
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationHandler.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs` (add `HandleViewIntent`)

**Interfaces:**
- Consumes: `IncomingCallNotifications.DeclineAction/ChatIdExtraKey/AcceptExtraKey/Dismiss` (Task 5), `IncomingCallUI.Accept` (Task 2), `ILiveSessions.DeclineCall(Session, ChatId, CancellationToken)` (existing RPC client, registered app-wide by `ApiContractsModule.AddClient<ILiveSessions>()`).
- Produces: `MauiSession.ReadStored(): Task<Session?>` — static, Blazor-free session read.

- [x] **Step 1: Add `MauiSession.ReadStored`**

In `MauiSession.cs`, after `public static Task Start()`:

```csharp
    public static Task<Session?> ReadStored()
        => _readSessionTask ?? Task.Run(Read);
```

- [x] **Step 2: Implement `CallActionReceiver`**

Replace the stub body:

```csharp
using ActualChat.App.Maui.Services;
using ActualChat.Streaming;
using Android.Content;

namespace ActualChat.App.Maui;

[BroadcastReceiver(Exported = false)]
public class CallActionReceiver : BroadcastReceiver
{
    private ILogger Log => field ??= StaticLog.For<CallActionReceiver>();

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != IncomingCallNotifications.DeclineAction)
            return;

        var chatId = ChatId.TryParse(intent.GetStringExtra(IncomingCallNotifications.ChatIdExtraKey), allowNull: true);
        if (chatId is null)
            return;

        IncomingCallNotifications.Dismiss(chatId);
        // The RPC call needs async work the receiver's 10s budget must survive.
        var pendingResult = GoAsync();
        _ = BackgroundTask.Run(async () => {
            try {
                var session = await MauiSession.ReadStored().ConfigureAwait(false);
                var liveSessions = IPlatformApplication.Current?.Services.GetService<ILiveSessions>();
                if (session is null || liveSessions is null) {
                    Log.LogWarning("Decline: no session or ILiveSessions client; chat #{ChatId}", chatId);
                    return;
                }
                await liveSessions.DeclineCall(session, chatId, CancellationToken.None).ConfigureAwait(false);
            }
            finally {
                pendingResult?.Finish();
            }
        }, Log, "Decline call failed");
    }
}
```

- [x] **Step 3: Accept routing on notification tap**

In `IncomingCallNotifications.cs`, add `using ActualChat.UI.Blazor.App.Services;` to the usings and this method:

```csharp
    public static void HandleViewIntent(Intent intent)
    {
        if (!intent.GetBooleanExtra(AcceptExtraKey, false))
            return;

        var chatId = ChatId.TryParse(intent.GetStringExtra(ChatIdExtraKey), allowNull: true);
        if (chatId is null)
            return;

        Dismiss(chatId);
        // Accept re-verifies the ring against LiveSessionUI.Get once Blazor is up —
        // a stale tap yields a "Call ended" toast, not a phantom join.
        _ = AppServicesAccessor.DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().Accept(chatId),
            "IncomingCallUI.Accept", whenRendered: true);
    }
```

In `NotificationHandler.cs`:

```csharp
    public static void HandleIntent(Intent intent)
    {
        if (NotificationHelper.NotificationViewAction != intent.Action)
            return;

        AppNavigationQueue.EnqueueOrNavigateToUrl(intent.Data?.ToString(), AutoNavigationReason.Notification);
        IncomingCallNotifications.HandleViewIntent(intent);
    }
```

(Body tap carries no `AcceptExtraKey` → only navigates to the chat, banner shows there via the still-listed ring. Accept tap navigates AND accepts; the double navigation to the same chat is idempotent.)

- [x] **Step 4: Build**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
Expected: build OK.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/Services/MauiSession.cs src/dotnet/App.Maui/Platforms/Android/CallActionReceiver.cs src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationHandler.cs src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs
git commit -m "feat(calls): Android Decline-without-UI receiver and Accept-from-notification routing"
```

---

### Task 7: Android: `AndroidIncomingCallsBridge` (looping ringer + reconciliation) + DI

**Files:**
- Create: `src/dotnet/App.Maui/Platforms/Android/AndroidIncomingCallsBridge.cs`
- Modify: `src/dotnet/App.Maui/MauiProgram.Android.cs:42-44` (register next to `IDeviceNotifications`)

**Interfaces:**
- Consumes: `IIncomingCallsBridge` (Task 2), `IncomingCallNotifications.ListActiveCallChatIds/Dismiss` (Task 5).
- Produces: nothing consumed later.

- [x] **Step 1: Create the bridge**

`src/dotnet/App.Maui/Platforms/Android/AndroidIncomingCallsBridge.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using Android.Media;
using Android.OS;
using Application = Android.App.Application;

namespace ActualChat.App.Maui;

public sealed class AndroidIncomingCallsBridge : IIncomingCallsBridge, IDisposable
{
    private readonly Lock _lock = new();
    private Ringtone? _ringtone;
    private Vibrator? _vibrator;

    private ILogger Log => field ??= StaticLog.For<AndroidIncomingCallsBridge>();
    private static Context Context => Application.Context;

    public void StartRinging()
    {
        lock (_lock) {
            try {
                var audioManager = (AudioManager?)Context.GetSystemService(Context.AudioService);
                var ringerMode = audioManager?.RingerMode ?? RingerMode.Normal;
                if (ringerMode != RingerMode.Silent)
                    StartVibration();
                if (ringerMode == RingerMode.Normal)
                    StartRingtone();
            }
            catch (Exception e) {
                Log.LogWarning(e, "StartRinging failed");
            }
        }
    }

    public void StopRinging()
    {
        lock (_lock) {
            try {
                _ringtone?.Stop();
                _ringtone = null;
                _vibrator?.Cancel();
                _vibrator = null;
            }
            catch (Exception e) {
                Log.LogWarning(e, "StopRinging failed");
            }
        }
    }

    public Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken)
        => Task.FromResult(IncomingCallNotifications.ListActiveCallChatIds());

    public void DismissCallNotification(ChatId chatId)
        => IncomingCallNotifications.Dismiss(chatId);

    public void Dispose()
        => StopRinging();

    // Private methods

    private void StartRingtone()
    {
        if (_ringtone is not null)
            return;

        var uri = RingtoneManager.GetDefaultUri(RingtoneType.Ringtone);
        var ringtone = RingtoneManager.GetRingtone(Context, uri);
        if (ringtone is null)
            return;

        ringtone.AudioAttributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.NotificationRingtone)!
            .SetContentType(AudioContentType.Music)!
            .Build()!;
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            ringtone.Looping = true;
        ringtone.Play();
        _ringtone = ringtone;
    }

    private void StartVibration()
    {
        if (_vibrator is not null)
            return;

        var vibrator = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? ((VibratorManager?)Context.GetSystemService(Context.VibratorManagerService))?.DefaultVibrator
            : (Vibrator?)Context.GetSystemService(Context.VibratorService);
        if (vibrator is null || !vibrator.HasVibrator)
            return;

        var effect = VibrationEffect.CreateWaveform([0, 700, 500, 700, 500, 500], 0);
        vibrator.Vibrate(effect);
        _vibrator = vibrator;
    }
}
```

- [x] **Step 2: Register in Android DI**

In `MauiProgram.Android.cs`, `ConfigureBlazorWebViewAppPlatformServices`, after the `IDeviceNotifications` line (43):

```csharp
        services.AddScoped<IIncomingCallsBridge>(_ => new AndroidIncomingCallsBridge());
```

Add `using ActualChat.UI.Blazor.App.Services;` only if not already present (it is — line 5).

Scoped lifetime matters: the bridge is disposed with the Blazor scope, so a mid-ring scope teardown stops the ringtone (`IDisposable.Dispose` → `StopRinging`).

- [x] **Step 3: Build**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
Expected: build OK.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/Android/AndroidIncomingCallsBridge.cs src/dotnet/App.Maui/MauiProgram.Android.cs
git commit -m "feat(calls): Android bridge — looping ringtone/vibration + call-notification reconciliation"
```

---

### Task 8: Camera preview toggle in `IncomingCallModal` (video calls)

Independent of Tasks 5-7; requires Tasks 2 and 4. If review decides to defer it, everything else still ships — video-call accept then simply joins with the camera off (toggleable in-call via the existing `VideoToggle`).

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/VideoSessions.cs` (extracted from the modal)
- Modify: `src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/JoinVideoCallModal.razor` (remove the extracted nested types)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatVideoUI.Recording.cs:134` (`private` → `internal StartVideoStreaming`)
- Modify: `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs` (implement `withCamera`)
- Modify: `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModal.razor` (preview + toggle)
- Modify: `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/incoming-call-modal.css` (preview styles)

**Interfaces:**
- Consumes: `ChatVideoUI.StartCameraWarmup(ChatId, string?, bool, CancellationToken): Task<bool>`, `ChatVideoUI.CancelCameraWarmup(ChatId)`, the `blazorApp.JoinVideoCallModal.create(container, blazorRef)` JS module (reused as-is — the DOM contract is a container with `video.camera-preview-video` + `canvas.camera-preview`, and a `[JSInvokable] OnFirstFrameRendered` on the Blazor ref), `CameraPermissionHandler.CheckOrRequest`, `Hub.LocalSettings.LocalAppSettings()`.
- Produces: top-level `internal interface IVideoSession`, `internal abstract class VideoSessionBase`, `internal sealed class WarmupRecorderVideoSession`, `internal sealed class RecorderVideoSession`, `public enum CameraState { Off, Starting, On, Unavailable }` in namespace `ActualChat.UI.Blazor.App.Components` (file `VideoSessions.cs`).

- [ ] **Step 1: Extract the session types**

Move from `JoinVideoCallModal.razor` `@code` into the new `VideoSessions.cs` (namespace `ActualChat.UI.Blazor.App.Components`), keeping the code byte-identical except visibility:
- `private interface IVideoSession` → `internal interface IVideoSession` (with its XML doc)
- `private abstract class VideoSessionBase` → `internal abstract class VideoSessionBase`
- `private sealed class WarmupRecorderVideoSession` → `internal sealed class WarmupRecorderVideoSession`
- `private sealed class RecorderVideoSession` → `internal sealed class RecorderVideoSession`
- `public enum CameraState { Off, Starting, On, Unavailable }` → top-level `public enum CameraState` in the same file.

The file needs `using ActualChat.UI.Blazor.App.Services;` (for `ChatVideoUI`, `CameraUI`) and `using Microsoft.JSInterop;` (for `IJSObjectReference`). Delete the moved members from `JoinVideoCallModal.razor`. Then check nothing else referenced the nested names:

Run: `rg -n "JoinVideoCallModal\.CameraState|JoinVideoCallModal\.IVideoSession" src tests`
Expected: no matches. (If `CameraState` collides with another type in the namespace, rename the moved enum to `CameraPreviewState` consistently in both files.)

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: build OK.

- [ ] **Step 2: Make `StartVideoStreaming` internal**

In `ChatVideoUI.Recording.cs:134`: `private void StartVideoStreaming(...)` → `internal void StartVideoStreaming(...)`.

- [ ] **Step 3: Implement `withCamera` in `IncomingCallUI.Accept`**

Replace the `_ = withCamera;` line with:

```csharp
        if (withCamera) {
            var settings = await Hub.LocalSettings.LocalAppSettings().Get().ConfigureAwait(true);
            Hub.ChatVideoUI.StartVideoStreaming(chatId, settings.SelectedCameraDeviceId, settings.IsBackgroundBlurEnabledOrDefault);
        }
```

(`StartVideoStreaming`'s state sync claims the modal's warmup recorder via `TryClaimCameraWarmupRecorder`, so the camera the user previewed keeps streaming without a re-acquire.)

- [ ] **Step 4: Add the preview to `IncomingCallModal`**

Changes to `IncomingCallModal.razor` (on top of Task 4's version):

1. Add directives: `@using ActualChat.UI.Blazor.App.Module` and `@implements IAsyncDisposable`.
2. Add markup between `<IncomingCallModalHeader .../>` and `</Body>`:

```razor
    @if (call.HasVideo) {
        <div class="c-camera-block">
            @if (_isCameraOn) {
                <div @ref="_previewRef" class="c-video">
                    <div class="video-frame">
                        <video class="camera-preview-video live-stream-video"
                               autoplay playsinline muted aria-hidden="true"
                               disablepictureinpicture disableremoteplayback
                               controlslist="nodownload nofullscreen noremoteplayback"></video>
                        <canvas class="camera-preview"></canvas>
                    </div>
                </div>
            }
            <ButtonRound Class="@(_isCameraOn ? "btn-camera-preview on" : "btn-camera-preview")"
                         Click="@OnCameraToggle"
                         Tooltip="@(_isCameraOn ? "Turn camera off" : "Turn camera on")">
                <i class="icon-video text-2xl"></i>
            </ButtonRound>
        </div>
    }
```

3. Add to `@code`:

```csharp
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.JoinVideoCallModal.create";

    private ElementReference _previewRef;
    private DotNetObjectReference<IncomingCallModal>? _blazorRef;
    private IJSObjectReference? _jsRef;
    private WarmupRecorderVideoSession? _session;
    private bool _isCameraOn;
    private bool _isAccepted;
    private readonly CancellationTokenSource _disposeCts = new();

    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private CameraPermissionHandler CameraPermission => field ??= Hub.Services.GetRequiredService<CameraPermissionHandler>();

    private async Task OnCameraToggle() {
        if (_isCameraOn) {
            _isCameraOn = false;
            StateHasChanged();
            if (_session is not null)
                await _session.SetVideoEnabled(false);
            return;
        }

        if (!await CameraPermission.CheckOrRequest(mustRequest: true, mustTroubleshoot: false, _disposeCts.Token)) {
            Hub.ToastUI.Show("Camera access is blocked", "icon-alert-circle", ToastDismissDelay.Long);
            return;
        }

        _isCameraOn = true;
        StateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) {
        // The preview container renders only while the toggle is on — create/dispose the JS
        // side in lockstep with it (see ui-components.md, "Conditional host element").
        if (!_isCameraOn) {
            if (_jsRef is { } jsRef) {
                _jsRef = null;
                _session = null;
                await jsRef.DisposeSilentlyAsync("dispose");
            }
            return;
        }
        if (_jsRef is not null)
            return;

        _blazorRef ??= DotNetObjectReference.Create(this);
        _jsRef = await JS.InvokeAsync<IJSObjectReference>(JSCreateMethod, _previewRef, _blazorRef);
        var settings = await Hub.LocalSettings.LocalAppSettings().Get();
        _session = new WarmupRecorderVideoSession(
            _jsRef,
            ChatVideoUI,
            ChatId,
            () => _isCameraOn,
            () => settings.SelectedCameraDeviceId,
            () => settings.IsBackgroundBlurEnabledOrDefault,
            _disposeCts.Token);
        await _session.Start();
    }

    [JSInvokable]
    public Task OnFirstFrameRendered() {
        _session?.NotifyFirstFrameRendered();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() {
        _disposeCts.CancelAndDisposeSilently();
        if (_jsRef is { } jsRef) {
            _jsRef = null;
            await jsRef.DisposeSilentlyAsync("dispose");
        }
        _blazorRef?.Dispose();
        _blazorRef = null;
        // Accepted with camera on → StartVideoStreaming claims the warmup; otherwise tear it down.
        if (_isCameraOn && !_isAccepted)
            await ChatVideoUI.CancelCameraWarmup(ChatId);
    }
```

4. Change `OnAcceptClick` to pass the camera state:

```csharp
    private Task OnAcceptClick() {
        var chatId = ChatId;
        _isAccepted = true;
        Modal.Close();
        return IncomingCallUI.Accept(chatId, withCamera: _isCameraOn);
    }
```

Note: `ComputedStateComponent` already implements `IAsyncDisposable` — override semantics must match `JoinVideoCallModal` (`public override async ValueTask DisposeAsync()` calling `await base.DisposeAsync()` at the end). Follow that exact pattern.

5. Append to `incoming-call-modal.css`:

```css
/* ── Camera preview ── */

.incoming-call-modal .c-camera-block {
    @apply flex-y items-center gap-2;
    @apply p-2;
}
.incoming-call-modal .c-video {
    @apply w-full;
    @apply rounded-lg;
    @apply overflow-hidden;
}
.incoming-call-modal .c-video .video-frame {
    @apply relative w-full;
    aspect-ratio: 4 / 3;
}
.incoming-call-modal .btn-camera-preview.on {
    @apply text-primary;
}
```

- [ ] **Step 5: Build and run TS-free verification**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: build OK. No TypeScript changes were made (the existing `JoinVideoCallModal` JS module is reused), so `npm run build:Verify` is NOT required.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/VideoSessions.cs src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/JoinVideoCallModal.razor src/dotnet/UI.Blazor.App/Services/ChatVideoUI.Recording.cs src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModal.razor src/dotnet/UI.Blazor.App/Components/IncomingCallModal/incoming-call-modal.css
git commit -m "feat(calls): camera preview toggle in IncomingCallModal; camera-on accept claims the warmup recorder"
```

---

### Task 9: AOT regen, full build, tests, manual E2E matrix

**Files:**
- Modify (generated): `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs` — regenerate, do NOT hand-edit.

- [x] **Step 1: Regenerate the AOT source**

New Razor components (`IncomingCallBanner`) must appear in the generated keep-list. Per `docs/native-aot.md`: regenerated by `App.AotHelper -g`.

Run: `dotnet run --project src/dotnet/App.AotHelper -- -g`
Expected: `BlazorUIAppAotSource.g.cs` diff includes `CodeKeeper.Keep<...IncomingCallBanner>()`. If the command syntax differs, check `docs/native-aot.md` → App.AotHelper section.

- [x] **Step 2: Full build + all touched test projects**

```bash
dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj
dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android
dotnet test tests/Chat.UI.Blazor.UnitTests
dotnet test tests/Notifications.IntegrationTests --filter "CallNotificationTag|NotificationSerializationTests"
```
Expected: all builds OK, all tests PASS.

- [ ] **Step 3: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs
git commit -m "chore(calls): regenerate AOT keep-list for incoming-call components"
```

- [ ] **Step 4: Manual E2E matrix (device required — report results, don't guess)**

Caller: `/test/voice-call` (admin-only) on web (multi-login via debug-ui). Callee: Android device/emulator with the app.

| # | App state | Action | Expected |
|---|---|---|---|
| 1 | Foreground | Caller starts call | Banner appears in-app, device rings (looping) + vibrates, NO system notification |
| 2 | Foreground | Tap banner body | Modal opens (caller avatar, chat, Accept/Decline); video call → camera toggle present |
| 3 | Foreground | Accept (banner or modal) | Ring stops, navigates to chat, mic on (recording), participation visible on caller side |
| 4 | Foreground | Decline | Ring stops, banner gone, caller sees invite Declined |
| 5 | Foreground | Caller cancels | Banner + ringer stop by themselves (computed) |
| 6 | Foreground | Wait 40 s | Same — ring self-expires |
| 7 | Background | Caller starts call | System notification on `incoming_calls` channel with ringtone + vibration, Accept/Decline buttons; rings through muted notification profile |
| 8 | Background | Tap Decline on notification | Notification gone WITHOUT opening the app; caller sees Declined |
| 9 | Background | Tap Accept on notification | App opens → chat opens → joined with mic on |
| 10 | Killed (swipe from recents) | Caller starts call | Same as 7 |
| 11 | Killed | Tap notification body | App opens on the chat; banner shows if still ringing |
| 12 | Background, push arrived | Open app from launcher (not notification) | Banner appears via reconciliation; notification dismissed on accept/decline |
| 13 | Background | Caller cancels | Dismissal push removes the notification |
| 14 | Any | Accept on ANOTHER device, watch this one | Banner/notification clears itself |
| 15 | Foreground, video call | Enable camera preview in modal, Accept | Camera streams immediately in the call (warmup claimed, no re-acquire flash) |
| 16 | Any | Tap Accept on a stale notification (after ring end, within 40 s) | App opens, "Call ended" toast, no join |

---

## Execution notes

- Task order: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9. Task 1 is independent (can go last); Task 8 is deferrable.
- The server must be redeployed (or `/server-loop` restarted) for Task 1's tag change to affect pushes during manual testing — the tag is computed server-side.
- Stage A (CallStyle + full-screen intent + foreground service) intentionally NOT here; the channel, receiver, and `IncomingCallUI` are its foundation.
