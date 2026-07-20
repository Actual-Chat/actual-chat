# Walkie-Talkie Reply — Sub-Project E1: Core Pipeline + On-Screen PTT

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the platform-neutral hands-free-reply pipeline (target resolution, incoming-voice tracking, the hot-mic lifecycle with a cold-start dead-man switch) and wire one foreground trigger — an on-screen PTT button — so a user can reply to the chat that most recently spoke, with the mic auto-closing on silence.

**Architecture:** A new `UI.Blazor.App` service, `WalkieTalkieReplyUI`, owns the reply policy: it resolves a target chat (`ReplyTargetResolver`, a pure testable unit), opens the mic by reusing `ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk:true)` (which already runs the full recorder + idle-stop machinery), and runs a **cold-start dead-man switch** over `AudioRecorderState.IsVoiceActive`. A new `IncomingVoiceActivityUI` service stamps per-chat "last incoming voice" times (excluding own author) to feed the resolver. The on-screen PTT button calls `RequestReply`/`StopReply`. Native triggers (shake, media button, Apple PTT transmit) and the `WalkieTalkieSession` de-static refactor are **later sub-projects (E2/E3/E4)** that reuse this same core.

**Tech Stack:** .NET / ActualLab.Fusion compute + UI services, Blazor Razor components, xUnit + AwesomeAssertions unit tests (`Chat.UI.Blazor.UnitTests`), `TuneUI`.

Spec: `docs/superpowers/specs/2026-07-20-walkie-talkie-reply-to-voice-design.md`.

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any code.** No `Async` suffix; no XML docs on members; comments only where the code cannot express a constraint; mirror surrounding brace/naming style.
- Branch: `feat/walkie-talkie-push`. Commit per task; **never push**.
- Build with `dotnet build ActualChat.CI.slnf` (never the full `.sln`).
- **E1 does NOT touch `WalkieTalkieSession`** (App.Maui) or any wake/scope machinery — it is foreground-only and lives entirely in `UI.Blazor.App`. The de-static refactor is deferred to E3/E4.
- New `Tune` members MUST be **appended to the end** of both `TuneUI.cs` `enum Tune` and `tune-ui.ts` `enum Tune` (there is a known desync at indices 11–13; appending stays clear of it).
- The mic-open side effects must match the existing button exactly: mic-permission check → lift own soft-mute via `LiveSessionUI.MutePeer(..., muted:false)` → `SetRecordingChatId(chatId, isPushToTalk:true)`. Closing is `SetRecordingChatId(null)`.
- Do not edit `ChatAudioUI.RecordChat` / `ObserveStreamingIdleBoundaries` — the hot-phase close reuses them as-is; E1 only adds the cold-start dead-man switch on top.
- `.superpowers/sdd/progress.md` is gitignored — update it, never `git add` it.

## Reuse

**Existing abstractions reused (verified 2026-07-20):**

| Abstraction | Location | Use |
|---|---|---|
| `ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk)` / `GetRecordingChatId()` / `Enable()` / `WhenEnabled` | `UI.Blazor.App/Services/ChatAudioUI.cs:164,156,77` | Open/close the mic; `isPushToTalk:true` keeps other listening chats alive. The existing `PushRecordingState`→`RecordChat` owns recorder + `RecordingDuration` idle-stop, which already resets on incoming-from-others via `GetStreamingAuthorIds`. |
| `AudioRecorder.State` (`IState<AudioRecorderState>`), `AudioRecorderState.IsVoiceActive` | `UI.Blazor.App/Components/AudioRecorder/AudioRecorder.cs:35`, `AudioRecorderState.cs` | Own-VAD signal for the cold-start dead-man switch; observe via `State.Computed.When(...)`/`.Changes(...)`. |
| `LiveStreamUI.GetStreamingAuthorIds(chatId, ct)` | `UI.Blazor.App/Services/LiveStreamUI.cs` | Raw per-chat streaming-authors source for the incoming tracker (minus own author). |
| `Authors.GetOwn(Session, chatId, ct)` | via `Hub.Authors` | Own author id, to exclude self from "incoming". |
| `ChatAudioUI.GetChatsYouNeedToKeepListeningTo(ct)` | `ChatAudioUI.cs:105` | Armed set for the resolver. |
| `ChatUI.SelectedChatId` (`IState<ChatId?>`) | `UI.Blazor.App/Services/ChatUI.cs:54` | Focused-chat fallback. |
| `MicrophonePermission.CheckOrRequest`, `LiveSessionUI.MutePeer` | `AudioRecorder.cs:32`, `LiveSessionUI.cs:53` | Mic-open side effects (replicate `RecorderToggle.StartRecording`). |
| `TuneUI` + `Tune` enum + `Tunes` dict | `UI.Blazor/Services/TuneUI/TuneUI.cs`, `tune-ui.ts` | Cues; reuse `Tune.BeginRecording`; add two members. |
| `WalkieTalkie.ComputeIdleDropAt` pattern | `UI.Blazor.App/Services/WalkieTalkie.cs` | Precedent: pure timing helper unit-tested in `Chat.UI.Blazor.UnitTests/WalkieTalkieTest.cs`. |
| `Constants.Audio` | `Api/Constants.Audio.cs` | Home for new timeout constants. |
| `ComputedStateComponent<AppUIHub, Model>` / `FusionComponentBase<AppUIHub>` Razor pattern | `RecorderToggle.razor`, `ChatListRecordingToggle.razor` | On-screen PTT button shape. |

