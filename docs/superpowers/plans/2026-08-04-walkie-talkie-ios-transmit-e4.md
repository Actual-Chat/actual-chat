# Walkie-Talkie iOS Apple PTT Transmit (Sub-Project E4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flip the Apple Push to Talk channel out of `ListenOnly` so the system Talk button appears on the Lock Screen, record a real chat voice message when iOS reports a transmission, and pre-roll microphone audio natively so a Talk press against a killed process keeps the words spoken while the app boots.

**Architecture:** The PTT delegate is a process singleton initialised from `FinishedLaunching`, long before any Blazor scope exists. `IosPushToTalk` gains transmit callbacks; `WalkieTalkieSession` gains a `HandleTransmit` sibling to `HandleWake` that boots the app, resolves a scope (WebView or headless), and calls `WalkieTalkieReplyUI.RequestReply` with an unbounded recency window. A new `PttPreRoll` captures audio from `DidActivateAudioSession` into a bounded, token-guarded `PreRollBuffer`; `AppleAudioCapture` drains it ahead of live frames. `AudioSession`'s `IsExternallyActivated` bool becomes a typed `AudioSessionOwner` so the recorder cannot configure a session the framework owns.

**Tech Stack:** .NET 11 / MAUI iOS (`net11.0-ios`), Apple `PushToTalk` + `AVFoundation`, ActualLab.Fusion compute services, Blazor Razor, xUnit + AwesomeAssertions.

Spec: `docs/superpowers/specs/2026-08-04-walkie-talkie-ios-transmit-e4-design.md`.

## Global Constraints

- **Branch:** stay on `feat/walkie-talkie-push`. Do not create a sub-project branch, and do not push — the user pushes.
- **Read `docs/CODING_STYLE.md` before writing any C#.** This project drops the `Async` suffix, forbids `///` XML docs on members, uses Allman braces for types/methods and K&R everywhere else, requires a blank line after control-flow statements, and prefers `Volatile.Read`/`Volatile.Write` over the `volatile` modifier.
- **Max line length 120; 4-space indent; LF endings.**
- **`.ConfigureAwait(false)`** in service-layer code; `ConfigureAwait(true)` in UI code only when instance state is touched after the await.
- **Build with the CI solution filter:** `dotnet build ActualChat.CI.slnf`. `App.Maui.csproj` is *not* in it.
- **`AudioSessionOwner` values, `PreRollBuffer` semantics and the recency-window sentinel are fixed by this plan** — later tasks depend on the exact names in Task 2, 3 and 4's Interfaces blocks.

### The platform-compile blind spot — read this before Tasks 6–9

`App.Maui.csproj` is outside `ActualChat.CI.slnf`, and `net11.0-ios` only builds on macOS. Nothing on this machine compiles `IosPushToTalk.cs`, `AudioSession.cs`, `AppleAudioCapture.cs`, or anything you add beside them. E2 and E3 produced five defects of exactly this shape.

Task 5 builds `scripts/csc-ios-probe.sh`, which **is proven to work** — it was validated while writing this plan. Run it after every edit in Tasks 6–9. It does *not* cover analyzers, the `Microsoft.Maui*` global-using block, linking, or anything on-device.

### Verified Apple API facts

These were confirmed by compiling probes against `ref/net11.0/Microsoft.iOS.dll` from `Microsoft.iOS.Ref.net11.0_26.2` version `26.2.11588-net11-p3`. Do not second-guess them; do not "fix" the spellings.

- `PTTransmissionMode.FullDuplex`, `.HalfDuplex`, `.ListenOnly` all exist.
- `PTChannelTransmitRequestSource` has **exactly three** members: `Unknown`, `UserRequest`, `HandsfreeButton`. There is no `PlayButton`, `Siri`, or `CarPlay`.
- `PTChannelManager.RequestBeginTransmitting(NSUuid)`, `.StopTransmitting(NSUuid)`, `.SetTransmissionMode(PTTransmissionMode, NSUuid, Action<NSError>)` and **`.SetChannelDescriptor(PTChannelDescriptor, NSUuid, Action<NSError>)`** all exist with those signatures.
- The delegate overrides are `DidBeginTransmitting(PTChannelManager, NSUuid, PTChannelTransmitRequestSource)`, `DidEndTransmitting(PTChannelManager, NSUuid, PTChannelTransmitRequestSource)`, and — note the unusual name — **`FailedToBeginTransmittingInChannel(PTChannelManager, NSUuid, NSError)`**. `DidFailToBeginTransmitting` does *not* exist.
- `PTChannelError.ChannelLimitReached`, `.CallActive`, `.TransmissionNotAllowed` exist.
- `AVAudioPcmBuffer.FloatChannelData` is `nint`, not an indexable array. Use the existing `AVAudioPcmBufferExt.AsReadOnlySpan()` helper instead of dereferencing it yourself.

### Deviations from the spec, decided during planning

- **The timer-based ownership watchdog is not implemented.** The spec asks for one that reverts `AudioSessionOwner` if no PTT callback arrives. A timer cannot distinguish a stuck flag from a legitimately long playback, so instead ownership reverts deterministically on `DidDeactivateAudioSession`, `DidEndTransmitting`, `DidLeaveChannel`, `OnWakeFailed` and `OnHeadlessTeardown` — five paths, all of which already exist. If a device shows the flag sticking anyway, add the timer then, with evidence about which path leaked.
- **The pre-roll capacity is pinned to the boot timeout at 8 s**, because `AppleAudioCapture`'s `outBuffer` holds `RecordingSampleRate * 10` samples and a longer pre-roll could not be resampled into it in one go.

---

## Task 1: The `IsPttTransmitEnabled` setting and its toggle

**Files:**
- Modify: `src/dotnet/Api/Users/StoredSettings/UserWalkieTalkieSettings.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkSettings.razor`
- Test: `tests/Users.UnitTests/SettingsRoundTripSerializationTest.cs` (existing, verify only)

**Interfaces:**
- Consumes: nothing.
- Produces: `UserWalkieTalkieSettings.IsPttTransmitEnabled` (`bool?`, read as `?? true`), used by Tasks 8 and 9.

- [ ] **Step 1: Add the setting**

In `UserWalkieTalkieSettings.cs`, after `IsHeadsetButtonEnabled`:

```csharp
    // Nullable, read as `?? true`: a blob predating this member reads it as default, not as `= true`.
    [DataMember, MemoryPackOrder(9), Key(9)]
    public bool? IsPttTransmitEnabled { get; init; }
```

- [ ] **Step 2: Verify the settings round-trip still passes**

`UserWalkieTalkieSettings` is already registered in `UserSettings.KeyToType` (`src/dotnet/Users.Service/UserSettings.cs:135`) — E3 fixed that. This step only confirms the new member serialises under both MemoryPack and MessagePack.

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~SettingsRoundTrip"`
Expected: PASS (216 passed, 1 skipped — the skip is the pre-existing `GenerateTestCases`).

- [ ] **Step 3: Gate the existing headset toggle to Android and add the iOS toggle**

In `PushToTalkSettings.razor`, replace the `Headset button` `TileItem` (currently at line 54, inside the `@if (HostInfo.HostKind.IsMauiApp())` block) with:

```razor
        @if (HostInfo.AppKind == AppKind.Android) {
            <TileItem Click="@OnToggleHeadsetButton">
                <Icon><i class="icon-headphones-fill text-2xl"></i></Icon>
                <Content>Headset button</Content>
                <Caption>Press the button on your earbuds to reply</Caption>
                <Right><Toggle IsChecked="@(m.Settings.IsHeadsetButtonEnabled ?? true)"/></Right>
            </TileItem>
        }
        @if (HostInfo.AppKind == AppKind.Ios) {
            <TileItem Click="@OnTogglePttTransmit">
                <Icon><i class="icon-talking text-2xl"></i></Icon>
                <Content>Lock Screen talk button</Content>
                <Caption>Reply from the Lock Screen without unlocking</Caption>
                <Right><Toggle IsChecked="@(m.Settings.IsPttTransmitEnabled ?? true)"/></Right>
            </TileItem>
        }
```

The headset toggle was previously rendered on every MAUI app including iOS, where it controls nothing — E3's oversight.

- [ ] **Step 4: Add the toggle handler**

In the `@code` block, immediately after `OnToggleHeadsetButton`:

```csharp
    private Task OnTogglePttTransmit()
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(
                x => x with { IsPttTransmitEnabled = !(x.IsPttTransmitEnabled ?? true) },
                CancellationToken.None);
```

`Toggle` has no `Click` parameter — the `TileItem`'s `Click` is what fires. Do not add one.

- [ ] **Step 5: Build**

Run: `dotnet build ActualChat.CI.slnf`
Expected: build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Users/StoredSettings/UserWalkieTalkieSettings.cs \
        src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkSettings.razor
