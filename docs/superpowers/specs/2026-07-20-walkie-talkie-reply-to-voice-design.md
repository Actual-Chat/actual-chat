# Walkie-Talkie: Hands-Free Reply to Incoming Voice

Date: 2026-07-20
Status: E1-E4 implemented (device verification pending)

## Background

The walkie-talkie feature so far is receive-oriented: sub-projects A/B/C
wake a backgrounded or killed device and play incoming voice headlessly,
and sub-project D reports "heard" back to the sender. What's missing is the
*talk* half — a hands-free way to reply to an incoming message without
opening the app and holding a button.

iOS ships an Apple Push to Talk integration, but it is **receive-only
today**: the channel runs in `ListenOnly` mode and
`DidBeginTransmitting`/`DidEndTransmitting` are empty stubs. Android has no
walkie transmit trigger at all and no sensor/media-button code. So a working
"reply" experience is unbuilt on both platforms.

Note: ordinary **full-duplex voice (record while listening) already works on
both platforms** in normal chat use. This feature does not introduce
duplex or echo-cancellation work — it reuses the existing capture path. What
it adds is the *triggering* and *lifecycle* of a hands-free reply.

## Goals

- Let a user reply to an incoming walkie-talkie message hands-free, via a
  trigger rather than the app UI.
- Triggers: **shake** (both OSes), **Android media/hardware button**,
  **on-screen PTT** (both OSes), and **iOS Apple PTT transmit**.
- One trigger opens a **hot conversation window**: the mic stays live across
  a back-and-forth and closes only after ~1 minute of two-way silence.
- Route the reply to the chat that most recently spoke.
- Keep accidental activations from broadcasting anything.

## Non-Goals

- Duplex or AEC work — already solved by the existing capture path.
- Threaded reply-to-a-specific-entry semantics (replies post as plain voice
  messages; the `repliedChatEntryId` plumbing stays dormant).
- Always-on open-mic / voice-activated start (rejected: battery + privacy +
  bystander capture).
- Changes to the receive/wake pipeline (A/B/C) or heard receipts (D) beyond
  reading their signals.
- A user-facing shake-sensitivity settings surface (possible later; v1 ships
  one tuned profile).

## Key Decisions (with rationale)

1. **Extend `WalkieTalkieSession` to own both directions; do not add a
   parallel service.** Receive and transmit are one walkie session with a
   listen side and a talk side — splitting them is artificial. Refactor:
   (a) de-static-ify `WalkieTalkieSession` into an instance service; (b) move
   its platform-neutral core into `UI.Blazor.App` next to `WalkieTalkie.cs`;
   (c) keep `App.Maui` as the platform-adapter layer via the existing
   `WalkieTalkiePlatform` hook shape. The transmit policy's every input and
   output already lives in `UI.Blazor.App`, so the core belongs there and
   becomes unit-testable without MAUI. (Considered and rejected: a separate
   reply-controller service — it would duplicate the session's scope/FGS
   management and fracture "walkie session" into two brains.)

2. **All triggers funnel into one core entry point.** `RequestReply(source)`
   / `StopReply(reason)`. Triggers resolve nothing and carry no policy; the
   core does target resolution and lifecycle. New trigger types are new
   adapters, no core change.

3. **Immediate mic open, VAD dead-man switch — no pre-open confirm.**
   Because the outbound opus stream is already VAD-gated
   (`onVoiceActivityChange` starts/stops publishing per utterance), a stray
   trigger in a silent environment opens the mic but **publishes nothing**;
   the cold-start timeout then closes it. This buys most of what a
   confirm/countdown would, without the friction. The residual gap — a stray
   trigger while real speech exists nearby (in-person talk, bystander, TV) —
   is knowingly traded away for the snappy walkie feel, and mitigated by
   audible cues and deliberate-pattern shake detection.

4. **Shake requires a deliberate pattern, not a spike.** ≥2–3 axis reversals
   above a magnitude threshold within ~0.5s, ~1s debounce. Kills most
   walking/car/hand-down jostles before VAD is even consulted. One shared
   cross-platform `Microsoft.Maui.Devices.Sensors.Accelerometer` detector
   serves both OSes.

