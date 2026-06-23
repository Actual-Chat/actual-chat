# iOS Call Audio Session — Investigation & Plan

Branch: `feat/3296-ios-calls`. This document explains how `AVAudioSession`
behaves during VoIP calls, maps that onto the current implementation, lists
the concrete correctness gaps, and proposes a plan to close them.

## TL;DR

The call feature reuses the existing "audio focus" machinery built for
recording/playback. That machinery **owns activation of the shared
`AVAudioSession`** (it calls `SetActive(true/false)` itself). CallKit, by
contract, **also owns activation**. The two regimes currently collide:

1. On answer, the app activates the session *before* CallKit does, and then
   activates it *again* (via the normal recording pipeline) *after* CallKit
   already activated it — deactivating and reactivating a session CallKit
   owns.
2. The session is only ever configured by **category**; `AVAudioSession.Mode`
   is never set to `VoiceChat`/`VideoChat`, which is the Apple-recommended
   mode for VoIP and changes routing, gain, and signal processing.
3. There is no `DidDeactivateAudioSession` handler and no route-change
   observer, so the call cannot react to CallKit tearing the session down or
   to headset/Bluetooth changes mid-call.
4. The outgoing-call path (`CallToggle`) bypasses CallKit entirely, so caller
   and callee end up on two different session-activation regimes.

The fix is to make CallKit the single owner of activation during a call, set
the call mode, and add the missing lifecycle handlers — without disturbing the
existing recording/playback focus behavior when there is no call.

---

## 1. AVAudioSession fundamentals that matter for calls

These are the iOS rules the implementation has to live within:

- **Category vs. Mode are orthogonal.** Category (`PlayAndRecord`, `Playback`,
  `Ambient`) decides what the app may do and the mixing/ducking policy. Mode
  (`Default`, `VoiceChat`, `VideoChat`, `Measurement`, …) tunes the signal
  path: routing defaults, hardware gain, and whether the system voice-processing
  I/O is engaged. **A VoIP call should be `PlayAndRecord` + `VoiceChat`**
  (`VideoChat` for video). `VoiceChat` implies bidirectional voice processing
  (AEC/AGC/NS) and a **receiver-first** route (earpiece), with the user free to
  switch to speaker.

- **`DefaultToSpeaker` is for speakerphone-style apps, not held-to-ear calls.**
  Combined with `VoiceChat` mode it fights the intended receiver-first routing.
  For a phone-like call you typically want receiver by default and an explicit
  speaker toggle, *not* `DefaultToSpeaker` + a forced `OverrideOutputAudioPort(Speaker)`.

- **CallKit owns `SetActive` during a call.** When a `CXProvider` action is
  fulfilled, CallKit activates the session on its schedule and calls
  `provider:didActivateAudioSession:`; on teardown it calls
  `provider:didDeactivateAudioSession:`. The app must **configure category/mode
  but must not call `SetActive(true)` itself**, and must start/stop its audio
  engines from the activate/deactivate callbacks. Calling `SetActive` yourself
  races CallKit and produces `AVAudioSessionErrorCodeCannotStartPlaying`/
  `…CannotInterruptOthers` style failures and dropped audio.

- **Interruptions during a CallKit call are CallKit's job.** For an app-managed
  (non-CallKit) session, the app handles `AVAudioSessionInterruptionNotification`
  and reactivates on `.ended`. Inside a CallKit call you should *not*
  self-reactivate on interruption-ended — CallKit will re-activate and call
  `didActivateAudioSession` again.

- **Route changes are dynamic.** Plugging/unplugging headphones, Bluetooth
  connect/disconnect, and the system's own re-routes all raise
  `AVAudioSessionRouteChangeNotification`. A call must re-evaluate its desired
  output (e.g. re-apply or drop the speaker override) on each route change, not
  once at start.

- **Voice-processing I/O (VPIO) vs. session mode.** `AVAudioEngine`'s
  `inputNode.setVoiceProcessingEnabled(true)` engages the same VPIO audio unit
  that `VoiceChat` mode engages at the session level. Turning VPIO on forces the
  route toward the receiver — which is *correct* for a call but is the reason the
  current recording path needs the `EnsureCorrectOutputRoute()` → speaker
  override hack (recording-while-not-in-a-call wants speaker).

---

## 2. Current implementation map

Single shared `AVAudioSession`, three "focus modes" ordered
`Tune < Playback < Recording`, each mapped to a category:

| `AudioFocusMode` | Category | Options | Mode |
|---|---|---|---|
| Tune | `Ambient` | — | (never set) |
| Playback | `Playback` | — | (never set) |
| Recording | `PlayAndRecord` | `DefaultToSpeaker \| AllowBluetooth \| AllowBluetoothA2DP` | (never set) |