**New components and placement:**
- `ReplyTargetResolver` (pure static/helper) → `UI.Blazor.App/Services` (walkie namespace, beside `WalkieTalkie.cs`) — fully unit-testable.
- `IncomingVoiceActivityUI` (background tracker) → `UI.Blazor.App/Services`.
- `WalkieTalkieReplyUI` (coordinator + hot-mic lifecycle) → `UI.Blazor.App/Services`.
- On-screen PTT trigger component → `UI.Blazor.App/Components/ChatAudioPanel`.
- Two `Tune` members → `TuneUI.cs` + `tune-ui.ts` (vibration-only v1; audio assets a deferred host step, so no binary is required to ship).

No `ActualChat.Core` type is warranted — this is entirely client/UI behavior. The reply core stays in `UI.Blazor.App` so the future `WalkieTalkieSession` (App.Maui) delegates transmit to it, honoring "one session owns both directions."

---

### Task 1: Constants + reply cues

**Files:**
- Modify: `src/dotnet/Api/Constants.Audio.cs`
- Modify: `src/dotnet/UI.Blazor/Services/TuneUI/TuneUI.cs`
- Modify: `src/dotnet/UI.Blazor/Services/TuneUI/tune-ui.ts`
- Test: `tests/Chat.UnitTests` or `tests/Core.UnitTests` — none needed (constants/enum only); verified by build.

**Interfaces:**
- Produces: `Constants.Audio.WalkieTalkieReplyColdStartTimeout` (15s), `Constants.Audio.WalkieTalkieReplyRecencyWindow` (TimeSpan, 150s). `Tune.WalkieReplyEnded`, `Tune.WalkieReplyNothingHeard` (appended, vibration-only). Later tasks consume these.

- [ ] **Step 1: Add constants**

In `src/dotnet/Api/Constants.Audio.cs`, next to the existing walkie constants (`WalkieTalkieIdleTimeout` etc.):

```csharp
public static readonly TimeSpan WalkieTalkieReplyColdStartTimeout = TimeSpan.FromSeconds(15);
public static readonly TimeSpan WalkieTalkieReplyRecencyWindow = TimeSpan.FromSeconds(150);
```

Match the exact `static readonly TimeSpan` style of the surrounding lines (some use `TimeSpan.FromSeconds`, confirm and mirror).

