# Walkie-Talkie PTT Settings + Gesture Engine (Sub-Project E2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give push-to-talk its own explicit opt-in chat set and settings surface, and add accelerometer gestures (flip-to-talk, double-shake to start; face-down/pocket to stop) so a walkie-talkie reply can be triggered without touching the screen.

**Architecture:** A new `UserWalkieTalkieSettings` KVAS record becomes the sole "armed" source on both server (wake-push gate) and client (reply target set), decoupled from the pre-existing "Keep listening" option. Gesture recognition is split into pure, device-free state machines in `UI.Blazor.App/Services/Gestures/` (unit-testable) and a thin MAUI sensor feed that only produces timestamped accelerometer samples. A `GestureUI` worker subscribes the feed only while a reply is plausible (recent incoming voice in a PTT chat), routes start gestures to `WalkieTalkieReplyUI.RequestReply` and the stop gesture to `ChatAudioUI.SetRecordingChatId(null)`.

**Tech Stack:** C# 13 / .NET 10, Blazor (Razor components), ActualLab.Fusion compute services, MemoryPack + MessagePack union serialization, .NET MAUI Essentials sensors (`Accelerometer`), Android `SensorManager` / iOS `UIDevice` proximity, xUnit + AwesomeAssertions.

**Spec:** `docs/superpowers/specs/2026-07-26-walkie-talkie-ptt-settings-design.md`

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any C#/TS.** In particular: no `Async` suffix on async methods; no `///` XML docs on members (type-level `<summary>` only, and only when the name isn't self-explanatory); Allman braces for classes/methods, K&R for everything else including razor; max 120 chars/line; 4-space indent; control-flow statements get their own line followed by a blank line; boolean names prefixed `is`/`must`/`has`.
- **Read `docs/development/ui-components.md` before touching any `.razor`.** Components inside a container that already owns a CSS file (SettingsModal) do NOT get their own CSS file — styles go into `settings-modal.css` under a section comment.
- **Comment budget:** default to none. A comment is justified only for a non-obvious invariant, constraint, or platform quirk. Every comment written verbatim in this plan is deliberate — copy those, and add no others.
- **`StoredSettings` union id for the new type is `17`** — free in both the MemoryPack list (0–14, then 50+) and the MessagePack list (0–16, then 50+). Use the same id in both.
- **`UserAppSettings` next free member order is `8`** — orders 0–3 and 5–7 are taken, 4 is reserved-do-not-reuse. The spec's "use order 6" is stale.
- **`PttChatIds` cap is 3**, matching `ActiveChatsUI.MaxActiveChatCount` and bounding server wake fan-out.
- **Detector cores must have no clock, no I/O, and no MAUI reference.** They take timestamped samples and return decisions. This is what makes them testable on a build machine with no device.
- **Sensitivity firing sets must nest: Low ⊆ Medium ⊆ High.** Lower sensitivity demands a harder shake.
- **Never widen the mic-open surface.** On any ambiguity, fail toward the mic staying/becoming closed: stop gesture beats start gesture, failed settings read means "not armed", practice mode never calls `RequestReply`.
- Build check: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3` → `0 Error(s)`. TS/CSS check (only if `.ts`/`.css` touched): `npm run build:Verify 2>&1 | tail -20` → clean.

## Decisions resolved during planning

These close the spec's "Open Questions" and correct two stale spec statements. Each is a judgment call the implementer must NOT re-litigate.

1. **Union id 17, both lists** (spec open question 1) — verified free in `src/dotnet/Api/StoredSettings.cs`.
2. **Accelerometer only; `OrientationSensor` is dropped** (spec open question 3). One sensor is cheaper, and orientation is derived sign-agnostically from the dominant gravity axis (`|Y|` dominant = portrait, `|X|` = landscape, `|Z|` = flat), which sidesteps per-platform quaternion conventions. Only `FaceDownDetector` needs the Z sign, and MAUI normalizes it (face-up ≈ `Z = -1`).
3. **Proximity lives inside `MauiSensorFeed` behind `#if ANDROID` / `#if IOS`**, not in a separate per-platform interface + two files (spec component 6). This is exactly the `MauiThermalTracker` precedent, which does per-platform sensor work in one class.
4. **Concrete thresholds** (spec open question 2) are specified in Task 5 and are seeded, not final: the practice panel exists to correct them on a device.
5. **Hot-window seam** (spec open question 5): an optional `idleDuration` argument on `ChatAudioUI.SetRecordingChatId`, stored in a field that `RecordChat` reads when building `RecordingIdleOptions`. `RecordChat`'s structure is untouched, honouring E1's constraint.
6. **Settings tab icon** (spec open question 4): reuse `icon-talking`, already used by `WalkieReplyToggle`. No new asset.
7. **CORRECTION to spec — `IsWalkieTalkieArmed` has TWO server consumers, not one.** Sub-project D added `LiveAudioStreams.ReportPlayback` (`src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs:128`) alongside the wake gate. Switching the predicate therefore also narrows *heard receipts* to PTT chats. That is the correct outcome (heard receipts are a walkie-talkie feature), but `ReportPlaybackTest` arms via `ListeningMode.Forever` and must be updated too, or it silently goes vacuous.
8. **PTT chats are force-listened on the client.** `GetChatsYouNeedToKeepListeningTo` returns the union of `AlwaysListenedChatIds` and `PttChatIds`. Rationale: without this, a PTT-only chat in the foreground would wake you but play nothing, because listening — not arming — is what starts a player. The *settings* stay independent (decision 1 of the spec is about consent, not about audio routing); only the runtime listening behaviour is implied. **Surface this to the user in the Task 3 report.**
9. **`IsFaceDownMicStopEnabled` defaults to OFF.** It lives on `UserAppSettings`, which every user has, and it closes the mic on *any* recording — turning it on by default would change behaviour for users who never asked for walkie-talkie.

---

### Task 1: Settings contracts

**Files:**
- Create: `src/dotnet/Api/Users/StoredSettings/UserWalkieTalkieSettings.cs`
- Modify: `src/dotnet/Api/StoredSettings.cs` (union lists)
- Modify: `src/dotnet/Api/Users/StoredSettings/UserAppSettings.cs`
- Modify: `src/dotnet/Api/Users/UserSettingsUIExt.cs`
- Modify: `src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs`
- Test: `tests/Users.UnitTests/UserWalkieTalkieSettingsTest.cs`

**Interfaces:**
- Produces: `ActualChat.Users.UserWalkieTalkieSettings` with `ChatId[] PttChatIds`, `bool IsFlipToTalkEnabled`, `bool IsDoubleShakeEnabled`, `ShakeSensitivity ShakeSensitivity`, `bool AreGesturesAlwaysOn`, `TimeSpan HotWindow`, `bool AreAudibleCuesEnabled`, `string Origin`; `const int MaxChatCount = 3`; methods `WithPttChat(ChatId)`, `WithoutPttChat(ChatId)`.
- Produces: `ActualChat.Users.ShakeSensitivity` enum `{ Low, Medium, High }` with `Medium` as the default (value 0 is `Medium` — see below).
- Produces: `UserAppSettings.IsFaceDownMicStopEnabled` (`bool?`).
- Produces: `UserSettingsUIExt.UserWalkieTalkieSettings(this UserSettingsUI)` → `UserSettingsAccessor<UserWalkieTalkieSettings>`; `UserScopedKvasBackendExt.UserWalkieTalkieSettings(this UserScopedKvasBackend)` → `KvasAccessor<UserWalkieTalkieSettings>`.

- [ ] **Step 1: Write the failing test**

Create `tests/Users.UnitTests/UserWalkieTalkieSettingsTest.cs`:

```csharp
namespace ActualChat.Users.UnitTests;

public class UserWalkieTalkieSettingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void Defaults_AreSafe()
    {
        var settings = new UserWalkieTalkieSettings();
        settings.PttChatIds.Should().BeEmpty();
        settings.IsFlipToTalkEnabled.Should().BeTrue();
        settings.IsDoubleShakeEnabled.Should().BeTrue();
        settings.ShakeSensitivity.Should().Be(ShakeSensitivity.Medium);
        settings.AreGesturesAlwaysOn.Should().BeFalse();
        settings.HotWindow.Should().Be(TimeSpan.FromSeconds(60));
        settings.AreAudibleCuesEnabled.Should().BeTrue();
    }

    [Fact]
    public void WithPttChat_IsIdempotent()
    {
        var settings = new UserWalkieTalkieSettings().WithPttChat(TestChatId).WithPttChat(TestChatId);
        settings.PttChatIds.Should().Equal(TestChatId);
        settings.WithoutPttChat(TestChatId).PttChatIds.Should().BeEmpty();
    }

    [Fact]
    public void PassesThroughAllSerializers()
    {
        var settings = new UserWalkieTalkieSettings {
            PttChatIds = [TestChatId],
            IsFlipToTalkEnabled = false,
            ShakeSensitivity = ShakeSensitivity.High,
            AreGesturesAlwaysOn = true,
            HotWindow = TimeSpan.FromSeconds(120),
            AreAudibleCuesEnabled = false,
            Origin = "test",
        };
        ((StoredSettings)settings).AssertPassesThroughAllSerializers(
            (deserialized, original) => {
                var d = (UserWalkieTalkieSettings)deserialized;
                var o = (UserWalkieTalkieSettings)original;
                d.PttChatIds.Should().Equal(o.PttChatIds);
                d.IsFlipToTalkEnabled.Should().Be(o.IsFlipToTalkEnabled);
                d.IsDoubleShakeEnabled.Should().Be(o.IsDoubleShakeEnabled);
                d.ShakeSensitivity.Should().Be(o.ShakeSensitivity);
                d.AreGesturesAlwaysOn.Should().Be(o.AreGesturesAlwaysOn);
                d.HotWindow.Should().Be(o.HotWindow);
                d.AreAudibleCuesEnabled.Should().Be(o.AreAudibleCuesEnabled);
            }, Out);
    }

    [Fact]
    public void UserAppSettings_FaceDownFlag_PassesThroughAllSerializers()
    {
        var settings = new UserAppSettings { IsFaceDownMicStopEnabled = true };
        ((StoredSettings)settings).AssertPassesThroughAllSerializers(
            (deserialized, _) => ((UserAppSettings)deserialized).IsFaceDownMicStopEnabled.Should().BeTrue(),
            Out);
    }
}
```

The cast to `StoredSettings` is deliberate: it exercises the *union* registration, which is the part that breaks if the id is wrong or missing. This is the plan's substitute for the spec's "ApiEvolutionTest guards the settings union" — `ApiEvolutionTest` covers only a hand-picked subset of settings types and needs committed artifacts per type, so it is run unchanged in Task 10 as a no-regression check rather than extended here.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~UserWalkieTalkieSettingsTest" 2>&1 | tail -5`
Expected: build FAILS — `UserWalkieTalkieSettings` does not exist.

- [ ] **Step 3: Create the settings record**

Create `src/dotnet/Api/Users/StoredSettings/UserWalkieTalkieSettings.cs`:

```csharp
using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for walkie-talkie push-to-talk: which chats may wake the
/// device, and how the hands-free gestures behave.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserWalkieTalkieSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserWalkieTalkieSettings>
{
    // Matches ActiveChatsUI.MaxActiveChatCount, and bounds server wake fan-out per speaker.
    public const int MaxChatCount = 3;

    [DataMember, MemoryPackOrder(0), Key(0)]
    public ChatId[] PttChatIds { get; init; } = [];
    [DataMember, MemoryPackOrder(1), Key(1)]
    public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(2)]
    public bool IsFlipToTalkEnabled { get; init; } = true;
    [DataMember, MemoryPackOrder(3), Key(3)]
    public bool IsDoubleShakeEnabled { get; init; } = true;
    [DataMember, MemoryPackOrder(4), Key(4)]
    public ShakeSensitivity ShakeSensitivity { get; init; } = ShakeSensitivity.Medium;
    [DataMember, MemoryPackOrder(5), Key(5)]
    public bool AreGesturesAlwaysOn { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)]
    public TimeSpan HotWindow { get; init; } = TimeSpan.FromSeconds(60);
    [DataMember, MemoryPackOrder(7), Key(7)]
    public bool AreAudibleCuesEnabled { get; init; } = true;

    public UserWalkieTalkieSettings WithPttChat(ChatId chatId)
        => this with { PttChatIds = PttChatIds.WithOrSkip(chatId).ToArray() };

    public UserWalkieTalkieSettings WithoutPttChat(ChatId chatId)
        => this with { PttChatIds = PttChatIds.Without(chatId).ToArray() };
}

// Values are ordered so Medium is the zero default; the firing sets nest: Low ⊆ Medium ⊆ High.
public enum ShakeSensitivity
{
    Medium = 0,
    Low = 1,
    High = 2,
}
```

`Medium = 0` is not cosmetic: a version-tolerant blob missing this member deserializes to `0`, and the default must be Medium.

- [ ] **Step 4: Register the union id**

In `src/dotnet/Api/StoredSettings.cs`, add to the MemoryPack "User settings" block right after `[MemoryPackUnion(14, typeof(UserReplaySettings))]`:

```csharp
[MemoryPackUnion(17, typeof(UserWalkieTalkieSettings))]
```

and to the MessagePack "User settings" block right after `[Union(16, typeof(RecentGifs))]`:

```csharp
[Union(17, typeof(UserWalkieTalkieSettings))]
```

- [ ] **Step 5: Add the face-down flag to `UserAppSettings`**

In `src/dotnet/Api/Users/StoredSettings/UserAppSettings.cs`, append after the `IsAudioDiagnosticsEnabled` line:

```csharp
    [DataMember, MemoryPackOrder(8), Key(8)] public bool? IsFaceDownMicStopEnabled { get; init; }
```

- [ ] **Step 6: Add the accessors**

In `src/dotnet/Api/Users/UserSettingsUIExt.cs`, append before the closing brace:

```csharp
    public static UserSettingsAccessor<UserWalkieTalkieSettings> UserWalkieTalkieSettings(
        this UserSettingsUI settingsUI)
        => new(settingsUI, nameof(UserWalkieTalkieSettings));
```

In `src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs`, append before the closing brace:

```csharp
    public static KvasAccessor<UserWalkieTalkieSettings> UserWalkieTalkieSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserWalkieTalkieSettings>();
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~UserWalkieTalkieSettingsTest" 2>&1 | tail -5`
Expected: 4 PASS.

If the union round-trip fails with a "not registered"/"unknown union tag" error, the id collides — re-check both lists in `StoredSettings.cs`.

- [ ] **Step 8: Build and commit**

```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
git add src/dotnet/Api/Users/StoredSettings/UserWalkieTalkieSettings.cs \
        src/dotnet/Api/StoredSettings.cs \
        src/dotnet/Api/Users/StoredSettings/UserAppSettings.cs \
        src/dotnet/Api/Users/UserSettingsUIExt.cs \
        src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs \
        tests/Users.UnitTests/UserWalkieTalkieSettingsTest.cs
git commit -m "feat(users): UserWalkieTalkieSettings - PTT chat set + gesture preferences"
```

---

### Task 2: Server armed predicate switches to `PttChatIds`

**Files:**
- Modify: `src/dotnet/Users.Contracts/ServerKvasBackendExt.cs`
- Test: `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs`
- Test: `tests/Streaming.IntegrationTests/ReportPlaybackTest.cs`

**Interfaces:**
- Consumes: `UserScopedKvasBackendExt.UserWalkieTalkieSettings(...)` (Task 1).
- Produces: `IServerKvasBackend.IsWalkieTalkieArmed(userId, chatId, ct)` now returns `PttChatIds.Contains(chatId)` and nothing else. Both consumers — `NotificationsBackend` (wake push) and `LiveAudioStreams.ReportPlayback` (heard receipts) — change behaviour through it.

- [ ] **Step 1: Write the failing tests**

In `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs`, replace the two arming helpers at the bottom of the file:

```csharp
    private Task ArmByPtt(UserId userId, ChatId chatId)
        => ServerKvasBackend.ForUser(userId).UserWalkieTalkieSettings()
            .Update(x => x.WithPttChat(chatId));

    private Task SetForeverListeningMode(UserId userId, ChatId chatId)
        => ServerKvasBackend.ForUser(userId).ChatUserSettings(chatId)
            .Update(x => x with { ListeningMode = ListeningMode.Forever });
```

Replace every `await ArmByAlwaysListened(` call site with `await ArmByPtt(`. Rename the first test to `ArmedByPttChatGetsWake` (it calls `ArmByPtt`).

Replace the `ArmedByForeverListeningModeGetsWake` test — the behaviour it asserts is exactly what this task removes — with the decoupling regression, which is the single most important test in this sub-project:

```csharp
    [Fact]
    public async Task ForeverListeningWithoutPttGetsNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT listen-only");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await SetForeverListeningMode(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task PttWithoutAnyListeningSettingsGetsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT ptt-only");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }
```

In `tests/Streaming.IntegrationTests/ReportPlaybackTest.cs`, replace the `Arm` helper body (currently `ListeningMode.Forever`) with:

```csharp
    private Task Arm(UserId userId, ChatId chatId)
        => ServerKvasBackend.ForUser(userId).UserWalkieTalkieSettings()
            .Update(x => x.WithPttChat(chatId));
```

Match the surrounding accessor name for the backend service in that file — if it resolves the backend under a different property name than `ServerKvasBackend`, use that name; do not add a new field.

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
  --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -6
```
Expected: `ForeverListeningWithoutPttGetsNoWake` FAILS (a wake still arrives — the old predicate honours `ListeningMode.Forever`), and `PttWithoutAnyListeningSettingsGetsWake` FAILS (no wake — the old predicate never reads `PttChatIds`).

- [ ] **Step 3: Rewrite the predicate**

Replace the body of `IsWalkieTalkieArmed` in `src/dotnet/Users.Contracts/ServerKvasBackendExt.cs`:

```csharp
    public static async Task<bool> IsWalkieTalkieArmed(
        this IServerKvasBackend serverKvasBackend,
        UserId userId,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        // PTT is a separate opt-in from "Keep listening": waking a killed device is a
        // materially different commitment, so it gets its own chat set and its own consent.
        var pttChatIds = await serverKvasBackend.ForUser(userId).UserWalkieTalkieSettings()
            .Get(x => x.PttChatIds, cancellationToken)
            .ConfigureAwait(false);
        return pttChatIds.Contains(chatId);
    }
```

Remove any `using` directive that this leaves unused.

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
  --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -4
dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ReportPlaybackTest" 2>&1 | tail -4
```
Expected: all PASS (WalkieTalkiePushTest keeps its previous count — one test replaced, one added).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Users.Contracts/ServerKvasBackendExt.cs \
        tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs \
        tests/Streaming.IntegrationTests/ReportPlaybackTest.cs
git commit -m "feat(users): walkie-talkie armed predicate reads PttChatIds only"
```

---

### Task 3: Client PTT chat set + reply-core rewiring

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/IncomingVoiceActivityUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/WalkieReplyToggle.razor`

**Interfaces:**
- Consumes: `UserSettingsUIExt.UserWalkieTalkieSettings(...)` (Task 1).
- Produces: `ChatAudioUI.GetPttChatIds(CancellationToken)` → `Task<List<ChatId>>`, `[ComputeMethod(MinCacheDuration = 300)]`, virtual.
- Produces: `GetChatsYouNeedToKeepListeningTo` now returns `AlwaysListenedChatIds ∪ PttChatIds` (decision 8).
- Produces: `WalkieReplyToggle` becomes a start/stop toggle — a tap while the target chat is recording calls `WalkieTalkieReplyUI.StopReply()`.

- [ ] **Step 1: Add `GetPttChatIds` and union it into the listening set**

In `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`, replace `GetChatsYouNeedToKeepListeningTo` and add the new method beside it:

```csharp
    [ComputeMethod(MinCacheDuration = 300)] // Synced
    public virtual async Task<List<ChatId>> GetChatsYouNeedToKeepListeningTo(CancellationToken cancellationToken)
    {
        await Hub.ChatUI.WhenReady.ConfigureAwait(false);
        var alwaysListened = await UserSettingsUI.UserListeningSettings()
            .Get(x => x.AlwaysListenedChatIds, cancellationToken)
            .ConfigureAwait(false);
        // A PTT chat wakes you to hear someone, so it must also be listened to -
        // arming alone starts no player.
        var pttChatIds = await GetPttChatIds(cancellationToken).ConfigureAwait(false);
        return alwaysListened.Concat(pttChatIds).Distinct().ToList();
    }

    [ComputeMethod(MinCacheDuration = 300)] // Synced
    public virtual async Task<List<ChatId>> GetPttChatIds(CancellationToken cancellationToken)
    {
        await Hub.ChatUI.WhenReady.ConfigureAwait(false);
        return await UserSettingsUI.UserWalkieTalkieSettings()
            .Get(x => x.PttChatIds.ToList(), cancellationToken)
            .ConfigureAwait(false);
    }
```

- [ ] **Step 2: Point the walkie paths at the PTT set**

In `src/dotnet/UI.Blazor.App/Services/IncomingVoiceActivityUI.cs`, inside `TrackArmedChats`, change the captured computed from `GetChatsYouNeedToKeepListeningTo` to `GetPttChatIds`:

```csharp
        var cArmedChats = await Computed
            .Capture(() => ChatAudioUI.GetPttChatIds(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
```

In `src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs`, inside `RequestReply`, change the armed lookup:

```csharp
        var armed = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
```

In `src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/WalkieReplyToggle.razor`, change `ComputeState`'s first line:

```csharp
        var pttChatIds = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
        return pttChatIds.Contains(Chat.Id);
```

(rename the local from `armedChatIds` to `pttChatIds`.)

- [ ] **Step 3: Make the on-screen button a true toggle**

`StopReply` currently has zero callers, so an accidental open cannot be cancelled. In `WalkieReplyToggle.razor`, change the state type to a record so the button knows whether it is hot, and change the click handler.

Replace the `@inherits` line and the render block:

```razor
@inherits ComputedStateComponent<AppUIHub, WalkieReplyToggle.ComputedModel>
@{
    var m = State.Value;
}

@if (m.IsPtt) {
    <ButtonRound
        Class="@($"walkie-reply-btn {(m.IsHot ? "on" : "")} {Class}")"
        Click="@OnClick"
        Tooltip="@(m.IsHot ? "Stop walkie reply" : "Walkie reply")"
        TooltipPosition="FloatingPosition.Top">
        <i class="icon-talking text-2xl"></i>
    </ButtonRound>
}
```

Replace the state options, `ComputeState`, `OnClick`, and add the nested record:

```csharp
    protected override ComputedState<ComputedModel>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<ComputedModel>.Options {
                InitialValue = ComputedModel.None,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = GetStateCategory(t),
            });

    protected override async Task<ComputedModel> ComputeState(CancellationToken cancellationToken) {
        var pttChatIds = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        return new ComputedModel(pttChatIds.Contains(Chat.Id), recordingChatId == Chat.Id);
    }

    private void OnClick() {
        var isHot = State.Value.IsHot;
        _ = BackgroundTask.Run(
            () => isHot
                ? WalkieTalkieReplyUI.StopReply()
                : WalkieTalkieReplyUI.RequestReply(CancellationToken.None),
            Log, "Walkie reply toggle failed", CancellationToken.None);
    }

    // Nested types

    public sealed record ComputedModel(bool IsPtt, bool IsHot) {
        public static readonly ComputedModel None = new(false, false);
    }
```

