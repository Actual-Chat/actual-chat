# Walkie-Talkie Headset Button + Headless Reply Pipeline (Sub-Project E3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the walkie-talkie reply pipeline work from a killed-then-woken process, and add a Bluetooth/headset media-button trigger that opens a reply without touching the screen.

**Architecture:** Startup becomes scope-driven rather than Blazor-render-driven, so the same walkie services start in the headless wake scope as in the WebView scope — which also revives E2's shake and flip after a wake. On top of that, the `MediaSessionCompat` that `AndroidAudioWidgetForegroundService` already owns gains an `OnMediaButtonEvent` override that consults a pure policy and either starts/stops a reply or defers to today's play/pause behaviour.

**Tech Stack:** C# 13 / .NET 10, .NET MAUI (Android), AndroidX `MediaSessionCompat`, ActualLab.Fusion compute services, Blazor, xUnit + AwesomeAssertions.

**Spec:** `docs/superpowers/specs/2026-08-03-walkie-talkie-headset-button-design.md`

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any C#.** No `Async` suffix on async methods; **no `///` XML docs on members — ever** (type-level `<summary>` only, and only where the name isn't self-explanatory); Allman braces for classes/methods, K&R for everything else including razor; max 120 chars/line; 4-space indent; control-flow statements get their own line followed by a blank line; private static readonly fields and constants PascalCase, other private fields `_camelCase`; boolean names prefixed `is`/`must`/`has`.
- **Read `docs/development/ui-components.md` before touching any `.razor`.**
- **Comment budget: default to none.** Every comment written verbatim in this plan is deliberate — copy those, add no others.
- **Fail closed, and fail to pass-through.** This feature opens a microphone *and* intercepts a button that already does something. On any ambiguity — no scope, no window, unknown key, failed settings read — the correct outcome is `PassThrough`, i.e. today's playback behaviour, mic shut.
- **`UserWalkieTalkieSettings` next free member order is `8`.** E2 used 0–7.
- Build check: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`. TS/CSS check (only if `.ts`/`.css` touched): `npm run build:Verify 2>&1 | tail -20` → clean.

### The platform-compile blind spot — read this before Tasks 3–6

**`App.Maui.csproj` is not in `ActualChat.CI.slnf`.** Nothing on the build machine compiles `MainActivity.cs`, `AndroidAudioWidget*.cs`, `HeadlessBlazorScope.cs`, or anything you add beside them. Sub-project E2 produced two defects of exactly this shape — a missing `using Microsoft.Maui.Devices.Sensors`, and a settings type missing from `UserSettings.KeyToType` — and both were invisible to every check available locally.

So for every MAUI file you touch:

1. Verify each API you call by name, type and casing against the real binding assemblies or against existing platform code in `src/dotnet/App.Maui/Platforms/`.
2. Separately verify that every unqualified type resolves through the file's own usings or `App.Maui`'s global usings (`src/dotnet/App.Maui/GlobalUsings.cs`, `src/dotnet/App.Maui/Directory.Build.props`, repo-root `src/dotnet/Directory.Build.props`).
3. Report what you checked and where.

**"The API exists" and "this file can see it" are separate claims.** Only the compiler normally checks the second, and here it won't.

## Decisions resolved during planning

1. **The open question from the spec is resolved, favourably.** The `MediaSession` lives inside `AndroidAudioWidgetForegroundService`, which runs only while `AudioWidgetState` is non-null — i.e. while something is listening, recording or replaying. That is not a problem, because E2 decision 8 force-listens PTT chats: `GetChatsYouNeedToKeepListeningTo` unions `PttChatIds`, so a PTT chat is continuously listened, the widget is shown, and the FGS and its media session are alive whenever an answer window can be open. **Verify this on device rather than trusting the reasoning** — it is the load-bearing assumption of the whole button path, and if it is wrong the design needs a session-activation step.
2. **The policy is Android-free.** It takes a small `HeadsetKey` enum that the Android layer maps from `Keycode`, so it lives in `UI.Blazor.App` beside `GestureActivationPolicy` and is unit-testable on a build machine.
3. **Act on `ACTION_DOWN` with `repeatCount == 0`.** Down gives a faster response than Up, and the repeat guard drops the auto-repeat stream a long-press generates. Handling both edges is the classic media-button bug and here it is worse than cosmetic — see Task 2.
4. **`StartScopedServices` is an explicit inclusion list, not a skip-list.** Only `IncomingVoiceActivityUI`, `GestureUI`, `TuneUI` and `AudioWidget` move. Everything else in `AfterFirstRender` stays render-driven. A reader must be able to see what runs headlessly without reasoning about what was disabled.

---

### Task 1: The `IsHeadsetButtonEnabled` setting

**Files:**
- Modify: `src/dotnet/Api/Users/StoredSettings/UserWalkieTalkieSettings.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkSettings.razor`
- Test: `tests/Users.UnitTests/UserWalkieTalkieSettingsTest.cs`

**Interfaces:**
- Produces: `UserWalkieTalkieSettings.IsHeadsetButtonEnabled`, `bool`, `MemoryPackOrder(8)` / `Key(8)`, default `true`.

- [ ] **Step 1: Extend the round-trip test**

In `tests/Users.UnitTests/UserWalkieTalkieSettingsTest.cs`, add to `Defaults_AreSafe`:

```csharp
        settings.IsHeadsetButtonEnabled.Should().BeTrue();