Key files:

- `UI.Blazor/Services/AudioFocusUI.cs` — base abstraction: `AudioFocusMode`
  enum, `AudioFocusRequester`, `AudioFocusScope`, `TryAcquire`/`TryRecover`.
- `App.Maui/Platforms/iOS/Audio/IosAudioFocusUI.cs` — owns the session
  lifecycle. `TryAcquire` → `SetModeUnsafe(maxMode)` → `AudioEngines.Pause()` →
  `AudioSession.Reconfigure(mode)` → `AudioEngines.Resume(mode)`. Holds an
  `ActiveScopes` dictionary keyed by mode (the `_byMode` dict the
  [[feedback_iosaudio_keep_modes]] note refers to — **do not collapse it**).
  Subscribes to interruption + engine-configuration-change notifications.
- `App.Maui/Platforms/iOS/Audio/AudioSession.cs` — the only place that touches
  `SetCategory`/`SetActive`/`OverrideOutputAudioPort`. `Reconfigure` =
  `SetActive(false)` → configure category → `SetActive(true)`. `Reactivate` =
  configure → `SetActive(true)` (with retry).
- `App.Maui/Platforms/iOS/Audio/AudioEngines.cs` + `EngineWrapper/AudioEngine.cs`
  — three independent `AVAudioEngine`s sharing the one session.
- `App.Maui/Platforms/iOS/Audio/IosAudioCapture.cs` — mic capture: enables VPIO,
  installs tap, `EnsureRunning()`, then `EnsureCorrectOutputRoute()`.
- `App.Maui/Platforms/iOS/IosCalls.cs` — `CXProviderDelegate`. Incoming/outgoing
  call reporting and the answer/end actions.
- `App.Maui/Platforms/iOS/IosVoipPushes.cs` — `PKPushRegistryDelegate`. VoIP push
  token refresh + incoming-push → `ReportIncomingCall`.
- `UI.Blazor.App/Components/ChatAudioPanel/CallToggle.razor` — the outgoing-call
  button.

Confirmed answer-path call chain:

```
DidActivateAudioSession (CallKit)            [IosCalls.cs]
  → ChatAudioUI.SetRecordingChatId(chatId)   [sets ActiveChats IsRecording flag]
    → PushRecordingState worker observes      [ChatAudioUI.StateSync.cs]
      → AudioRecorder.StartRecording          [AudioRecorder.cs:89]
        → AudioFocusUI.TryAcquire(Recording)  [IosAudioFocusUI.cs:56]
          → SetModeUnsafe → AudioSession.Reconfigure(Recording)
                                              → SetActive(false)/SetActive(true)  ← (!!)
        → MauiRecorderEngine.Start
          → IosAudioCapture.Capture           [enables VPIO, taps mic, EnsureRunning]
```

---

## 3. Concrete gaps (correlation between AVAudioSession rules and current code)

### G1 — App activates the session that CallKit owns (highest priority)
- `IosCalls.PerformAnswerCallAction` calls `audioSession.Reconfigure(AudioFocusMode.Recording)`.
  The inline comment says "Only configuring the session here, not starting it",
  but `Reconfigure` **does** call `SetActive(true)` (`AudioSession.cs:56`). This
  activates the session *before* CallKit, violating the contract.
- Worse, the subsequent recording pipeline (`SetRecordingChatId` →
  `TryAcquire(Recording)` → `Reconfigure`) runs **after** `DidActivateAudioSession`
  and calls `SetActive(false)` then `SetActive(true)` again — tearing down and
  re-activating a session CallKit just activated.
- Net: 2–3 redundant activations per answered call, racing CallKit.

### G2 — `AVAudioSession.Mode` is never set
- `ConfigureUnsafe` only ever calls `SetCategory`. No call ever sets
  `AVAudioSession.Mode = VoiceChat`/`VideoChat`. Calls run in `Default` mode,
  losing the system's voice-optimized routing/gain and relying solely on the
  engine-level VPIO toggle.

### G3 — Receiver-first routing is inverted for calls
- Recording category uses `DefaultToSpeaker`, and `IosAudioCapture` then calls
  `EnsureCorrectOutputRoute()` which **forces speaker** whenever the route is the
  receiver. That is right for hands-free dictation but wrong for a phone call,
  where the default should be the earpiece with a user-controlled speaker toggle.