- [ ] **Step 4: Build and verify the existing walkie unit tests still pass**

Run:
```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~ReplyTargetResolverTest|FullyQualifiedName~IncomingVoiceActivitySnapshotTest|FullyQualifiedName~HotMicColdStartTest|FullyQualifiedName~WalkieTalkieTest" 2>&1 | tail -4
```
Expected: `0 Error(s)`; all tests PASS (they cover pure helpers and are unaffected).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs \
        src/dotnet/UI.Blazor.App/Services/IncomingVoiceActivityUI.cs \
        src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs \
        src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/WalkieReplyToggle.razor
git commit -m "feat(audio-ui): client walkie paths read PttChatIds; reply button becomes a toggle"
```

In the task report, state explicitly that PTT chats are force-listened (decision 8) so the user can veto it.

---

### Task 4: Hot-window plumbing

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/RecordingIdleWindowTest.cs`

**Interfaces:**
- Consumes: `UserWalkieTalkieSettings.HotWindow` (Task 1); `ChatAudioUI.SetRecordingChatId` (existing).
- Produces: `ChatAudioUI.SetRecordingChatId(ChatId? chatId, bool isPushToTalk = false, TimeSpan? idleDuration = null)`.
- Produces: `ChatAudioUI.GetRecordingIdleOptions(TimeSpan? idleDuration, AudioSettings audioSettings)` → `RecordingIdleOptions`, `public static`, pure — the unit-testable part of the seam.

E1 shipped a 30 s hot window against a specced ~60 s because the window is `Constants.Audio.RecordingDuration`, shared with ordinary recording. This makes it a per-recording value without touching `RecordChat`'s control flow.

- [ ] **Step 1: Write the failing test**

Create `tests/Chat.UI.Blazor.UnitTests/RecordingIdleWindowTest.cs`:

```csharp
using ActualChat.Audio;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class RecordingIdleWindowTest
{
    private static readonly AudioSettings Settings = new();

    [Fact]
    public void NullIdleDurationKeepsDefaults()
    {
        var options = ChatAudioUI.GetRecordingIdleOptions(null, Settings);
        options.IdleTimeout.Should().Be(Constants.Audio.RecordingDuration);
        options.PreCountdownTimeout.Should().Be(Settings.IdleRecordingPreCountdownTimeout);
        options.CheckPeriod.Should().Be(Settings.IdleRecordingCheckPeriod);
    }

    [Fact]
    public void CustomIdleDurationShiftsPreCountdownWithIt()
    {
        var options = ChatAudioUI.GetRecordingIdleOptions(TimeSpan.FromSeconds(120), Settings);
        options.IdleTimeout.Should().Be(TimeSpan.FromSeconds(120));
        // The countdown cue must still start 10s before the close, as it does at the default 30s.
        options.PreCountdownTimeout.Should().Be(TimeSpan.FromSeconds(110));
        options.CheckPeriod.Should().Be(Settings.IdleRecordingCheckPeriod);
    }

    [Fact]
    public void ShortIdleDurationNeverYieldsNegativePreCountdown()
    {
        var options = ChatAudioUI.GetRecordingIdleOptions(TimeSpan.FromSeconds(5), Settings);
        options.PreCountdownTimeout.Should().Be(TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~RecordingIdleWindowTest" 2>&1 | tail -5`
Expected: build FAILS — `GetRecordingIdleOptions` does not exist.

- [ ] **Step 3: Add the helper and the field**

In `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs`, add the field beside the other private fields:

```csharp
    private volatile object? _recordingIdleDurationBox;
```

