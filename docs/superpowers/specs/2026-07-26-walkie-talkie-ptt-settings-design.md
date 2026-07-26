# Walkie-Talkie: PTT Settings + Gesture Engine (Sub-Project E2)

Date: 2026-07-26
Status: Approved design, pre-implementation
Depends on: Sub-project E1
(`2026-07-20-walkie-talkie-reply-to-voice-design.md`) — the
`WalkieTalkieReplyUI.RequestReply`/`StopReply` core, `ReplyTargetResolver`,
and `IncomingVoiceActivityUI`, all shipped and foreground-only.

## Background

Sub-projects A–D deliver the receive half of walkie-talkie: the server
pushes a speech-start wake, Android and iOS play the utterance headlessly
from a killed process, and the listener's playback reports a `Heard`
watermark back to the sender. E1 added the reply *pipeline* plus one
trigger — an on-screen button.

Two problems remain, and they compound.

First, there is no hands-free trigger. A woken user hears the message with
the phone in their pocket and must unlock, open the app, and tap to answer.
The receive half is done; the talk half is plumbing with nothing reachable
attached to it.

Second, nothing in the product tells a user that walkie-talkie exists or
lets them control it. Being woken is governed entirely by the pre-existing
"Keep listening" option, which used to mean only "don't auto-stop the
listen icon while the app is active." Sub-project A quietly overloaded it
into "wake my killed device with a high-priority push." A user who set it
for the old reason inherited the new behavior without being asked.

This sub-project fixes both: an explicit PTT opt-in with its own settings
surface, and a cross-platform gesture engine that makes replying hands-free.

## Goals

- Give PTT its own opt-in chat set, distinct from "Keep listening", so
  being woken is a deliberate choice.
- Ship a settings surface for the PTT experience: which chats, which
  gestures, sensitivity, hot-window length, cue audibility.
- Ship two start gestures — **flip to talk** (rotate 90° and back) and
  **double-shake** — that open the mic without touching the screen.
- Ship one stop gesture — **face-down / pocket** — that closes the mic,
  for any recording, not just walkie replies.
- Make the gestures learnable via an in-settings practice area.
- Bound sensor battery cost to actual conversation.

## Non-Goals

- **E3** — Android media / Bluetooth PTT hardware button.
- **E4** — iOS Apple PTT transmit (`DidBeginTransmitting` wiring, flipping
  the channel off `ListenOnly`).
- The `WalkieTalkieSession` de-static refactor, still deferred to E3/E4.
- Audio assets for the walkie cues. This spec adds the on/off toggle; the
  two cues remain vibration-only until someone produces the sounds.
- Changes to the receive/wake pipeline (A/B/C) or heard receipts (D).
- Per-chat gesture overrides. Gesture preferences are global to the device
  user; only chat membership is per-chat.
- Voice-keyword activation ("hey Voxt"), rejected in E1 for battery,
  privacy, and bystander capture.

## Key Decisions (with rationale)

1. **PTT is a separate opt-in set, not a rename of "Keep listening."** A
   new `UserWalkieTalkieSettings.PttChatIds` becomes the sole armed
   source. "Keep listening" reverts to its original, narrower meaning, and
   a chat can be listen-only, PTT-only, or both. Waking a killed device is
   a materially different commitment from keeping an icon lit, and it
   deserves its own consent. (Considered and rejected: one shared set —
   zero migration cost, but permanently conflates the two meanings;
   nesting PTT inside always-listened — forbids PTT on a chat you don't
   otherwise keep listening to, for no benefit.)

2. **No migration. Clean break.** The armed predicate switches to
   `PttChatIds` and nobody is enrolled until they opt in. This branch has
   never merged to `dev` and the whole path sits behind
   `Features_EnableWalkieTalkiePush`, so there is no installed base to
   grandfather — and silently auto-enrolling existing "Keep listening"
   users into background wakes would undo decision 1 on day one.