### G4 — No `DidDeactivateAudioSession`
- `IosCalls` overrides `DidActivateAudioSession` but **not**
  `DidDeactivateAudioSession`. When CallKit tears the session down (call ended,
  GSM call took over), the app never learns it should stop engines / release.

### G5 — No route-change observer
- `IosAudioFocusUI` observes interruption + engine-config-change but **not**
  `AVAudioSession.Notifications.ObserveRouteChange`. Speaker/earpiece/Bluetooth
  changes mid-call are not handled; `EnsureCorrectOutputRoute` only runs once at
  capture start.

### G6 — Interruption handler self-reactivates during CallKit calls
- `HandleInterruption(.Ended)` always calls `TryRecover()` →
  `AudioSession.Reactivate` → `SetActive(true)`. During a CallKit call this
  races CallKit's own re-activation. The "always recover" logic is correct for
  the non-call recording/playback case but must be suppressed while a CallKit
  call owns the session.

### G7 — Two activation regimes for caller vs. callee
- `CallToggle.OnCallClick` calls `ChatAudioUI.SetRecordingChatId` +
  `Notifications_SendVoip` directly and **never** calls
  `IosCalls.StartOutgoingCall`. So the **caller** drives audio through the
  AudioFocus path (app-owned activation) while the **callee** drives it through
  CallKit (CallKit-owned activation). The two sides behave differently
  (routing, interruption handling, lock-screen UI, recents).

### G8 — Cross-path mutation of the shared session without a shared lock
- `IosCalls` (CallKit thread) calls `AudioSession.Reconfigure` directly while
  `IosAudioFocusUI` mutates the same session under its `AsyncLock`. The two
  paths can interleave on the shared `AVAudioSession`. `AudioSession` itself has
  no internal lock; it relies on its caller holding one — but the CallKit path
  doesn't take the focus-UI lock.

### G9 — Minor / hardening
- No `setPreferredSampleRate` for the call (only IO buffer duration is set).
- `CXProviderConfiguration` doesn't set `IncludesCallsInRecents`, ringtone, or
  icon (cosmetic / privacy choice).
- `StartOutgoingCall` hardcodes `CXHandleType.PhoneNumber` even when the identity
  is an email/generic handle (mismatch with `ReportIncomingCall`).

---

## 4. Plan

Goal: **CallKit is the single owner of session activation during a call.** The
existing focus machinery keeps owning activation only when there is no active
call. Smallest change set that makes the call path correct without regressing
recording/playback.

### Reuse (mandatory section)

**Existing abstractions to reuse — do NOT introduce parallel machinery:**
- `AudioSession` (`AudioSession.cs`) is already the single chokepoint for
  `SetCategory`/`SetActive`/`OverrideOutputAudioPort`. Extend it; do not add a
  second class that touches the session.
- `AudioFocusMode` / `AudioFocusRequester` / `AudioFocusScope`
  (`UI.Blazor/Services/AudioFocusUI.cs`) — the call should acquire focus through
  the same `TryAcquire` path, not a bespoke one.
- `IosAudioFocusUI.ActiveScopes._byMode` — keep the per-mode dictionary intact
  (per [[feedback_iosaudio_keep_modes]]); add call-awareness as state on the
  focus UI, not by reshaping this dict.
- `AudioEngines.Pause/Resume(mode)` — reuse for start/stop of engines from the
  CallKit activate/deactivate callbacks.
- `EnsureCorrectOutputRoute()` already centralizes route decisions and the
  external-port list — extend it to be call-aware rather than writing new route
  logic.
- `IosCalls` / `IosVoipPushes` already exist — extend, don't replace.

**New components and their placement:**
- A small "is a CallKit call active?" signal. This is iOS-only state — keep it on
  `IosCalls` (the `CXProviderDelegate`) and read it from `IosAudioFocusUI` /
  `AudioSession`. **No shared-project candidate** — it is platform-specific.
- A "configure-without-activate" method on `AudioSession`
  (`ConfigureForCall(mode, activate:false)`). Lives in the existing iOS
  `AudioSession` class. Not reusable cross-platform; stays local.
- Route-change subscription: add to the existing `IosAudioFocusUI` subscriptions
  (it already manages interruption/config-change observers) — no new class.

No `ActualChat.Core` / `Core.Server` / nodejs candidates here: everything is
iOS-native `AVFoundation`/`CallKit` glue.

### Step 1 — Split "configure" from "activate" in `AudioSession`
- Add `AVAudioSession.Mode` to `ConfigureUnsafe`: `VoiceChat` (or `VideoChat`)
  when the mode is `Recording` **and a call is active**; `Default` otherwise.