```

and to the settings instance built in `PassesThroughAllSerializers`, add `IsHeadsetButtonEnabled = false,` plus its assertion:

```csharp
                d.IsHeadsetButtonEnabled.Should().Be(o.IsHeadsetButtonEnabled);
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~UserWalkieTalkieSettingsTest" 2>&1 | tail -5`
Expected: build FAILS — the member does not exist.

- [ ] **Step 3: Add the member**

In `UserWalkieTalkieSettings.cs`, after `AreAudibleCuesEnabled`:

```csharp
    [DataMember, MemoryPackOrder(8), Key(8)]
    public bool IsHeadsetButtonEnabled { get; init; } = true;
```

- [ ] **Step 4: Add the settings row**

In `PushToTalkSettings.razor`, inside the "Answer gestures" `Tile`, after the double-shake row and before the sensitivity row:

```razor
        <TileItem Click="@OnToggleHeadsetButton">
            <Icon><i class="icon-headphones text-2xl"></i></Icon>
            <Content>Headset button</Content>
            <Caption>Press the button on your earbuds to reply</Caption>
            <Right><Toggle IsChecked="@m.Settings.IsHeadsetButtonEnabled"/></Right>
        </TileItem>
```

and the handler beside the other toggle handlers:

```csharp
    private Task OnToggleHeadsetButton()
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(x => x with { IsHeadsetButtonEnabled = !x.IsHeadsetButtonEnabled }, CancellationToken.None);
```

**Verify `icon-headphones` exists** by grepping `src/nodejs/fonts/svgtofont/icon.css`; substitute the nearest real icon if not and say which in your report. Do not invent an asset.

Note the row sits inside the section gated on `hasSensors` in E2's markup. That gate is wrong for this row — a headset button needs no accelerometer. Move this row outside that condition, into a section that renders whenever `HostInfo.HostKind.IsMauiApp()`, and say in your report where you put it.

- [ ] **Step 5: Run the test and build**

Run:
```bash
dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~UserWalkieTalkieSettingsTest" 2>&1 | tail -4
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
npm run build:Verify 2>&1 | tail -20
```
Expected: PASS; `0 Error(s)`; build:Verify clean.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Users/StoredSettings/UserWalkieTalkieSettings.cs \
        src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkSettings.razor \
        tests/Users.UnitTests/UserWalkieTalkieSettingsTest.cs
git commit -m "feat(users): headset-button PTT setting"
```

---

### Task 2: `HeadsetButtonPolicy`

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/HeadsetButtonPolicy.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/HeadsetButtonPolicyTest.cs`