(a boxed `TimeSpan?` is used because `volatile` cannot be applied to `TimeSpan?`; the field is written on the caller's thread and read by the `RecordChat` worker.)

Add the pure helper as a public static member of `ChatAudioUI`:

```csharp
    public static RecordingIdleOptions GetRecordingIdleOptions(TimeSpan? idleDuration, AudioSettings audioSettings)
    {
        if (idleDuration is not { } duration)
            return new RecordingIdleOptions(
                Constants.Audio.RecordingDuration,
                audioSettings.IdleRecordingPreCountdownTimeout,
                audioSettings.IdleRecordingCheckPeriod);

        var preCountdown = Constants.Audio.RecordingDuration - audioSettings.IdleRecordingPreCountdownTimeout;
        return new RecordingIdleOptions(
            duration,
            (duration - preCountdown).Positive(),
            audioSettings.IdleRecordingCheckPeriod);
    }
```

Change the `SetRecordingChatId` signature and store the value at its top:

```csharp
    public ValueTask SetRecordingChatId(ChatId? chatId, bool isPushToTalk = false, TimeSpan? idleDuration = null)
    {
        _recordingIdleDurationBox = chatId is null ? null : (object?)idleDuration;
        if (chatId is not null)
            Hub.AudioAttachmentPlayer.OnConversationJoined();
        // ... rest unchanged
```

- [ ] **Step 4: Read it in `RecordChat`**

In `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs`, replace the `RecordingIdleOptions` construction at ~line 237:

```csharp
                var options = GetRecordingIdleOptions((TimeSpan?)_recordingIdleDurationBox, AudioSettings);
```

- [ ] **Step 5: Pass the configured hot window from the walkie reply**

In `src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs`, in `RequestReply`, read the setting and pass it:

```csharp
        var hotWindow = await Hub.UserSettingsUI.UserWalkieTalkieSettings()
            .Get(x => x.HotWindow, cancellationToken)
            .ConfigureAwait(false);
        await ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk: true, idleDuration: hotWindow)
            .ConfigureAwait(false);
```

If `UserSettingsUI` is not already reachable from `WalkieTalkieReplyUI`, add the private accessor beside the existing ones, matching their style:

```csharp
    private UserSettingsUI UserSettingsUI => Hub.UserSettingsUI;
```

- [ ] **Step 6: Run the test to verify it passes**

Run:
```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~RecordingIdleWindowTest" 2>&1 | tail -4
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
```
Expected: 3 PASS; `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs \
        src/dotnet/UI.Blazor.App/Services/ChatAudioUI.StateSync.cs \
        src/dotnet/UI.Blazor.App/Services/WalkieTalkieReplyUI.cs \
        tests/Chat.UI.Blazor.UnitTests/RecordingIdleWindowTest.cs
git commit -m "feat(audio-ui): per-recording idle window; walkie reply uses the configured hot window"
```

---

### Task 5: Pure gesture detectors

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/SensorSample.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/GestureEvent.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/FlipToTalkDetector.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/ShakeDetector.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/FaceDownDetector.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/GestureRecognizer.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/GestureDetectorTest.cs`

**Interfaces:**
- Consumes: `ShakeSensitivity` (Task 1).
- Produces (namespace `ActualChat.UI.Blazor.App.Services.Gestures`):
  - `readonly record struct SensorSample(Moment At, float X, float Y, float Z)` with `float Magnitude` and `GravityAxis GetDominantAxis(float minDominance)`.
  - `enum GravityAxis { None = 0, X, Y, Z }`
  - `enum GestureKind { None = 0, FlipToTalk, DoubleShake, FaceDown }`
  - `readonly record struct GestureEvent(GestureKind Kind, Moment At)`
  - `FlipToTalkDetector.Process(SensorSample) → bool`, `.Reset()`
  - `ShakeDetector(ShakeSensitivity)`, `.Process(SensorSample) → bool`, `.Reset()`, `.PeakDeviation` (float, for the practice panel), statics `GetMagnitudeThreshold(ShakeSensitivity)`, `GetReversalCount(ShakeSensitivity)`
  - `FaceDownDetector.Process(SensorSample) → bool`, `.SetProximityCovered(bool)`, `.Reset()`
  - `GestureRecognizer(GestureOptions)`, `.Process(SensorSample) → GestureEvent?`, `.SetProximityCovered(bool)`, `.Reset()`, `.Options` (get/set), `.ShakePeakDeviation` (float)
  - `sealed record GestureOptions(bool IsFlipToTalkEnabled, bool IsDoubleShakeEnabled, bool IsFaceDownEnabled, ShakeSensitivity ShakeSensitivity)`

Sample convention: MAUI's accelerometer reports **g units**, and a device lying flat face-up reads `Z ≈ -1`. Orientation is classified by which axis carries gravity, which is sign-free and therefore platform-convention-free; only face-down needs the sign.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat.UI.Blazor.UnitTests/GestureDetectorTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services.Gestures;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GestureDetectorTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    private static SensorSample Portrait(double atMs) => new(At(atMs), 0f, -1f, 0f);
    private static SensorSample Landscape(double atMs) => new(At(atMs), -1f, 0f, 0f);
    private static SensorSample FaceUp(double atMs) => new(At(atMs), 0f, 0f, -1f);
    private static SensorSample FaceDown(double atMs) => new(At(atMs), 0f, 0f, 1f);
    private static Moment At(double ms) => T0 + TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Flip_FiresOnPortraitLandscapePortrait()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Landscape(600)).Should().BeFalse();
        d.Process(Portrait(900)).Should().BeTrue();
    }

    [Fact]
    public void Flip_DoesNotFireOnHalfRotation()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Landscape(5000)).Should().BeFalse();
    }

    [Fact]
    public void Flip_DoesNotFireWhenReturnExceedsWindow()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(300)).Should().BeFalse();
        d.Process(Portrait(4000)).Should().BeFalse();
    }

    [Fact]
    public void Flip_DoesNotFireThroughFlat()
    {
        var d = new FlipToTalkDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Landscape(200)).Should().BeFalse();
        d.Process(FaceUp(400)).Should().BeFalse();
        d.Process(Portrait(600)).Should().BeFalse();
    }

    [Fact]
    public void Shake_FiresOnAlternatingSpikes()
    {
        var d = new ShakeDetector(ShakeSensitivity.Medium);
        Shake(d, ShakeSensitivity.Medium, reversals: 3, stepMs: 80).Should().BeTrue();
    }

    [Fact]
    public void Shake_DoesNotFireOnSingleSpike()
    {
        var d = new ShakeDetector(ShakeSensitivity.Medium);
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(new SensorSample(At(50), 0f, -3f, 0f)).Should().BeFalse();
        d.Process(Portrait(100)).Should().BeFalse();
    }

    [Fact]
    public void Shake_DoesNotFireWhenSpikesAreTooSlow()
    {
        var d = new ShakeDetector(ShakeSensitivity.Medium);
        Shake(d, ShakeSensitivity.Medium, reversals: 3, stepMs: 400).Should().BeFalse();
    }

    [Fact]
    public void Shake_HonoursDebounce()
    {
        var d = new ShakeDetector(ShakeSensitivity.High);
        Shake(d, ShakeSensitivity.High, reversals: 3, stepMs: 60).Should().BeTrue();
        Shake(d, ShakeSensitivity.High, reversals: 3, stepMs: 60, startMs: 400).Should().BeFalse();
        Shake(d, ShakeSensitivity.High, reversals: 3, stepMs: 60, startMs: 3000).Should().BeTrue();
    }

    [Theory]
    [InlineData(ShakeSensitivity.Low)]
    [InlineData(ShakeSensitivity.Medium)]
    public void Shake_SensitivityIsMonotonic(ShakeSensitivity fired)
    {
        // Anything that fires at a lower sensitivity must also fire at a higher one.
        var reversals = ShakeDetector.GetReversalCount(fired);
        var stronger = fired == ShakeSensitivity.Low ? ShakeSensitivity.Medium : ShakeSensitivity.High;
        Shake(new ShakeDetector(fired), fired, reversals, stepMs: 70).Should().BeTrue();
        Shake(new ShakeDetector(stronger), fired, reversals, stepMs: 70).Should().BeTrue();
    }

    [Fact]
    public void FaceDown_FiresAfterDwell()
    {
        var d = new FaceDownDetector();
        d.Process(FaceDown(0)).Should().BeFalse();
        d.Process(FaceDown(400)).Should().BeFalse();
        d.Process(FaceDown(1200)).Should().BeTrue();
    }

    [Fact]
    public void FaceDown_DoesNotFireOnTransientPickUp()
    {
        var d = new FaceDownDetector();
        d.Process(FaceDown(0)).Should().BeFalse();
        d.Process(FaceDown(200)).Should().BeFalse();
        d.Process(Portrait(400)).Should().BeFalse();
        d.Process(FaceDown(600)).Should().BeFalse();
    }

    [Fact]
    public void FaceDown_FiresOnCoveredAndUpright()
    {
        var d = new FaceDownDetector();
        d.SetProximityCovered(true);
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(1200)).Should().BeTrue();
    }

    [Fact]
    public void FaceDown_UprightAloneDoesNotFire()
    {
        var d = new FaceDownDetector();
        d.Process(Portrait(0)).Should().BeFalse();
        d.Process(Portrait(5000)).Should().BeFalse();
    }

    [Fact]
    public void Recognizer_RoutesOnlyToEnabledDetectors()
    {
        var options = new GestureOptions(false, true, true, ShakeSensitivity.Medium);
        var r = new GestureRecognizer(options);
        r.Process(Portrait(0));
        r.Process(Landscape(300));
        r.Process(Portrait(600)).Should().BeNull();
    }

    [Fact]
    public void Recognizer_StopBeatsStart()
    {
        var options = new GestureOptions(true, true, true, ShakeSensitivity.High);
        var r = new GestureRecognizer(options);
        // A shake that ends face-down must report the stop, never the start.
        r.Process(FaceDown(0));
        r.Process(new SensorSample(At(60), 0f, 0f, 3f));
        r.Process(new SensorSample(At(120), 0f, 0f, -2f));
        r.Process(new SensorSample(At(180), 0f, 0f, 3f));
        var e = r.Process(FaceDown(1500));
        e!.Value.Kind.Should().Be(GestureKind.FaceDown);
    }

    private static bool Shake(
        ShakeDetector detector,
        ShakeSensitivity sensitivity,
        int reversals,
        double stepMs,
        double startMs = 0)
    {
        // Alternating |a| spikes above and below 1g by more than the sensitivity threshold.
        var threshold = ShakeDetector.GetMagnitudeThreshold(sensitivity) + 0.2f;
        var hasFired = false;
        for (var i = 0; i <= reversals; i++) {
            var magnitude = i % 2 == 0 ? 1f + threshold : Math.Max(0f, 1f - threshold);
            var sample = new SensorSample(At(startMs + (i * stepMs)), 0f, -magnitude, 0f);
            hasFired |= detector.Process(sample);
        }
        return hasFired;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~GestureDetectorTest" 2>&1 | tail -5`
Expected: build FAILS — none of the gesture types exist.

- [ ] **Step 3: Create the sample and event types**

`src/dotnet/UI.Blazor.App/Services/Gestures/SensorSample.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// One accelerometer reading in g units. MAUI normalizes axes across platforms;
/// a device lying flat face-up reads Z ≈ -1.
/// </summary>
public readonly record struct SensorSample(Moment At, float X, float Y, float Z)
{
    public float Magnitude => MathF.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public GravityAxis GetDominantAxis(float minDominance)
    {
        var (ax, ay, az) = (MathF.Abs(X), MathF.Abs(Y), MathF.Abs(Z));
        if (ax >= minDominance && ax > ay && ax > az)
            return GravityAxis.X;
        if (ay >= minDominance && ay > ax && ay > az)
            return GravityAxis.Y;
        if (az >= minDominance && az > ax && az > ay)
            return GravityAxis.Z;

        return GravityAxis.None;
    }
}

public enum GravityAxis
{
    None = 0,
    X,
    Y,
    Z,
}
```

`src/dotnet/UI.Blazor.App/Services/Gestures/GestureEvent.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services.Gestures;

public readonly record struct GestureEvent(GestureKind Kind, Moment At);

public enum GestureKind
{
    None = 0,
    FlipToTalk,
    DoubleShake,
    FaceDown,
}
```

- [ ] **Step 4: Create `FlipToTalkDetector`**

`src/dotnet/UI.Blazor.App/Services/Gestures/FlipToTalkDetector.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires on portrait → landscape → portrait within <see cref="FlipWindow"/>.
/// Two rotations in sequence is what makes it deliberate enough to open the mic.
/// </summary>
public sealed class FlipToTalkDetector
{
    public static readonly TimeSpan FlipWindow = TimeSpan.FromSeconds(2);
    private const float MinDominance = 0.7f;

    private GravityAxis _lastAxis;
    private Moment _leftPortraitAt;
    private bool _hasLeftPortrait;

    public bool Process(SensorSample sample)
    {
        var axis = sample.GetDominantAxis(MinDominance);
        if (axis == GravityAxis.None || axis == _lastAxis)
            return false;

        var lastAxis = _lastAxis;
        _lastAxis = axis;
        if (axis == GravityAxis.X && lastAxis == GravityAxis.Y) {
            _leftPortraitAt = sample.At;
            _hasLeftPortrait = true;
            return false;
        }
        if (axis == GravityAxis.Y && _hasLeftPortrait && sample.At - _leftPortraitAt <= FlipWindow) {
            Reset();
            _lastAxis = axis;
            return true;
        }

        _hasLeftPortrait = false;
        return false;
    }

    public void Reset()
    {
        _lastAxis = GravityAxis.None;
        _hasLeftPortrait = false;
        _leftPortraitAt = default;
    }
}
```

- [ ] **Step 5: Create `ShakeDetector`**

`src/dotnet/UI.Blazor.App/Services/Gestures/ShakeDetector.cs`:

```csharp
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires when the acceleration magnitude reverses across ±threshold around 1g
/// enough times inside <see cref="ReversalWindow"/>.
/// </summary>
public sealed class ShakeDetector(ShakeSensitivity sensitivity)
{
    public static readonly TimeSpan ReversalWindow = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    private readonly List<Moment> _reversals = new();
    private int _lastSign;
    private Moment _debouncedUntil;

    public ShakeSensitivity Sensitivity { get; } = sensitivity;
    public float PeakDeviation { get; private set; }

    // Lower sensitivity demands a harder shake, so the firing sets nest: Low ⊆ Medium ⊆ High.
    public static float GetMagnitudeThreshold(ShakeSensitivity sensitivity)
        => sensitivity switch {
            ShakeSensitivity.Low => 1.2f,
            ShakeSensitivity.High => 0.5f,
            _ => 0.8f,
        };

    public static int GetReversalCount(ShakeSensitivity sensitivity)
        => sensitivity == ShakeSensitivity.Low ? 4 : 3;

    public bool Process(SensorSample sample)
    {
        var deviation = sample.Magnitude - 1f;
        PeakDeviation = MathF.Max(PeakDeviation * 0.9f, MathF.Abs(deviation));
        if (sample.At < _debouncedUntil)
            return false;

        var threshold = GetMagnitudeThreshold(Sensitivity);
        var sign = deviation > threshold ? 1
            : deviation < -threshold ? -1
            : 0;
        if (sign == 0 || sign == _lastSign)
            return false;

        var hadSign = _lastSign != 0;
        _lastSign = sign;
        if (!hadSign)
            return false;

        _reversals.Add(sample.At);
        _reversals.RemoveAll(at => sample.At - at > ReversalWindow);
        if (_reversals.Count < GetReversalCount(Sensitivity))
            return false;

        _debouncedUntil = sample.At + Debounce;
        _reversals.Clear();
        _lastSign = 0;
        return true;
    }

    public void Reset()
    {
        _reversals.Clear();
        _lastSign = 0;
        _debouncedUntil = default;
        PeakDeviation = 0f;
    }
}
```

- [ ] **Step 6: Create `FaceDownDetector`**

`src/dotnet/UI.Blazor.App/Services/Gestures/FaceDownDetector.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires when the device is held face-down, or covered and near-vertical (pocket),
/// for <see cref="Dwell"/> — the stop gesture, so twitchy signals are acceptable here.
/// </summary>
public sealed class FaceDownDetector
{
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(700);
    // MAUI reports Z ≈ -1 face-up, so face-down is the positive end.
    private const float FaceDownZ = 0.85f;
    private const float PocketMaxZ = 0.5f;

    private Moment? _heldSince;
    private bool _isCovered;
    private bool _hasFired;

    public void SetProximityCovered(bool isCovered)
    {
        _isCovered = isCovered;
        if (!isCovered)
            _heldSince = null;
    }

    public bool Process(SensorSample sample)
    {
        var isFaceDown = sample.Z >= FaceDownZ;
        var isPocketed = _isCovered && MathF.Abs(sample.Z) <= PocketMaxZ;
        if (!isFaceDown && !isPocketed) {
            _heldSince = null;
            _hasFired = false;
            return false;
        }

        _heldSince ??= sample.At;
        if (_hasFired || sample.At - _heldSince.Value < Dwell)
            return false;

        _hasFired = true;
        return true;
    }

    public void Reset()
    {
        _heldSince = null;
        _hasFired = false;
        _isCovered = false;
    }
}
```

- [ ] **Step 7: Create `GestureRecognizer`**

`src/dotnet/UI.Blazor.App/Services/Gestures/GestureRecognizer.cs`:

```csharp
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

public sealed record GestureOptions(
    bool IsFlipToTalkEnabled,
    bool IsDoubleShakeEnabled,
    bool IsFaceDownEnabled,
    ShakeSensitivity ShakeSensitivity);

/// <summary>
/// Routes samples to the enabled detectors and emits a single gesture stream.
/// The stop gesture is evaluated first: on the mic, closing always beats opening.
/// </summary>
public sealed class GestureRecognizer
{
    private readonly FlipToTalkDetector _flip = new();
    private readonly FaceDownDetector _faceDown = new();
    private ShakeDetector _shake;
    private GestureOptions _options;

    public GestureOptions Options {
        get => _options;
        set {
            if (value.ShakeSensitivity != _options.ShakeSensitivity)
                _shake = new ShakeDetector(value.ShakeSensitivity);
            _options = value;
        }
    }

    public float ShakePeakDeviation => _shake.PeakDeviation;

    public GestureRecognizer(GestureOptions options)
    {
        _options = options;
        _shake = new ShakeDetector(options.ShakeSensitivity);
    }

    public void SetProximityCovered(bool isCovered)
        => _faceDown.SetProximityCovered(isCovered);

    public GestureEvent? Process(SensorSample sample)
    {
        if (_options.IsFaceDownEnabled && _faceDown.Process(sample))
            return new GestureEvent(GestureKind.FaceDown, sample.At);
        if (_options.IsFlipToTalkEnabled && _flip.Process(sample))
            return new GestureEvent(GestureKind.FlipToTalk, sample.At);
        if (_options.IsDoubleShakeEnabled && _shake.Process(sample))
            return new GestureEvent(GestureKind.DoubleShake, sample.At);

        return null;
    }

    public void Reset()
    {
        _flip.Reset();
        _shake.Reset();
        _faceDown.Reset();
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~GestureDetectorTest" 2>&1 | tail -4`
Expected: all PASS.

If a threshold test fails, adjust the **test's** sample construction only if the sample sequence is unrealistic; do NOT loosen a detector until it passes a sequence a user would not produce. The `Recognizer_StopBeatsStart` test in particular must not be weakened — it encodes the fail-closed rule.

- [ ] **Step 9: Commit**

```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
git add src/dotnet/UI.Blazor.App/Services/Gestures/ tests/Chat.UI.Blazor.UnitTests/GestureDetectorTest.cs
git commit -m "feat(audio-ui): pure gesture detectors - flip, shake, face-down + recognizer"
```

---

### Task 6: Sensor feed

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/SensorFeed.cs`
- Create: `src/dotnet/App.Maui/Services/MauiSensorFeed.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`
- Modify: `src/dotnet/App.Maui/Module/MauiAppModule.cs`

**Interfaces:**
- Consumes: `SensorSample` (Task 5).
- Produces: `ActualChat.UI.Blazor.App.Services.Gestures.SensorFeed` — `virtual bool IsAccelerometerAvailable`, `virtual bool IsProximityAvailable`, `event Action<SensorSample>? SampleReceived`, `event Action<bool>? ProximityChanged`, `virtual void StartAccelerometer()/StopAccelerometer()/StartProximity()/StopProximity()`, `protected void OnSample(SensorSample)`, `protected void OnProximityChanged(bool)`.
- The base class is a working no-op: on web it reports nothing available and emits nothing, which is exactly what the settings UI needs to render gesture sections as unavailable.

- [ ] **Step 1: Create the base feed**

`src/dotnet/UI.Blazor.App/Services/Gestures/SensorFeed.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Source of timestamped accelerometer and proximity readings.
/// The base implementation is a no-op: there are no sensors on the web.
/// </summary>
public class SensorFeed
{
    public event Action<SensorSample>? SampleReceived;
    public event Action<bool>? ProximityChanged;

    public virtual bool IsAccelerometerAvailable => false;
    public virtual bool IsProximityAvailable => false;

    public virtual void StartAccelerometer()
    { }

    public virtual void StopAccelerometer()
    { }

    public virtual void StartProximity()
    { }

    public virtual void StopProximity()
    { }

    protected void OnSample(SensorSample sample)
        => SampleReceived?.Invoke(sample);

    protected void OnProximityChanged(bool isCovered)
        => ProximityChanged?.Invoke(isCovered);
}
```

- [ ] **Step 2: Create the MAUI feed**

`src/dotnet/App.Maui/Services/MauiSensorFeed.cs`. Follow `MauiThermalTracker` exactly for the per-platform `#if` shape.

```csharp
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSensorFeed(AppUIHub hub) : SensorFeed
{
    private bool _isAccelerometerOn;
    private bool _isProximityOn;

    private ILogger Log => field ??= hub.LogFor(GetType());

    public override bool IsAccelerometerAvailable => Accelerometer.Default.IsSupported;

    public override void StartAccelerometer()
    {
        if (_isAccelerometerOn || !Accelerometer.Default.IsSupported)
            return;

        try {
            Accelerometer.Default.ReadingChanged += OnReadingChanged;
            Accelerometer.Default.Start(SensorSpeed.UI);
            _isAccelerometerOn = true;
        }
        catch (Exception e) {
            Accelerometer.Default.ReadingChanged -= OnReadingChanged;
            Log.LogWarning(e, "Failed to start the accelerometer");
        }
    }

    public override void StopAccelerometer()
    {
        if (!_isAccelerometerOn)
            return;

        _isAccelerometerOn = false;
        Accelerometer.Default.ReadingChanged -= OnReadingChanged;
        try {
            Accelerometer.Default.Stop();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to stop the accelerometer");
        }
    }

    private void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        var a = e.Reading.Acceleration;
        OnSample(new SensorSample(hub.Clocks.CpuClock.Now, a.X, a.Y, a.Z));
    }
}
```

Then append the proximity half inside the same class, per platform:

```csharp
#if ANDROID
    private ProximityListener? _proximityListener;

    public override bool IsProximityAvailable
        => GetProximitySensor() is not null;

    public override void StartProximity()
    {
        if (_isProximityOn)
            return;

        var sensorManager = GetSensorManager();
        var sensor = GetProximitySensor();
        if (sensorManager is null || sensor is null)
            return;

        _proximityListener = new ProximityListener(sensor.MaximumRange, OnProximityChanged);
        sensorManager.RegisterListener(
            _proximityListener, sensor, Android.Hardware.SensorDelay.Normal);
        _isProximityOn = true;
    }

    public override void StopProximity()
    {
        if (!_isProximityOn)
            return;

        _isProximityOn = false;
        if (_proximityListener is { } listener)
            GetSensorManager()?.UnregisterListener(listener);
        _proximityListener = null;
        OnProximityChanged(false);
    }

    private static Android.Hardware.SensorManager? GetSensorManager()
        => Android.App.Application.Context.GetSystemService(Android.Content.Context.SensorService)
            as Android.Hardware.SensorManager;

    private static Android.Hardware.Sensor? GetProximitySensor()
        => GetSensorManager()?.GetDefaultSensor(Android.Hardware.SensorType.Proximity);

    private sealed class ProximityListener(float maxRange, Action<bool> onChange)
        : Java.Lang.Object, Android.Hardware.ISensorEventListener
    {
        public void OnAccuracyChanged(Android.Hardware.Sensor? sensor, Android.Hardware.SensorStatus accuracy)
        { }

        public void OnSensorChanged(Android.Hardware.SensorEvent? e)
        {
            if (e?.Values is not { Count: > 0 } values)
                return;

            onChange(values[0] < maxRange);
        }
    }
#elif IOS
    private Foundation.NSObject? _proximityObserver;

    public override bool IsProximityAvailable => true;

    public override void StartProximity()
    {
        if (_isProximityOn)
            return;

        UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = true;
        _proximityObserver = Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
            UIKit.UIDevice.ProximityStateDidChangeNotification,
            _ => OnProximityChanged(UIKit.UIDevice.CurrentDevice.ProximityState));
        _isProximityOn = true;
    }

    public override void StopProximity()
    {
        if (!_isProximityOn)
            return;

        _isProximityOn = false;
        if (_proximityObserver is { } observer)
            Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
        _proximityObserver = null;
        UIKit.UIDevice.CurrentDevice.ProximityMonitoringEnabled = false;
        OnProximityChanged(false);
    }
#endif
```

The `StopProximity` implementations end with `OnProximityChanged(false)` so a detector can never stay latched on a stale "covered" reading after the subscription drops.

- [ ] **Step 3: Register the feed**

In `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`, add to the "Live stream UI" block after `fusion.AddService<WalkieTalkieReplyUI>(...)`:

```csharp
        if (!HostInfo.HostKind.IsMauiApp())
            services.AddScoped(_ => new SensorFeed()); // MauiSensorFeed is registered in MauiAppModule
```

Add `using ActualChat.UI.Blazor.App.Services.Gestures;` to the file's usings. If `HostInfo` is not already used in this module, read it the same way `BlazorUICoreModule` does (`HostInfo.HostKind`) — `HostModule` exposes it.

In `src/dotnet/App.Maui/Module/MauiAppModule.cs`, add to the "Audio" block, right before `services.AddScoped<IAudioInitializer>(...)`:

```csharp
        services.AddScoped<SensorFeed>(c => new MauiSensorFeed(c.AppUIHub()));
```

Add `using ActualChat.UI.Blazor.App.Services.Gestures;` to that file's usings.

- [ ] **Step 4: Build**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: `0 Error(s)`.

The CI solution filter does not build the Android/iOS targets, so the `#if ANDROID` / `#if IOS` blocks are **not** compiled here. Note that in the task report as host-deferred; do not attempt an Android or iOS build on this machine (the Android resource designer generates empty on this host — a known, pre-existing condition).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/Gestures/SensorFeed.cs \
        src/dotnet/App.Maui/Services/MauiSensorFeed.cs \
        src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs \
        src/dotnet/App.Maui/Module/MauiAppModule.cs
git commit -m "feat(maui): accelerometer + proximity sensor feed behind a platform-neutral base"
```

---

### Task 7: `GestureUI` — activation policy and routing

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/GestureActivationPolicy.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/Gestures/GestureUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/AppScopedServiceStarter.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/GestureActivationPolicyTest.cs`

**Interfaces:**
- Consumes: `GestureRecognizer`, `GestureOptions`, `GestureKind`, `SensorFeed` (Tasks 5–6); `ChatAudioUI.GetPttChatIds` (Task 3); `IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt` (E1); `WalkieTalkieReplyUI.RequestReply`/`StopReply` (E1); `UserWalkieTalkieSettings`, `UserAppSettings.IsFaceDownMicStopEnabled` (Task 1).
- Produces: `GestureActivationPolicy.ShouldSenseStartGestures(bool areGesturesAlwaysOn, bool isPracticeMode, IReadOnlyList<ChatId> pttChatIds, IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt, Moment now, TimeSpan recencyWindow)` → `bool`, `public static`.
- Produces: `GestureUI` with `bool IsPracticeMode { get; set; }`, `event Action<GestureEvent>? PracticeGestureDetected`, `SensorFeed Feed { get; }`, `float ShakePeakDeviation { get; }`, `int SampleCount { get; }`. It is started **explicitly** by `AppScopedServiceStarter`, mirroring `ThrottledTranslations` — NOT via `INotifyInitialized`, which a lambda-factory `AddScoped` registration never invokes.
- Produces: `AppUIHub.GestureUI`.

- [ ] **Step 1: Write the failing policy test**

Create `tests/Chat.UI.Blazor.UnitTests/GestureActivationPolicyTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GestureActivationPolicyTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(150);
    private static readonly ChatId ChatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId ChatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");
    private static readonly IReadOnlyDictionary<ChatId, Moment> NoVoice = new Dictionary<ChatId, Moment>();

    [Fact]
    public void SensesInsideTheAnswerWindow()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(20) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeTrue();
    }

    [Fact]
    public void DoesNotSenseOutsideTheAnswerWindow()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(400) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeFalse();
    }

    [Fact]
    public void IgnoresVoiceInNonPttChats()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatB] = T0 - TimeSpan.FromSeconds(5) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeFalse();
    }

    [Fact]
    public void AlwaysOnSensesWithoutVoice()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(true, false, [ChatA], NoVoice, T0, Window)
            .Should().BeTrue();

    [Fact]
    public void AlwaysOnStillNeedsAtLeastOnePttChat()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(true, false, [], NoVoice, T0, Window)
            .Should().BeFalse();

    [Fact]
    public void PracticeModeSensesWithNoPttChatsAtAll()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(false, true, [], NoVoice, T0, Window)
            .Should().BeTrue();
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~GestureActivationPolicyTest" 2>&1 | tail -5`
Expected: build FAILS — `GestureActivationPolicy` does not exist.

- [ ] **Step 3: Create the policy**

`src/dotnet/UI.Blazor.App/Services/Gestures/GestureActivationPolicy.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services.Gestures;

public static class GestureActivationPolicy
{
    public static bool ShouldSenseStartGestures(
        bool areGesturesAlwaysOn,
        bool isPracticeMode,
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        Moment now,
        TimeSpan recencyWindow)
    {
        if (isPracticeMode)
            return true;
        if (pttChatIds.Count == 0)
            return false;
        if (areGesturesAlwaysOn)
            return true;

        var since = now - recencyWindow;
        foreach (var chatId in pttChatIds)
            if (lastIncomingVoiceAt.TryGetValue(chatId, out var at) && at > since)
                return true;

        return false;
    }
}
```

- [ ] **Step 4: Create `GestureUI`**

`src/dotnet/UI.Blazor.App/Services/Gestures/GestureUI.cs`. It mirrors `IncomingVoiceActivityUI`: a `UIWorkerBase<AppUIHub>` that self-starts via `INotifyInitialized` and runs its loop under `RetryForever`.

```csharp
using ActualChat.Users;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Owns the sensor subscription lifecycle and turns recognized gestures into
/// walkie-talkie reply start/stop. Sensors are live only while a reply is plausible.
/// </summary>
public sealed class GestureUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    private readonly GestureRecognizer _recognizer = new(
        new GestureOptions(false, false, false, ShakeSensitivity.Medium));
    private volatile bool _isPracticeMode;
    private int _sampleCount;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private WalkieTalkieReplyUI WalkieTalkieReplyUI => Hub.WalkieTalkieReplyUI;
    private UserSettingsUI UserSettingsUI => Hub.UserSettingsUI;

    public SensorFeed Feed { get; } = hub.Services.GetRequiredService<SensorFeed>();
    public float ShakePeakDeviation => _recognizer.ShakePeakDeviation;
    public int SampleCount => Volatile.Read(ref _sampleCount);
    public event Action<GestureEvent>? PracticeGestureDetected;

    public bool IsPracticeMode {
        get => _isPracticeMode;
        set {
            _isPracticeMode = value;
            _recognizer.Reset();
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        Feed.SampleReceived += OnSample;
        Feed.ProximityChanged += OnProximityChanged;
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(TrackActivation)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task TrackActivation(CancellationToken cancellationToken)
    {
        var isSensing = false;
        var isProximityOn = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var settings = await UserSettingsUI.UserWalkieTalkieSettings()
                    .Get(cancellationToken)
                    .ConfigureAwait(false);
                var isFaceDownStopEnabled = await UserSettingsUI.UserAppSettings()
                    .Get(x => x.IsFaceDownMicStopEnabled ?? false, cancellationToken)
                    .ConfigureAwait(false);
                var pttChatIds = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
                var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
                var isMicOpen = recordingChatId is not null;
                var mustSenseStart = GestureActivationPolicy.ShouldSenseStartGestures(
                    settings.AreGesturesAlwaysOn,
                    _isPracticeMode,
                    pttChatIds,
                    IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt(),
                    Clocks.ServerClock.Now,
                    Constants.Audio.WalkieTalkieReplyRecencyWindow);
                var mustSenseStop = isFaceDownStopEnabled && (isMicOpen || _isPracticeMode);

                _recognizer.Options = new GestureOptions(
                    settings.IsFlipToTalkEnabled && mustSenseStart,
                    settings.IsDoubleShakeEnabled && mustSenseStart,
                    mustSenseStop,
                    settings.ShakeSensitivity);

                var mustSense = mustSenseStart || mustSenseStop;
                if (mustSense != isSensing) {
                    isSensing = mustSense;
                    if (mustSense)
                        Feed.StartAccelerometer();
                    else {
                        Feed.StopAccelerometer();
                        _recognizer.Reset();
                    }
                }
                if (mustSenseStop != isProximityOn) {
                    isProximityOn = mustSenseStop;
                    if (mustSenseStop)
                        Feed.StartProximity();
                    else
                        Feed.StopProximity();
                }

                await Clocks.CpuClock.Delay(Constants.Audio.WalkieTalkieIdleCheckPeriod, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally {
            Feed.StopAccelerometer();
            Feed.StopProximity();
        }
    }

    private void OnProximityChanged(bool isCovered)
        => _recognizer.SetProximityCovered(isCovered);

    private void OnSample(SensorSample sample)
    {
        Interlocked.Increment(ref _sampleCount);
        if (_recognizer.Process(sample) is not { } gesture)
            return;

        // Practice never transmits: rehearsing a gesture in Settings must not open the mic.
        if (_isPracticeMode) {
            PracticeGestureDetected?.Invoke(gesture);
            return;
        }

        var whenHandled = gesture.Kind == GestureKind.FaceDown
            ? ChatAudioUI.SetRecordingChatId(null).AsTask()
            : WalkieTalkieReplyUI.RequestReply(CancellationToken.None);
        _ = BackgroundTask.Run(() => whenHandled, Log, $"{gesture.Kind} handling failed", CancellationToken.None);
    }
}
```

The 15 s re-evaluation cadence (`WalkieTalkieIdleCheckPeriod`) is a deliberate poll rather than a computed-change subscription: the answer window expires on a wall clock, not on an invalidation, so something must re-check it anyway.

- [ ] **Step 5: Register and eagerly start it**

In `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`, beside the `SensorFeed` registration:

```csharp
        services.AddScoped(c => new GestureUI(c.AppUIHub()));
```

In `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs`, add beside the other walkie accessors:

```csharp
    public GestureUI GestureUI => field ??= Services.GetRequiredService<GestureUI>();
```

Add `using ActualChat.UI.Blazor.App.Services.Gestures;` to `AppUIHub.cs` if the namespace isn't already imported.

In `src/dotnet/UI.Blazor.App/Services/AppScopedServiceStarter.cs`, add right after the `IncomingVoiceActivityUI` touch line, matching the explicit-start form already used two lines below for `ThrottledTranslations`:

```csharp
            Hub.GestureUI.Start();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~GestureActivationPolicyTest" 2>&1 | tail -4
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
```
Expected: 6 PASS; `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/Gestures/GestureActivationPolicy.cs \
        src/dotnet/UI.Blazor.App/Services/Gestures/GestureUI.cs \
        src/dotnet/UI.Blazor.App/Services/AppUIHub.cs \
        src/dotnet/UI.Blazor.App/Services/AppScopedServiceStarter.cs \
        src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs \
        tests/Chat.UI.Blazor.UnitTests/GestureActivationPolicyTest.cs
git commit -m "feat(audio-ui): GestureUI - answer-window-scoped sensing, gesture routing, practice mode"
```

---

### Task 8: Push to Talk settings tab

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkSettings.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkPracticePanel.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/Settings/PttChatPickerModal.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/SettingsTabId.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/SettingsModal.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/settings-modal.css`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` (modal type map)

**Interfaces:**
- Consumes: `UserWalkieTalkieSettings` + accessor (Task 1); `GestureUI` (Task 7); `SensorFeed.IsAccelerometerAvailable` (Task 6); `ContactSelector` (existing); `HostInfo.HostKind.IsMauiApp()` (existing).
- Produces: `SettingsTabId.PushToTalk`; `PttChatPickerModal.Model(IReadOnlySet<ChatId> ExcludedChatIds, int MaxCount)` registered in the `IModalView` type map.

- [ ] **Step 1: Add the tab id and register the tab**

In `src/dotnet/UI.Blazor.App/Components/Settings/SettingsTabId.cs`, add after `Transcription`:

```csharp
    public static readonly string PushToTalk = nameof(PushToTalk).Decapitalize();
```

In `src/dotnet/UI.Blazor.App/Components/Settings/SettingsModal.razor`, insert after the Transcription tab and renumber the tabs below it (`App` 4→5, `Sessions` 5→6, `ApiKeys` 6→7, `Privacy` 7→8) in **both** `@key` and `TabIndex`:

```razor
        <SettingsTab @key="4" TabIndex="4" Title="Push to Talk" Id="@SettingsTabId.PushToTalk" IconClass="icon-talking">
            <PushToTalkSettings/>
        </SettingsTab>
```

- [ ] **Step 2: Create the chat picker modal**

`src/dotnet/UI.Blazor.App/Components/Settings/PttChatPickerModal.razor`, modeled on `ForwardMessageModal`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@implements IModalView<PttChatPickerModal.Model>

<DialogFrame
    Class="ptt-chat-picker-modal"
    Title="Add a Push to Talk chat"
    HasCloseButton="true"
    NarrowViewSettings="@_viewSettings">
    <Body>
    <FormBlock Class="with-contact-list">
        <ContactSelector
            @ref="@_contactSelectorRef"
            ExcludeChatIds="@ModalModel.ExcludedChatIds"
            SearchQuery="@_searchQuery"
            Changed="@StateHasChanged">
            <SearchBox
                Placeholder="Search chats"
                MaxLength="@Constants.Chat.MaxSearchFilterLength"
                TextChanged="@OnFilter"/>
            <ContactSelectorListView/>
        </ContactSelector>
    </FormBlock>
    </Body>
    <Buttons>
        <Button Type="@ButtonType.Button" Class="btn-modal" Click="@(() => Modal.Close())">Cancel</Button>
        <Button Type="@ButtonType.Submit" Class="btn-modal btn-primary" Click="@OnAdd" IsDisabled="@(!CanAdd)">
            Add
        </Button>
    </Buttons>
</DialogFrame>

@code {
    private ContactSelector? _contactSelectorRef;
    private SearchQuery _searchQuery;
    private DialogFrameNarrowViewSettings _viewSettings = null!;

    private ImmutableHashSet<ChatId> SelectedChatIds
        => _contactSelectorRef?.SelectedChatIds.Value ?? ImmutableHashSet<ChatId>.Empty;
    private bool CanAdd
        => SelectedChatIds.Count > 0 && SelectedChatIds.Count <= ModalModel.MaxCount;

    [CascadingParameter] public Modal Modal { get; set; } = null!;
    [Parameter] public Model ModalModel { get; set; } = null!;

    protected override void OnInitialized()
        => _viewSettings = DialogFrameNarrowViewSettings.ForSubmitButton(OnAdd, "Add");

    private void OnFilter(string filter) {
        _searchQuery = new SearchQuery(filter);
        StateHasChanged();
    }

    private async Task OnAdd() {
        var chatIds = SelectedChatIds;
        if (chatIds.Count == 0)
            return;

        var settingsUI = Hub.UserSettingsUI;
        await settingsUI.UserWalkieTalkieSettings().Update(x => {
            foreach (var chatId in chatIds) {
                if (x.PttChatIds.Length >= UserWalkieTalkieSettings.MaxChatCount)
                    break;

                x = x.WithPttChat(chatId);
            }
            return x;
        }, CancellationToken.None);
        Modal.Close();
    }

    // Nested types

    public sealed record Model(IReadOnlySet<ChatId> ExcludedChatIds, int MaxCount);
}
```

Match `ForwardMessageModal`'s exact `DialogFrame` parameter names and its `@inherits`/`Hub` access pattern — inspect it before writing, and copy the shape rather than inventing parameters. If `UserSettingsUI.Update` there takes a different overload shape (e.g. no `CancellationToken`), follow the call sites in `VoiceSettingsListeningModalPage.razor`.

Register the modal in `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` in the `IModalView` type map that already contains `ForwardMessageModal`:

```csharp
            .Add<PttChatPickerModal.Model, PttChatPickerModal>()
```

- [ ] **Step 3: Create the practice panel**

`src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkPracticePanel.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@implements IDisposable
@inherits ComponentBase
@{
    var lastGesture = _lastGesture is { } g ? g.Kind.ToString() : "none yet";
}

<Tile Class="ptt-practice">
    <TileItem IsHoverable="false">
        <Icon><i class="icon-talking text-2xl"></i></Icon>
        <Content>Practice</Content>
        <Caption>@(_isOn ? "Try a gesture - it won't transmit" : "Off")</Caption>
        <Right>
            <Toggle IsChecked="@_isOn" Click="@OnToggle"/>
        </Right>
    </TileItem>
    @if (_isOn) {
        <div class="c-readout">
            <div>Samples: @GestureUI.SampleCount</div>
            <div>Shake peak: @GestureUI.ShakePeakDeviation.ToString("F2") g</div>
            <div>Last gesture: @lastGesture</div>
        </div>
    }
</Tile>

@code {
    private bool _isOn;
    private GestureEvent? _lastGesture;
    private CancellationTokenSource? _refreshCts;

    [CascadingParameter] public AppUIHub Hub { get; set; } = null!;

    private GestureUI GestureUI => Hub.GestureUI;

    public void Dispose() {
        Stop();
    }

    private void OnToggle() {
        if (_isOn)
            Stop();
        else
            Start();
        StateHasChanged();
    }

    private void Start() {
        _isOn = true;
        _lastGesture = null;
        GestureUI.PracticeGestureDetected += OnGesture;
        GestureUI.IsPracticeMode = true;
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        _ = BackgroundTask.Run(() => RefreshLoop(cts.Token), CancellationToken.None);
    }

    private void Stop() {
        if (!_isOn)
            return;

        _isOn = false;
        GestureUI.IsPracticeMode = false;
        GestureUI.PracticeGestureDetected -= OnGesture;
        _refreshCts.CancelAndDisposeSilently();
        _refreshCts = null;
    }

    private async Task RefreshLoop(CancellationToken cancellationToken) {
        // The readout is a liveness indicator - a dead sensor must look dead, not mysterious.
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(500, cancellationToken).ConfigureAwait(true);
            await InvokeAsync(StateHasChanged).ConfigureAwait(true);
        }
    }

    private void OnGesture(GestureEvent gesture) {
        _lastGesture = gesture;
        _ = InvokeAsync(StateHasChanged);
    }
}
```

Add `@using ActualChat.UI.Blazor.App.Services.Gestures` at the top if the namespace isn't in `_Imports.razor`. Check `_Imports.razor` first; if you add the namespace there instead, note it in the report. Also confirm how sibling settings components obtain `Hub` — if they inherit a base that supplies it, inherit the same base rather than taking a cascading parameter.

- [ ] **Step 4: Create the settings tab body**

`src/dotnet/UI.Blazor.App/Components/Settings/PushToTalkSettings.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Hosting
@inherits ComputedStateComponent<AppUIHub, PushToTalkSettings.Model>
@{
    var m = State.Value;
    var hasSensors = HostInfo.HostKind.IsMauiApp() && GestureUI.Feed.IsAccelerometerAvailable;
    var canAddChat = m.Chats.Count < UserWalkieTalkieSettings.MaxChatCount;
}