- Add a `ConfigureForCall(AudioFocusMode mode)` that sets category + mode **and
  does NOT call `SetActive`** (CallKit will). Keep `Reconfigure`/`Reactivate`
  for the non-call path.
- Drop `DefaultToSpeaker` for the call case; choose receiver-first. Keep
  `AllowBluetooth`/`AllowBluetoothA2DP`.

### Step 2 — Make `IosCalls` the activation owner during a call
- Track `IsCallActive` on `IosCalls` (set in `PerformAnswer`/`PerformStart`,
  cleared in `PerformEndCall`/`DidReset`/`DidDeactivateAudioSession`).
- `PerformAnswerCallAction`: call `ConfigureForCall(Recording)` (configure only,
  **no SetActive**), then `action.Fulfill()`. Remove the current `Reconfigure`.
- `DidActivateAudioSession`: this is where engines start. Set recording chat id
  here (as today) but ensure the downstream `TryAcquire(Recording)` does **not**
  re-activate the session (Step 3).
- Add `DidDeactivateAudioSession`: stop engines / release the recording focus.

### Step 3 — Teach `IosAudioFocusUI` to defer to CallKit
- When `IosCalls.IsCallActive`, `SetModeUnsafe`/`TryRecover` must **configure
  only** (no `SetActive`) — CallKit owns activation. Engines are still
  paused/resumed; only the `SetActive` calls are suppressed.
- `HandleInterruption(.Ended)`: skip `TryRecover()`'s self-reactivation while a
  call is active (G6).
- This is the surgical fix for G1: the post-`DidActivateAudioSession` recording
  pipeline still runs, but its `Reconfigure` becomes a configure-only no-op for
  activation.

### Step 4 — Route handling for calls
- Subscribe to `AVAudioSession.Notifications.ObserveRouteChange` in
  `IosAudioFocusUI`; on change, while a call is active, re-evaluate the desired
  output (respect external devices via the existing `IsExternalPort` list; honor
  a speaker toggle).
- Make `EnsureCorrectOutputRoute` call-aware: in a call, default to receiver
  unless the user enabled speaker; outside a call keep the current
  speaker-for-recording behavior (G3).

### Step 5 — Unify the outgoing-call path (G7)
- `CallToggle` (outgoing) should go through `IosCalls.StartOutgoingCall` so the
  caller uses the same CallKit-owned activation as the callee. Fix
  `StartOutgoingCall` to pick the handle type from the available identity
  (phone/email/generic), matching `ReportIncomingCall`.
- Decide and document the desktop/web fallback (non-CallKit platforms keep the
  current AudioFocus path) — gate the CallKit unification behind the iOS app.

### Step 6 — Hardening (G8/G9)
- Route the CallKit session mutations through the same `AsyncLock` as
  `IosAudioFocusUI` (or move all `AudioSession` mutations behind one serializer)
  so CallKit and focus paths can't interleave.
- Optionally set `setPreferredSampleRate` for calls; set
  `CXProviderConfiguration.IncludesCallsInRecents` deliberately.

### Out of scope
- The actual media transport / SFU plumbing for the call (this doc is only about
  the local `AVAudioSession`/engine lifecycle).
- Android audio-focus parity.

---

## 5. Verification

- **Manual, on device** (the simulator does not exercise CallKit audio
  faithfully): answer an incoming VoIP push → confirm exactly **one**
  activation, audio flows both ways, and route defaults to receiver with a
  working speaker toggle. End call → confirm `DidDeactivateAudioSession` fires
  and engines stop.
- **Interruption**: take a GSM call mid-VoIP-call → on end, confirm the app does
  *not* self-reactivate (CallKit does) and audio resumes once.
- **Route change**: connect/disconnect Bluetooth and wired headset mid-call →
  confirm route follows the device and speaker override is re-evaluated.
- **Regression**: plain recording and plain playback (no call) behave exactly as
  before — speaker-for-recording, interruption auto-recover, focus
  acquire/release.
- Use the `ios-logs` skill to watch `IosAudioFocusUI` / `IosCalls` /
  `AudioSession` log lines (`Configure: mode=…`, `DidActivateAudioSession`,
  `SetMode`, `Interruption type=…`) during each scenario.

---

## 6. Open questions

1. Receiver-first vs. speaker-first default for a Voxt call — product call. The
   plan assumes receiver-first (phone-like) with a speaker toggle.
2. Is `VideoChat` mode needed now, or is audio-only the first target? (`hasVideo`
   already flows through the push payload and `CXCallUpdate`.)
3. Should non-iOS platforms also adopt a unified call path, or keep the
   AudioFocus path as the fallback indefinitely?
