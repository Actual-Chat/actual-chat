# Audio pipeline redesign: one platform-independent session model with lazy teardown

## Goal

Replace the four independent, per-platform audio-lifetime implementations with a
single platform-independent model that owns:

1. **Who wants audio right now** — one source of truth for what today is called
   "audio focus", derived from refcounted leases rather than reimplemented per
   platform.
2. **What must therefore exist** — which native resources (mic, playback engine,
   tune engine, session/category) should be allocated and running.
3. **When to give them back** — lazily, on a per-resource delay, so a burst of
   short activities doesn't allocate and free the same expensive resource
   repeatedly.

The platform layer shrinks to "make the world look like this state", plus
inbound events. It stops owning policy.

## Why now

An investigation into iOS CPU during live audio (2026-08-11/12) found an idle cost
of **~0.33 cores in `audiomxd` forever after the first call**, and several defects
in native audio lifetime handling along the way.

> **Correction (2026-08-12, later).** The `audiomxd` cost was **not** the audio
> pipeline. Root cause was `CHHapticEngine` in `Platforms/iOS/Audio/Haptics.cs`,
> started once and never stopped: created without an audio session it makes its own,
> and while it runs it holds CoreAudio's global I/O running state, keeping the
> speaker, microphone and Taptic virtual devices all live. Fixed by stopping the
> engine when no vibration is in flight; audiomxd then drops to absent.
>
> The `AVAudioEngine` instances were being deallocated correctly the whole time —
> proven with `CFGetRetainCount` (`1 -> 0`, live count 0). Treat the per-call
> `AggDev` number climbing as normal: `vdef` is rebuilt on every route-configuration
> change. Device four-CCs decode as `vdef` = Speaker, `vspd` = built-in mic,
> `vzzz` = Taptic actuator.
>
> The measurements below are still accurate as *measurements*; their attribution to
> the audio pipeline is not.

The redesign still stands on its own merits: four independent per-platform
lifetime implementations remain the thing that made this take two days to find.

---

# Part 1 — Findings (iOS, 2026-08-12)

All numbers are 30-second `xctrace` Time Profiler captures, `--all-processes`, on
an iPhone 13 Pro (iOS 26.5), thermal state Nominal throughout.

## Reference: what `audiomxd` should cost

For one 48 kHz mono **voice-processed** capture on an iPhone 13:

| `audiomxd` | reading |
|---|---|
| 0.05–0.12 cores | normal (~0.8–2% of aggregate six-core time) |
| 0.20 sustained | somewhat high |
| 0.30+ sustained | unusually high for a single capture path |

Idle should be **zero**. Use this to judge any future measurement.

## Root cause: the `AVAudioEngine` object's lifetime is the device's lifetime

Each `AVAudioEngine` holds a **virtual audio device** with a live IO thread and
DSP chain in mediaserverd.

- `Pause()` does **not** release it.
- `Stop()` does **not** release it.
- Deactivating the `AVAudioSession` does **not** release it.
- **Only deallocating the engine does.**

The app held three engines (`Tunes`, `Playback`, `Recording`) for the whole
process lifetime, so after the first call all three devices ran forever.

### How it was identified

Thread names in `audiomxd` were `audio IO: VAD [vdef]/[vspd]/[vzzz] AggDev N` —
**three of them, matching the three engines exactly**. Full stacks read:

```
libsystem_pthread -> CoreAudio -> VirtualAudio -> libAudioDSP
                  -> AudioToolboxCore -> libAudioDSP -> libvDSP
```

`VirtualAudio` is the frame that settles it: **"VAD" here is *Virtual Audio
Device***, not voice-activity detection. Misreading it as the latter sent the
investigation after `corespeechd` and Siri for a while.

### The control that proved it was ours

| state | audiomxd | corespeechd |
|---|---|---|
| app running, never made a call | absent | absent |
| app running, after a call | 0.27–0.30 | 0.018 |
| app killed | absent | absent |

## Measured progression

Idle, after a call, app in foreground:

| build | device total | audiomxd |
|---|---|---|
| baseline | — | 0.267 |
| engines stopped when idle | 0.561 | 0.276 |
| + session deactivated when no scopes | 0.650 | 0.300 |
| **+ engine deallocated when idle** | **0.242** | **absent** |
| app killed (floor, for reference) | 0.233 | absent |

Idle `audiomxd` **0.267 → 0**. The app idling after a call now costs **0.009
cores** more than a dead app.

## The three defects found (all real, all fixed on `feat/ios-video-perf`)

1. **Engines were started and never stopped.** `AudioEngine.Stop()` had exactly
   one call site in the whole repo (`StopRecording`, for the Recording engine).
   `Tunes` was started by `AppleTuneUI.PlaySound` and `Playback` by
   `VoicePlayer.Play`, and nothing ever stopped either. `AudioEngines.Resume(mode)`
   is cumulative (`if (mode >= Tune/Playback/Recording)`), and `Resume()` no-ops
   only while `_isStarted` is false — which flips true permanently at the first
   `EnsureRunning()`. So seconds into any session, all three engines ran.
2. **Voice processing was enabled and never disabled.**
   `SetVoiceProcessingEnabled(true)` on every capture; no `(false)` anywhere.
   `StopRecording()` → `Input.Reset()` removes the tap and calls `Node.Reset()`;
   neither undoes VP.
3. **The session was always re-activated.** `ReconfigureUnsafe` ends in
   `SetActive(true)` unconditionally, so releasing the last scope set the category
   to Ambient and re-activated. There was no "nobody wants audio" state.

Fixes 1 and 2 moved **nothing** (0.276). Fix 3 moved **nothing** (0.300). Only
deallocation worked. **Do not re-derive this**: all three are correct and stay in,
but the device lifetime is the one that pays.

## Method note — the profiler was not the decisive tool

The decisive channel was **`idevicesyslog` + the app's own `ILogger` output**:

```
idevicesyslog -u <udid> -p ActualChat > /tmp/syslog.log
```

`devicectl ... --console` only carries native stdout/stderr; our logs go to OSLog.
The syslog interleaves our `CaptureInternal` / `SetMode` / `Configure: mode=` /
`ReleaseIfIdle` lines with AVFoundation's own `Deactivated session 0x...` and
`AVAudioEngine.mm: Engine@0x...: stop, was running 1`. Every hypothesis above was
settled from that log plus one 30 s trace. See also the
`ios-device-diagnostics-channel` note: syslog drops lines under heavy load, so for
*counting* events write to a file in the app container instead — but for *state
transitions* at idle it is reliable and immediate.

## Other findings worth keeping

- **A stale duplicate process keeps resurrecting.** Bundle container
  `874767BF-...` — an old build, background-audio capable — relaunches itself
  (caught doing it with a fresh `MauiProgram` startup trace at 13:05:31). It was
  **not** the audio client (killing it left `audiomxd` unchanged) but it
  independently burned ~0.35 cores (WebContent 0.264 + ActualChat 0.109), and it
  silently inflated during-call measurements taken the same day. **Find what
  relaunches it.** Any future measurement must first assert exactly one
  `ActualChat.app/ActualChat` process.
- **During-call `audiomxd` is unchanged at ~0.24**, still roughly double the top
  of the reference band. Untouched levers: session mode `Default` rather than
  `VoiceChat` while VPIO runs on the speaker (`AudioSession.cs`, the
  `Owner == App ? Default : VoiceChat` choice), and a **48 kHz AEC whose output we
  immediately downsample to 16 kHz** (`RecordingSampleRate = 16000`, no
  `SetPreferredSampleRate` anywhere). The second trades against 48 kHz playback
  quality, since one session serves both.
- **Session churn per recording.** Measured 3 full
  deactivate→configure→activate pairs for one 19-second call. `TryAcquire` only
  reconfigures on *escalation*, but `Release` reconfigures on **every**
  de-escalation — so each recording start/stop costs a pair, plus an
  `AudioEngines.Pause()`/`Resume()` bounce, plus a VPIO restart whose AEC then
  re-converges. In walkie-talkie use that is per press.