5. **Reply target = last chat that spoke, with fallbacks.** Answer-the-call
   semantics with a sane cold start (see Target Resolution).

## Architecture

Layering after the refactor:

```
UI.Blazor.App (platform-neutral, testable)
  WalkieTalkieSession (instance)      — orchestration: wake→playback (existing) + reply
    ReplyTargetResolver               — last-spoke policy
    HotMicController                  — mic lifecycle state machine
  WalkieTalkie.cs (existing helpers)  — IsStaleWake, ComputeIdleDropAt
  On-screen PTT trigger component

App.Maui (platform adapters, via WalkieTalkiePlatform hooks)
  ShakeDetector (shared, both OSes)   — Accelerometer → RequestReply
  Android: media-button adapter, FGS mode
  iOS: Apple PTT transmit wiring (DidBeginTransmitting → RequestReply)
```

Inbound: every trigger → `WalkieTalkieSession.RequestReply(source)`.
Outbound: the core calls `ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk: true)`.

## Trigger Layer

All triggers are dumb adapters calling `RequestReply` / `StopReply`.

- **Shake** — one shared `Accelerometer` detector in `App.Maui/Services`,
  active on both OSes while at least one chat is armed. Deliberate-pattern
  detection (decision 4). Low sample rate (`SensorSpeed.UI`/`Game`);
  `HIGH_SAMPLING_RATE_SENSORS` not required.
- **Android media/hardware button** — a `KeyEvent`/media-button adapter
  (headset hook, Bluetooth PTT, volume-as-PTT) → `RequestReply`. Tap-to-start
  (enters the hot window), not hold.
- **On-screen PTT** — a walkie-reply button in `UI.Blazor.App`, tap-toggles
  `RequestReply`/`StopReply`. Distinct from the legacy hold-to-talk
  `RecorderToggle`, which is unchanged for normal chat recording.
- **iOS Apple PTT transmit** — wire the empty
  `DidBeginTransmitting`/`DidEndTransmitting` stubs to `RequestReply`/
  `StopReply` and flip the channel off `ListenOnly`.

## Reply Target Resolution (`ReplyTargetResolver`)

On `RequestReply`, choose the target in order:

1. Armed chat with incoming voice in the last ~2–3 min ("answer the call").
2. Else the on-screen focused walkie chat (foreground only).
3. Else the sole armed chat, if exactly one is armed.
4. Else ignore the trigger and play a soft error tune.

Recency source: per-chat last-incoming-stream time from the listening
pipeline (`LiveStreamUI` / `ChatListeningPlayer` stream-started). Exact API
pinned during planning. Replies post as plain voice entries (no
`repliedChatEntryId`).

## Hot-Mic Lifecycle (`HotMicController`)

A state machine over a fake-clockable timer.

- **Idle → (target resolved):** `SetRecordingChatId(chatId, isPushToTalk:true)`,
  play open cue (`Tune.BeginRecording`), → **ColdListen**.
- **ColdListen** (mic open, no voice yet): run `T_cold` (10–20s). VAD
  voice-active → **Hot**. `T_cold` expires → close, play "nothing heard"
  cue → **Idle**. (Dead-man switch; nothing was published.)
- **Hot** (voice seen ≥ once): mic open, VAD gates uploads per utterance.
  `T_hot` (~60s) **resets on either** own VAD voice-active **or** incoming
  audio activity in the target chat. `T_hot` expires (~1 min two-way
  silence) → close, play "session ended" cue → **Idle**.
- **Any state → `StopReply`** (on-screen tap, Apple PTT release): close.
- **Re-trigger while Hot:** no-op (idempotent).

Inputs: `AudioRecorderState.IsVoiceActive` (own VAD), the incoming-activity
signal (same source as the resolver), the target chat. Output:
`SetRecordingChatId`. All in `UI.Blazor.App` — the whole machine unit-tests
without MAUI. Concrete timer values (`T_cold`, `T_hot`, recency window) land
in `AudioSettings`/`Constants.Audio`.

## Platform Specifics