3. **False stops are cheap; false starts are not.** A gesture that opens
   the mic by accident broadcasts the user's surroundings. A gesture that
   closes it by accident is a minor annoyance. So start gestures must be
   deliberate multi-step motions (flip needs two rotations in sequence;
   shake needs repeated axis reversals), while the stop gesture can use
   the twitchy signals — face-down, pocket — that would be unacceptable
   for starting. This asymmetry is the organizing principle of the whole
   gesture set.

4. **The stop gesture is not PTT-scoped.** Face-down/pocket closes the mic
   whenever the mic is open, from any source: walkie reply, hold-to-talk,
   ordinary chat recording. Its setting therefore lives outside
   `UserWalkieTalkieSettings` (on `UserAppSettings`, surfaced under
   Privacy) and works for users who never enable PTT. A privacy control
   that only functions inside one feature is the wrong shape.

5. **Start gestures are live only during the answer window by default.**
   Sensors subscribe when a PTT chat has had incoming voice within
   `WalkieTalkieReplyRecencyWindow` (150 s), which `IncomingVoiceActivityUI`
   already tracks — so this is a subscription to existing state, not new
   tracking. This bounds battery to actual conversation and slashes the
   accidental-open surface. It also aligns with platform reality: the
   answer window is the only time the app is reliably alive in the
   background (post-wake FGS on Android, active PTT session on iOS), so
   backgrounded sensing is defensible rather than a fight with the OS. An
   `AreGesturesAlwaysOn` switch lifts the restriction for users who want to
   *initiate* hands-free; it ships **off**, with a battery caption, and on
   iOS it degrades to foreground-only because a killed process cannot
   sense anything.

6. **Detector cores are pure and live in `UI.Blazor.App`.** Each gesture is
   a state machine taking timestamped samples and emitting a fired/not-fired
   decision — no clock of its own, no I/O, no MAUI. The MAUI layer only
   feeds samples. This mirrors `ReplyTargetResolver` and
   `WalkieTalkie.ComputeIdleDropAt`, and it is what makes the E1 spec's
   requirement — "shake-pattern algorithm on recorded accelerometer sample
   sequences" — testable without a device.

7. **A practice area ships with v1, not later.** Gesture features fail in
   two directions users cannot diagnose: the motion was wrong, or the
   sensor is dead. The practice panel distinguishes them and doubles as the
   sensitivity calibration surface. For a feature whose entire risk profile
   is false positives and missed triggers, rehearsal is not polish.

8. **PTT chats are capped at 3**, matching `ActiveChatsUI.MaxActiveChatCount`
   and the original walkie premise of up to three continuously-listened
   chats. The cap also bounds server wake fan-out per speaker.

## Architecture & Data Flow

```
Settings (KVAS, per user)
  UserWalkieTalkieSettings   PttChatIds, gesture toggles, sensitivity,
                             AreGesturesAlwaysOn, HotWindow, cues
  UserAppSettings            IsFaceDownMicStopEnabled   (not PTT-scoped)
        │
        ├──── server ────────────────────────────────────────────────
        │   ServerKvasBackendExt.IsWalkieTalkieArmed(userId, chatId)
        │     now reads PttChatIds ONLY
        │       └─ gates NotificationsBackend.OnSpeechStartedEvent wakes
        │
        └──── client ────────────────────────────────────────────────
            IncomingVoiceActivityUI  (existing) — last incoming voice per chat
                    │
                    ▼
            MauiSensorFeed (App.Maui)
              subscribes Accelerometer + OrientationSensor while
                (answer window active) OR AreGesturesAlwaysOn OR practice mode
              subscribes proximity while the mic is open
                    │  timestamped samples
                    ▼
            GestureRecognizer (UI.Blazor.App, pure)
              ├─ FlipToTalkDetector  ─┐
              ├─ ShakeDetector       ─┤→ start → WalkieTalkieReplyUI.RequestReply
              └─ FaceDownDetector    ──→ stop  → ChatAudioUI.SetRecordingChatId(null)
```