- **`Tunes` and `Playback` are the same engine twice.** `AudioEngine`'s only
  per-instance state besides its `AVAudioEngine` is `Mode`, which is read **only
  in log messages**. Both are a bare engine with player nodes on `MainMixerNode`.
  Formats differ per *player*, not per engine, and `AVAudioMixerNode` converts per
  input bus. They can be merged.
- **Tunes take no focus scope on iOS.** `AppleTuneUI.PlaySound` overrides
  `MauiTuneUI.PlaySound` and never calls `TryAcquireAudioFocus`, so `_activeScopes`
  only ever holds Playback and Recording. On Android, tunes *do* take focus.
- **`TryAcquire` can never fail on iOS** — it returns a scope or throws. Callers
  like `AudioRecorder` have "continue without focus" branches that are dead code
  there, and live on Android.

---

# Part 2 — What each platform actually does today

## iOS (`App.Maui/MaciOS/Audio/`)

`AppleAudioFocusUI : AudioFocusUI` — **does not** derive from `MauiAudioFocusUI`.
Keeps `ActiveScopes`: a `Dictionary<AudioFocusMode, Dictionary<Requester, Scope>>`,
and `GetMode()` = max active mode. That maximum picks the process-global
`AVAudioSession` category:

| mode | category |
|---|---|
| Tune | `Ambient` |
| Playback | `Playback` |
| Recording | `PlayAndRecord` + `DefaultToSpeaker \| AllowBluetooth \| AllowBluetoothA2DP`, preferred IO buffer duration |

There is no iOS "audio focus" API — this layer is entirely ours. iOS's actual
contribution is **inbound only**: interruption, route-change,
media-services-reset and engine-configuration-change notifications, which
`AppleAudioFocusUI` translates back into `AudioFocusLostHandler` /
`AudioFocusRestoreHandler` calls.

Above focus there is a **second, genuinely external** arbitration layer:
`AudioSessionOwner` / `AudioSessionOwnership`. When Apple's PushToTalk framework
activates the session, `MayActivate` goes false and `MayConfigure` allows only
*raising* to Recording. Hence `AudioSessionSetup(IsConfigured, IsActivated)` as two
separate bits.

Resource lifetime: three long-lived `AVAudioEngine`s. `PttPreRoll` owns a
**fourth**, independent engine for pre-roll capture.

## Android (`App.Maui/Platforms/Android/Audio/`)

`AndroidAudioFocusUI : MauiAudioFocusUI : AudioFocusUI` — a **different**
bookkeeping model from iOS: one `_lastAudioFocusHolder` with a `Scopes` dictionary,
and `FocusModeHasChanged` driving renewal.

Android has a **real** focus API, and the request can be **refused**:

| mode | request |
|---|---|
| Recording | `GainTransient`, `VoiceCommunication` / `Speech` |
| Playback | `Gain`, `VoiceCommunication` / `Speech` |
| Tune | `GainTransientMayDuck`, `AssistanceSonification` / `Sonification` |

Inbound: `OnAudioFocusChange` maps `LossTransient` / `LossTransientCanDuck` /
`Loss` / `Gain*` onto the same lost/restore handlers.

Resource lifetime is already **per-use**: `AndroidAudioCapture` builds an
`AudioRecord` per `Capture()` and releases it; `AndroidAudioPlaybackEngine` builds
an `AudioTrack` per engine and releases it. Android also has explicit device
routing (`ModernAudioDeviceRouter` / `LegacyAudioDeviceRouter`) and
**`WarmUpAudioMode()`**, which deliberately flips to `Mode.InCommunication` early
to prime the HAL — direct evidence that acquisition is slow there too, and that
the current answer is an ad-hoc warm-up rather than a lifetime policy.

## Windows (`App.Maui/Platforms/Windows/Audio/`)

No focus concept at all. `WindowsAudioCapture` builds a WebRTC
`AudioProcessingModule` (AEC/NS/AGC/HPF) plus WASAPI capture per `Capture()` call;
`CustomWasapiLoopbackCapture` for the render reference. Per-use lifetime, no
session, no arbitration.

## Web (`UI.Blazor.App/Services/audio-context-source.ts`)