git commit -m "feat(users): PTT transmit setting, and gate the headset toggle to Android"
```

---

## Task 2: An unbounded recency window for a deliberate press

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ReplyTargetResolver.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs:26-58`
- Test: `tests/Chat.UI.Blazor.UnitTests/ReplyTargetResolverTest.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ReplyTargetResolver.UnboundedRecencyWindow` (`TimeSpan`), and `WalkieTalkieReplyUI.RequestReply(TimeSpan recencyWindow, CancellationToken)` alongside the existing `RequestReply(CancellationToken)`. Task 8 calls the two-argument overload.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Chat.UI.Blazor.UnitTests/ReplyTargetResolverTest.cs`:

```csharp
    [Fact]
    public void AnUnboundedWindowResolvesArbitrarilyOldVoice()
    {
        // arrange
        var longAgo = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromDays(30) };

        // act
        var bounded = ReplyTargetResolver.Resolve([ChatA, ChatB], longAgo, null, T0, Window);
        var unbounded = ReplyTargetResolver.Resolve(
            [ChatA, ChatB], longAgo, null, T0, ReplyTargetResolver.UnboundedRecencyWindow);

        // assert
        bounded.Should().BeNull();
        unbounded.Should().Be(ChatA);
    }

    [Fact]
    public void AnUnboundedWindowStillPicksTheMostRecentChat()
    {
        // arrange
        var voices = new Dictionary<ChatId, Moment> {
            [ChatA] = T0 - TimeSpan.FromDays(30),
            [ChatB] = T0 - TimeSpan.FromDays(2),
        };

        // act
        var target = ReplyTargetResolver.Resolve(
            [ChatA, ChatB], voices, null, T0, ReplyTargetResolver.UnboundedRecencyWindow);

        // assert
        target.Should().Be(ChatB);
    }

    [Fact]
    public void AnUnboundedWindowStillReturnsNullWithNoArmedChats()
        => ReplyTargetResolver
            .Resolve([], new Dictionary<ChatId, Moment>(), null, T0, ReplyTargetResolver.UnboundedRecencyWindow)
            .Should().BeNull();
```

If `ChatB`, `T0` or `Window` are not already declared in that file with these names, reuse whatever it declares rather than renaming them.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~ReplyTargetResolverTest"`
Expected: FAIL — `ReplyTargetResolver` does not contain a definition for `UnboundedRecencyWindow`.

- [ ] **Step 3: Implement the sentinel**

Replace the body of `ReplyTargetResolver.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public static class ReplyTargetResolver
{
    public static readonly TimeSpan UnboundedRecencyWindow = TimeSpan.MaxValue;

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
        // Moment.EpochStart precedes every real stamp; now - TimeSpan.MaxValue would overflow.
        var bestAt = recencyWindow == UnboundedRecencyWindow ? Moment.EpochStart : now - recencyWindow;
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

- [ ] **Step 4: Add the `RequestReply` overload**

In `WalkieTalkieReplyUI.cs`, replace the `RequestReply` method with:

```csharp
    public Task RequestReply(CancellationToken cancellationToken)
        => RequestReply(Constants.Audio.WalkieTalkieReplyRecencyWindow, cancellationToken);

    public async Task RequestReply(TimeSpan recencyWindow, CancellationToken cancellationToken)
    {
        ChatAudioUI.Enable();
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
            return; // Already hot - idempotent

        var settings = await UserSettingsUI.UserWalkieTalkieSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var armed = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
        var focused = ChatUI.SelectedChatId.Value;
        var snapshot = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var target = ReplyTargetResolver.Resolve(
            armed, snapshot, focused, Clocks.ServerClock.Now, recencyWindow);
        if (target is not { } chatId) {
            if (settings.AreAudibleCuesEnabled)
                _ = TuneUI.Play(Tune.WalkieReplyNothingHeard);
            return;
        }

        if (!await AudioRecorder.MicrophonePermission.CheckOrRequest(cancellationToken).ConfigureAwait(false))
            return;

        cancellationToken.ThrowIfCancellationRequested();
        // Opening the mic lifts a soft "mute all" applied by the host, exactly like RecorderToggle.
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.Author?.Id is { } ownAuthorId)
            await LiveSessionUI.MutePeer(chatId, ownAuthorId, false, cancellationToken).ConfigureAwait(false);
        await ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk: true, idleDuration: settings.HotWindow)
            .ConfigureAwait(false);

        StartColdStartWatch(chatId);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~ReplyTargetResolverTest"`
Expected: PASS, all cases.

- [ ] **Step 6: Run the whole unit suite to confirm no caller broke**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj`
Expected: PASS (245+ tests, 0 failed).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ReplyTargetResolver.cs \
        src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs \
        tests/Chat.UI.Blazor.UnitTests/ReplyTargetResolverTest.cs
git commit -m "feat(walkie): unbounded reply target window for a deliberate press"
```

---

## Task 3: `AudioSessionOwner` and its pure transition policy

**Files:**
- Create: `src/dotnet/UI.Blazor/Services/AudioSessionOwnership.cs`
- Create: `tests/UI.Blazor.UnitTests/AudioSessionOwnershipTest.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AudioSessionOwner` (`App`, `PttPlayback`, `PttTransmit`), `AudioSessionRelease` (`Deactivated`, `TransmitEnded`, `ChannelLeft`), and `AudioSessionOwnership.OnActivated(bool)` / `.OnReleased(AudioSessionOwner, AudioSessionRelease)` / `.MayActivate(AudioSessionOwner)` / `.MayConfigure(AudioSessionOwner)`. Tasks 6 and 8 use all of them.

It lives in `UI.Blazor` — beside `AudioFocusMode` in `Services/AudioFocusUI.cs` — rather than in `App.Maui/MaciOS`, because `MaciOS` compiles in no test host. The concept is Apple-specific; the transitions are pure, and purity is what makes them verifiable here.

- [ ] **Step 1: Write the failing tests**

Create `tests/UI.Blazor.UnitTests/AudioSessionOwnershipTest.cs`:

```csharp
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class AudioSessionOwnershipTest
{
    [Fact]
    public void ActivationDuringTransmitTakesTransmitOwnership()
        => AudioSessionOwnership.OnActivated(true).Should().Be(AudioSessionOwner.PttTransmit);

    [Fact]
    public void ActivationWithoutTransmitTakesPlaybackOwnership()
        => AudioSessionOwnership.OnActivated(false).Should().Be(AudioSessionOwner.PttPlayback);

    [Theory]
    [InlineData(AudioSessionOwner.PttTransmit)]
    [InlineData(AudioSessionOwner.PttPlayback)]
    [InlineData(AudioSessionOwner.App)]
    public void DeactivationAlwaysReturnsOwnershipToTheApp(AudioSessionOwner current)
        => AudioSessionOwnership.OnReleased(current, AudioSessionRelease.Deactivated)
            .Should().Be(AudioSessionOwner.App);

    [Theory]
    [InlineData(AudioSessionOwner.PttTransmit)]
    [InlineData(AudioSessionOwner.PttPlayback)]
    [InlineData(AudioSessionOwner.App)]
    public void LeavingTheChannelAlwaysReturnsOwnershipToTheApp(AudioSessionOwner current)
        => AudioSessionOwnership.OnReleased(current, AudioSessionRelease.ChannelLeft)
            .Should().Be(AudioSessionOwner.App);

    [Fact]
    public void EndingATransmitReleasesOnlyTransmitOwnership()
    {
        // act
        var fromTransmit = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttTransmit, AudioSessionRelease.TransmitEnded);
        var fromPlayback = AudioSessionOwnership
            .OnReleased(AudioSessionOwner.PttPlayback, AudioSessionRelease.TransmitEnded);

        // assert
        fromTransmit.Should().Be(AudioSessionOwner.App);
        // Full duplex: a wake playback can still own the session after the transmit ends.
        fromPlayback.Should().Be(AudioSessionOwner.PttPlayback);
    }

    [Fact]
    public void OnlyTheAppMayActivateTheSession()
    {
        AudioSessionOwnership.MayActivate(AudioSessionOwner.App).Should().BeTrue();
        AudioSessionOwnership.MayActivate(AudioSessionOwner.PttPlayback).Should().BeFalse();
        AudioSessionOwnership.MayActivate(AudioSessionOwner.PttTransmit).Should().BeFalse();
    }

    [Fact]
    public void OnlyTransmitForbidsConfiguration()
    {
        // Playback keeps today's behaviour: the app may still set category and mode.
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.App).Should().BeTrue();
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.PttPlayback).Should().BeTrue();
        AudioSessionOwnership.MayConfigure(AudioSessionOwner.PttTransmit).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/UI.Blazor.UnitTests/UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~AudioSessionOwnershipTest"`
Expected: FAIL — the type or namespace `AudioSessionOwnership` could not be found.

- [ ] **Step 3: Implement**

Create `src/dotnet/UI.Blazor/Services/AudioSessionOwnership.cs`:

```csharp
namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Who currently owns AVAudioSession activation. Apple-only in practice, but the transition
/// rules live here — outside the platform projects — so they can be tested.
/// </summary>
public enum AudioSessionOwner
{
    App = 0,
    PttPlayback,
    PttTransmit,
}

public enum AudioSessionRelease
{
    Deactivated = 0,
    TransmitEnded,
    ChannelLeft,
}

public static class AudioSessionOwnership
{
    public static AudioSessionOwner OnActivated(bool isTransmitting)
        => isTransmitting ? AudioSessionOwner.PttTransmit : AudioSessionOwner.PttPlayback;

    public static AudioSessionOwner OnReleased(AudioSessionOwner current, AudioSessionRelease release)
        => release switch {
            AudioSessionRelease.Deactivated => AudioSessionOwner.App,
            AudioSessionRelease.ChannelLeft => AudioSessionOwner.App,
            // Full duplex: ending a transmit must not steal the session from a running playback.
            AudioSessionRelease.TransmitEnded when current == AudioSessionOwner.PttTransmit
                => AudioSessionOwner.App,
            _ => current,
        };

    public static bool MayActivate(AudioSessionOwner owner)
        => owner == AudioSessionOwner.App;

    public static bool MayConfigure(AudioSessionOwner owner)
        => owner != AudioSessionOwner.PttTransmit;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/UI.Blazor.UnitTests/UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~AudioSessionOwnershipTest"`