**Interfaces:**
- Produces, in `ActualChat.UI.Blazor.App.Services.Gestures`:
  - `enum HeadsetKey { Unknown = 0, Hook, PlayPause }`
  - `enum HeadsetButtonAction { PassThrough = 0, StartReply, StopReply }`
  - `HeadsetButtonPolicy.Decide(HeadsetKey key, bool isDown, int repeatCount, bool isEnabled, bool hasAnswerWindow, bool isReplyHot) → HeadsetButtonAction`

This is the highest test-value piece in the sub-project and the only part with real automated coverage. Everything else resolves on a device.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat.UI.Blazor.UnitTests/HeadsetButtonPolicyTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class HeadsetButtonPolicyTest
{
    [Theory]
    [InlineData(HeadsetKey.Hook)]
    [InlineData(HeadsetKey.PlayPause)]
    public void StartsAReplyInsideTheWindow(HeadsetKey key)
        => HeadsetButtonPolicy
            .Decide(key, isDown: true, repeatCount: 0, isEnabled: true, hasAnswerWindow: true, isReplyHot: false)
            .Should().Be(HeadsetButtonAction.StartReply);

    [Fact]
    public void StopsAHotReply()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: true, hasAnswerWindow: true, isReplyHot: true)
            .Should().Be(HeadsetButtonAction.StopReply);

    [Fact]
    public void StopsAHotReplyEvenAfterTheWindowClosed()
    {
        // The window can expire mid-reply; the second press must still be able to close the mic.
        HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: true, hasAnswerWindow: false, isReplyHot: true)
            .Should().Be(HeadsetButtonAction.StopReply);
    }

    [Fact]
    public void PassesThroughOutsideTheWindow()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: true, hasAnswerWindow: false, isReplyHot: false)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PassesThroughWhenDisabled()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, 0, isEnabled: false, hasAnswerWindow: true, isReplyHot: false)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void PassesThroughOnAnUnknownKey()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Unknown, true, 0, isEnabled: true, hasAnswerWindow: true, isReplyHot: false)
            .Should().Be(HeadsetButtonAction.PassThrough);

    [Fact]
    public void ActsOnExactlyOneEdge()
    {
        // Handling both edges of one press would open the mic and immediately close it:
        // by the time ACTION_UP arrives the reply is hot, so the policy would map it to StopReply.
        HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, isDown: false, 0, true, hasAnswerWindow: true, isReplyHot: true)
            .Should().Be(HeadsetButtonAction.PassThrough);
    }

    [Fact]
    public void IgnoresAutoRepeat()
        => HeadsetButtonPolicy
            .Decide(HeadsetKey.Hook, true, repeatCount: 1, isEnabled: true, hasAnswerWindow: true, isReplyHot: false)
            .Should().Be(HeadsetButtonAction.PassThrough);
}
```

`StopsAHotReplyEvenAfterTheWindowClosed` encodes a real case: `WalkieTalkieReplyRecencyWindow` is 150 s and a reply can outlive it. If stopping required an open window, the user could open the mic and then be unable to close it with the same button.

- [ ] **Step 2: Run and watch it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~HeadsetButtonPolicyTest" 2>&1 | tail -5`
Expected: build FAILS — the type does not exist.

- [ ] **Step 3: Write the policy**

Create `src/dotnet/UI.Blazor.App/Services/Gestures/HeadsetButtonPolicy.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class HeadsetButtonPolicy
{
    public static HeadsetButtonAction Decide(
        HeadsetKey key,
        bool isDown,
        int repeatCount,
        bool isEnabled,
        bool hasAnswerWindow,
        bool isReplyHot)
    {
        // One press delivers both edges plus auto-repeats; acting on more than one would
        // open the mic and immediately close it, because the later edges see a hot reply.
        if (!isDown || repeatCount != 0)
            return HeadsetButtonAction.PassThrough;
        if (!isEnabled || key == HeadsetKey.Unknown)
            return HeadsetButtonAction.PassThrough;
        // A reply can outlive the answer window, so closing it must not depend on the window.
        if (isReplyHot)
            return HeadsetButtonAction.StopReply;

        return hasAnswerWindow ? HeadsetButtonAction.StartReply : HeadsetButtonAction.PassThrough;
    }
}

public enum HeadsetKey
{
    Unknown = 0,
    Hook,
    PlayPause,
}

public enum HeadsetButtonAction
{
    PassThrough = 0,
    StartReply,
    StopReply,
}
```