<TileTopic Topic="Push to Talk chats"/>
<Tile>
    @foreach (var chat in m.Chats) {
        <TileItem IsHoverable="false">
            <Icon><i class="icon-talking text-2xl"></i></Icon>
            <Content>@chat.Title</Content>
            <Right>
                <ButtonRound Class="btn-xs btn-danger" Tooltip="Remove" Click="@(() => OnRemove(chat.Id))">
                    <i class="icon-minus-circle text-2xl"></i>
                </ButtonRound>
            </Right>
        </TileItem>
    }
    @if (m.Chats.Count == 0) {
        <p class="c-description">
            No chats yet. A Push to Talk chat may wake your device when someone starts speaking.
        </p>
    }
</Tile>
<Button Class="add-avatar-btn" Click="@OnAddChat" IsDisabled="@(!canAddChat)">
    <Icon><i class="icon-plus text-xl"></i></Icon>
    <Title>@(canAddChat ? "Add chat" : $"Up to {UserWalkieTalkieSettings.MaxChatCount} chats")</Title>
</Button>

@if (hasSensors) {
    <TileTopic Topic="Answer gestures"/>
    <Tile>
        <TileItem Click="@OnToggleFlip">
            <Icon><i class="icon-refresh text-2xl"></i></Icon>
            <Content>Flip to talk</Content>
            <Caption>Rotate the phone 90° and back</Caption>
            <Right><Toggle IsChecked="@m.Settings.IsFlipToTalkEnabled"/></Right>
        </TileItem>
        <TileItem Click="@OnToggleShake">
            <Icon><i class="icon-alert-circle text-2xl"></i></Icon>
            <Content>Double shake</Content>
            <Caption>Shake the phone twice</Caption>
            <Right><Toggle IsChecked="@m.Settings.IsDoubleShakeEnabled"/></Right>
        </TileItem>
        @if (m.Settings.IsDoubleShakeEnabled) {
            <TileItem IsHoverable="false">
                <Icon><i class="icon-settings text-2xl"></i></Icon>
                <Content>Shake sensitivity</Content>
                <Right>
                    <div class="c-sensitivity">
                        @foreach (var value in SensitivityOrder) {
                            var isSelected = m.Settings.ShakeSensitivity == value;
                            <Button
                                Class="@($"btn-xs {(isSelected ? "btn-primary" : "")}")"
                                Click="@(() => OnSetSensitivity(value))">
                                @value.ToString()
                            </Button>
                        }
                    </div>
                </Right>
            </TileItem>
        }
        <TileItem Click="@OnToggleAlwaysOn">
            <Icon><i class="icon-infinity text-2xl"></i></Icon>
            <Content>Always listen for gestures</Content>
            <Caption>Uses more battery. Off: gestures work only right after someone speaks.</Caption>
            <Right><Toggle IsChecked="@m.Settings.AreGesturesAlwaysOn"/></Right>
        </TileItem>
    </Tile>

    <TileTopic Topic="Practice"/>
    <PushToTalkPracticePanel/>
} else if (HostInfo.HostKind.IsMauiApp()) {
    <TileTopic Topic="Answer gestures"/>
    <Tile>
        <p class="c-description">This device has no motion sensor, so gestures are unavailable.</p>
    </Tile>
}