The stop path deliberately bypasses `WalkieTalkieReplyUI.StopReply` and
calls `ChatAudioUI` directly, because it must close recordings that the
walkie core never opened. (`StopReply` gains its first caller separately:
the on-screen button becomes a true toggle — see Components.)

## Components

1. **`UserWalkieTalkieSettings`** — `src/dotnet/Api/Users/StoredSettings/`,
   modeled on `UserListeningSettings`: `StoredSettings, IHasOrigin,
   IHasKvasKey<UserWalkieTalkieSettings>`, plus a `UserSettingsUIExt`
   accessor and a `StoredSettings` union entry. Union id **17** — free in
   both the MemoryPack list (ends at 14) and the MessagePack list (ends at
   16), which have already diverged; verify both at implementation time.

   ```
   ChatId[]         PttChatIds            = []
   bool             IsFlipToTalkEnabled   = true
   bool             IsDoubleShakeEnabled  = true
   ShakeSensitivity ShakeSensitivity      = Medium   // enum: Low | Medium | High
   bool             AreGesturesAlwaysOn   = false
   TimeSpan         HotWindow             = 60s      // 30s | 60s | 120s
   bool             AreAudibleCuesEnabled = true
   ```

   `ShakeSensitivity` is a new enum beside the record. Lower sensitivity
   demands a harder shake, so the firing sets nest: Low ⊆ Medium ⊆ High.

   `With*/Without*` helpers for `PttChatIds` mirror
   `UserListeningSettings.WithAlwaysListeningChat`.

2. **`UserAppSettings.IsFaceDownMicStopEnabled`** — one nullable bool
   (`MemoryPackOrder(6)`, `Key(6)`) appended to the existing record, whose
   established shape is exactly this kind of toggle. No new settings type
   for a single flag.

3. **`ServerKvasBackendExt.IsWalkieTalkieArmed`** — body replaced by a
   single `PttChatIds.Contains(chatId)` read. It is the only server-side
   consumer of the armed concept, so the change is one function plus its
   tests.

4. **Gesture detectors** — `UI.Blazor.App/Services/Gestures/`, pure:
   - `FlipToTalkDetector` — portrait → landscape → portrait within a
     window (~2 s), from orientation samples.
   - `ShakeDetector` — counts signed axis reversals above a
     sensitivity-derived magnitude threshold within ~0.5 s, with ~1 s
     debounce.
   - `FaceDownDetector` — sustained face-down orientation, or proximity
     covered plus near-vertical inverted tilt (pocket). Requires the state
     to hold for a short dwell so a pick-up does not fire it.
   - `GestureRecognizer` — routes samples to the enabled detectors and
     emits a single `GestureEvent` stream.

5. **`MauiSensorFeed`** — `App.Maui/Services/`. Owns `Accelerometer` and
   `OrientationSensor` subscriptions and their lifecycle; reports sensor
   availability; exposes a practice-mode subscription independent of the
   answer window. Sample rate `SensorSpeed.UI`;
   `HIGH_SAMPLING_RATE_SENSORS` is not required.

6. **Proximity monitors** — `App.Maui/Platforms/Android` and
   `.../iOS`. MAUI exposes no cross-platform proximity API, so this is a
   small per-platform pair behind one interface (Android `SensorManager`
   `TYPE_PROXIMITY`; iOS `UIDevice.ProximityMonitoringEnabled`).

7. **`PushToTalkSettings.razor`** — `UI.Blazor.App/Components/Settings/`,
   registered in `SettingsModal.razor` as a new `SettingsTab` with a new
   `SettingsTabId.PushToTalk`, placed after Transcription (tabs below it
   renumber). Sections: chats (add via `ContactSelector`, remove, cap 3);
   answer gestures (flip toggle, shake toggle + sensitivity, always-on
   switch with battery caption); practice; hot window; audible cues.
   Sections 2–3 render only when `HostInfo.HostKind.IsMauiApp()` — there
   are no sensors on web, and an inert toggle is worse than an absent one.
   The remaining sections apply everywhere, since the on-screen button
   works on web.