- [ ] **Step 4: Run and watch it pass, then build**

Run:
```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~HeadsetButtonPolicyTest" 2>&1 | tail -4
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
```
Expected: 9 PASS; `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/Gestures/HeadsetButtonPolicy.cs \
        tests/Chat.UI.Blazor.UnitTests/HeadsetButtonPolicyTest.cs
git commit -m "feat(audio-ui): pure headset-button policy"
```

---

### Task 3: Scope-driven startup

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/AppScopedServiceStarter.cs`
- Modify: `src/dotnet/App.Maui/Services/HeadlessBlazorScope.cs`

**Interfaces:**
- Produces: `AppScopedServiceStarter.StartScopedServices(IServiceProvider services)`, `public static`, callable from any scope. Idempotent per scope.

This is the task that makes every trigger work after a wake — including E2's shake and flip, which are dead there today.

- [ ] **Step 1: Extract the headless-safe list**

In `AppScopedServiceStarter.cs`, add a public static method and call it from the existing `AfterFirstRender` body in place of the four lines it replaces:

```csharp
    public static void StartScopedServices(IServiceProvider services)
    {
        // Runs for any scope, headless or WebView. Everything here must work with a
        // disconnected SafeJSRuntime - see HeadlessBlazorScope.
        var hub = services.GetRequiredService<AppUIHub>();
        _ = hub.TuneUI;
        _ = hub.AudioWidget;
        _ = hub.IncomingVoiceActivityUI;
        hub.GestureUI.Start();
    }
```

In `AfterFirstRender`, replace these four lines:

```csharp
            _ = Hub.TuneUI; // Touch. Auto-starts on construction
            _ = Hub.AudioWidget; // Touch. Auto-starts on construction
            _ = Hub.IncomingVoiceActivityUI; // Touch. Auto-starts the incoming-voice tracker
            Hub.GestureUI.Start();
```

with:

```csharp
            StartScopedServices(Hub.Services);
```

Leave `_ = Hub.VideoQualityUI;` and everything else exactly where it is — video is not walkie and its chains gate on video activity that cannot occur headlessly.

- [ ] **Step 2: Call it from the headless scope**

In `HeadlessBlazorScope.GetOrCreate`, after `MarkDisconnected()` and before `_current` is assigned:

```csharp
            AppScopedServiceStarter.StartScopedServices(scope.ServiceProvider);
```

Add the `using` for its namespace. Consider ordering: the call must happen before `_current` is published, so nothing can observe a half-started scope — but it must not throw out of `GetOrCreate`, because the wake path treats a null return as "a WebView scope already exists" rather than "startup failed". Wrap it so a failure logs and leaves the scope usable for playback:

```csharp
            try {
                AppScopedServiceStarter.StartScopedServices(scope.ServiceProvider);
            }
            catch (Exception e) {
                // A failed trigger startup must not cost us the wake: playback is the primary job.
                Log.LogWarning(e, "Couldn't start scoped services in the headless scope");
            }
