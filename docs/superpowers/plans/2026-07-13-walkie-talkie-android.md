# Walkie-Talkie Android Armed/Hot Lifecycle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a `SpeechStarted` FCM wake, the Android app plays the utterance
from its first word — even from a dead process, with no UI — then drops back
to a battery-neutral armed state after 5 minutes of background silence.

**Architecture:** The FCM handler starts the existing audio foreground
service, boots the app's service container headlessly
(`BlazorWebViewApp.EnsureStarted()` — a DI container, no WebView), creates a
private DI scope with JS calls neutralized (`SafeJSRuntime.MarkDisconnected()`
→ the `JSRuntimeDisconnected` failure mode the UI code already tolerates),
and drives the existing orchestration: `ChatAudioUI` walkie-talkie replay
(from the push's `startedAt`) that auto-restores live listening for all armed
chats when the replay catches up. A new background-only idle watcher clears
listening after 5 silent minutes; a teardown watcher then stops the FGS and
disposes the headless scope.

**Tech Stack:** .NET 10 MAUI (Android, workload `maui-android` installed),
ActualLab Fusion computed states, FirebaseMessagingService (data pushes),
`AndroidAudioWidgetForegroundService` (existing FGS), Plugin.Maui.Audio
(native tunes), xUnit for the pure-logic tests.

**Spec:** `docs/superpowers/specs/2026-07-13-walkie-talkie-android-design.md`

**Spec refinements discovered during source reading** (the spec's intent is
kept; the mechanics differ — the spec's altitude doesn't change):
1. Replay and listening are mutually exclusive by design
   (`ChatAudioUI.StartReplay` snapshots + clears listening chats and
   `StartStopReplayingPlayers` restores them when replay ends). So the wake
   flow does NOT start listening first: it starts the walkie-talkie replay
   with the armed-chat set as the restore snapshot — live listening for all
   armed chats starts automatically when the replay catches up/ends.
2. `MauiBackgroundState.Set(true)` is NOT called headlessly (it suspends the
   SQLite KVAS backend — too risky). Instead `ChatAudioUI` gets an
   `IsWalkieTalkieHeadless` flag; the idle watcher treats
   `IsBackground || IsWalkieTalkieHeadless` as "background".
3. The headless path bypasses `AudioWidget` entirely (its dispatch waits on
   the global WebView scope): the wake handler owns FGS show/update/stop via
   direct intents to `AndroidAudioWidgetForegroundService` (which is fully
   self-contained and already has a tap-to-open content intent).

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any code. Highlights: **no
  `Async` suffix**; **no XML docs on members** (type-level 3-line summary
  only when the name isn't self-explanatory; default to no comments —
  comments only for non-obvious "why"); classes/methods Allman braces,
  everything else K&R; max 120 chars/line; `.ConfigureAwait(false)` in
  service code; blank line after block-escaping statements unless last in
  block; test names PascalCase without underscores, AAA with lowercase
  comments.
- Spec values, verbatim: idle timeout **5 min** (background only, includes
  `ListeningMode.Forever` chats), stale-wake age **60 s** (matches the
  push's FCM TTL), squelch cue only on background/headless session starts,
  FGS notification stays on the existing Low-importance `audio_widget`
  channel.
- The server invariant (recorded in sub-project A's spec): client post-wake
  listening window must stay **> WalkieTalkieWakeTtl (30 s)** — 5 min
  satisfies it; keep the cross-reference comment on the constant.
- The headless scope is **never** published via
  `AppServicesAccessor.BlazorAppServices` (that setter requires
  `MauiWebViewPageContextTracker` + `UIHub.WhenInitialized` and would break
  `DispatchToBlazor`). It stays private to the wake session.
- Branch: `feat/walkie-talkie-push` (already checked out; do NOT create
  branches). Always `git add` explicit paths.
- Builds: shared code → `dotnet build ActualChat.CI.slnf`; MAUI code →
  `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
  (App.Maui is NOT in the CI filter; the android workload is installed).
- On-device behavior can't be auto-tested; the manual script is in the
  spec's Testing section. Every task still must compile and pass the unit
  tests that exist.

---

### Task 1: Shared walkie-talkie primitives (constants, helper, tune, replay API)

**Files:**
- Modify: `src/dotnet/Api/Constants.Audio.cs` (inside `public static class Audio`)
- Create: `src/dotnet/UI.Blazor.App/Services/WalkieTalkie.cs`
- Modify: `src/dotnet/UI.Blazor/Services/TuneUI/TuneUI.cs` (Tune enum + Tunes table)
- Create: `resources/sounds/raw/walkie-talkie-squelch.wav` (generated)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs` (headless flag)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.Players.cs` (walkie-talkie replay)
- Test: `tests/Chat.UI.Blazor.UnitTests/WalkieTalkieTest.cs` (create)

**Interfaces:**
- Consumes: `Constants.Audio` statics pattern; `TuneInfo(int[] Vibration, string Sound = "")`;
  `ChatAudioUI._listeningChatsBeforeReplay` / `_replayState` /
  `ClearListeningChats()` / `StopReplay()` (all existing);
  `ReplayState(ChatId, Moment StartAt, TimeSpan RewindOffset, double Speed)`.
- Produces (later tasks call these — exact signatures):
  - `Constants.Audio.WalkieTalkieIdleTimeout` (TimeSpan, 5 min),
    `Constants.Audio.WalkieTalkieIdleCheckPeriod` (TimeSpan, 15 s),
    `Constants.Audio.WalkieTalkieStaleWakeAge` (TimeSpan, 60 s)
  - `static bool WalkieTalkie.IsStaleWake(Moment startedAt, Moment now)`
  - `static Moment? WalkieTalkie.ComputeIdleDropAt(IReadOnlyList<Moment?> lastActivityTimes, Moment idleSince, TimeSpan idleTimeout)`
  - `Tune.WalkieTalkieSquelch`
  - `ChatAudioUI.IsWalkieTalkieHeadless { get; set; }` (bool)
  - `Task ChatAudioUI.StartWalkieTalkieReplay(ChatId chatId, Moment startAt, IReadOnlyCollection<ChatId> listeningChatsToRestore)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat.UI.Blazor.UnitTests/WalkieTalkieTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class WalkieTalkieTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    [Fact]
    public void FreshWakeIsNotStale()
    {
        // act + assert
        WalkieTalkie.IsStaleWake(T0, T0 + TimeSpan.FromSeconds(3)).Should().BeFalse();
        WalkieTalkie.IsStaleWake(T0, T0 + Constants.Audio.WalkieTalkieStaleWakeAge).Should().BeFalse();
    }

    [Fact]
    public void OldWakeIsStale()
    {
        // act + assert
        WalkieTalkie.IsStaleWake(T0, T0 + Constants.Audio.WalkieTalkieStaleWakeAge + TimeSpan.FromSeconds(1))
            .Should().BeTrue();
    }

    [Fact]
    public void OngoingStreamingYieldsNoDropTime()
    {
        // arrange: null last-activity means someone is streaming right now
        var lastActivityTimes = new List<Moment?> { T0, null };

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(lastActivityTimes, T0, TimeSpan.FromMinutes(5));

        // assert
        dropAt.Should().BeNull();
    }

    [Fact]
    public void DropTimeIsIdleTimeoutAfterLatestActivity()
    {
        // arrange
        var idleTimeout = TimeSpan.FromMinutes(5);
        var lastActivityTimes = new List<Moment?> { T0 + TimeSpan.FromMinutes(1), T0 + TimeSpan.FromMinutes(2) };

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(lastActivityTimes, T0, idleTimeout);

        // assert
        dropAt.Should().Be(T0 + TimeSpan.FromMinutes(2) + idleTimeout);
    }

    [Fact]
    public void IdleSinceClampsStaleActivityTimes()
    {
        // arrange: cached activity from a prior session must not shorten the idle window
        var idleTimeout = TimeSpan.FromMinutes(5);
        var lastActivityTimes = new List<Moment?> { T0 - TimeSpan.FromHours(2) };

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(lastActivityTimes, T0, idleTimeout);

        // assert
        dropAt.Should().Be(T0 + idleTimeout);
    }

    [Fact]
    public void NoActivityTimesFallBackToIdleSince()
    {
        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt([], T0, TimeSpan.FromMinutes(5));

        // assert
        dropAt.Should().Be(T0 + TimeSpan.FromMinutes(5));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
    --filter "FullyQualifiedName~WalkieTalkieTest" 2>&1 | tail -5
```
Expected: build FAILURE — `WalkieTalkie` type does not exist yet. That's the
red signal.

- [ ] **Step 3: Add the constants**

In `src/dotnet/Api/Constants.Audio.cs`, inside `public static class Audio`
(next to `ListeningDuration`):

```csharp
        // Walkie-talkie mode (docs/superpowers/specs/2026-07-13-walkie-talkie-android-design.md).
        // Invariant: must stay > the server's NotificationsSettings.WalkieTalkieWakeTtl (30s).
        public static readonly TimeSpan WalkieTalkieIdleTimeout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan WalkieTalkieIdleCheckPeriod = TimeSpan.FromSeconds(15);
        // Matches the wake push's FCM TTL; older wakes skip replay-from-start and go live.
        public static readonly TimeSpan WalkieTalkieStaleWakeAge = TimeSpan.FromSeconds(60);
```

- [ ] **Step 4: Create the pure helper**

`src/dotnet/UI.Blazor.App/Services/WalkieTalkie.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public static class WalkieTalkie
{
    public static bool IsStaleWake(Moment startedAt, Moment now)
        => now - startedAt > Constants.Audio.WalkieTalkieStaleWakeAge;

    public static Moment? ComputeIdleDropAt(
        IReadOnlyList<Moment?> lastActivityTimes, Moment idleSince, TimeSpan idleTimeout)
    {
        // A null last-activity means the chat is streaming right now (see
        // LiveStreamUI.GetLastActivityServerTime), so there is no drop time at all.
        var lastActivity = idleSince;
        foreach (var t in lastActivityTimes) {
            if (t is null)
                return null;

            lastActivity = Moment.Max(lastActivity, t.Value);
        }
        return lastActivity + idleTimeout;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
    --filter "FullyQualifiedName~WalkieTalkieTest" 2>&1 | tail -5
```
Expected: PASS — 6 passed, 0 failed.

- [ ] **Step 6: Add the squelch tune**

In `src/dotnet/UI.Blazor/Services/TuneUI/TuneUI.cs`:

(a) In `enum Tune`, add a member at the end (after `ClickButton,`):

```csharp
    WalkieTalkieSquelch,
```

(b) In the `Tunes` dictionary (after the `// Playback` group's
`[Tune.StopReplay]` line):

```csharp
        [Tune.WalkieTalkieSquelch] = new ([30, 20, 30], "walkie-talkie-squelch"),
```

- [ ] **Step 7: Generate the squelch sound source asset**

Run from the repo root:

```bash
python3 - <<'EOF'
import wave, random, struct, math
sr = 24000
n = int(sr * 0.35)
frames = bytearray()
prev = 0.0
random.seed(7)
for i in range(n):
    t = i / n
    white = random.uniform(-1, 1)
    prev = prev + 0.35 * (white - prev)          # one-pole low-pass "static"
    env = math.exp(-4.0 * t)                     # fast attack, exponential decay
    if t > 0.82:                                 # the classic squelch tail-click
        env += 0.8 * math.exp(-60.0 * (t - 0.82))
    sample = max(-1.0, min(1.0, prev * env * 2.2))
    frames += struct.pack('<h', int(sample * 32767 * 0.7))
with wave.open('resources/sounds/raw/walkie-talkie-squelch.wav', 'wb') as f:
    f.setnchannels(1); f.setsampwidth(2); f.setframerate(sr)
    f.writeframes(bytes(frames))
print('wrote', n, 'frames')
EOF
```

Expected: `wrote 8400 frames`, and
`resources/sounds/raw/walkie-talkie-squelch.wav` exists (~16 KB).

**Known limitation (document in the commit body, do not try to work
around):** this machine has no AAC encoder, so the converted
`resources/sounds/converted/walkie-talkie-squelch.m4a` (which
`App.Maui.csproj`'s existing `MauiAsset` wildcard would pick up
automatically) cannot be produced here. Until someone runs
`ffmpeg -i resources/sounds/raw/walkie-talkie-squelch.wav -c:a aac -b:a 96k resources/sounds/converted/walkie-talkie-squelch.m4a`
the cue plays vibration-only (`MauiTuneUI.PlaySound` handles the missing
file gracefully).

- [ ] **Step 8: Add the headless flag and the walkie-talkie replay API**

(a) In `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`, after the
`public bool IsAudioSyncEnabled { get; set; } = true;` line, add:

```csharp
    public bool IsWalkieTalkieHeadless { get; set; }
```

(b) In `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.Players.cs`, right
after the existing `StartReplay` method, add:

```csharp
    public async Task StartWalkieTalkieReplay(
        ChatId chatId, Moment startAt, IReadOnlyCollection<ChatId> listeningChatsToRestore)
    {
        // Wake-driven StartReplay variant: no confirm modal, and the listening set restored
        // when the replay ends is the armed-chat set supplied by the wake handler.
        lock (Lock) {
            if (_listeningChatsBeforeReplay.IsEmpty)
                _listeningChatsBeforeReplay = listeningChatsToRestore.ToImmutableHashSet();
        }
        await ClearListeningChats().ConfigureAwait(false);

        var speed = ReplaySettings.Value.Speed;
        DebugLog?.LogInformation("StartWalkieTalkieReplay: chatId={ChatId}, startAt={StartAt}, speed={Speed}",
            chatId, startAt, speed);

        StopReplay();
        _replayState.Value = new ReplayState(chatId, startAt, default, speed);
        _ = Hub.AudioAttachmentPlayer.Stop();
    }
```

- [ ] **Step 9: Build the shared code**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: 0 errors.

- [ ] **Step 10: Commit**

```bash
git add src/dotnet/Api/Constants.Audio.cs \
        src/dotnet/UI.Blazor.App/Services/WalkieTalkie.cs \
        src/dotnet/UI.Blazor/Services/TuneUI/TuneUI.cs \
        src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs \
        src/dotnet/UI.Blazor.App/Services/ChatAudioUI.Players.cs \
        resources/sounds/raw/walkie-talkie-squelch.wav \
        tests/Chat.UI.Blazor.UnitTests/WalkieTalkieTest.cs
git commit -m "feat(audio): walkie-talkie shared primitives - constants, squelch tune, wake replay

The converted m4a for the squelch tune is not committed: no AAC encoder in
this environment. Produce it with:
ffmpeg -i resources/sounds/raw/walkie-talkie-squelch.wav -c:a aac -b:a 96k resources/sounds/converted/walkie-talkie-squelch.m4a

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Background-only idle watcher (hot → armed)

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs`

**Interfaces:**
- Consumes (from Task 1): `Constants.Audio.WalkieTalkieIdleTimeout` /
  `WalkieTalkieIdleCheckPeriod`, `WalkieTalkie.ComputeIdleDropAt(...)`,
  `ChatAudioUI.IsWalkieTalkieHeadless`. Existing: `BackgroundStateTracker`
  (abstract, `ActualChat.Hosting`; registered on every platform — web
  `WebBackgroundStateTracker` scoped, MAUI `MauiBackgroundStateTracker`
  singleton), `LiveStreamUI.GetLastActivityServerTime(chatId, ct)` →
  `Moment?` where **null means "streaming right now"**,
  `GetListeningChatIds()`, `GetRecordingChatId()`, `ClearListeningChats()`,
  `_replayState`, `Hub.Services`, `Clocks`.
- Produces: the `StopListeningWhenIdleInBackground` chain — dropping to
  armed clears ALL listening chats (including `ListeningMode.Forever` ones)
  after 5 background-silent minutes. Task 4's teardown watcher reacts to the
  resulting empty listening set.

The chain is driven by wall-clock + computed states of a scoped hub — not
practically unit-testable without the full hub (same as the sibling
`StopListeningWhenIdle`); its pure decision core was extracted and tested in
Task 1. Verification here is compile + the existing suite + the on-device
script.

- [ ] **Step 1: Register the chain**

In `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs`, in `OnRun`'s
`baseChains` array, after `AsyncChain.From(StopListeningWhenIdle),` add:

```csharp
            AsyncChain.From(StopListeningWhenIdleInBackground),
```

- [ ] **Step 2: Implement the watcher**

In the same file, right after the `SetStopListeningAt` method, add:

```csharp
    // Walkie-talkie hot->armed drop: in background (or a headless wake session), stop ALL
    // listening - including ListeningMode.Forever chats, which the watcher above deliberately
    // never stops - after WalkieTalkieIdleTimeout of silence. The FCM wake push re-arms us.
    private async Task StopListeningWhenIdleInBackground(CancellationToken cancellationToken)
    {
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var backgroundStateTracker = Hub.Services.GetRequiredService<BackgroundStateTracker>();
        var serverClock = Clocks.ServerClock;
        Moment? idleSince = null;
        while (!cancellationToken.IsCancellationRequested) {
            await Clocks.CpuClock.Delay(Constants.Audio.WalkieTalkieIdleCheckPeriod, cancellationToken)
                .ConfigureAwait(false);

            var isBackground = backgroundStateTracker.IsBackground.Value || IsWalkieTalkieHeadless;
            if (!isBackground) {
                idleSince = null;
                continue;
            }

            var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
            var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
            if (listeningChatIds.IsEmpty || _replayState.Value is not null || recordingChatId is not null) {
                idleSince = null;
                continue;
            }

            var now = serverClock.Now;
            idleSince ??= now;
            var lastActivityTimes = new List<Moment?>(listeningChatIds.Count);
            foreach (var chatId in listeningChatIds)
                lastActivityTimes.Add(
                    await LiveStreamUI.GetLastActivityServerTime(chatId, cancellationToken).ConfigureAwait(false));

            var dropAt = WalkieTalkie.ComputeIdleDropAt(
                lastActivityTimes, idleSince.Value, Constants.Audio.WalkieTalkieIdleTimeout);
            if (dropAt is null || now < dropAt)
                continue;

            Log.LogInformation(
                "Walkie-talkie: {Count} listening chat(s) idle in background, dropping to armed",
                listeningChatIds.Count);
            await ClearListeningChats().ConfigureAwait(false);
            idleSince = null;
        }
    }
```

- [ ] **Step 3: Build + run the unit-test suite**

Run:
```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj 2>&1 | tail -4
```
Expected: 0 build errors; all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs
git commit -m "feat(audio): background idle watcher drops walkie-talkie listening to armed

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Headless scope runtime + wake-payload parsing

**Files:**
- Create: `src/dotnet/App.Maui/Services/HeadlessBlazorScope.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationData.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/MainActivity.cs:69` (one line after `EnsureStarted`)

**Interfaces:**
- Consumes: `BlazorWebViewApp.Current.Services` / `.EnsureStarted()`,
  `AppServicesAccessor.TryGetScopedServices(out var s)`,
  `SafeJSRuntime.MarkDisconnected()` (scoped —
  `src/dotnet/App.Maui/Services/JSRuntime/SafeJSRuntime.cs:50`),
  `Constants.Notification.MessageDataKeys.Timestamp` (epoch ms; a `Moment`
  is epoch-ms × 10_000 ticks — see the existing
  `new Moment(messageSentTime * 10_000)` in `FirebaseMessagingService`).
- Produces:
  - `HeadlessBlazorScope.GetOrCreate()` → `HeadlessBlazorScope?` (null when
    the WebView scope owns audio), `.Current` (static),
    `.Services` (IServiceProvider),
    `static Task HeadlessBlazorScope.DisposeCurrent(string reason)`
  - `NotificationData.StartedAt` → `Moment?`

- [ ] **Step 1: Create HeadlessBlazorScope**

`src/dotnet/App.Maui/Services/HeadlessBlazorScope.cs`:

```csharp
namespace ActualChat.App.Maui.Services;

/// <summary>
/// A private DI scope over the app container for wake-driven headless audio playback.
/// Never published via <see cref="AppServicesAccessor"/>; the WebView scope always wins.
/// </summary>
public sealed class HeadlessBlazorScope : IAsyncDisposable
{
    private static readonly Lock StaticLock = new();
    private static volatile HeadlessBlazorScope? _current;
    private static ILogger Log => field ??= StaticLog.For<HeadlessBlazorScope>();

    private readonly IServiceScope _scope;

    public static HeadlessBlazorScope? Current => _current;

    public IServiceProvider Services => _scope.ServiceProvider;

    private HeadlessBlazorScope(IServiceScope scope)
        => _scope = scope;

    public static HeadlessBlazorScope? GetOrCreate()
    {
        lock (StaticLock) {
            if (AppServicesAccessor.TryGetScopedServices(out _))
                return null;

            if (_current is not null)
                return _current;

            var scope = BlazorWebViewApp.Current.Services.CreateScope();
            // No WebView will ever attach here: make every JS call fail with the
            // JSRuntimeDisconnected the UI code already tolerates (the page-reload path).
            scope.ServiceProvider.GetRequiredService<SafeJSRuntime>().MarkDisconnected();
            _current = new HeadlessBlazorScope(scope);
            Log.LogInformation("Headless scope created");
            return _current;
        }
    }

    public static Task DisposeCurrent(string reason)
    {
        HeadlessBlazorScope? current;
        lock (StaticLock) {
            current = _current;
            _current = null;
        }
        if (current is null)
            return Task.CompletedTask;

        Log.LogInformation("Disposing headless scope ({Reason})", reason);
        return current.DisposeAsyncCore();
    }

    public ValueTask DisposeAsync()
    {
        lock (StaticLock)
            if (_current == this)
                _current = null;
        return new ValueTask(DisposeAsyncCore());
    }

    // Private methods

    private async Task DisposeAsyncCore()
    {
        if (_scope is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            _scope.Dispose();
    }
}
```

- [ ] **Step 2: Parse StartedAt from the wake payload**

In `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationData.cs`,
after the `EntryLocalId` property, add:

```csharp
    // The wake push's speech-start moment (epoch ms in the Timestamp data key).
    public Moment? StartedAt {
        get {
            data.TryGetValue(Constants.Notification.MessageDataKeys.Timestamp, out var sTimestamp);
            return long.TryParse(sTimestamp, out var epochMs)
                ? new Moment(epochMs * 10_000)
                : null;
        }
    }
```

- [ ] **Step 3: Dispose the headless scope when the app opens**

In `src/dotnet/App.Maui/Platforms/Android/MainActivity.cs`, `OnCreate`,
right after `BlazorWebViewApp.EnsureStarted();` (line 69), add:

```csharp
        _ = HeadlessBlazorScope.DisposeCurrent("MainActivity.OnCreate");
```

(`ActualChat.App.Maui.Services` is already imported in that file via
`using ActualChat.App.Maui.Services;` — verify, add if missing.)

Deliberately untested in automation (documented, not an oversight): the
`StartedAt` parser lives in the android-targeted `App.Maui` project — there
is no plain-.NET test project that can reference it, and no Android
instrumentation test harness exists in-repo. Its logic mirrors the
`Moment(ms * 10_000)` precedent already in `FirebaseMessagingService` and is
covered by the on-device script.

- [ ] **Step 4: Build**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android 2>&1 | tail -3`
Expected: 0 errors (first android build may take several minutes).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/Services/HeadlessBlazorScope.cs \
        src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationData.cs \
        src/dotnet/App.Maui/Platforms/Android/MainActivity.cs
git commit -m "feat(maui): headless Blazor scope + SpeechStarted payload parsing

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Android wake handler (FGS, warm/headless routing, replay, fallback, teardown)

**Files:**
- Create: `src/dotnet/App.Maui/Platforms/Android/Audio/WalkieTalkieWakeHandler.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs:92` (new branch)
- Modify: `src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidget.cs:22` (Stop fallback)

**Interfaces:**
- Consumes: everything produced by Tasks 1–3, plus existing:
  `AndroidAudioWidgetForegroundService` (`ActionShow`, `IntentExtras`,
  self-contained, notification already opens the chat on tap),
  `AudioWidgetMode.Listening`, `AndroidUtils.IsAppForeground()` → `bool?`,
  `TrueSessionResolver.SessionTask`, `AppUIHub` (scoped; `.ChatAudioUI`,
  `.TuneUI`, `.Chats`, `.Session`, `.Clocks`),
  `ChatAudioUI.GetChatsYouNeedToKeepListeningTo(ct)` → `List<ChatId>`
  (safe headlessly: awaits `ChatUI.WhenReady` = a local-storage read),
  `NotificationHelper.ShowChatNotification(tag, title, body, imageUrl, link, silent)`,
  `Links.Chat(chatId)`, `BackgroundTask.Run(func, log, message, ct)`.
- Produces: `static void WalkieTalkieWakeHandler.Handle(NotificationData data)`
  and `static void WalkieTalkieWakeHandler.StopHeadlessSession()`.

- [ ] **Step 1: Create the wake handler**

`src/dotnet/App.Maui/Platforms/Android/Audio/WalkieTalkieWakeHandler.cs`:

```csharp
using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Handles kind=SpeechStarted FCM wakes: starts the audio FGS, boots the app container
/// headlessly when no WebView scope exists, and replays the utterance from its start.
/// </summary>
public static class WalkieTalkieWakeHandler
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TeardownCheckPeriod = TimeSpan.FromSeconds(5);
    private const int TeardownIdleChecks = 2;
    private static readonly Lock Lock = new();
    private static Task? _teardownWatcher;
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
            () => HandleImpl(chatId, startedAt, isForeground),
            Log, "SpeechStarted wake failed", CancellationToken.None);
    }

    public static void StopHeadlessSession()
        => _ = BackgroundTask.Run(async () => {
            if (HeadlessBlazorScope.Current is not { } headless)
                return;

            var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
            chatAudioUI.StopReplay();
            await chatAudioUI.ClearListeningChats().ConfigureAwait(false);
            HideForegroundService();
            await HeadlessBlazorScope.DisposeCurrent("stopped from the notification").ConfigureAwait(false);
        }, Log, "StopHeadlessSession failed", CancellationToken.None);

    // Private methods

    private static async Task HandleImpl(ChatId chatId, Moment startedAt, bool isForeground)
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

            await StartPlayback(scopedServices, chatId, startedAt, isForeground, isHeadless)
                .ConfigureAwait(false);
            if (isHeadless)
                EnsureTeardownWatcher();
        }
        catch (Exception e) {
            Log.LogError(e, "SpeechStarted wake failed for chat #{ChatId}", chatId);
            ShowFallbackNotification(chatId);
            HideForegroundService();
            await HeadlessBlazorScope.DisposeCurrent("wake failed").ConfigureAwait(false);
        }
    }

    private static async Task StartPlayback(
        IServiceProvider scopedServices, ChatId chatId, Moment startedAt, bool isForeground, bool isHeadless)
    {
        var hub = scopedServices.GetRequiredService<AppUIHub>();
        var chatAudioUI = hub.ChatAudioUI;
        if (isHeadless)
            chatAudioUI.IsWalkieTalkieHeadless = true;
        chatAudioUI.Enable();

        // The server gates wakes on the same settings; re-read them for the restore set.
        var restoreSet = await chatAudioUI.GetChatsYouNeedToKeepListeningTo(CancellationToken.None)
            .ConfigureAwait(false);
        if (!restoreSet.Contains(chatId))
            restoreSet = [..restoreSet, chatId];

        if (!isForeground)
            _ = hub.TuneUI.Play(Tune.WalkieTalkieSquelch);

        if (WalkieTalkie.IsStaleWake(startedAt, hub.Clocks.SystemClock.Now))
            foreach (var armedChatId in restoreSet)
                await chatAudioUI.SetListeningState(armedChatId, true).ConfigureAwait(false);
        else
            await chatAudioUI.StartWalkieTalkieReplay(chatId, startedAt, restoreSet).ConfigureAwait(false);

        if (!isForeground)
            _ = UpdateForegroundServiceTitle(hub, chatId);
    }

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

    private static void EnsureTeardownWatcher()
    {
        lock (Lock)
            _teardownWatcher ??= BackgroundTask.Run(
                WatchTeardown, Log, "Teardown watcher failed", CancellationToken.None);
    }

    private static async Task WatchTeardown()
    {
        try {
            var idleChecks = 0;
            while (true) {
                await Task.Delay(TeardownCheckPeriod).ConfigureAwait(false);
                if (HeadlessBlazorScope.Current is not { } headless)
                    return; // The WebView scope owns audio now; its AudioWidget owns the FGS

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
                HideForegroundService();
                await HeadlessBlazorScope.DisposeCurrent("armed (idle)").ConfigureAwait(false);
                return;
            }
        }
        finally {
            lock (Lock)
                _teardownWatcher = null;
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
    }

    private static void HideForegroundService()
    {
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        context.StopService(intent);
    }

    private static void ShowFallbackNotification(ChatId chatId)
        => NotificationHelper.ShowChatNotification(
            chatId.Value,
            "Voxt",
            "Someone is talking in a chat you keep listening to",
            null,
            Links.Chat(chatId),
            silent: false);
}
```

- [ ] **Step 2: Route SpeechStarted pushes to the handler**

In `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs`,
in `OnMessageReceivedImpl`, after the `DismissedTags` block (line 90) and
BEFORE the `Attention` check, add:

```csharp
        if (data.NotificationKind == NotificationKind.SpeechStarted) {
            WalkieTalkieWakeHandler.Handle(data);
            return;
        }
```

Add the using if not present: `using ActualChat.App.Maui.Audio;`

- [ ] **Step 3: Make the notification Stop button work headlessly**

In `src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidget.cs`,
replace:

```csharp
    public static void Stop() => _instance?.InvokeAction(ActionNames.Stop);
```

with:

```csharp
    public static void Stop()
    {
        // In a headless wake session no AndroidAudioWidget instance exists - the wake
        // handler owns the FGS and the listening state.
        if (_instance is { } instance)
            instance.InvokeAction(ActionNames.Stop);
        else
            WalkieTalkieWakeHandler.StopHeadlessSession();
    }
```

(Pause/Resume stay no-ops without an instance — acceptable for v1; the
notification's tap-to-open covers the rest.)

- [ ] **Step 4: Build**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android 2>&1 | tail -3`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/Android/Audio/WalkieTalkieWakeHandler.cs \
        src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs \
        src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidget.cs
git commit -m "feat(android): SpeechStarted wake handler - FGS, headless playback, teardown

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full shared build + unit tests**

Run:
```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj 2>&1 | tail -4
```
Expected: 0 errors; all tests pass (including the 6 WalkieTalkieTest ones).

- [ ] **Step 2: Android build**

Run: `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android 2>&1 | tail -3`
Expected: 0 errors.

- [ ] **Step 3: Server-side regression check**

The branch also carries sub-project A; make sure nothing here disturbed it:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
    --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -4
```
Expected: 7 passed.

- [ ] **Step 4: Confirm clean tree**

Run: `git status --short`
Expected: empty (everything committed).

The on-device manual script (kill app → speak → squelch + first-word
playback; 5-min silence → FGS gone; re-speak → re-wake; open app mid-session
→ clean handover; signed-out → fallback notification) is in the spec's
Testing section and requires a physical device + a second account — outside
this plan's automation.