- [ ] **Step 2: Append the two tunes (C#)**

In `src/dotnet/UI.Blazor/Services/TuneUI/TuneUI.cs`, append to the **end** of `enum Tune` (after `ClickButton`):

```csharp
    WalkieReplyEnded,
    WalkieReplyNothingHeard,
```

And add vibration-only entries (empty `Sound`) to the `Tunes` dictionary, mirroring an existing vibration-only entry's `Vibration` array shape (copy the pattern used by e.g. `StopListening`/`StopReplay`):

```csharp
    [Tune.WalkieReplyEnded] = new([100, 50, 100]),
    [Tune.WalkieReplyNothingHeard] = new([80]),
```

(If the existing vibration-only entries use a different int[] convention, match it; the point is empty `Sound` so no audio asset is required yet.)

- [ ] **Step 3: Append the two tunes (TS)**

In `src/dotnet/UI.Blazor/Services/TuneUI/tune-ui.ts`, append the same two members to the **end** of `export enum Tune` (after the last member). No `cooldownMap` entry needed (no sound). Keep the two enums' tail order identical.

- [ ] **Step 4: Build + TS verify**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`.
Run: `npm run build:Verify 2>&1 | tail -20` → no tsc/eslint errors. (If `/server-loop` is running, trigger its rebuild instead per CLAUDE.md.)

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Constants.Audio.cs src/dotnet/UI.Blazor/Services/TuneUI/TuneUI.cs src/dotnet/UI.Blazor/Services/TuneUI/tune-ui.ts
git commit -m "feat(audio): walkie reply timeouts + cue tunes (vibration-only)"
```

---

### Task 2: `ReplyTargetResolver` (pure, unit-tested)

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/ReplyTargetResolver.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/ReplyTargetResolverTest.cs`

**Interfaces:**
- Consumes: `Constants.Audio.WalkieTalkieReplyRecencyWindow` (Task 1).
- Produces: `static ChatId? ReplyTargetResolver.Resolve(IReadOnlyList<ChatId> armedChatIds, IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt, ChatId? focusedChatId, Moment now, TimeSpan recencyWindow)` — the pure fallback chain. `WalkieTalkieReplyUI` (Task 4) calls it.

Resolution order (from the spec): (1) armed chat with the most recent incoming voice within `recencyWindow`; (2) else `focusedChatId` if it is armed; (3) else the sole armed chat; (4) else `null`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat.UI.Blazor.UnitTests/ReplyTargetResolverTest.cs`. Mirror the harness of the sibling `WalkieTalkieTest.cs` (namespace, `T0` moment base, `[Fact]`, AwesomeAssertions):

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ReplyTargetResolverTest
{
    private static readonly Moment T0 = new(DateTime.UnixEpoch + TimeSpan.FromDays(20000));
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(150);
    private static ChatId Chat(string s) => ChatId.Parse(s);

    [Fact]
    public void PicksMostRecentSpeakerWithinWindow()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var b = Chat("bbbbbbbbbbbbbbbbbbbb");
        var armed = new[] { a, b };
        var last = new Dictionary<ChatId, Moment> { [a] = T0 - TimeSpan.FromSeconds(90), [b] = T0 - TimeSpan.FromSeconds(20) };
        ReplyTargetResolver.Resolve(armed, last, focusedChatId: null, T0, Window).Should().Be(b);
    }

    [Fact]
    public void IgnoresSpeakersOutsideWindow_FallsBackToFocused()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var b = Chat("bbbbbbbbbbbbbbbbbbbb");
        var armed = new[] { a, b };
        var last = new Dictionary<ChatId, Moment> { [a] = T0 - TimeSpan.FromSeconds(400) };
        ReplyTargetResolver.Resolve(armed, last, focusedChatId: b, T0, Window).Should().Be(b);
    }

    [Fact]
    public void FocusedFallbackOnlyIfArmed()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var other = Chat("cccccccccccccccccccc");
        var armed = new[] { a };
        ReplyTargetResolver.Resolve(armed, new Dictionary<ChatId, Moment>(), focusedChatId: other, T0, Window)
            .Should().Be(a); // focused not armed → falls through to sole-armed
    }

    [Fact]
    public void SoleArmedFallback()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa");
        ReplyTargetResolver.Resolve(new[] { a }, new Dictionary<ChatId, Moment>(), focusedChatId: null, T0, Window)
            .Should().Be(a);
    }

    [Fact]
    public void AmbiguousColdStart_ReturnsNull()
    {
        var a = Chat("aaaaaaaaaaaaaaaaaaaa"); var b = Chat("bbbbbbbbbbbbbbbbbbbb");
        ReplyTargetResolver.Resolve(new[] { a, b }, new Dictionary<ChatId, Moment>(), focusedChatId: null, T0, Window)
            .Should().BeNull();
    }

    [Fact]
    public void NoArmedChats_ReturnsNull()
    {
        ReplyTargetResolver.Resolve(Array.Empty<ChatId>(), new Dictionary<ChatId, Moment>(), null, T0, Window)
            .Should().BeNull();
    }
}
```

If `ChatId.Parse` needs a specific length/format, mirror how `WalkieTalkieTest.cs`/other unit tests build a `ChatId` (use whatever helper they use).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~ReplyTargetResolverTest" 2>&1 | tail -5`
Expected: FAIL — `ReplyTargetResolver` does not exist.