8. **Practice panel** — part of the settings tab. Subscribes the
   recognizer in practice mode; detectors fire into the panel instead of
   `RequestReply`. Displays sample liveness (a dead sensor becomes visible
   rather than mysterious), a flash on each recognized gesture, and — for
   shake — peak magnitude against the active sensitivity threshold, so
   tuning sensitivity is legible instead of guesswork.

9. **Per-chat PTT toggle** — a row on `VoiceSettingsStartModalPage`,
   beside the existing listening controls, writing the same `PttChatIds`
   so a user can arm the chat they are looking at without leaving it.

10. **Hot-window plumbing** — `WalkieTalkieReplyUI` reads
    `HotWindow` from settings instead of inheriting
    `Constants.Audio.RecordingDuration` (30 s). This resolves the E1
    divergence, where the shipped window was 30 s against a specced ~60 s,
    by making it an explicit user choice with a 60 s default.

11. **On-screen button becomes a toggle** — `WalkieReplyToggle` calls
    `StopReply` when a reply is already hot. Today `StopReply` has zero
    callers and an accidental open cannot be cancelled at all; the stop
    gesture and this toggle both fix that.

## Reuse

**Existing abstractions to reuse (verified 2026-07-26):**

| Need | Existing abstraction |
|---|---|
| Settings record shape, KVAS storage, origin tracking | `StoredSettings` + `IHasOrigin` + `IHasKvasKey<T>`; `UserListeningSettings` is the template |
| Client settings read/write | `UserSettingsUI` + `UserSettingsAccessor<T>` via a new `UserSettingsUIExt` accessor |
| Server-side settings read | `IServerKvasBackend.ForUser(userId)` + typed accessors |
| Armed gate | `ServerKvasBackendExt.IsWalkieTalkieArmed` (rewritten, not replaced) |
| Mic open / close | `ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk:true)` / `(null)` |
| Reply policy, target resolution, cold-start dead-man | `WalkieTalkieReplyUI.RequestReply`/`StopReply`, `ReplyTargetResolver` (E1) |
| Answer-window signal | `IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt` (E1) + `Constants.Audio.WalkieTalkieReplyRecencyWindow` |
| Armed-chat cap precedent | `ActiveChatsUI.MaxActiveChatCount` |
| Settings tab shell | `SettingsModal.razor` + `SettingsTab` + `SettingsTabId` |
| Chat picking | `ContactSelector` |
| Settings rows / tiles | `FormBlock`, `Tile`, `TileItem`, `TileTopic` (see `PrivacySettings.razor`, `VoiceSettingsListeningModalPage.razor`) |
| Per-chat voice settings host | `VoiceSettingsModal` / `VoiceSettingsStartModalPage` |
| Sensors | `Microsoft.Maui.Devices.Sensors.Accelerometer`, `OrientationSensor` |
| Resilient background workers | `UIWorkerBase<AppUIHub>` + `AsyncChain.RetryForever` + `RunIsolated` (see `IncomingVoiceActivityUI`) |
| Cues | `TuneUI` + existing `Tune.WalkieReplyEnded` / `WalkieReplyNothingHeard` |
| Host-kind gating | `HostInfo.HostKind.IsMauiApp()` |
| Wire-compat guard | `ApiEvolutionTest` |

No sibling `ActualLab.Fusion` abstraction is needed beyond the compute
services already in use.