```

- [ ] **Step 3: Verify what you just made run headlessly**

This is the step that matters, and no test can do it for you. For each of `TuneUI`, `AudioWidget`, `IncomingVoiceActivityUI` and `GestureUI`, trace the constructor and the started loop and confirm it does not require a live JS runtime. Specifically:

- `TuneUI` on MAUI resolves to `MauiTuneUI`/`AppleTuneUI` (native vibration) — check the registration in `MauiAppModule`, not the base class.
- `AudioWidget` on Android resolves to `AndroidAudioWidget`, whose constructor calls `DispatchToBlazor` — **check what that does with no WebView**, since it is the most likely failure.
- `IncomingVoiceActivityUI` and `GestureUI` are Fusion workers over RPC; confirm no JS.

Write the trace into your report, per service, with file:line. If any of them does need JS, **stop and report** rather than removing it from the list — the list is the design, and shrinking it silently would leave a trigger dead in a way nobody would notice.

- [ ] **Step 4: Build and confirm the WebView path is unchanged**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`.

There is no automated coverage for either path here. State that plainly in your report.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/AppScopedServiceStarter.cs \
        src/dotnet/App.Maui/Services/HeadlessBlazorScope.cs
git commit -m "feat(app): scope-driven startup for the walkie-talkie services"
```

---

### Task 4: `AppScopeAccessor`

**Files:**
- Create: `src/dotnet/App.Maui/Services/AppScopeAccessor.cs`

**Interfaces:**
- Produces: `AppScopeAccessor.Current` → `IServiceProvider?` — the published WebView scope if there is one, else `HeadlessBlazorScope.Current?.Services`, else null.

- [ ] **Step 1: Write it**

```csharp
namespace ActualChat.App.Maui.Services;

/// <summary>
/// The scope a static Android component should talk to: the WebView scope when the UI
/// is up, the headless wake scope otherwise.
/// </summary>
public static class AppScopeAccessor
{
    public static IServiceProvider? Current
        => AppServicesAccessor.TryGetScopedServices(out var services)
            ? services
            : HeadlessBlazorScope.Current?.Services;
}
```

**Verify `AppServicesAccessor.TryGetScopedServices`'s real signature** before writing this — check whether it takes a timeout, whether it blocks, and whether it is safe to call from a binder thread. `MauiLivenessProbe.cs:143` and `MauiAppModule.cs:43` both use it; match their usage. If it can block, this accessor must not — a media-button callback runs on a system thread with a deadline. Report which overload you used and why.

- [ ] **Step 2: Build**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`.

`App.Maui` is not in the CI filter, so this proves nothing about the new file. Do the namespace-scope verification from the Global Constraints and report it.

- [ ] **Step 3: Commit**

```bash
git add src/dotnet/App.Maui/Services/AppScopeAccessor.cs
git commit -m "feat(maui): AppScopeAccessor - the live scope for static Android components"
```

---

### Task 5: Wire the media button

**Files:**
- Modify: `src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidgetForegroundService.cs`

**Interfaces:**
- Consumes: `HeadsetButtonPolicy` (Task 2), `AppScopeAccessor` (Task 4), `UserWalkieTalkieSettings.IsHeadsetButtonEnabled` (Task 1), and from E1/E2: `WalkieTalkieReplyUI.RequestReply`/`StopReply`, `ChatAudioUI.GetPttChatIds`, `ChatAudioUI.GetRecordingChatId`, `IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt`, `GestureActivationPolicy.ShouldSenseStartGestures`, `Constants.Audio.WalkieTalkieReplyRecencyWindow`.

- [ ] **Step 1: Override `OnMediaButtonEvent`**

In the nested `Callback` class, add:

```csharp
        public override bool OnMediaButtonEvent(Intent? mediaButtonEvent)
        {
            var keyEvent = GetKeyEvent(mediaButtonEvent);
            if (keyEvent is null)
                return base.OnMediaButtonEvent(mediaButtonEvent);

            var key = keyEvent.KeyCode switch {
                Keycode.Headsethook => HeadsetKey.Hook,
                Keycode.MediaPlayPause => HeadsetKey.PlayPause,
                _ => HeadsetKey.Unknown,
            };
            if (key == HeadsetKey.Unknown)
                return base.OnMediaButtonEvent(mediaButtonEvent);

            var isDown = keyEvent.Action == KeyEventActions.Down;
            if (!TryHandleHeadsetButton(key, isDown, keyEvent.RepeatCount))
                return base.OnMediaButtonEvent(mediaButtonEvent);

            return true;
        }
```