Expected: PASS, all 11 cases.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor/Services/AudioSessionOwnership.cs \
        tests/UI.Blazor.UnitTests/AudioSessionOwnershipTest.cs
git commit -m "feat(audio): typed AVAudioSession ownership with a pure transition policy"
```

---

## Task 4: `PreRollBuffer`

**Files:**
- Create: `src/dotnet/Core.Audio/PreRollBuffer.cs`
- Create: `tests/Core.Audio.UnitTests/PreRollBufferTest.cs`
- Modify: `src/dotnet/Api/Constants.Audio.cs:78` (append after `WalkieTalkieReplyRecencyWindow`)

**Interfaces:**
- Consumes: nothing.
- Produces: `ActualChat.Audio.PreRollBuffer` with `Token`, `SampleRate`, `Count`, `Duration`, `IsOverflowed`, `TryAppend(ReadOnlySpan<float>)`, `TryDrain(long token, int minSampleCount)`. Task 7 constructs and drains it. Also the three new constants below, used by Tasks 7 and 8.

- [ ] **Step 1: Add the constants**

In `src/dotnet/Api/Constants.Audio.cs`, after `WalkieTalkieReplyRecencyWindow`:

```csharp
        // Apple PTT transmit: the framework chimes when it activates the session, not when our
        // recorder exists, so audio is captured natively across the gap. Capacity must stay <=
        // 10 s, which is AppleAudioCapture's outBuffer size at RecordingSampleRate.
        public static readonly TimeSpan WalkieTalkiePttTransmitStartupTimeout = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan WalkieTalkiePreRollCapacity = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan WalkieTalkiePreRollMinDuration = TimeSpan.FromSeconds(0.4);
        public static readonly TimeSpan WalkieTalkiePreRollFlushDelay = TimeSpan.FromSeconds(1.5);
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Core.Audio.UnitTests/PreRollBufferTest.cs`:

```csharp
using ActualChat.Audio;

namespace ActualChat.Core.Audio.UnitTests;

public class PreRollBufferTest
{
    private const int SampleRate = 48_000;

    [Fact]
    public void AppendedSamplesDrainInOrder()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);

        // act
        buffer.TryAppend([1f, 2f, 3f]).Should().BeTrue();
        buffer.TryAppend([4f, 5f]).Should().BeTrue();
        var drained = buffer.TryDrain(7, 1);

        // assert
        drained.Should().Equal([1f, 2f, 3f, 4f, 5f]);
    }

    [Fact]
    public void DrainingWithAForeignTokenReturnsNothing()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);
        buffer.TryAppend([1f, 2f, 3f]);

        // act
        var drained = buffer.TryDrain(8, 1);

        // assert
        drained.Should().BeNull();
        buffer.Count.Should().Be(3);
    }

    [Fact]
    public void ASecondDrainReturnsNothing()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);
        buffer.TryAppend([1f, 2f, 3f]);

        // act
        var first = buffer.TryDrain(7, 1);
        var second = buffer.TryDrain(7, 1);

        // assert
        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public void OverflowVoidsTheWholeBuffer()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 4);
        buffer.TryAppend([1f, 2f]);

        // act
        var isAppended = buffer.TryAppend([3f, 4f, 5f]);

        // assert
        // A fragment whose start is missing is worse than nothing: the boot budget was blown.
        isAppended.Should().BeFalse();
        buffer.IsOverflowed.Should().BeTrue();
        buffer.Count.Should().Be(0);
        buffer.TryDrain(7, 1).Should().BeNull();
    }

    [Fact]
    public void AppendingAfterOverflowKeepsFailing()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 2);
        buffer.TryAppend([1f, 2f, 3f]);

        // act
        var isAppended = buffer.TryAppend([1f]);

        // assert
        isAppended.Should().BeFalse();
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void TooLittleAudioIsNotDrained()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);
        buffer.TryAppend([1f, 2f]);

        // act
        var drained = buffer.TryDrain(7, 3);

        // assert
        drained.Should().BeNull();
        // Not consumed: more audio may still arrive before the recorder exists.
        buffer.TryAppend([3f]).Should().BeTrue();
        buffer.TryDrain(7, 3).Should().Equal([1f, 2f, 3f]);
    }

    [Fact]
    public void DurationFollowsTheSampleRate()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, SampleRate);

        // act
        buffer.TryAppend(new float[SampleRate / 2]);

        // assert
        buffer.Duration.Should().Be(TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void AnEmptyAppendIsANoOp()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 4);

        // act
        var isAppended = buffer.TryAppend([]);

        // assert
        isAppended.Should().BeTrue();
        buffer.IsOverflowed.Should().BeFalse();
        buffer.Count.Should().Be(0);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Core.Audio.UnitTests/Core.Audio.UnitTests.csproj --filter "FullyQualifiedName~PreRollBufferTest"`
Expected: FAIL — the type or namespace `PreRollBuffer` could not be found.

- [ ] **Step 4: Implement**

Create `src/dotnet/Core.Audio/PreRollBuffer.cs`:

```csharp
namespace ActualChat.Audio;

/// <summary>
/// A bounded, one-shot capture buffer that fills from a native tap before the app's recorder
/// exists and is drained once. The token ties its content to the capture that armed it, so a
/// buffer abandoned by a failed capture can never be drained by an unrelated later recording.
/// </summary>
public sealed class PreRollBuffer
{
    private readonly Lock _lock = new();
    private readonly float[] _samples;
    private int _count;
    private bool _isOverflowed;
    private bool _isDrained;

    public long Token { get; }
    public int SampleRate { get; }
    public int Capacity => _samples.Length;

    public int Count {
        get {
            lock (_lock)
                return _count;
        }
    }