**This is the prior art for what we want.** `AudioContextSource` already has:

- **Refcounted leases**: `createRef()` / `AudioContextRef.whenDisposed()`,
  `_refCount`, `isUsed`.
- **Two-stage lazy teardown with different delays**:
  ```ts
  const SuspendDebounceTimeMs = 2000;       // suspend after 2s idle
  const CloseUnusedContextDebounce = 60000; // close after 60s idle
  private _suspendContextDebounced = debounce(() => this.suspendContext(), SuspendDebounceTimeMs);
  private _closeContextDebounced   = debounce(() => this.closeContext(),   CloseUnusedContextDebounce);
  ```
- A **maintain loop** that periodically tests and repairs a broken context.

Gated off entirely on MAUI (`BrowserInfo.hostKind === 'MauiApp'` → `null`), so
native never runs it.

## The asymmetries that make this a redesign, not a refactor

| | iOS | Android | Windows | Web |
|---|---|---|---|---|
| focus bookkeeping | `ActiveScopes` per mode | single holder + scope dict | none | refcount |
| can a request fail? | never | yes | n/a | n/a |
| do tunes take focus? | **no** | yes | n/a | n/a |
| native resource lifetime | process-lifetime (was) | per use | per use | **lazy, 2-stage** |
| session/mode concept | `AVAudioSession` category | `AudioManager.Mode` | none | context state |
| "nothing active" state | **not representable** | holder == null | n/a | refCount == 0 |

Two independent implementations of the same scope bookkeeping, three different
resource-lifetime policies, and one platform that already does it right.

## The modelling bug at the centre of it

`AudioFocusMode` is `{ Tune, Playback, Recording }` — **there is no `None`**.
`GetMode()` is `DefaultIfEmpty(AudioFocusMode.Tune).Max()`, so *"nobody wants
audio"* is indistinguishable from *"a tune wants audio"*. Everything downstream
keys off the mode, which is precisely why the session was never released and why
the fix had to test `_activeScopes.IsEmpty` separately. It is worse on iOS, where
tunes never take a scope at all — so there, `Tune` is *only* ever the empty-set
default.

---

# Part 3 — Proposed design

## Principles

1. **One source of truth, platform-independent.** Lease bookkeeping, mode
   derivation and teardown policy live in shared code. Platforms implement a
   narrow driver and raise events. No platform reimplements the model.
2. **Model "nothing" explicitly.** `AudioMode` gains `None` as its lowest value.
   No `DefaultIfEmpty`.
3. **Reconcile, don't command.** Compute a *desired state* (mode + which
   resources should exist + which should run), then converge actual → desired in
   one step. This removes the current "every de-escalation reconfigures"
   behaviour, where intermediate states are applied even though a higher state is
   about to be requested again.
4. **Lazy, per-resource release.** Every resource gets a keep-alive delay after
   its last lease ends. Acquisition cost, not tidiness, sets the delay. Two-stage
   where the platform supports it: *stop/suspend* first, *release/deallocate*
   later — exactly the web `suspend` → `close` split.
5. **Coalesce transitions.** Debounce reconciliation so a burst of
   acquire/release never produces a burst of native transitions. Minimise mode
   switches, not just allocations.

## Sketch

```
Core (platform-independent)
  AudioMode { None, Tune, Playback, Recording }
  IAudioLease : IAsyncDisposable            // what callers hold
  AudioSessionModel                          // THE source of truth
    - leases by mode (refcounted)
    - DesiredMode = max(active leases) ?? None
    - per-resource keep-alive timers
    - DesiredState { Mode, NeedMic, NeedPlayback, NeedTune }
    - Reconcile() -> IAudioPlatform calls, debounced
  IAudioPlatform                             // the thin driver
    - ApplyMode(AudioMode)
    - Acquire/Release(AudioResource)
    - events: FocusLost(mayRecover, canDuck), FocusGained, RouteChanged, Reset
```

`AudioSessionModel` is where the delays, hysteresis and reconciliation live, and it
is unit-testable without a device — the same reason `AudioSessionOwnership` was put
in `UI.Blazor` rather than a platform project.