`GetKeyEvent` extracts the parcelable. **The non-deprecated overload differs by API level** — `Intent.GetParcelableExtra(string)` is deprecated from API 33 in favour of the typed overload. Check what the current `Mono.Android` binding exposes and what `minSdkVersion 28` / `targetSdkVersion 34` require, follow whatever pattern the repo already uses for parcelable extras if there is one, and report which you used.

- [ ] **Step 2: Expose a synchronous snapshot from `GestureUI`**

A media-button callback runs on a system binder thread with a deadline, so the handler **must not block** — and every input the policy needs is async (`GetPttChatIds`, the settings read, `GetRecordingChatId`). Blocking on them with `.Result` would risk an ANR on the audio path.

It does not need to. `GestureUI`'s activation loop already reads all three every iteration and on every relevant invalidation. Have it publish them.

In `src/dotnet/UI.Blazor.App/Services/Gestures/GestureUI.cs`, add the fields and the accessor:

```csharp
    private volatile bool _isHeadsetButtonEnabled;
    private volatile bool _hasAnswerWindow;

    public HeadsetButtonState GetHeadsetButtonState()
        => new(_isHeadsetButtonEnabled, _hasAnswerWindow, ChatAudioUI.IsRecording());
```

and set both inside `TrackActivation`, beside where it already computes `mustSenseStart`:

```csharp
                _isHeadsetButtonEnabled = settings.IsHeadsetButtonEnabled;
                _hasAnswerWindow = mustSenseStart;
```

Reusing `mustSenseStart` is deliberate: the button and the gestures then cannot disagree about whether a reply is plausible, because they are literally the same value.

Add the record beside `HeadsetButtonPolicy`:

```csharp
public readonly record struct HeadsetButtonState(bool IsEnabled, bool HasAnswerWindow, bool IsReplyHot);
```

`ChatAudioUI.IsRecording()` does not exist yet — add it as a synchronous sibling of `GetRecordingChatId`, reading the same `ActiveChatsUI.ActiveChats.Value` that the compute method reads, so there is no new source of truth:

```csharp
    public bool IsRecording()
        => ActiveChatsUI.ActiveChats.Value.Any(c => c.IsRecording);
```

**Confirm before writing it** that `GetRecordingChatId` really does read `ActiveChatsUI.ActiveChats.Value` synchronously and wrap it in `Task.FromResult` — if it does not, say so and stop rather than inventing a second recording-state source.

- [ ] **Step 3: Add the handler**

Keep the Android-specific extraction separate from the decision. Add beside the service:

```csharp
    private static bool TryHandleHeadsetButton(HeadsetKey key, bool isDown, int repeatCount)
    {
        if (AppScopeAccessor.Current is not { } services)
            return false;

        var hub = services.GetRequiredService<AppUIHub>();
        var state = hub.GestureUI.GetHeadsetButtonState();
        var action = HeadsetButtonPolicy.Decide(
            key, isDown, repeatCount, state.IsEnabled, state.HasAnswerWindow, state.IsReplyHot);
        if (action == HeadsetButtonAction.PassThrough)
            return false;

        var replyUI = hub.WalkieTalkieReplyUI;
        var whenHandled = action == HeadsetButtonAction.StopReply
            ? replyUI.StopReply()
            : replyUI.RequestReply(CancellationToken.None);
        _ = BackgroundTask.Run(() => whenHandled, Log, $"{action} from the headset button failed",
            CancellationToken.None);
        return true;
    }
```

Name it exactly this — Step 1's override calls `TryHandleHeadsetButton`. Every read here is synchronous; nothing awaits.

Note `Log` is the service's instance logger and this method is static — resolve that mismatch the way the file already does for its other statics, and say which you used.