<TileTopic Topic="Reply window"/>
<Tile>
    @foreach (var window in HotWindows) {
        var isSelected = m.Settings.HotWindow == window;
        <TileItem Click="@(() => OnSetHotWindow(window))">
            <Icon><i class="icon-clock-2 text-2xl"></i></Icon>
            <Content>@($"{window.TotalSeconds:F0} seconds")</Content>
            <Caption>How long the mic stays open in silence</Caption>
            <Right>
                @if (isSelected) {
                    <i class="icon-checkmark-simple text-primary text-2xl"></i>
                }
            </Right>
        </TileItem>
    }
</Tile>

<TileTopic Topic="Cues"/>
<Tile>
    <TileItem Click="@OnToggleCues">
        <Icon><i class="icon-voice-01 text-2xl"></i></Icon>
        <Content>Audible cues</Content>
        <Caption>Play a sound when a reply starts and ends</Caption>
        <Right><Toggle IsChecked="@m.Settings.AreAudibleCuesEnabled"/></Right>
    </TileItem>
</Tile>

@code {
    private static readonly ShakeSensitivity[] SensitivityOrder =
        [ShakeSensitivity.Low, ShakeSensitivity.Medium, ShakeSensitivity.High];
    private static readonly TimeSpan[] HotWindows =
        [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];

    private GestureUI GestureUI => Hub.GestureUI;
    private IChats Chats => Hub.Chats;

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.None, Category = GetStateCategory(GetType()) };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var settings = await UserSettingsUI.UserWalkieTalkieSettings().Get(cancellationToken).ConfigureAwait(false);
        var chats = new List<ChatItem>(settings.PttChatIds.Length);
        foreach (var chatId in settings.PttChatIds) {
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is not null)
                chats.Add(new ChatItem(chatId, chat.Title));
        }
        return new Model(settings, chats);
    }

    private Task OnRemove(ChatId chatId)
        => UserSettingsUI.UserWalkieTalkieSettings().Update(x => x.WithoutPttChat(chatId), CancellationToken.None);

    private Task OnAddChat()
        => ModalUI.Show(new PttChatPickerModal.Model(
            State.Value.Chats.Select(c => c.Id).ToHashSet(),
            UserWalkieTalkieSettings.MaxChatCount - State.Value.Chats.Count));

    private Task OnToggleFlip()
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(x => x with { IsFlipToTalkEnabled = !x.IsFlipToTalkEnabled }, CancellationToken.None);

    private Task OnToggleShake()
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(x => x with { IsDoubleShakeEnabled = !x.IsDoubleShakeEnabled }, CancellationToken.None);

    private Task OnToggleAlwaysOn()
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(x => x with { AreGesturesAlwaysOn = !x.AreGesturesAlwaysOn }, CancellationToken.None);

    private Task OnToggleCues()
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(x => x with { AreAudibleCuesEnabled = !x.AreAudibleCuesEnabled }, CancellationToken.None);

    private Task OnSetSensitivity(ShakeSensitivity sensitivity)
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(x => x with { ShakeSensitivity = sensitivity }, CancellationToken.None);

    private Task OnSetHotWindow(TimeSpan hotWindow)
        => UserSettingsUI.UserWalkieTalkieSettings()
            .Update(x => x with { HotWindow = hotWindow }, CancellationToken.None);

    // Nested types

    public sealed record Model(UserWalkieTalkieSettings Settings, IReadOnlyList<ChatItem> Chats) {
        public static readonly Model None = new(new UserWalkieTalkieSettings(), []);
    }

    public sealed record ChatItem(ChatId Id, string Title);
}
```

Verify the icon classes exist (`icon-plus`, `icon-refresh`, `icon-clock-2`, `icon-infinity`, `icon-checkmark-simple`, `icon-minus-circle`, `icon-settings`, `icon-voice-01`, `icon-alert-circle`, `icon-talking`) by grepping the CSS/font map; substitute the nearest existing icon rather than inventing one, and note substitutions in the report.

- [ ] **Step 5: Add the styles**

Append to `src/dotnet/UI.Blazor.App/Components/Settings/settings-modal.css`:

```css
/* ── Push to Talk tab ── */