- [ ] **Step 3: Implement**

Create `src/dotnet/UI.Blazor.App/Services/ReplyTargetResolver.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public static class ReplyTargetResolver
{
    public static ChatId? Resolve(
        IReadOnlyList<ChatId> armedChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        ChatId? focusedChatId,
        Moment now,
        TimeSpan recencyWindow)
    {
        if (armedChatIds.Count == 0)
            return null;

        ChatId? best = null;
        var bestAt = now - recencyWindow;
        foreach (var chatId in armedChatIds) {
            if (lastIncomingVoiceAt.TryGetValue(chatId, out var at) && at > bestAt) {
                bestAt = at;
                best = chatId;
            }
        }
        if (best is not null)
            return best;

        if (focusedChatId is { } focused && armedChatIds.Contains(focused))
            return focused;

        return armedChatIds.Count == 1 ? armedChatIds[0] : null;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~ReplyTargetResolverTest" 2>&1 | tail -5`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ReplyTargetResolver.cs tests/Chat.UI.Blazor.UnitTests/ReplyTargetResolverTest.cs
git commit -m "feat(audio-ui): ReplyTargetResolver - last-spoke target with focused/sole-armed fallback"
```

---

### Task 3: `IncomingVoiceActivityUI` (per-chat last-incoming tracker)

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/IncomingVoiceActivityUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` (lazy accessor)
- Modify: the UI.Blazor.App DI module (register the service) — locate via the existing `LiveStreamUI`/`ChatAudioUI` registration.
- Test: `tests/Chat.UI.Blazor.UnitTests/IncomingVoiceActivitySnapshotTest.cs` (pure snapshot helper only).

**Interfaces:**
- Consumes: `LiveStreamUI.GetStreamingAuthorIds`, `Authors.GetOwn`, `ChatAudioUI.GetChatsYouNeedToKeepListeningTo`, `Clocks.ServerClock`.
- Produces: `IReadOnlyDictionary<ChatId, Moment> IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt()` (for the resolver) and a background loop that stamps per-chat times. `WalkieTalkieReplyUI` (Task 4) reads the snapshot.