    public bool IsOverflowed {
        get {
            lock (_lock)
                return _isOverflowed;
        }
    }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Count / SampleRate);

    public PreRollBuffer(long token, int sampleRate, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Token = token;
        SampleRate = sampleRate;
        _samples = new float[capacity];
    }

    public bool TryAppend(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return true;

        lock (_lock) {
            if (_isDrained || _isOverflowed)
                return false;

            if (samples.Length > _samples.Length - _count) {
                // The boot budget was blown. A fragment whose start is missing would be sent as
                // if it were the whole reply, so the buffer is voided rather than truncated.
                _isOverflowed = true;
                _count = 0;
                return false;
            }

            samples.CopyTo(_samples.AsSpan(_count));
            _count += samples.Length;
            return true;
        }
    }

    public float[]? TryDrain(long token, int minSampleCount)
    {
        lock (_lock) {
            if (_isDrained || _isOverflowed || token != Token || _count < minSampleCount)
                return null;

            _isDrained = true;
            return _samples.AsSpan(0, _count).ToArray();
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Core.Audio.UnitTests/Core.Audio.UnitTests.csproj --filter "FullyQualifiedName~PreRollBufferTest"`
Expected: PASS, all 8 cases.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Core.Audio/PreRollBuffer.cs tests/Core.Audio.UnitTests/PreRollBufferTest.cs \
        src/dotnet/Api/Constants.Audio.cs
git commit -m "feat(audio): bounded token-guarded pre-roll buffer"
```

---

## Task 5: `scripts/csc-ios-probe.sh`

**Files:**
- Create: `scripts/csc-ios-probe.sh`

**Interfaces:**
- Consumes: nothing.
- Produces: a working probe that Tasks 6–9 run after each edit. Invoke as `scripts/csc-ios-probe.sh <path-to-ios-source> [<git-ref-to-diff-against>]`.

This technique is **already validated**: the ref pack downloads on Linux and `csc` compiles `PushToTalk`/`AVFoundation`/`UIKit` code against it. Do not redesign it; port the Android probe.

- [ ] **Step 1: Write the script**

Create `scripts/csc-ios-probe.sh`:

```bash
#!/usr/bin/env bash
# Compiles a single App.Maui iOS source file with `csc` alone, against the real Microsoft.iOS
# reference assembly, plus the freshly-built ActualChat.*/ActualLab.* closure from a test bin folder.
#
# Why this exists: App.Maui.csproj is outside ActualChat.CI.slnf AND net11.0-ios only builds on
# macOS, so `dotnet build` cannot touch iOS code here at all. This is the only thing on this
# machine that has ever compiled it. Sibling of scripts/csc-android-probe.sh.
#
# What it covers: the target file compiles against a real Microsoft.iOS ref assembly, so wrong
# API names, wrong overloads and wrong enum members are caught. It found three during E4 planning
# (FailedToBeginTransmittingInChannel's name, PTChannelTransmitRequestSource's member set, and
# AVAudioPcmBuffer.FloatChannelData being an nint rather than an indexable array).
#
# What it does NOT cover: Roslyn analyzers (CA1416 platform checks stay silent under bare csc),
# the Microsoft.Maui/Microsoft.Maui.Controls global-using block that only applies inside App.Maui
# proper, the native linker, and anything on-device.
#
# What it requires: a prior `dotnet build ActualChat.CI.slnf` (or a test run) for TESTBIN, and
# network access on first run to fetch the iOS ref pack into tmp/.
#
# The stub set below is hand-tuned per target file. Pointing it at a new file usually needs edits.
#
# Usage: scripts/csc-ios-probe.sh <source-file> [<git-ref-to-diff-against>]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC=${1:?usage: csc-ios-probe.sh <source-file> [<baseline-git-ref>]}
BASELINE_REF=${2:-}
[[ -f "$SRC" ]] || { echo "No such file: $SRC"; exit 2; }
REL=${SRC#"$REPO"/}

SDK=$(ls -d "$HOME"/.dotnet/sdk/11.0.* | tail -1)
CSC="$SDK/Roslyn/bincore/csc.dll"
BCLREF=$(ls -d "$HOME"/.dotnet/packs/Microsoft.NETCore.App.Ref/11.0.*/ref/net11.0 | tail -1)
TESTBIN=$REPO/artifacts/tests/bin/Chat.UI.Blazor.UnitTests/debug

WORK=${WORK:-$REPO/tmp/csc-ios-probe}
mkdir -p "$WORK"; cd "$WORK"

# --- the iOS ref pack: a plain NuGet package, so it restores on Linux even though the
# --- net11.0-ios TFM itself only builds on macOS ---
IOSPKG=microsoft.ios.ref.net11.0_26.2
IOSVER=26.2.11588-net11-p3
IOSREF=$WORK/iosref/ref/net11.0/Microsoft.iOS.dll
if [[ ! -f "$IOSREF" ]]; then
    echo "Fetching $IOSPKG/$IOSVER ..."
    mkdir -p iosref
    curl -sSL --max-time 300 -o iosref/pkg.nupkg \
        "https://api.nuget.org/v3-flatcontainer/$IOSPKG/$IOSVER/$IOSPKG.$IOSVER.nupkg"
    python3 -c "import zipfile; zipfile.ZipFile('iosref/pkg.nupkg').extract('ref/net11.0/Microsoft.iOS.dll','iosref')"
fi

# --- reference set: the ref packs win over the test bin on simple-name collisions ---
: > refs.rsp
declare -A seen
add() { local n; n=$(basename "$1" .dll); [[ -n "${seen[$n]:-}" ]] || { seen[$n]=1; echo "-r:$1" >> refs.rsp; }; }
for f in "$BCLREF"/*.dll; do add "$f"; done
add "$IOSREF"
for f in "$TESTBIN"/*.dll; do add "$f"; done

# --- the project's global usings (root Directory.Build.props + App.Maui/Directory.Build.props) ---
cat > GlobalUsings.cs <<'EOF'
global using System;
global using System.Collections;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.Collections.Immutable;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Globalization;
global using System.Linq;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Runtime.Serialization;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Channels;
global using System.Threading.Tasks;
global using static System.FormattableString;
global using ActualChat;
global using ActualChat.Collections;
global using ActualChat.DependencyInjection;
global using ActualChat.Diff;
global using ActualChat.IO;
global using ActualChat.Mathematics;
global using ActualChat.Chat;
global using ActualChat.Media;
global using ActualChat.Users;
global using ActualChat.Performance;
global using ActualChat.Serialization;
global using ActualChat.Validation;
global using ActualLab;
global using ActualLab.Api;
global using ActualLab.Async;
global using ActualLab.Channels;
global using ActualLab.Collections;
global using ActualLab.Compliance;
global using ActualLab.DependencyInjection;
global using ActualLab.Mathematics;
global using ActualLab.Serialization;
global using ActualLab.OS;
global using ActualLab.Reflection;
global using ActualLab.Text;
global using ActualLab.Time;
global using ActualLab.Trimming;
global using ActualLab.Fusion;
global using ActualLab.Fusion.Operations;
global using ActualLab.CommandR;
global using ActualLab.CommandR.Configuration;
global using ActualLab.CommandR.Commands;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.DependencyInjection;
global using static ActualChat.App.Maui.AppServicesAccessor;
EOF

# --- stubs, ONLY for App.Maui-local types that live in no built assembly ---
cat > Stubs.cs <<'EOF'
namespace ActualChat.App.Maui
{
    public class AppServicesAccessor
    {
        public static bool TryGetScopedServices(out IServiceProvider services)
        {
            services = null!;
            return false;
        }
        public static Task<T> DispatchToMainThread<T>(Func<T> func) => Task.FromResult(func());
        public static Task DispatchToMainThread(Action action) { action(); return Task.CompletedTask; }
    }
    public static class BlazorWebViewApp
    {
        public static void EnsureStarted() { }
        public static Task<IServiceProvider> WhenAppReady => Task.FromResult<IServiceProvider>(null!);
    }
}
namespace ActualChat.App.Maui.Services
{
    public static class AppScopeAccessor
    {
        public static IServiceProvider? Current => null;
    }
}
EOF

compile() { # $1 = source file, $2 = out name, $3 = log
    dotnet exec "$CSC" -nostdlib -noconfig -target:library -nullable:enable \
        -langversion:preview -define:IOS -define:__IOS__ -unsafe \
        @refs.rsp GlobalUsings.cs Stubs.cs -out:"$2" "$1" > "$3" 2>&1
}

cp "$SRC" Current.cs
compile Current.cs current.dll current.log && echo "CURRENT: exit 0" || { echo "CURRENT: FAILED"; cat current.log; exit 1; }
echo "CURRENT errors: $(grep -c ' error ' current.log || true)"
echo "CURRENT warnings:"; grep -o 'warning CS[0-9]*' current.log | sort | uniq -c

if [[ -n "$BASELINE_REF" ]]; then
    git -C "$REPO" show "$BASELINE_REF:$REL" > Baseline.cs
    compile Baseline.cs baseline.dll baseline.log && echo "BASELINE($BASELINE_REF): exit 0" || { echo "BASELINE: FAILED"; cat baseline.log; exit 1; }
    echo "BASELINE errors: $(grep -c ' error ' baseline.log || true)"
fi
```

- [ ] **Step 2: Make it executable and prove it works on an untouched file**

```bash
chmod +x scripts/csc-ios-probe.sh
dotnet build ActualChat.CI.slnf
scripts/csc-ios-probe.sh src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs
```

Expected: `CURRENT: exit 0`. If it reports missing App.Maui-local types, add them to the `Stubs.cs` heredoc — that is the intended way to extend the probe, not a failure of it.

- [ ] **Step 3: Commit**

```bash
git add scripts/csc-ios-probe.sh
git commit -m "chore(scripts): csc probe that compiles iOS sources against Microsoft.iOS refs"
```

---

## Task 6: Wire `AudioSessionOwner` through the Apple audio session

**Files:**
- Modify: `src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs`
- Modify: `src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs`

**Interfaces:**
- Consumes: `AudioSessionOwner`, `AudioSessionRelease`, `AudioSessionOwnership` from Task 3.
- Produces: `AudioSession.Owner` (get), `AudioSession.SetOwner(AudioSessionOwner)`, `AudioSession.ReleaseOwner(AudioSessionRelease)`. Task 8 calls all three. `AudioSession.IsExternallyActivated` is **removed** — no other file may reference it afterwards.

- [ ] **Step 1: Replace the flag with typed ownership**

In `AudioSession.cs`, replace the `IsExternallyActivated` field with:

```csharp
    private static int _owner;

    public static AudioSessionOwner Owner => (AudioSessionOwner)Volatile.Read(ref _owner);

    public static void SetOwner(AudioSessionOwner owner)
        => Volatile.Write(ref _owner, (int)owner);

    public static void ReleaseOwner(AudioSessionRelease release)
        => Volatile.Write(ref _owner, (int)AudioSessionOwnership.OnReleased(Owner, release));
```

Delete the old comment above the field; the enum's own doc carries it now.

- [ ] **Step 2: Apply ownership in the three session paths**

In `DisposeAsync`, replace `if (IsExternallyActivated) return;` with:

```csharp
                    if (!AudioSessionOwnership.MayActivate(Owner))
                        return;
```

Replace `ReactivateUnsafe`'s opening with:

```csharp
    private void ReactivateUnsafe(AudioFocusMode mode)
    {
        var session = AVAudioSession.SharedInstance();
        var owner = Owner;
        // Under a PTT transmit the framework owns category and mode too - configuring underneath
        // it is what the typed owner exists to prevent.
        if (AudioSessionOwnership.MayConfigure(owner))
            ConfigureUnsafe(session, mode);
        if (!AudioSessionOwnership.MayActivate(owner))
            return;
```

Replace `ReconfigureUnsafe`'s opening with:

```csharp
    private void ReconfigureUnsafe(AudioFocusMode minMode)
    {
        var session = AVAudioSession.SharedInstance();
        var owner = Owner;
        if (!AudioSessionOwnership.MayActivate(owner)) {
            if (AudioSessionOwnership.MayConfigure(owner))
                ConfigureUnsafe(session, minMode);
            return;
        }
```

- [ ] **Step 3: Update the PTT receive path to the new API**

In `IosPushToTalk.cs`:
- `OnAudioSessionActivated`: replace `AudioSession.IsExternallyActivated = true;` with `AudioSession.SetOwner(AudioSessionOwner.PttPlayback);`
- `DidDeactivateAudioSession`: replace `AudioSession.IsExternallyActivated = false;` with `AudioSession.ReleaseOwner(AudioSessionRelease.Deactivated);`
- `DidLeaveChannel`: replace `AudioSession.IsExternallyActivated = false;` with `AudioSession.ReleaseOwner(AudioSessionRelease.ChannelLeft);`
- `IosPlatform.OnWakeFailed` and `IosPlatform.OnHeadlessTeardown`: add `AudioSession.ReleaseOwner(AudioSessionRelease.ChannelLeft);` before the existing `ClearActiveParticipant()` call.

Add `using ActualChat.UI.Blazor.Services;` to `IosPushToTalk.cs` if it is not already there.

- [ ] **Step 4: Confirm nothing else referenced the old flag**

Run: `grep -rn "IsExternallyActivated" src/dotnet`
Expected: no output.

- [ ] **Step 5: Probe both files**

```bash
scripts/csc-ios-probe.sh src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs
scripts/csc-ios-probe.sh src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs
```
Expected: `CURRENT: exit 0` for both. Extend `Stubs.cs` in the probe if App.Maui-local types are missing.

- [ ] **Step 6: Build and commit**

```bash
dotnet build ActualChat.CI.slnf
git add src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs \
        src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs
git commit -m "refactor(maui): typed AVAudioSession ownership replaces the external-activation flag"
```

---

## Task 7: `PttPreRoll` and the capture drain

**Files:**
- Create: `src/dotnet/App.Maui/MaciOS/Audio/PttPreRoll.cs`
- Modify: `src/dotnet/App.Maui/MaciOS/Audio/AppleAudioCapture.cs:20-40`

**Interfaces:**
- Consumes: `PreRollBuffer` and the constants from Task 4.
- Produces: `PttPreRoll.Start()` returning a `long` token (`0` on failure), `PttPreRoll.Discard(long token)`, `PttPreRoll.TryTake()` returning `PreRollTake?`. Task 8 calls `Start` and `Discard`.

`PttPreRoll` lives in `MaciOS/Audio/` rather than `Platforms/iOS/` so `AppleAudioCapture` — which Mac Catalyst also compiles — can reference it without conditional compilation. Nothing on Catalyst ever arms it, so `TryTake` simply returns null there.

- [ ] **Step 1: Implement `PttPreRoll`**

Create `src/dotnet/App.Maui/MaciOS/Audio/PttPreRoll.cs`:

```csharp
using ActualChat.Audio;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Captures microphone audio from the moment Apple Push to Talk activates the audio session until
/// the app's own recorder exists, so a transmit from a killed process keeps its first words.
/// </summary>
public static class PttPreRoll
{
    private static readonly Lock Lock = new();
    private static long _lastToken;
    private static AVAudioEngine? _engine;
    private static AVAudioFormat? _format;
    private static PreRollBuffer? _buffer;
    private static ILogger Log => field ??= StaticLog.For(typeof(PttPreRoll));

    public static long Start()
    {
        lock (Lock) {
            StopUnsafe();
            var token = ++_lastToken;
            try {
                var engine = new AVAudioEngine();
                var input = engine.InputNode;
                var format = input.GetBusOutputFormat(0);
                var sampleRate = (int)format.SampleRate;
                if (sampleRate <= 0) {
                    Log.LogWarning("Pre-roll: the input node reports no sample rate");
                    engine.Dispose();
                    return 0;
                }

                var capacity = (int)(sampleRate * Constants.Audio.WalkieTalkiePreRollCapacity.TotalSeconds);
                var buffer = new PreRollBuffer(token, sampleRate, capacity);
                var frameLength = (uint)(sampleRate / 1000 * Constants.Audio.OpusFrameDurationMs);
                input.InstallTapOnBus(0, frameLength, format, (pcm, _) => buffer.TryAppend(pcm.AsReadOnlySpan()));
                engine.Prepare();
                engine.StartAndReturnError(out var error);
                if (error is not null) {
                    Log.LogWarning("Pre-roll engine didn't start: {Error}", error.LocalizedDescription);
                    input.RemoveTapOnBus(0);
                    engine.Dispose();
                    return 0;
                }

                (_engine, _format, _buffer) = (engine, format, buffer);
                Log.LogInformation("Pre-roll capture started ({SampleRate} Hz)", sampleRate);
                return token;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Pre-roll capture failed to start");
                return 0;
            }
        }
    }

    public static void Discard(long token)
    {
        lock (Lock) {
            if (_buffer is not { } buffer || buffer.Token != token)
                return;

            StopUnsafe();
        }
    }

    public static PreRollTake? TryTake()
    {
        lock (Lock) {
            if (_buffer is not { } buffer || _format is not { } format)
                return null;

            var minSampleCount =
                (int)(buffer.SampleRate * Constants.Audio.WalkieTalkiePreRollMinDuration.TotalSeconds);
            var samples = buffer.TryDrain(buffer.Token, minSampleCount);
            // Stopping here, before the caller starts AudioEngines.Recording, is the point: two
            // AVAudioEngine instances must never hold the hardware input node at once.
            StopUnsafe();
            return samples is null ? null : new PreRollTake(samples, format);
        }
    }

    // Private methods

    private static void StopUnsafe()
    {
        if (_engine is { } engine) {
            try {
                engine.InputNode.RemoveTapOnBus(0);
                engine.Stop();
                engine.Dispose();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Pre-roll capture failed to stop cleanly");
            }
        }
        (_engine, _format, _buffer) = (null, null, null);
    }
}

public sealed record PreRollTake(float[] Samples, AVAudioFormat Format);
```

- [ ] **Step 2: Drain the pre-roll in `AppleAudioCapture`**

In `AppleAudioCapture.cs`, inside `CaptureInternal`, insert the drain immediately after the `resampler` is created and **before** `engine.Input.SetVoiceProcessingEnabled(true)`:

```csharp
        var preRoll = PttPreRoll.TryTake();
        if (preRoll is { } take) {
            // Only a format match is safe: a route change between arming and draining would make
            // the buffered samples the wrong rate for this resampler.
            if (take.Format.SampleRate.Equals(hwFormat.SampleRate)
                && take.Format.ChannelCount == hwFormat.ChannelCount) {
                using var preRollBuffer = new AVAudioPcmBuffer(hwFormat, (uint)take.Samples.Length);
                preRollBuffer.SetData(take.Samples);
                resampler.Transform(preRollBuffer, outBuffer);
                Log.LogInformation("Drained {Count} pre-roll samples", take.Samples.Length);
            }
            else
                Log.LogWarning("Dropped the pre-roll: format changed since it was captured");
        }
```

`SetData` takes a `Span<float>`, and `take.Samples` is a `float[]`, which converts implicitly.

- [ ] **Step 3: Probe both files**

```bash
scripts/csc-ios-probe.sh src/dotnet/App.Maui/MaciOS/Audio/PttPreRoll.cs
scripts/csc-ios-probe.sh src/dotnet/App.Maui/MaciOS/Audio/AppleAudioCapture.cs
```
Expected: `CURRENT: exit 0` for both.

- [ ] **Step 4: Re-run the pre-roll unit tests**

Run: `dotnet test tests/Core.Audio.UnitTests/Core.Audio.UnitTests.csproj --filter "FullyQualifiedName~PreRollBufferTest"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/MaciOS/Audio/PttPreRoll.cs \
        src/dotnet/App.Maui/MaciOS/Audio/AppleAudioCapture.cs
git commit -m "feat(maui): native pre-roll capture across the PTT transmit cold start"
```

---

## Task 8: The transmit path

**Files:**
- Modify: `src/dotnet/App.Maui/Services/WalkieTalkieSession.cs`
- Modify: `src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs`

**Interfaces:**
- Consumes: everything from Tasks 2, 3, 4, 6 and 7.
- Produces: `WalkieTalkieSession.HandleTransmit(WalkieTalkiePlatform)` returning `Task<bool>` (true when a reply is recording). Task 9 does not use it; this is the last consumer.

- [ ] **Step 1: Extract scope resolution and fix the teardown watcher**

In `WalkieTalkieSession.cs`, replace the inline scope block in `HandleWake` with a call, and add the private helper. `HandleWake`'s body becomes:

```csharp
    public static async Task HandleWake(
        ChatId chatId, Moment startedAt, bool isForeground, WalkieTalkiePlatform platform)
    {
        try {
            var app = await BlazorWebViewApp.WhenAppReady.WaitAsync(StartupTimeout).ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            await sessionResolver.SessionTask.WaitAsync(StartupTimeout).ConfigureAwait(false);

            var (scopedServices, isHeadless) = ResolveScope();
            await StartPlayback(scopedServices, chatId, startedAt, isForeground, isHeadless, platform)
                .ConfigureAwait(false);
            if (isHeadless)
                EnsureTeardownWatcher(platform);
        }
        catch (Exception e) {
            Log.LogError(e, "Walkie-talkie wake failed for chat #{ChatId}", chatId);
            platform.OnWakeFailed(chatId);
            await StopAndDisposeCurrent("wake failed").ConfigureAwait(false);
        }
    }
```

Add to the private section, above `StartPlayback`:

```csharp
    private static (IServiceProvider Services, bool IsHeadless) ResolveScope()
    {
        if (AppServicesAccessor.TryGetScopedServices(out var liveScope))
            return (liveScope, false);
        if (HeadlessBlazorScope.GetOrCreate() is { } headless)
            return (headless.Services, true);
        if (AppServicesAccessor.TryGetScopedServices(out liveScope!))
            // Lost the creation race to a just-published WebView scope
            return (liveScope, false);

        throw StandardError.Internal("No service scope is available.");
    }
```

In `WatchTeardown`, insert a recording check before the listening/replay check:

```csharp
                var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
                // A transmit into a headless scope with nothing playing looks exactly like an idle
                // session - without this the watcher would dispose the scope under an open mic.
                if (chatAudioUI.IsRecording()) {
                    idleChecks = 0;
                    continue;
                }

                var listeningChatIds = await chatAudioUI.GetListeningChatIds().ConfigureAwait(false);
```

- [ ] **Step 2: Add `HandleTransmit`**

In `WalkieTalkieSession.cs`, immediately after `HandleWake`:

```csharp
    public static async Task<bool> HandleTransmit(WalkieTalkiePlatform platform)
    {
        var timeout = Constants.Audio.WalkieTalkiePttTransmitStartupTimeout;
        try {
            var app = await BlazorWebViewApp.WhenAppReady.WaitAsync(timeout).ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            await sessionResolver.SessionTask.WaitAsync(timeout).ConfigureAwait(false);

            var (scopedServices, isHeadless) = ResolveScope();
            var hub = scopedServices.GetRequiredService<AppUIHub>();
            if (isHeadless)
                hub.ChatAudioUI.IsWalkieTalkieHeadless = true;
            if (hub.GestureUI.IsPracticeMode)
                return false; // Rehearsing in Settings must never transmit

            // RequestReply is idempotent, so a gesture-opened mic would make it report success and
            // the transmission would later close a reply it never started.
            if (await hub.ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
                return false;

            // The mic-permission check cannot show a prompt from a locked screen, so it must not be
            // allowed to outlive the boot budget.
            using var cts = new CancellationTokenSource(timeout);
            await hub.WalkieTalkieReplyUI
                .RequestReply(ReplyTargetResolver.UnboundedRecencyWindow, cts.Token)
                .ConfigureAwait(false);
            var isRecording = await hub.ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null;
            if (isRecording && isHeadless)
                EnsureTeardownWatcher(platform);

            return isRecording;
        }
        catch (Exception e) {
            Log.LogError(e, "Walkie-talkie transmit failed");
            return false;
        }
    }
```

Add `using ActualChat.UI.Blazor.App.Services;` if it is not already present (it is — `WalkieTalkieSession.cs` already imports it).

- [ ] **Step 3: Add the transmission state to `IosPushToTalk`**

In `IosPushToTalk.cs`, add beside the existing static fields:

```csharp
    private static Transmission? _transmission;
```

and add the nested type in the `// Nested types` section, right after `PendingWake`:

```csharp
    private sealed class Transmission
    {
        public long PreRollToken { get; set; }
        public bool IsMicOwned { get; set; }
        public bool IsEndPending { get; set; }
    }
```

All three members are read and written under `Lock`. Identity is by reference — every path compares with `ReferenceEquals(_transmission, transmission)`, so the type needs no id.

- [ ] **Step 4: Wire the transmit callbacks**

Replace the empty `DidBeginTransmitting` / `DidEndTransmitting` in `ManagerDelegate`, and add the failure callback:

```csharp
        public override void DidBeginTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        {
            Log.LogInformation("PTT transmit began ({Source})", source);
            OnTransmitBegan();
        }

        public override void DidEndTransmitting(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelTransmitRequestSource source)
        {
            Log.LogInformation("PTT transmit ended ({Source})", source);
            OnTransmitEnded();
        }

        public override void FailedToBeginTransmittingInChannel(
            PTChannelManager channelManager, NSUuid channelUuid, NSError error)
        {
            Log.LogWarning("PTT transmit was refused: {Error}", error.LocalizedDescription);
            OnTransmitEnded();
        }
```

In `DidLeaveChannel`, add `OnTransmitEnded();` as the first statement. Leaving the channel — which is what turning the setting off does — must close a hot reply and discard the buffer, not just release session ownership.

- [ ] **Step 5: Implement the transmit lifecycle**

Add to the `// Private methods` section of `IosPushToTalk`, above `OnAudioSessionActivated`:

```csharp
    private static void OnTransmitBegan()
    {
        BlazorWebViewApp.EnsureStarted();
        lock (Lock)
            _transmission = new Transmission();
    }

    private static void StartTransmitReply(Transmission transmission)
    {
        var preRollToken = PttPreRoll.Start();
        lock (Lock) {
            if (!ReferenceEquals(_transmission, transmission))
                return;

            transmission.PreRollToken = preRollToken;
        }

        _ = BackgroundTask.Run(async () => {
            var isRecording = await WalkieTalkieSession.HandleTransmit(IosPlatform.Instance)
                .ConfigureAwait(false);
            bool isEndPending;
            lock (Lock) {
                if (!ReferenceEquals(_transmission, transmission))
                    return;

                transmission.IsMicOwned = isRecording;
                isEndPending = transmission.IsEndPending;
            }
            if (!isRecording) {
                PttPreRoll.Discard(preRollToken);
                StopTransmitting();
                return;
            }

            if (isEndPending) {
                // The user let go before the app finished booting. The buffered words are real
                // speech, so the reply still goes out - it just holds open long enough for
                // AppleAudioCapture to drain the pre-roll into the encoder.
                await Task.Delay(Constants.Audio.WalkieTalkiePreRollFlushDelay).ConfigureAwait(false);
                await StopTransmitReply(transmission).ConfigureAwait(false);
            }
        }, Log, "PTT transmit reply failed", CancellationToken.None);
    }

    private static void OnTransmitEnded()
    {
        Transmission? transmission;
        lock (Lock) {
            transmission = _transmission;
            if (transmission is null)
                return;

            if (!transmission.IsMicOwned) {
                transmission.IsEndPending = true;
                AudioSession.ReleaseOwner(AudioSessionRelease.TransmitEnded);
                return;
            }
        }
        AudioSession.ReleaseOwner(AudioSessionRelease.TransmitEnded);
        _ = BackgroundTask.Run(
            () => StopTransmitReply(transmission),
            Log, "Stopping the PTT transmit reply failed", CancellationToken.None);
    }

    private static async Task StopTransmitReply(Transmission transmission)
    {
        lock (Lock) {
            if (!ReferenceEquals(_transmission, transmission))
                return;

            _transmission = null;
        }
        PttPreRoll.Discard(transmission.PreRollToken);
        if (AppScopeAccessor.Current is not { } services)
            return;

        // Only stop what this transmission started: the mic may belong to a gesture reply.
        if (!transmission.IsMicOwned)
            return;

        var hub = services.GetRequiredService<AppUIHub>();
        await hub.WalkieTalkieReplyUI.StopReply().ConfigureAwait(false);
    }

    private static void StopTransmitting()
    {
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        manager.StopTransmitting(ChannelUuid);
    }
```

- [ ] **Step 6: Branch the audio-session activation**

Replace `OnAudioSessionActivated`:

```csharp
    private static void OnAudioSessionActivated()
    {
        Transmission? transmission;
        lock (Lock)
            transmission = _transmission;
        AudioSession.SetOwner(AudioSessionOwnership.OnActivated(transmission is not null));
        if (transmission is not null) {
            StartTransmitReply(transmission);
            return;
        }

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
```

Add `using ActualChat.App.Maui.Services;` for `AppScopeAccessor` if it is not already imported (it is — the file already uses `ActualChat.App.Maui.Services`).

- [ ] **Step 7: Probe both files**

```bash
scripts/csc-ios-probe.sh src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs
scripts/csc-ios-probe.sh src/dotnet/App.Maui/Services/WalkieTalkieSession.cs
```
Expected: `CURRENT: exit 0` for both.

- [ ] **Step 8: Build and run the unit suites**

```bash
dotnet build ActualChat.CI.slnf
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj
```
Expected: build succeeded; tests PASS.

- [ ] **Step 9: Commit**

```bash
git add src/dotnet/App.Maui/Services/WalkieTalkieSession.cs \
        src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs
git commit -m "feat(maui): record a walkie reply when Apple PTT reports a transmission"
```

---

## Task 9: Transmission mode and the channel descriptor

**Files:**
- Modify: `src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs`
- Modify: `src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs`

**Interfaces:**
- Consumes: `UserWalkieTalkieSettings.IsPttTransmitEnabled` from Task 1.
- Produces: `IosPushToTalk.SetTransmitEnabled(bool)`. Nothing consumes it after this task.

- [ ] **Step 1: Make the transmission mode settings-driven**

In `IosPushToTalk.cs`, add beside the other static fields:

```csharp
    private static int _isTransmitEnabled;
```

Add to the public methods, after `Leave`:

```csharp
    public static void SetTransmitEnabled(bool isEnabled)
    {
        Volatile.Write(ref _isTransmitEnabled, isEnabled ? 1 : 0);
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        ApplyTransmissionMode(manager, ChannelUuid, isEnabled);
    }
```

Add to the private methods:

```csharp
    private static void ApplyTransmissionMode(PTChannelManager manager, NSUuid channelUuid, bool isEnabled)
    {
        // Off must mean ListenOnly, not an inert button: a Talk press that silently does nothing
        // is worse than no Talk button at all.
        var mode = isEnabled ? PTTransmissionMode.FullDuplex : PTTransmissionMode.ListenOnly;
        manager.SetTransmissionMode(mode, channelUuid, error => {
            if (error is not null)
                Log.LogWarning("SetTransmissionMode({Mode}) failed: {Error}", mode, error.LocalizedDescription);
        });
    }

    private static void SetDescriptorTitle(string chatTitle)
    {
        var manager = _manager;
        if (manager?.ActiveChannelUuid is null)
            return;

        // The channel is the aggregate "Voxt", so without this the system sheet cannot tell the
        // user which chat a Talk press would reach.
        manager.SetChannelDescriptor(new PTChannelDescriptor(chatTitle, UIImage.FromBundle("AppIcon")),
            ChannelUuid,
            error => {
                if (error is not null)
                    Log.LogWarning("SetChannelDescriptor failed: {Error}", error.LocalizedDescription);
            });
    }
```

- [ ] **Step 2: Use the stored mode on join, and refresh the title on each push**

In `ManagerDelegate.DidJoinChannel`, replace the hardcoded `ListenOnly` block with:

```csharp
        public override void DidJoinChannel(
            PTChannelManager channelManager, NSUuid channelUuid, PTChannelJoinReason reason)
        {
            Log.LogInformation("PTT channel joined ({Reason})", reason);
            ApplyTransmissionMode(channelManager, channelUuid, Volatile.Read(ref _isTransmitEnabled) != 0);
        }
```

In `ManagerDelegate.IncomingPushResult`, immediately before `return PTPushResult.Create(new PTParticipant(chatTitle, null!));`:

```csharp
            SetDescriptorTitle(chatTitle);
```

- [ ] **Step 3: Drive the mode from settings**

Replace `IosPushToTalkUI.OnRun`:

```csharp
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var chatAudioUI = Hub.ChatAudioUI;
        var cArmedChatIds = await Computed
            .Capture(() => chatAudioUI.GetPttChatIds(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        // GetPttChatIds reads the whole UserWalkieTalkieSettings record, so its invalidation also
        // covers IsPttTransmitEnabled - see the same note in GestureUI.TrackActivation.
        await foreach (var change in cArmedChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value.Count == 0) {
                IosPushToTalk.Leave();
                continue;
            }

            var settings = await UserSettingsUI.UserWalkieTalkieSettings()
                .Get(cancellationToken)
                .ConfigureAwait(false);
            IosPushToTalk.SetTransmitEnabled(settings.IsPttTransmitEnabled ?? true);
            IosPushToTalk.EnsureJoined();
        }
    }
```

`SetTransmitEnabled` runs before `EnsureJoined` so the stored flag is correct when `DidJoinChannel` reads it.

- [ ] **Step 4: Probe both files**

```bash
scripts/csc-ios-probe.sh src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs
scripts/csc-ios-probe.sh src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs
```
Expected: `CURRENT: exit 0` for both.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs \
        src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs
git commit -m "feat(maui): settings-driven PTT transmission mode and a chat-named channel descriptor"
```

---

## Task 10: Final verification sweep

**Files:**
- Modify: `docs/superpowers/specs/2026-08-04-walkie-talkie-ios-transmit-e4-design.md` (status + resolved open questions)

**Interfaces:**
- Consumes: everything.
- Produces: nothing.

- [ ] **Step 1: Full build**

Run: `dotnet build ActualChat.CI.slnf`
Expected: build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 2: Run every affected test project by name**

This list is explicit because E3 found `GestureUITest` silently red since E2 — no plan's verification had run `Chat.UI.Blazor.IntegrationTests`.

```bash
dotnet test tests/Core.Audio.UnitTests/Core.Audio.UnitTests.csproj
dotnet test tests/UI.Blazor.UnitTests/UI.Blazor.UnitTests.csproj
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj
dotnet test tests/Users.UnitTests/Users.UnitTests.csproj
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj
```

Expected: all PASS. `Users.UnitTests` has one pre-existing skip (`SettingsRoundTripSerializationTest.GenerateTestCases`); `Chat.UI.Blazor.IntegrationTests` has two pre-existing skips. Any *failure* is a defect in this branch.

- [ ] **Step 3: Probe every iOS file this sub-project touched**

```bash
for f in src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalk.cs \
         src/dotnet/App.Maui/Platforms/iOS/PushToTalk/IosPushToTalkUI.cs \
         src/dotnet/App.Maui/MaciOS/Audio/AudioSession.cs \
         src/dotnet/App.Maui/MaciOS/Audio/AppleAudioCapture.cs \
         src/dotnet/App.Maui/MaciOS/Audio/PttPreRoll.cs \
         src/dotnet/App.Maui/Services/WalkieTalkieSession.cs; do
    echo "=== $f"; scripts/csc-ios-probe.sh "$f" || exit 1
done
```
Expected: `CURRENT: exit 0` for all six.

- [ ] **Step 4: Confirm the Android path is untouched**

Run: `scripts/csc-android-probe.sh dev`
Expected: `CURRENT: exit 0`, and the same error/warning counts as the baseline. E4 changed `WalkieTalkieSession` and `WalkieTalkieReplyUI`, both of which Android uses.

- [ ] **Step 5: Update the spec's status and resolved questions**

In the spec, set `Status: Implemented (device verification pending — see plan Task 10)`, and in **Open Questions** mark these resolved with what was found:

- Live descriptor updates — **resolved**: `PTChannelManager.SetChannelDescriptor(PTChannelDescriptor, NSUuid, Action<NSError>)` exists and compiles.
- `Microsoft.iOS.Ref` on Linux — **resolved**: `Microsoft.iOS.Ref.net11.0_26.2` / `26.2.11588-net11-p3` restores as a plain NuGet package and `csc` compiles `PushToTalk`/`AVFoundation`/`UIKit` code against `ref/net11.0/Microsoft.iOS.dll`.
- Pre-roll bounds and the boot cap — **resolved**: 8 s each, pinned to `AppleAudioCapture`'s 10 s `outBuffer`.
- The minimum-duration floor — **resolved**: 0.4 s.

Leave the half-duplex semantics and the `PTChannelManager.Create` ordering questions open; neither was answerable without a device.

Also record the two corrections this plan makes to the spec: the pre-roll does **not** wrap `BlockRingBuffer<T>` (its drop-newest, exact-length-read, SPSC-streaming semantics are wrong for a one-shot capture-then-drain), and `AudioSessionOwner` lives in `UI.Blazor/Services` rather than `MaciOS/Audio` so it is testable.

- [ ] **Step 6: Add the execution outcome and device list**

Append an `## Execution outcome` section to this plan recording what was built, then the device-verification list in this order:

1. **Verify sub-project C's iOS wake path on a device first.** It has never been compiled or run. Everything in E4 sits on it, and until it is known good a transmit failure and a wake failure are indistinguishable.
2. **Build for iOS on a Mac.** First real compile of everything in Tasks 6–9 beyond the probe's per-file coverage — no analyzers, no `Microsoft.Maui*` ambiguity, no linker have run.
3. **Toggle "Lock Screen talk button" off and on once** in Settings, to confirm the write path for a settings blob that predates the member.
4. **With transmit off, confirm no Talk button appears** in the system PTT sheet — the channel must stay `ListenOnly`.
5. **With transmit on and the app foregrounded**, press Talk: a reply records into the most recent PTT chat and stops on release.
6. **The killed-process case:** kill the app, have someone speak, then press Talk from the Lock Screen. This is the point of the sub-project.
7. **Verify the pre-roll actually lands** — speak immediately at the system chime on a cold start and confirm the first word is in the sent message. If it is not, `PttPreRoll.Start()` is returning 0 or the format guard is dropping the take; both log.
8. **Release the Talk button immediately on a cold start** (before the app can boot). Per decision 7 the buffered words should still send. **This is E4's least certain path**: if iOS deactivates the audio session on release, the recorder may open into a dead session and produce nothing. If so, change `OnTransmitEnded` to discard rather than flush.
9. **Press Talk with nothing recent in any armed chat** — expect the `WalkieReplyNothingHeard` cue and no recording.
10. **Check the system sheet names the chat**, not `"Voxt"`, after an incoming wake.
11. **Reply while an incoming utterance is still playing** — full duplex must allow it (decision 5).
12. **Listen for speakerphone echo** while transmitting. The framework owns the session configuration and `ConfigureUnsafe` is now skipped under `PttTransmit`, so we cannot correct the route.
13. **Confirm ownership never sticks:** after a transmit, an ordinary in-app recording must still activate the session. A stuck `AudioSessionOwner` disables the app's own activation permanently.
14. **Leave a headless transmit running past 10 seconds** with nothing playing, to confirm the teardown watcher's new `IsRecording` check keeps the scope alive.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/specs/2026-08-04-walkie-talkie-ios-transmit-e4-design.md \
        docs/superpowers/plans/2026-08-04-walkie-talkie-ios-transmit-e4.md
git commit -m "docs: mark walkie-talkie iOS PTT transmit (E4) implemented"
```

---

## Reuse

**Existing abstractions reused.** `WalkieTalkieSession` (boot, scope resolution, teardown watcher, `StopAndDispose`), `WalkieTalkiePlatform`, `AppScopeAccessor`, `HeadlessBlazorScope`, `BlazorWebViewApp`, `WalkieTalkieReplyUI.RequestReply`/`StopReply` with its cold-start dead-man switch, `ReplyTargetResolver`, `IncomingVoiceActivityUI`, `ChatAudioUI.SetRecordingChatId`/`GetPttChatIds`/`IsRecording`, `GestureUI.GetHeadsetButtonState` for the practice-mode check, `AppleAudioCapture`/`AudioEngines`/`ResamplerFactory`, `AVAudioPcmBufferExt.AsReadOnlySpan`/`SetData`, `AppleAudioFocusUI`/`AudioSession`, `UserWalkieTalkieSettings` + `UserSettingsAccessor`, `TuneUI` with the existing `Tune.WalkieReply*` cues, `PushToTalkSettings.razor`, `IosPushToTalk`/`IosPushToTalkUI`, and `scripts/csc-android-probe.sh` as the template for Task 5.

**New components and their placement.** `PreRollBuffer` goes in `Core.Audio` (namespace `ActualChat.Audio`) rather than beside its only caller: it must be testable off-platform to be worth anything, and Android will want the same thing if a transmit-from-wake path ever lands there. `AudioSessionOwner`/`AudioSessionOwnership` go in `UI.Blazor/Services` beside `AudioFocusMode` for the same reason — `MaciOS` compiles in no test host. `PttPreRoll` is genuinely platform-bound and stays in `MaciOS/Audio`, placed there rather than in `Platforms/iOS` so `AppleAudioCapture` can reference it without conditional compilation.

**Deliberately not reused.** `BlockRingBuffer<T>` was evaluated for the pre-roll and rejected: it is a blocking single-producer/single-consumer stream buffer that drops the *newest* data on overflow and requires exact-length reads. A pre-roll is a one-shot capture drained once, where overflow must void the whole buffer. Wrapping it would have meant fighting three of its four behaviours.

## Risks

- **Nothing in Tasks 6–9 has been compiled by a real iOS build.** The probe raises the floor considerably — it caught three API mistakes during planning alone — but it sees no analyzers, no MAUI global usings, and no linker.
- **Sub-project C is unverified.** Every risk below is conditional on a wake path that has never run on a device.
- **The early-release flush (Task 8, step 5) is the weakest link.** If iOS tears the audio session down on button release, the recorder opens into a dead session and decision 7's "send the buffered words" quietly becomes "send nothing". Device item 8 exists for exactly this, and the fallback is one branch.
- **Two `AVAudioEngine` instances must never hold the input node at once.** `PttPreRoll.TryTake` stops the pre-roll engine, and `AppleAudioCapture` calls it before touching `AudioEngines.Recording` — but the ordering is load-bearing and invisible at the call site if someone later moves that line.
- **Full duplex hands session configuration entirely to the framework.** Speakerphone echo is the plausible failure, and skipping `ConfigureUnsafe` under `PttTransmit` is precisely what removes our ability to correct it.
- **A stuck `AudioSessionOwner` permanently disables the app's own session activation.** The old bool already carried this hazard; three states and five release paths widen it, and the spec's timer-based watchdog was deliberately not built.

## Execution outcome

Tasks 1–9 are code-complete. Built: the `IsPttTransmitEnabled` setting and its iOS-only toggle, with the pre-existing "Headset button" toggle now gated to Android (Task 1); an unbounded recency window so a deliberate Talk-button press can resolve arbitrarily old voice activity, via `ReplyTargetResolver.UnboundedRecencyWindow` and a `WalkieTalkieReplyUI.RequestReply(TimeSpan, CancellationToken)` overload (Task 2); `AudioSessionOwner`/`AudioSessionOwnership` as a pure, fully-tested state machine in `UI.Blazor/Services` (Task 3); `PreRollBuffer`, a standalone bounded token-guarded capture buffer in `Core.Audio` (Task 4); `scripts/csc-ios-probe.sh`, the iOS sibling of the Android csc probe (Task 5); typed ownership wired through `AudioSession`'s three session paths and `IosPushToTalk`'s receive callbacks, replacing the `IsExternallyActivated` bool everywhere (Task 6); `PttPreRoll` — a process-level `AVAudioEngine` tap into `PreRollBuffer` — plus the drain in `AppleAudioCapture.CaptureInternal` ahead of live frames (Task 7); the transmit path itself — `WalkieTalkieSession.HandleTransmit`, `IosPushToTalk`'s `DidBeginTransmitting`/`DidEndTransmitting`/`FailedToBeginTransmittingInChannel` bodies, the teardown watcher's new `IsRecording` check, and the early-release-still-sends flush (Task 8); and the settings-driven transmission mode plus a chat-named channel descriptor (Task 9).

Two corrections to the spec surfaced during implementation, now reflected in the spec's "Corrections to this spec found during implementation" section: `PreRollBuffer` does not wrap `BlockRingBuffer<T>` — that type is a blocking SPSC streaming buffer that drops the *newest* data on overflow and requires exact-length reads, both wrong for a one-shot capture drained once, where overflow must void the whole buffer — so `PreRollBuffer` is standalone in `Core.Audio`; and `AudioSessionOwner` lives in `UI.Blazor/Services/AudioSessionOwnership.cs`, not `MaciOS/Audio`, because `App.Maui` compiles in no test host and the type's ownership transitions are only worth having if they're verifiable.

Two deliberate deviations from the spec, both shipped as-is rather than deferred:

- **The timer-based ownership watchdog was not built.** The spec asked for a timer that reverts `AudioSessionOwner` if no PTT callback arrives. A timer cannot distinguish a stuck flag from a legitimately long playback, so ownership instead reverts deterministically on five existing paths: `DidDeactivateAudioSession`, `DidEndTransmitting`, `DidLeaveChannel`, `OnWakeFailed`, and `OnHeadlessTeardown`.
- **The transmit boot budget is one shared 8 s, not 8 s per step.** `HandleTransmit` uses a single `CancellationTokenSource(WalkieTalkiePttTransmitStartupTimeout)` whose token is passed to `WhenAppReady.WaitAsync`, `SessionTask.WaitAsync`, and `RequestReply` alike, rather than a fresh 8 s window at each step. This is a real behaviour change: a cold start that takes 5 s to reach `WhenAppReady` and 4 s more to resolve the session now fails where independent 8 s windows would have let it proceed. It is justified because the pre-roll capacity is also 8 s — past that point the buffered speech is already gone, so a longer boot budget could not recover it anyway.

**Known gap carried forward, out of scope for E4:** `WalkieTalkieReplyUI.RequestReply` now returns `Task<WalkieTalkieReply?>` instead of `Task`, so a caller can prove it is the one that opened the recording it later stops (`StopOrphanedReply` in `HandleTransmit` uses this). Android's headset button and the on-screen `WalkieReplyToggle` both still discard that return value today, so they retain the older "stop whatever is recording" behaviour. Giving them the same protection is an obvious follow-up.

Also carried forward: the sweep-discipline follow-up E3 flagged — diff the CI test-project list against what plans actually invoke — is still open; Step 2 of this task ran the five projects named in the brief by hand rather than closing that gap structurally.

### Device-verification list

None of the following has been run on a device; all require a physical iPhone, an APNs `.p8` key, and a partner to speak into a second device or the Lock Screen. In this order:

1. **Verify sub-project C's iOS wake path on a device first.** It has never been compiled or run. Everything in E4 sits on it, and until it is known good a transmit failure and a wake failure are indistinguishable.
2. **Build for iOS on a Mac.** First real compile of everything in Tasks 6–9 beyond the probe's per-file coverage — no analyzers, no `Microsoft.Maui*` ambiguity, no linker have run.
3. **Toggle "Lock Screen talk button" off and on once** in Settings, to confirm the write path for a settings blob that predates the member.
4. **With transmit off, confirm no Talk button appears** in the system PTT sheet — the channel must stay `ListenOnly`.
5. **With transmit on and the app foregrounded**, press Talk: a reply records into the most recent PTT chat and stops on release.
6. **The killed-process case:** kill the app, have someone speak, then press Talk from the Lock Screen. This is the point of the sub-project.
7. **Verify the pre-roll actually lands** — speak immediately at the system chime on a cold start and confirm the first word is in the sent message. If it is not, `PttPreRoll.Start()` is returning 0 or the format guard is dropping the take; both log.
8. **Release the Talk button immediately on a cold start** (before the app can boot). Per decision 7 the buffered words should still send. **This is E4's least certain path**: if iOS deactivates the audio session on release, the recorder may open into a dead session and produce nothing. If so, change `OnTransmitEnded` to discard rather than flush.
9. **Press Talk with nothing recent in any armed chat** — expect the `WalkieReplyNothingHeard` cue and no recording.
10. **Check the system sheet names the chat**, not `"Voxt"`, after an incoming wake.
11. **Reply while an incoming utterance is still playing** — full duplex must allow it (decision 5).
12. **Listen for speakerphone echo** while transmitting. The framework owns the session configuration and `ConfigureUnsafe` is now skipped under `PttTransmit`, so we cannot correct the route.
13. **Confirm ownership never sticks:** after a transmit, an ordinary in-app recording must still activate the session. A stuck `AudioSessionOwner` disables the app's own activation permanently.
14. **Leave a headless transmit running past 10 seconds** with nothing playing, to confirm the teardown watcher's new `IsRecording` check keeps the scope alive.

Items 1–2 outrank the rest: until sub-project C's wake path and a real iOS build are both known good, a failure anywhere in items 3–14 cannot be attributed to E4 versus something upstream of it.