**Reusability of new components.** The detector cores are the only
plausibly reusable new code. They are pure state machines over sensor
samples with no chat, audio, or walkie dependency, so they could sit in
`ActualChat.Core`. This spec places them in
`UI.Blazor.App/Services/Gestures/` instead: `ActualChat.Core` is a
dependency of server projects that will never process accelerometer
samples, and today there is exactly one consumer. The namespace is
self-contained, so promotion later is a file move. Everything else is
inherently local — settings records belong with their siblings in `Api`,
the sensor feed is MAUI-bound by definition, and the Razor components are
UI.

## Error Handling

- **Sensors unavailable or permission denied** — `MauiSensorFeed` reports
  availability; the gesture sections render as unavailable rather than
  offering toggles that do nothing.
- **No proximity sensor** — `FaceDownDetector` degrades to
  accelerometer-only orientation: still catches face-down on a surface,
  loses in-pocket. Degrade, never disable.
- **Settings read failure** — defaults apply, and `PttChatIds` defaults to
  empty, so a failed read can never accidentally arm a chat. Failures fall
  toward silence.
- **Sensor feed crash** — `RetryForever` per the existing worker pattern. A
  dead feed degrades to on-screen-button-only and must never block
  recording.
- **Gesture fires with no resolvable target** — the existing E1 path: soft
  "nothing heard" cue, mic never opens.
- **Stop gesture races a start** — stop wins. Fail closed on the mic.
- **Practice mode leaking into live triggering** — practice subscriptions
  route detector output to the panel only; the panel never calls
  `RequestReply`, so a gesture rehearsed in settings cannot transmit.

## Testing

**Unit (no device, `Chat.UI.Blazor.UnitTests`):**
- `FlipToTalkDetector` — fires on portrait→landscape→portrait inside the
  window; does not fire on a half-rotation, on a slow drift, or when the
  return exceeds the window.
- `ShakeDetector` — fires at the required reversal count per sensitivity;
  does not fire on a single spike; honours debounce.
- `FaceDownDetector` — fires on sustained face-down and on
  proximity+inverted-tilt; does not fire on a transient pick-up.
- Sensitivity monotonicity — every sample sequence that fires at Low also
  fires at Medium and High.
- `GestureRecognizer` — routes only to enabled detectors; disabled gestures
  never emit.
- Settings round-trip and union serialization.

**Integration:**
- The decoupling regression, and the most important test here: a user with
  `ListeningMode.Forever` and empty `PttChatIds` receives **no** wake push.
- A user with `PttChatIds` containing the chat and no listening settings at
  all **does** receive one.
- `WalkieTalkiePushTest` and `IsWalkieTalkieArmed` coverage updated to the
  new predicate; `ApiEvolutionTest` guards the settings union.

**Device-only (host):**
- Practice panel registers real flips and shakes on Android and iOS.
- Backgrounded, inside the answer window: flip → mic opens → reply lands.
- Face-down closes a *normal*, non-PTT recording.
- Battery comparison, answer-window-only versus always-on.

**Known verification risk.** This adds device-only surface on top of a base
that has never been verified on a device: sub-projects B and C have never
been compiled, and the Android and iOS manual scripts from their specs have
never been run. Until someone completes a host pass on the existing walkie
stack, "the gesture did not fire" and "the wake never arrived" will be
difficult to tell apart. A host pass on B/C should precede device testing
of this sub-project.

## Open Questions (to resolve during planning)

- Exact `StoredSettings` union id, confirmed free in both the MemoryPack
  and MessagePack lists.
- Concrete detector thresholds: shake magnitude per sensitivity level,
  reversal count, flip window length, face-down dwell time. Seed from
  recorded samples during implementation rather than guessed here.
- Whether `OrientationSensor` or `Accelerometer`-derived orientation gives
  a cleaner flip signal in practice; the detector interface is the same
  either way.
- Whether the settings tab needs its own icon asset or can reuse an
  existing one (`icon-talking` is already used by `WalkieReplyToggle`).
- Whether `HotWindow` is best applied by passing it into
  `RecordingIdleOptions` at the `RecordChat` call site or by a narrower
  seam, given E1's constraint not to disturb `RecordChat`.