Design: a `UIServiceBase<AppUIHub>` (mirror `LiveStreamUI`'s base + `IComputeService` shape) that runs a background worker per the existing StateSync pattern. For each armed chat, reactively observe `GetStreamingAuthorIds(chatId)`; when the set **minus own author** transitions empty→non-empty, stamp `ServerClock.Now` into a `ConcurrentDictionary<ChatId, Moment>` (same field pattern as `LiveStreamUI._lastActivityTimes`). Expose `SnapshotLastIncomingVoiceAt()` returning a copy.

Because the background loop needs a live server, its full behavior is integration-verified. Extract the **pure decision** — "given previous other-author set and current other-author set, should we stamp?" — into a testable static so the transition logic has coverage.

- [ ] **Step 1: Write the failing test (pure transition helper)**

Create `tests/Chat.UI.Blazor.UnitTests/IncomingVoiceActivitySnapshotTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class IncomingVoiceActivitySnapshotTest
{
    [Fact]
    public void StampsOnEmptyToNonEmpty()
    {
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: false, nowHasOthers: true).Should().BeTrue();
    }

    [Fact]
    public void DoesNotStampWhileStillStreaming()
    {
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: true, nowHasOthers: true).Should().BeFalse();
    }

    [Fact]
    public void DoesNotStampOnStop()
    {
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: true, nowHasOthers: false).Should().BeFalse();
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: false, nowHasOthers: false).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~IncomingVoiceActivitySnapshotTest" 2>&1 | tail -5`
Expected: FAIL — type/method missing.

- [ ] **Step 3: Implement the service**

Create `src/dotnet/UI.Blazor.App/Services/IncomingVoiceActivityUI.cs`. Base it on `LiveStreamUI`'s shape (`UIServiceBase<AppUIHub>`, `IComputeService`) — read that file first. Skeleton:

```csharp
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class IncomingVoiceActivityUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<ChatId, Moment> _lastIncomingAt = new();

    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IAuthors Authors => Hub.Authors;

    public static bool ShouldStamp(bool prevHadOthers, bool nowHasOthers)
        => !prevHadOthers && nowHasOthers;

    public IReadOnlyDictionary<ChatId, Moment> SnapshotLastIncomingVoiceAt()
        => new Dictionary<ChatId, Moment>(_lastIncomingAt);

    // Background worker: for each armed chat, observe GetStreamingAuthorIds minus own author;
    // stamp ServerClock.Now on empty->non-empty via ShouldStamp. Wire it into the service's
    // OnRun/worker following the LiveStreamUI + ChatAudioUI.StateSync pattern (RunIsolated /
    // RetryForever, Computed.Capture(...).Changes over GetStreamingAuthorIds and the armed set).
}
```

Implement the worker using the same `Computed.Capture` + `.Changes` reactive pattern the codebase uses (see `ChatAudioUI.StateSync.PushRecordingState`). For each armed chat resolve own author once (`Authors.GetOwn(Session, chatId, ct)`), track previous "has others" per chat, and stamp on the rising edge. Keep the worker resilient (`RetryForever`, as siblings do). Follow whatever base-class run hook `LiveStreamUI`/`UIServiceBase` exposes; if `LiveStreamUI` has no background worker, model the worker on `ChatAudioUI.StateSync`'s `RunIsolated` usage instead and start it from the service's initialization hook.

- [ ] **Step 4: Register + hub accessor**

In `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs`, add next to the `LiveStreamUI` accessor (line ~42):

```csharp
public IncomingVoiceActivityUI IncomingVoiceActivityUI => field ??= Services.GetRequiredService<IncomingVoiceActivityUI>();
```

Register it in the UI.Blazor.App module where `LiveStreamUI` is registered (find `AddService<LiveStreamUI>` or equivalent and add the sibling line, matching lifetime).

- [ ] **Step 5: Run to verify pass + build**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~IncomingVoiceActivitySnapshotTest" 2>&1 | tail -5` → PASS.
Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/IncomingVoiceActivityUI.cs src/dotnet/UI.Blazor.App/Services/AppUIHub.cs tests/Chat.UI.Blazor.UnitTests/IncomingVoiceActivitySnapshotTest.cs
# plus the module registration file
git commit -m "feat(audio-ui): IncomingVoiceActivityUI - per-chat last-incoming-voice tracker (excludes own author)"
```

---

### Task 4: `WalkieTalkieReplyUI` (coordinator + hot-mic lifecycle)

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` (accessor)
- Modify: UI.Blazor.App module (register)
- Test: `tests/Chat.UI.Blazor.UnitTests/HotMicColdStartTest.cs` (pure cold-start decision helper)

**Interfaces:**
- Consumes: `ReplyTargetResolver.Resolve` (Task 2), `IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt` (Task 3), `ChatAudioUI.SetRecordingChatId`/`GetRecordingChatId`, `AudioRecorder.State`, `MicrophonePermission.CheckOrRequest`, `LiveSessionUI.MutePeer`, `ChatUI.SelectedChatId`, `ChatAudioUI.GetChatsYouNeedToKeepListeningTo`, `TuneUI`, `Constants.Audio.WalkieTalkieReply*`.
- Produces: `Task RequestReply(CancellationToken)` and `Task StopReply()`. The on-screen trigger (Task 5) and future native triggers call these.

Lifecycle (from the spec):
- `RequestReply`: if already recording (`GetRecordingChatId()` non-null), no-op. Else resolve target (armed set + incoming snapshot + focused). If null → `TuneUI.Play(Tune.WalkieReplyNothingHeard)` and return. Else replicate the mic-open side effects (permission → `MutePeer(false)` → `SetRecordingChatId(chatId, isPushToTalk:true)`), then start the **cold-start dead-man**: watch `AudioRecorder.State`; if `IsVoiceActive` has not become true within `WalkieTalkieReplyColdStartTimeout`, call `StopReply()` with the "nothing heard" cue. Once voice is seen, stop the dead-man and let the existing `RecordChat` idle own the hot-phase close (which already resets on incoming-from-others and stops after `RecordingDuration`).
- `StopReply`: `SetRecordingChatId(null)`; play `Tune.WalkieReplyEnded` (or `WalkieReplyNothingHeard` when closing from a never-voiced cold start).

Extract the cold-start timing decision (`voiced-yet?` vs elapsed) into a pure static for coverage; the orchestration itself is integration/manually verified (mirrors the untested `RecordChat` orchestration).

- [ ] **Step 1: Write the failing test (pure cold-start decision)**

Create `tests/Chat.UI.Blazor.UnitTests/HotMicColdStartTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class HotMicColdStartTest
{
    private static readonly TimeSpan Cold = TimeSpan.FromSeconds(15);

    [Fact]
    public void ClosesWhenNeverVoicedPastTimeout()
    {
        WalkieTalkieReplyUI.ShouldColdClose(everVoiced: false, elapsed: Cold + TimeSpan.FromSeconds(1), Cold)
            .Should().BeTrue();
    }

    [Fact]
    public void StaysOpenBeforeTimeout()
    {
        WalkieTalkieReplyUI.ShouldColdClose(everVoiced: false, elapsed: Cold - TimeSpan.FromSeconds(1), Cold)
            .Should().BeFalse();
    }

    [Fact]
    public void NeverColdClosesOnceVoiced()
    {
        WalkieTalkieReplyUI.ShouldColdClose(everVoiced: true, elapsed: Cold + TimeSpan.FromMinutes(5), Cold)
            .Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~HotMicColdStartTest" 2>&1 | tail -5` → FAIL.

- [ ] **Step 3: Implement the service**

Create `src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs`. Read `RecorderToggle.razor:219-253` and `ChatAudioUI.StateSync.PushRecordingState` first to match the start-recording side effects and the `AudioRecorder.State` observation idiom. Skeleton:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public sealed class WalkieTalkieReplyUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly object _lock = new();
    private CancellationTokenSource? _coldStartCts;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private TuneUI TuneUI => Hub.TuneUI;
    private IChats Chats => Hub.Chats;

    public static bool ShouldColdClose(bool everVoiced, TimeSpan elapsed, TimeSpan coldTimeout)
        => !everVoiced && elapsed >= coldTimeout;

    public async Task RequestReply(CancellationToken cancellationToken)
    {
        ChatAudioUI.Enable();
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
            return; // already hot (idempotent)

        var armed = await ChatAudioUI.GetChatsYouNeedToKeepListeningTo(cancellationToken).ConfigureAwait(false);
        var focused = Hub.ChatUI.SelectedChatId.Value;
        var snapshot = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var target = ReplyTargetResolver.Resolve(
            armed, snapshot, focused, Clocks.ServerClock.Now, Constants.Audio.WalkieTalkieReplyRecencyWindow);
        if (target is not { } chatId) {
            _ = TuneUI.Play(Tune.WalkieReplyNothingHeard);
            return;
        }

        if (!await AudioRecorder.MicrophonePermission.CheckOrRequest(cancellationToken).ConfigureAwait(false))
            return;
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.Author?.Id is { } ownAuthorId)
            await LiveSessionUI.MutePeer(chatId, ownAuthorId, false, cancellationToken).ConfigureAwait(false);
        await ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk: true).ConfigureAwait(false);

        StartColdStartWatch(chatId);
    }

    public async Task StopReply()
    {
        StopColdStartWatch();
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
            await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
    }

    // Private: StartColdStartWatch runs a BackgroundTask that observes AudioRecorder.State,
    // sets everVoiced=true on the first IsVoiceActive for chatId, and every CheckPeriod calls
    // ShouldColdClose(everVoiced, elapsed, WalkieTalkieReplyColdStartTimeout); on true it plays
    // Tune.WalkieReplyNothingHeard and StopReply(); once everVoiced it exits (existing RecordChat
    // idle owns the hot-phase close). StopColdStartWatch cancels it. Guard _coldStartCts with _lock.
}
```

Implement `StartColdStartWatch`/`StopColdStartWatch` using `BackgroundTask.Run` + `AudioRecorder.State.Computed.Changes(ct)` (or `.When`) exactly as `RecordChat` observes recorder state. When the hot phase ends (recording stops for any reason), play `Tune.WalkieReplyEnded` — detect via observing `GetRecordingChatId()` going null after a voiced session (a second small observer, or fold into the cold-start watcher's exit path).

- [ ] **Step 4: Register + hub accessor**

Add the `AppUIHub` accessor and module registration exactly as in Task 3 Step 4 (sibling of `ChatAudioUI`).

- [ ] **Step 5: Run to verify pass + build + TS**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~HotMicColdStartTest" 2>&1 | tail -5` → PASS.
Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs src/dotnet/UI.Blazor.App/Services/AppUIHub.cs tests/Chat.UI.Blazor.UnitTests/HotMicColdStartTest.cs
# plus the module registration file
git commit -m "feat(audio-ui): WalkieTalkieReplyUI - resolve target, open mic, cold-start dead-man switch"
```

---

### Task 5: On-screen PTT trigger component

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/WalkieReplyToggle.razor`
- Modify: the chat audio panel host that should show it (locate where `RecorderToggle`/`ChatListRecordingToggle` are placed) — add the button for armed/walkie chats.
- Test: none (Razor UI; web-verified — the existing `RecorderToggle` has no unit test).