.ptt-practice .c-readout {
    @apply flex-y gap-y-1 px-4 py-2;
    @apply text-sm text-03;
}

.ptt-practice .c-readout > div {
    @apply font-mono;
}

.push-to-talk-settings .c-sensitivity {
    @apply flex-x gap-x-1;
}
```

Wrap the whole tab body in `<div class="push-to-talk-settings"> … </div>` so the `.c-sensitivity` rule has its scope, per the `c-` child-class convention. Do not create a new CSS file — this tab lives inside `SettingsModal`, whose CSS file owns its children's styles.

- [ ] **Step 6: Verify build + TS/CSS**

Run:
```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
npm run build:Verify 2>&1 | tail -20
```
Expected: `0 Error(s)`; build:Verify clean.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Settings/ \
        src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs
git commit -m "feat(audio-ui): Push to Talk settings tab - chats, gestures, practice, hot window"
```

---

### Task 9: Per-chat PTT toggle and the Privacy face-down toggle

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/VoiceSettingsStartModalPage.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Settings/PrivacySettings.razor`

**Interfaces:**
- Consumes: `UserWalkieTalkieSettings` accessor (Task 1); `UserAppSettings.IsFaceDownMicStopEnabled` (Task 1).

- [ ] **Step 1: Add the per-chat PTT row**

In `src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/VoiceSettingsStartModalPage.razor`, add a row inside the `FormBlock` after the "Extended listening" tile:

```razor
    <TileItem Click="@OnPushToTalkClick">
        <Icon><i class="icon-talking text-2xl"></i></Icon>
        <Content>Push to Talk</Content>
        <Caption>@(m.IsPushToTalk ? "Wakes this device when someone speaks" : "Off")</Caption>
        <Right>
            <Toggle IsDisabled="@(!m.IsPushToTalk && m.IsPttFull)" IsChecked="@m.IsPushToTalk"/>
        </Right>
    </TileItem>
```

Extend `ComputedModel` and `ComputeState`:

```csharp
    protected override async Task<ComputedModel> ComputeState(CancellationToken cancellationToken) {
        var chatId = ChatId;
        var settings = await UserSettingsUI.GetChatVoiceMode(chatId, cancellationToken).ConfigureAwait(false);
        var listeningMode = await UserSettingsUI.GetListeningMode(chatId, cancellationToken).ConfigureAwait(false);
        var pttChatIds = await UserSettingsUI.UserWalkieTalkieSettings()
            .Get(x => x.PttChatIds, cancellationToken)
            .ConfigureAwait(false);
        var mustStreamVoice = settings.VoiceMode.HasVoice();
        return new (
            mustStreamVoice,
            settings.CanChange,
            listeningMode,
            pttChatIds.Contains(chatId),
            pttChatIds.Length >= UserWalkieTalkieSettings.MaxChatCount);
    }

    private async Task OnPushToTalkClick() {
        var m = State.Value;
        if (!m.IsPushToTalk && m.IsPttFull)
            return;

        var chatId = ChatId;
        await UserSettingsUI.UserWalkieTalkieSettings().Update(
            x => m.IsPushToTalk ? x.WithoutPttChat(chatId) : x.WithPttChat(chatId),
            CancellationToken.None);
    }

    // Nested types

    public sealed record ComputedModel(
        bool MustStreamVoice,
        bool CanChangeMustStreamVoice,
        ListeningMode ListeningMode,
        bool IsPushToTalk,
        bool IsPttFull);