## Keep-alive delays — starting points, to be measured

| resource | delay | rationale |
|---|---|---|
| Tune engine | ~1 s | one-off sounds; only has to outlive a burst |
| Playback engine | ~5 s | gaps between utterances are normal mid-call |
| Mic / recording | **longest, ~15–30 s** | most expensive to acquire on iOS (VPIO setup + AEC convergence) and on Android (HAL mode switch, hence `WarmUpAudioMode`) |
| Session / mode | after the last resource | downgrade only once nothing is left |

These are guesses from the acquisition costs observed so far. Each should be set
from a measurement, and the trade-off is explicit: **a longer delay costs idle CPU
and battery, a shorter one costs latency and switch churn.** The iOS finding
bounds one side — holding the mic engine costs ~0.09 cores per engine at idle, so
"keep it forever" is not free.

## What this fixes, concretely

- The `IsEmpty`-vs-`Tune` special case disappears; `None` is a real state.
- iOS stops paying for engines it isn't using, without the current
  all-or-nothing choice between "hold forever" and "rebuild per sound".
- Android's `WarmUpAudioMode` becomes a consequence of the keep-alive policy
  rather than a separate hack.
- Per-press session churn goes away: two rapid Recording leases inside the
  keep-alive window produce **zero** native transitions.
- Windows and web gain the same lifetime semantics for free.
- One place to answer "why is audio on right now", which is what made this
  investigation take a day.

## Phases

1. **Extract the model.** `AudioMode` with `None`, `AudioSessionModel`,
   `IAudioLease`, `IAudioPlatform`, plus unit tests for the state machine
   (transitions, hysteresis, coalescing). No platform changes.
2. **Port iOS** onto it — the platform with the most to gain and the best
   measurements. Fold `Tunes` + `Playback` into one output engine while doing it.
   Re-measure idle *and* during-call.
3. **Port Android**, replacing `MauiAudioFocusUI`'s holder model and folding
   `WarmUpAudioMode` into the keep-alive policy. Keep the "request can be
   refused" path — the core model must represent a *denied* lease, which iOS never
   produces.
4. **Adopt on web** — mostly aligning `AudioContextSource`'s existing refcount and
   debounces to the shared vocabulary; it already implements the policy.
5. **Windows** last: per-use today and cheap, so it only needs the lease API.

## Open questions

- **Should tunes take a lease on iOS?** They don't today. If the session is
  deactivated at idle, a tune must either re-activate it (a lease) or rely on
  `AVAudioEngine.start()` implicitly activating. Needs a device test —
  it is the most likely audible regression from the deactivation change already
  landed.
- **Where does `AudioSessionOwner` (PTT) fit?** It is genuine external ownership
  and outranks leases: under a PTT owner the app may configure but not activate.
  Probably a separate axis on the model, not a mode.
- **`PttPreRoll`'s fourth engine** is entirely outside `AudioEngines`. Does it
  become a lease, or stay special because it exists precisely for the window
  before the app's own recorder exists?
- **Session mode `Default` vs `VoiceChat`** and the **48 kHz → 16 kHz** AEC
  question are independent of this redesign, but they are the remaining
  during-call cost and should be A/B'd separately.
- **Interruptions vs leases.** A suspended lease is not a released lease; the
  model needs both, and today `AudioFocusScope.Suspend` carries that on one
  platform only.

## Already landed on `feat/ios-video-perf` (don't redo)

- Engines run only while they hold a player node; the `AVAudioEngine` is built on
  first use and **deallocated** when the last player node goes
  (`AudioEngine.Release`, `ReleaseIfIdle`, delays in `AudioEngines`).
- `Recording` is released when its capture ends rather than when the focus scope
  is released — which also closes a window where `AppleAudioCapture`'s `finally`
  cleared the input-node latch while the engine still held the node, so a PTT
  press in between could start a second `AVAudioEngine` on it.
- `AudioNode.Dispose` is idempotent (it drives the player count).
- The session is deactivated when no scopes remain, and recovery no longer
  re-activates into an empty scope set.