- [ ] **Step 4: Build and verify the APIs**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)` (which does not compile this file).

Do the full platform-API verification from the Global Constraints for `Keycode`, `KeyEventActions`, `KeyEvent.RepeatCount`, `Intent.ExtraKeyEvent`, `MediaSessionCompat.Callback.OnMediaButtonEvent`'s exact signature and return semantics, and every unqualified type you introduce. Report the table.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidgetForegroundService.cs \
        src/dotnet/UI.Blazor.App/Services/Gestures/GestureUI.cs \
        src/dotnet/UI.Blazor.App/Services/Gestures/HeadsetButtonPolicy.cs \
        src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs
git commit -m "feat(maui): headset media button starts and stops a walkie reply"
```

---

### Task 6: Close the mic on handoff

**Files:**
- Modify: `src/dotnet/App.Maui/Platforms/Android/MainActivity.cs`

Today `MainActivity.OnCreate` disposes the headless scope unconditionally (`MainActivity.cs:90`). With Task 3 in place a reply can be recording in that scope, and disposing it would cut a live transmission with no cue and no finalised entry.

- [ ] **Step 1: Stop a hot reply before disposing**

Change the disposal site so that, when the headless scope has a recording in progress, it goes through `WalkieTalkieReplyUI.StopReply()` first — the entry finalises and the cue plays — and only then disposes. Keep the existing behaviour when nothing is recording.

Do not make `OnCreate` wait on it: the UI must not be held up by a teardown. Sequence the stop and the disposal so the disposal happens after the stop completes, without blocking the activity.

- [ ] **Step 2: Report the resulting ordering**

Write out the exact sequence you implemented and what happens if the stop throws or never completes — the scope must still get disposed, or a wake session leaks for the process lifetime. State how you guaranteed that.

- [ ] **Step 3: Build and verify**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`. Platform-API verification per the Global Constraints.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/Android/MainActivity.cs
git commit -m "fix(maui): close a hot walkie reply before disposing the headless scope"
```

---

### Task 7: Final verification

**Files:**
- Modify: regenerated AOT sources
- Modify: `docs/superpowers/specs/2026-08-03-walkie-talkie-headset-button-design.md` (status line)

- [ ] **Step 1: Regenerate the AOT sources**

Run: `dotnet run --project src/dotnet/App.AotHelper -- -g 2>&1 | tail -5`

Then: `git status --short`

**Three generated files can change, not two:** `src/dotnet/Api/Module/ApiAotSource.g.cs`, `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs`, and `src/dotnet/UI.Blazor/Module/BlazorUIAotSource.g.cs`. E2's plan listed only the first two and the third was nearly left uncommitted. Commit whatever the generator produces; do not hand-edit any of them, and do not classify a hunk as unrelated drift without checking whether it names a type this sub-project introduced.

- [ ] **Step 2: Full build and TS/CSS**

```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
npm run build:Verify 2>&1 | tail -20
```
Expected: `0 Error(s)`; clean.

- [ ] **Step 3: Test sweep**

```bash
dotnet test tests/Users.UnitTests/Users.UnitTests.csproj 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj 2>&1 | tail -3
```
Expected: all PASS. Record the actual counts, not "all green".

- [ ] **Step 4: Update the spec status**

Change the `Status:` line to:

```
Status: Implemented (device verification pending — see plan Task 7)
```

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/specs/2026-08-03-walkie-talkie-headset-button-design.md \
        src/dotnet/Api/Module/ApiAotSource.g.cs \
        src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs \
        src/dotnet/UI.Blazor/Module/BlazorUIAotSource.g.cs