**Interfaces:**
- Consumes: `WalkieTalkieReplyUI.RequestReply`/`StopReply` (Task 4).

- [ ] **Step 1: Create the component**

Model it on `ChatListRecordingToggle.razor` (the lighter `FusionComponentBase<AppUIHub>` button) rather than the heavy `RecorderToggle`. It renders a PTT button and tap-toggles reply:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits FusionComponentBase<AppUIHub>

<ButtonRound Class="@Class" Click="OnClick">
    <i class="icon-walkie-talkie"></i>
</ButtonRound>

@code {
    private WalkieTalkieReplyUI WalkieTalkieReplyUI => Hub.WalkieTalkieReplyUI;

    [Parameter] public string Class { get; set; } = "";

    private void OnClick()
        => _ = WalkieTalkieReplyUI.RequestReply(CancellationToken.None);
}
```

Match the actual button primitive and icon the codebase uses (inspect `ChatListRecordingToggle.razor` for `ButtonRound`/`HeaderButton` and an existing icon class; pick the closest existing icon — do not invent an asset). Add the `WalkieTalkieReplyUI` accessor to `AppUIHub` if not already added in Task 4.

Decide tap semantics: a single tap starts a reply (RequestReply). Because the hot window auto-closes, an explicit stop button is optional for v1; if the host wants a visible stop while hot, bind a second state (observe `GetRecordingChatId()`) and call `StopReply()` — but keep v1 to start-only if that's simpler, and note it.

- [ ] **Step 2: Place the component**

Add `<WalkieReplyToggle />` where it belongs for walkie/armed chats — inspect where `ChatListRecordingToggle` is rendered and add alongside, ideally gated to armed chats. If placement is non-obvious, put it in the same audio panel next to the recorder toggle and note the placement choice in the report.

- [ ] **Step 3: Verify build + TS + web**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`.
Run: `npm run build:Verify 2>&1 | tail -20` → clean.
Manual (report as a verification note; do on host/browser): with a chat armed and a recent incoming message, tap the button → mic opens (BeginRecording cue), speak → records; stay silent from cold → closes within ~15s with the nothing-heard cue.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/WalkieReplyToggle.razor
# plus the host component that renders it
git commit -m "feat(audio-ui): on-screen walkie reply (PTT) button wired to WalkieTalkieReplyUI"
```

---

### Task 6: Final verification

**Files:**
- Modify: `docs/superpowers/specs/2026-07-20-walkie-talkie-reply-to-voice-design.md` (status note: E1 implemented)
- Modify (not committed): `.superpowers/sdd/progress.md`

- [ ] **Step 1: Full build + TS**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`.
Run: `npm run build:Verify 2>&1 | tail -20` → clean.

- [ ] **Step 2: Unit test sweep**

```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~ReplyTargetResolverTest|FullyQualifiedName~IncomingVoiceActivitySnapshotTest|FullyQualifiedName~HotMicColdStartTest|FullyQualifiedName~WalkieTalkieTest" 2>&1 | tail -4
```
Expected: all PASS.

- [ ] **Step 3: Spec status + ledger**

- Spec: add `E1 (core + on-screen PTT): implemented` to the status line.
- `.superpowers/sdd/progress.md`: append E1 task completion lines (do NOT `git add`).

- [ ] **Step 4: Commit + record device-verification items**

```bash
git add docs/superpowers/specs/2026-07-20-walkie-talkie-reply-to-voice-design.md
git commit -m "docs: mark walkie reply E1 (core + on-screen PTT) implemented"
```

Report to the user (host/browser, not this machine): (1) tap-to-reply end-to-end with the cold-start dead-man switch; (2) hot-phase behavior — reply, go silent, confirm the mic stays open across an incoming message (incoming resets the existing idle) and closes after two-way silence; note the observed close time vs the ~1-min target (governed by `RecordingDuration` + server stream expiration) so we can decide in a follow-up whether to lengthen it for hot-window sessions.