- **Android** — `SetRecordingChatId(chatId)` already drives
  `AudioWidget.ComputeState` to `AudioWidgetMode.Recording`, which starts the
  FGS with `ForegroundService.TypeMicrophone` (already declared; manifest
  perms present). Gap to close: the wake path starts the FGS in `Listening`
  mode, so a backgrounded reply must (re)start/switch the FGS to `Recording`
  so it holds the mic type. Verify the widget re-invokes `StartForeground`
  with the new type on mode change. No new permissions.
- **iOS** — background mic is only legal through Apple PTT. Foregrounded, a
  trigger opens the mic via the existing `PlayAndRecord` `AudioSession`;
  **backgrounded, the trigger routes through
  `PTChannelManager.requestBeginTransmitting`** for a sanctioned session, and
  `DidBeginTransmitting` then calls `RequestReply`. Exact Apple API surface
  (transmission-mode value, begin/stop calls) verified in planning.

## Full-Duplex & Echo

Incoming playback continues while the mic is hot. **This already works** —
record-while-listening is existing app behavior on both platforms, so AEC is
inherited from the current capture path, not new work. The two-device test
should still confirm no regression in the hot-window scenario, but this is a
verification step, not a design risk.

## Cues

Reuse `Tune.BeginRecording` for mic-open. Add two tunes (enum + assets, web +
native): "nothing heard / closed" (cold-timeout) and "session ended"
(hot-timeout); reuse the existing soft error tune for an ignored trigger.
These audible cues are the substitute for the confirm-countdown traded away
in decision 3 — they make an accidental open noticeable.

## Reuse

**Reuse as-is:** `ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk:true)`
(programmatic start/stop); `AudioRecorderState.IsVoiceActive` + the VAD
`onVoiceActivityChange` outbound gating; `repliedChatEntryId` plumbing (kept
dormant); mic-typed `AndroidAudioWidgetForegroundService` + manifest perms;
`WalkieTalkiePlatform` hook pattern; `WalkieTalkie.cs` helpers;
`ChatAudioUI.GetChatsYouNeedToKeepListeningTo` (armed set); the `PlayAndRecord`
`AudioSession` config; `IosPushToTalk` / `PTChannelManager` receive
scaffolding; the existing full-duplex capture path (AEC).

**New components and placement:**
- `WalkieTalkieSession` core (refactored to instance), `ReplyTargetResolver`,
  `HotMicController` → **`UI.Blazor.App`** (walkie namespace, beside
  `WalkieTalkie.cs`) — all I/O is local, unit-testable, reusable by web.
- Shake detector → **`App.Maui/Services`** (shared, both OSes).
- Android media-button adapter → `App.Maui/Platforms/Android`.
- iOS Apple PTT transmit wiring → `App.Maui/Platforms/iOS/PushToTalk`.
- On-screen PTT trigger → `UI.Blazor.App` component.
- New tunes → `Tune` enum + web/native assets.

No `ActualChat.Core` type is warranted — this is entirely client/UI behavior.

## Testing

- **Unit (no device):** `HotMicController` transitions on a fake clock
  (cold-timeout close; voice→Hot; `T_hot` reset on own-voice and on incoming;
  two-way-silence close; `StopReply`; idempotent re-trigger).
  `ReplyTargetResolver` recency-window + fallback chain on fabricated
  activity. Shake-pattern algorithm on recorded accelerometer sample
  sequences.
- **Device-only (host, as with B/C):** record-while-backgrounded on Android;
  Apple PTT background transmit on iOS; two-device hot-window pass
  (back-and-forth without re-triggering, confirm no echo regression, confirm
  ~1-min two-way-silence close).

## Open Questions (resolved during planning)

- Exact per-chat last-incoming-activity API (`LiveStreamUI` vs
  `ChatListeningPlayer` stream-started) for the resolver and the hot-window
  incoming-reset.
- Whether the Android FGS switches `foregroundServiceType` mid-session on a
  `Listening → Recording` mode change, or must be restarted.
- Exact Apple PTT transmit API surface and its interaction with the existing
  `ListenOnly` receive scaffolding.
- Whether shake-arming stays pure armed-only (v1 default) or gates on an
  active conversation to bound battery — accelerometer sample-rate choice is
  part of this.
- Media/hardware-button availability and which key(s) to grab on Android.