git commit -m "docs: mark walkie-talkie headset button + headless pipeline (E3) implemented"
```

- [ ] **Step 6: Write the device-verification list**

Nothing in Tasks 3–6 has been compiled, let alone run. Write these up concretely enough to execute without re-deriving context:

1. **Build `net10.0-android`.** First compile of everything in Tasks 3–6, on top of E2's platform code which has also never been compiled.
2. **Press the button *outside* the answer window** and confirm play/pause still works. This is the regression that matters most — the change intercepts a button that already had a job.
3. **Verify the load-bearing assumption**: with a PTT chat armed and nothing playing, does the FGS exist and does the media session receive the press? Planning decision 1 argues yes because PTT chats are force-listened, but that is reasoning, not evidence. If it is wrong, the design needs a session-activation step.
4. **Press inside the window** → a reply records and sends. Press again → it stops.
5. **The killed-process case**: wake, then press. This is the point of the sub-project.
6. **E2's shake and flip after a wake** — they should work for the first time.
7. **Open the app mid-reply** and confirm the handoff closes the mic with a cue rather than dropping silently.
8. **A long press** and a rapid double press, confirming neither opens-then-closes the mic.

## Reuse

**Existing abstractions reused (verified 2026-08-03):**

| Need | Existing abstraction | Where |
|---|---|---|
| Media-button delivery | `MediaSessionCompat` with `FlagHandlesMediaButtons` | `AndroidAudioWidgetForegroundService.cs:111` |
| Reply start/stop, target resolution, cold-start dead-man | `WalkieTalkieReplyUI` (E1) | `UI.Blazor.App/Services/` |
| Answer-window decision | `GestureActivationPolicy.ShouldSenseStartGestures` (E2) | `UI.Blazor.App/Services/Gestures/` |
| Pure-policy shape and its test style | `GestureActivationPolicy`, `HeadsetButtonPolicy` mirrors it | same folder |
| Armed chat set / recording state | `ChatAudioUI.GetPttChatIds`, `GetRecordingChatId` | `UI.Blazor.App/Services/ChatAudioUI.cs` |
| Settings record, storage, tab row | `UserWalkieTalkieSettings` + `PushToTalkSettings.razor` (E2) | `Api/Users/StoredSettings/`, `Components/Settings/` |
| Headless DI scope | `HeadlessBlazorScope` (B) | `App.Maui/Services/` |
| Reaching a live scope from a static component | `AndroidAudioWidget`'s instance pointer + headless fallback | `Platforms/Android/Audio/AndroidAudioWidget.cs:24-32` |
| Mic-capable FGS | `[Service(ForegroundServiceType = TypeMediaPlayback \| TypeMicrophone)]` | same file, line 14 |
| Fire-and-forget dispatch idiom | `BackgroundTask.Run(..., Log, "...", CancellationToken.None)` | `WalkieReplyToggle.razor`, `GestureUI.cs` |

No new abstraction is introduced beyond `HeadsetButtonPolicy` and `AppScopeAccessor`.

**Reusability of new components.** `HeadsetButtonPolicy` is pure and Android-free by construction, so it sits in `UI.Blazor.App` beside the other trigger policies — that is what makes it testable on a build machine, and E4 can reuse the same shape for iOS. `AppScopeAccessor` is MAUI-bound by definition and stays there; it is a generalisation of an existing ad-hoc fallback rather than a new concept. `StartScopedServices` belongs on `AppScopedServiceStarter` rather than in a new type, because it is a split of that class's existing responsibility and keeping it there makes the WebView/headless divide visible in one file.

## Risks

- **The media session may not exist when the window is open.** Planning decision 1 argues it does, via E2's force-listening. It is the single assumption the button path rests on and it is unverified. Device item 3.
- **Nothing in Tasks 3–6 compiles locally.** Two defects of exactly that shape landed in E2. The Global Constraints section exists for this.
- **`StartScopedServices` running headlessly is unprovable here.** Task 3 step 3 mitigates by tracing rather than testing; the real answer comes from device item 5.
- **The button intercepts an existing control.** A wrong policy or a wrong edge silently breaks play/pause for every user with earbuds, whether or not they use walkie-talkie. Device item 2 is the guard.
- **`AndroidAudioWidget`'s constructor calls `DispatchToBlazor`.** It is the most likely thing in the headless list to misbehave without a WebView, and it is now on a path where nothing renders.