```

Update the `InitialValue` in `GetStateOptions` to `new(false, false, ListeningMode.Default, false, false)`.

- [ ] **Step 2: Add the Privacy face-down toggle**

In `src/dotnet/UI.Blazor.App/Components/Settings/PrivacySettings.razor`, add above the existing "Blocked users" topic:

```razor
<TileTopic Topic="Microphone"/>

<Tile>
    <TileItem Click="@OnToggleFaceDownStop">
        <Icon><i class="icon-mic-off text-2xl"></i></Icon>
        <Content>Stop recording when face down</Content>
        <Caption>Turning the phone face down or pocketing it closes the mic</Caption>
        <Right>
            <Toggle IsChecked="@m.IsFaceDownMicStopEnabled"/>
        </Right>
    </TileItem>
</Tile>
```

Extend the component's `Model` with `bool IsFaceDownMicStopEnabled` (and its `None` value), read it in `ComputeState`:

```csharp
        var isFaceDownMicStopEnabled = await UserSettingsUI.UserAppSettings()
            .Get(x => x.IsFaceDownMicStopEnabled ?? false, cancellationToken)
            .ConfigureAwait(false);
```

and add the handler:

```csharp
    private Task OnToggleFaceDownStop()
        => UserSettingsUI.UserAppSettings().Update(
            x => x with { IsFaceDownMicStopEnabled = !(x.IsFaceDownMicStopEnabled ?? false) },
            CancellationToken.None);
```

If `UserSettingsUI` isn't already reachable in `PrivacySettings.razor`, add the accessor the same way sibling settings components do.

- [ ] **Step 3: Verify build + TS/CSS**

Run:
```bash
dotnet build ActualChat.CI.slnf 2>&1 | tail -3
npm run build:Verify 2>&1 | tail -20
```
Expected: `0 Error(s)`; clean.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/VoiceSettingsStartModalPage.razor \
        src/dotnet/UI.Blazor.App/Components/Settings/PrivacySettings.razor
git commit -m "feat(audio-ui): per-chat Push to Talk toggle + face-down mic stop under Privacy"
```

---

### Task 10: Final verification

**Files:**
- Modify: `src/dotnet/Api/Module/ApiAotSource.g.cs` (regenerated, not hand-edited)
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs` (regenerated, not hand-edited)
- Modify: `docs/superpowers/specs/2026-07-26-walkie-talkie-ptt-settings-design.md` (status line)
- Modify (not committed): `.superpowers/sdd/progress.md`

- [ ] **Step 1: Regenerate the AOT sources**

This sub-project adds a serializable type (`UserWalkieTalkieSettings`) and three Razor components, all of which the AOT keepers enumerate.

Run: `dotnet run --project src/dotnet/App.AotHelper -- -g 2>&1 | tail -5`
Then: `git diff --stat src/dotnet/Api/Module/ApiAotSource.g.cs src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs`
Expected: both files gain entries for the new type/components. If the tool fails to run in this environment, say so plainly in the report and mark AOT regeneration host-deferred — do not hand-edit the generated files.

- [ ] **Step 2: Full build**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: `0 Error(s)`.

- [ ] **Step 3: Full TS/CSS verification**

Run: `npm run build:Verify 2>&1 | tail -20`
Expected: clean (tsc + eslint + debug build).

- [ ] **Step 4: Test sweep**

Run:
```bash
dotnet test tests/Users.UnitTests/Users.UnitTests.csproj 2>&1 | tail -3
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj 2>&1 | tail -3
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
  --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -3
dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ReportPlaybackTest" 2>&1 | tail -3
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ApiEvolutionTest" 2>&1 | tail -3
```
Expected: all PASS. Record the counts in the report.

- [ ] **Step 5: Confirm nothing else reads the old armed source**

Run: `rg -n "AlwaysListenedChatIds" --type cs src/dotnet tests`
Expected: hits only in `UserListeningSettings`, `ChatAudioUI.GetChatsYouNeedToKeepListeningTo`, and the listening-settings UI. Any *walkie-talkie* path still reading it is a bug from an earlier task — fix it here.

- [ ] **Step 6: Update the spec status and the ledger**

In `docs/superpowers/specs/2026-07-26-walkie-talkie-ptt-settings-design.md`, change the `Status:` line to:

```
Status: Implemented (device verification pending — see plan Task 10)
```

Append E2 task-completion lines to `.superpowers/sdd/progress.md`. Do NOT `git add` that file.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/specs/2026-07-26-walkie-talkie-ptt-settings-design.md \
        src/dotnet/Api/Module/ApiAotSource.g.cs \
        src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs
git commit -m "docs: mark walkie-talkie PTT settings + gesture engine (E2) implemented"
```

- [ ] **Step 8: Report the device-verification debt**

These cannot be run on this machine. List them verbatim in the final report:

1. **`net10.0-android` and iOS builds.** Task 6 adds `#if ANDROID` / `#if IOS` code that the CI solution filter never compiles. This is the first compile of that code, and B/C's platform code has *never* been compiled either.
2. **Practice panel on a real device** — Android and iOS: does the sample counter move, do flips and shakes register, does the shake peak track the threshold? The MAUI accelerometer sign convention (face-up ≈ `Z = -1`) is assumed, not measured; the face-down detector is the only place the sign matters, so verify it there first.
3. **Backgrounded flip inside the answer window** → mic opens → reply lands.
4. **Face-down closes a normal, non-PTT recording** (Privacy toggle on).
5. **Battery comparison**, answer-window-only versus `AreGesturesAlwaysOn`.
6. **A host pass on sub-projects B and C should precede all of the above** — until the wake path is verified on a device, "the gesture didn't fire" and "the wake never arrived" are indistinguishable.

---

## Reuse

**Existing abstractions reused (verified against the tree on 2026-08-03):**

| Need | Existing abstraction | Where |
|---|---|---|
| Settings record shape, KVAS storage, origin tracking | `StoredSettings` + `IHasOrigin` + `IHasKvasKey<T>`, templated on `UserListeningSettings` | `src/dotnet/Api/Users/StoredSettings/` |
| Client settings read/write | `UserSettingsUI` + `UserSettingsAccessor<T>` via `UserSettingsUIExt` | `src/dotnet/Api/Users/UserSettingsUIExt.cs` |
| Server settings read | `IServerKvasBackend.ForUser` + `UserScopedKvasBackendExt` | `src/dotnet/Users.Contracts/` |
| Armed gate | `ServerKvasBackendExt.IsWalkieTalkieArmed` (rewritten, not replaced) | `src/dotnet/Users.Contracts/ServerKvasBackendExt.cs` |
| Mic open/close | `ChatAudioUI.SetRecordingChatId` | `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs` |
| Reply policy, target resolution, cold-start dead-man | `WalkieTalkieReplyUI`, `ReplyTargetResolver` (E1) | `src/dotnet/UI.Blazor.App/Services/` |
| Answer-window signal | `IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt` + `Constants.Audio.WalkieTalkieReplyRecencyWindow` | E1 |
| Resilient scoped worker | `UIWorkerBase<AppUIHub>` + `INotifyInitialized` + `AsyncChain.RetryForever` + `RunIsolated` | `IncomingVoiceActivityUI` is the exact template |
| Eager worker start | `AppScopedServiceStarter.AfterFirstRender` "touch" lines | `src/dotnet/UI.Blazor.App/Services/AppScopedServiceStarter.cs` |
| Platform-neutral base + MAUI override + per-platform `#if` | `ThermalTracker` / `MauiThermalTracker` | `src/dotnet/Core/Hosting/`, `src/dotnet/App.Maui/Services/` |
| Recording idle policy | `RecordingIdleOptions` + `ObserveStreamingIdleBoundaries` | `ChatAudioUI.StateSync.cs` |
| Settings tab shell | `SettingsModal.razor` + `SettingsTab` + `SettingsTabId` | `src/dotnet/UI.Blazor.App/Components/Settings/` |
| Settings rows | `TileTopic`, `Tile`, `TileItem`, `Toggle`, `FormBlock` (see `AppSettings.razor`, `PrivacySettings.razor`) | same |
| Chat picking | `ContactSelector` + `ContactSelectorListView` + `SearchBox`, in a `DialogFrame` (see `ForwardMessageModal`) | `src/dotnet/UI.Blazor.App/Components/` |
| Modal registration | `services.AddTypeMap<IModalView>(map => map.Add<Model, Component>())` | `BlazorUIAppModule.cs:123` |
| Per-chat voice settings host | `VoiceSettingsStartModalPage` / `VoiceSettingsListeningModalPage` | `src/dotnet/UI.Blazor.App/Components/ChatAudioPanel/` |
| Cues | `TuneUI` + `Tune.WalkieReplyEnded` / `Tune.WalkieReplyNothingHeard` | `src/dotnet/UI.Blazor/Services/TuneUI/` |
| Host-kind gating | `HostInfo.HostKind.IsMauiApp()` | used in `SettingsModal.razor` |
| Serialization round-trip assert | `AssertPassesThroughAllSerializers` | `tests/Users.UnitTests/UserCommandSerializationTest.cs` |
| Armed-chat cap precedent | `ActiveChatsUI.MaxActiveChatCount = 3` | `src/dotnet/UI.Blazor.App/Services/ActiveChatsUI.cs:9` |
| Sensors | `Microsoft.Maui.Devices.Sensors.Accelerometer`; Android `SensorManager`; iOS `UIDevice.ProximityMonitoringEnabled` | MAUI / platform SDKs |

No `ActualLab.Fusion` abstraction beyond the compute services already in use is needed. `OrientationSensor` was considered and dropped (decision 2).

**Reusability of new components.** The detector cores (`SensorSample`, `FlipToTalkDetector`, `ShakeDetector`, `FaceDownDetector`, `GestureRecognizer`) are pure state machines over accelerometer samples with no chat, audio, or walkie dependency — the only plausibly reusable new code.

- **Option A (chosen): `UI.Blazor.App/Services/Gestures/`.** Self-contained namespace, one consumer today, and promotion later is a file move.
- **Option B: `ActualChat.Core`.** Rejected for now: `Core` is a dependency of every server project, none of which will ever process accelerometer samples, and there is exactly one consumer. Revisit if a second consumer appears (e.g. a gesture-driven camera or call control).

Everything else is inherently local: the settings records belong beside their siblings in `Api`, `MauiSensorFeed` is MAUI-bound by definition, `GestureUI` is a scoped UI worker, and the Razor components are UI.

## Risks

- **Accelerometer sign convention.** Only `FaceDownDetector` depends on it. Mitigation: the practice panel shows live values, and the orientation classification used by flip/shake is sign-free by construction.
- **Seeded thresholds.** Shake/flip/dwell constants are first guesses. Mitigation: the practice panel is shipped in v1 precisely so they can be corrected on a device; the sensitivity monotonicity test guards against a correction that breaks the nesting invariant.
- **`HotWindow = 120 s` versus server-side stream lifetime.** `AudioSettings.StreamExpirationDelay` is 60 s but governs post-stream finalization, not the idle close, so 120 s should be safe — verify on the device pass that a 120 s window closes cleanly and the entry finalizes.
- **Heard receipts narrow with the armed predicate** (decision 7). Intended, but it means a user who keeps listening without PTT no longer reports `Heard`. Flag it in the Task 2 report.
- **First compile of platform code.** Task 6's `#if` blocks and all of sub-projects B/C are unverified on a device. Task 10 step 8 enumerates the debt.
