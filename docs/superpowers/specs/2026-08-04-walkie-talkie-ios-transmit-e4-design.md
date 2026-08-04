# Walkie-Talkie: iOS Apple PTT Transmit (Sub-Project E4)

Date: 2026-08-04
Status: Implemented (device verification pending — see plan Task 10)
Depends on: C (`2026-07-13-walkie-talkie-ios-design.md`) for the joined PTT
channel, `IosPushToTalk`, `IosPushToTalkUI` and the externally-activated
audio-session mode; E1 (`2026-07-20-walkie-talkie-reply-to-voice-design.md`)
for `WalkieTalkieReplyUI.RequestReply`/`StopReply` and `ReplyTargetResolver`;
E2 (`2026-07-26-walkie-talkie-ptt-settings-design.md`) for
`UserWalkieTalkieSettings` and `PushToTalkSettings.razor`; E3
(`2026-08-03-walkie-talkie-headset-button-design.md`) for scope-driven
startup, `AppScopeAccessor`, and the `HeadsetButtonPolicy` precedent.

## Background

Sub-projects A–D deliver the receive half of walkie-talkie. E1 added the
reply pipeline plus an on-screen button, E2 added an explicit PTT opt-in
and two motion gestures, and E3 made every trigger work after a wake and
added an Android headset button.

iOS can hear but cannot answer. Sub-project C joined the Apple Push to Talk
channel in `ListenOnly` mode deliberately — receive-only v1 — and left the
transmit callbacks empty. An iOS user woken by a PTT push hears the
utterance from a locked, killed phone and then has to unlock and open the
app to reply, which is the same defeat-the-premise problem E3 fixed on
Android.

The fix is not the same fix, because the platform inverts the control.

On Android our `MediaSessionCompat` callback receives the key event, we run
`HeadsetButtonPolicy`, and we decide. On iOS, once the channel leaves
`ListenOnly` the system owns the trigger: iOS renders its own Talk button in
the status-bar pill and on the Lock Screen, activates the audio session
itself, and calls `didBeginTransmitting`. The app's job is to record when
told, not to decide when.

Two consequences shape everything below.

**`requestBeginTransmitting` is foreground-only**, with one documented
exception — a CoreBluetooth peripheral's characteristic change. So E2's
shake and flip cannot start a transmission from the background on iOS. This
costs nothing in practice: a suspended iOS app receives no accelerometer
callbacks either, so those gestures were already foreground-only there.

**There is no iOS equivalent of E3's earbud button.** Ordinary AVRCP
play/pause is not a Push to Talk transmit source, and the app registers no
`MPRemoteCommandCenter` handlers at all today. Only a CoreBluetooth
accessory can transmit from the background.

What iOS gives instead is a system Talk button reachable from the Lock
Screen without unlocking. That is a good answer to the phone-in-pocket
case — just not the one E3 built.

## Goals

- Flip the PTT channel out of `ListenOnly` so the system Talk button
  appears, and record a real chat voice message when the system says a
  transmission has begun.
- Make transmit work from a killed process, which is the scenario the
  feature exists for, **without losing the words spoken before the app
  has finished booting**.
- Give transmit its own opt-in, so arming a chat for listening does not
  silently grant a Lock Screen microphone.
- Keep the send path's audio-session ownership explicit, so the framework
  and `AppleAudioFocusUI` cannot both configure `AVAudioSession`.

## Non-Goals

- **Routing foreground replies through PTT.** The in-app PTT button and
  E2's gestures keep today's `SetRecordingChatId` path unchanged. See
  decision 2.
- **CoreBluetooth PTT accessories.** Background hardware transmit needs a
  specific accessory in hand to build against — the same reason E3
  rejected vendor PTT pucks.
- **Half duplex.** See decision 4.
- **Mac Catalyst**, which has no Push to Talk framework.
- **The `WalkieTalkieSession` de-static refactor**, deferred since E2 and
  now closed rather than deferred again. See decision 9.
- Any change to the receive/wake path (A–C) or heard receipts (D).
- Android behaviour. In particular Android keeps its answer-window gate;
  the unbounded reply window introduced here belongs to the system Talk
  button alone.

## Key Decisions (with rationale)

1. **System Talk button only.** Flip off `ListenOnly`, wire
   `didBeginTransmitting` / `didActivateAudioSession` to the recorder, and
   leave every existing foreground trigger alone. This is the only part of
   the surface that works from a locked phone, and it is the part that
   does not exist today.

2. **Foreground replies do not go through `requestBeginTransmitting`**
   (considered and rejected: routing every reply through PTT for a single
   audio path). Three reasons. First, it would not actually unify
   anything: `IosPushToTalkUI` joins only while PTT chats are armed, so
   replies in unarmed chats would still take the old path — two paths plus
   a runtime branch that can flip underneath a live recording. Second, the
   two lifecycles have non-overlapping stop conditions — ours ends on the
   `HotWindow` idle timeout, the cold-start dead-man switch, a face-down
   gesture, `StopReply`, or scope disposal; PTT's ends on button release,
   `didEndTransmitting`, session deactivation, or channel leave — and every
   cross-pair would need reconciling on a platform nothing here can test.
   Third, it would put Apple's transmit chime and system sheet on ordinary
   in-app replies that work correctly today. The end state of one PTT
   boundary crossing is right, but reaching it by rerouting the working
   path is the wrong order.

3. **The answer window does not gate the Talk button.** On Android the
   window protects an overloaded control: play/pause already had a job, so
   starting a reply outside the window would hijack it. The iOS Talk button
   has no other job — it exists only because we joined a channel, it is
   labelled with a chat name, and pressing it is a deliberate act inside a
   system sheet. Fighting a deliberate press with a silent abort is worse
   than any targeting mistake. (Considered and rejected: mirroring
   Android's gate, which would chime, show "transmitting", and die half a
   second later with no explanation.)

4. **A resolvable target is still required.** Transmit records into the
   most recently active PTT chat however long ago that was; if
   `ReplyTargetResolver` returns nothing at all, we stop transmitting and
   play the existing `Tune.WalkieReplyNothingHeard` cue from E1. The
   accepted cost is that after a long silence a press can land in a chat
   the user had forgotten was armed — mitigated by decision 8.

5. **Full duplex** (considered and rejected: half duplex, the radio
   metaphor). Voxt is a chat: live sessions already carry simultaneous
   speakers, E1's `RequestReply` unmutes without stopping playback, and E3
   decided explicitly that a reply wins mid-playback rather than waiting
   for the incoming utterance to end. Half duplex would silently reverse
   that decision on one platform only. Two caveats are carried as risks:
   Apple's documentation does not say whether half duplex *blocks* a
   transmit request or *preempts* the active remote participant, so the
   divergence could not be bounded precisely; and full duplex means mic and
   speaker are live together under a session configuration the framework
   owns and we deliberately no longer touch.

6. **Pre-roll the audio natively and flush it into the recorder**
   (considered and rejected: accepting the loss, with or without a
   readiness cue). The PTT channel join survives app kill and reboot, so
   the primary case is a Talk press against a killed process, and audio
   spoken before the managed recorder exists is gone forever — unlike the
   receive side, where a slow start merely replays the same server-held
   audio later. The trap is specific: **the framework chimes when it
   activates the audio session, not when we are ready**, so iOS actively
   trains the user to speak at the moment we are least able to record them.
   A walkie-talkie that drops the first words of every reply from a locked
   phone is not shippable. The buffer is bounded in both seconds and bytes
   and self-limiting: if the scope is not up within the cap, it is
   discarded and the behaviour degrades to "accept the loss".

7. **An early release still sends.** On a cold start a short press ends
   before the recorder exists. The buffered words are real speech the user
   intended to send, so we finish booting and deliver the pre-roll as the
   message, subject to a minimum-duration floor. Discarding it is the exact
   loss decision 6 exists to prevent.

8. **Transmit gets its own setting, on by default.**
   `UserWalkieTalkieSettings.IsPttTransmitEnabled`, a nullable bool read as
   on, mirroring E3's `IsHeadsetButtonEnabled` including its treatment of
   settings blobs written before the member existed. Without it, arming a
   chat for listening would also grant a Lock Screen microphone — the same
   shape as sub-project A overloading "Keep listening" into "wake my killed
   device", which is the grievance E2 was written to correct. That
   grievance was really about invisibility, so a discoverable, revocable
   toggle answers it; off-by-default was rejected because it ships the
   sub-project's whole value behind a control nobody finds. Turning it off
   keeps the channel in `ListenOnly` so the button never appears — a dead
   button is the failure mode decision 3 rejects.
   The channel descriptor also tracks the last-active chat title, so the
   system sheet names the chat rather than the aggregate `"Voxt"`; this is
   what makes decision 4's targeting legible.

9. **The `WalkieTalkieSession` de-static refactor is closed, not
   deferred.** It was deferred from E2 to E3 to E4 on the theory that
   statics plus per-scope services are fragile. E3's `AppScopeAccessor`
   solved the actual problem — a static component reaching whichever scope
   is live — and the remaining statics are process-level by necessity,
   because the PTT delegate is a process singleton initialised from
   `FinishedLaunching`. Carrying it forward a third time is worse than
   closing it.

## Architecture & Data Flow

`IosPushToTalk.Initialize()` runs from `FinishedLaunching`
(`MauiProgram.iOS.cs:49`), so the PTT delegate is live long before any
Blazor scope exists. That gap is precisely what decision 6 exploits: the
native side can be capturing while the managed side is still booting.

The receive path already has the shape — `WalkieTalkieSession.HandleWake`
boots the app, resolves a scope, and acts. Transmit is the same three steps
with a different verb and a much tighter deadline, so it becomes a sibling
rather than a new mechanism.

```
Talk pressed (Lock Screen, app killed)
  -> iOS launches app -> FinishedLaunching -> IosPushToTalk.Initialize()
  -> DidBeginTransmitting(source)  : mark transmitting, BlazorWebViewApp.EnsureStarted()
  -> DidActivateAudioSession       : owner = PttTransmit, PttPreRoll.Start(token)
  -> WalkieTalkieSession.HandleTransmit
        await WhenAppReady (capped) -> resolve scope -> RequestReply(unbounded window)
  -> SetRecordingChatId(chatId, isPushToTalk: true)
  -> AppleAudioCapture drains pre-roll(token), then live frames
  -> release -> DidEndTransmitting -> StopReply -> DidDeactivateAudioSession -> owner = App
```

Warm and foreground presses take the same path; `AppScopeAccessor` returns
the WebView scope, and the pre-roll starts and drains almost immediately
rather than being special-cased.

## Components

**`IosPushToTalk` transmit wiring** *(existing)* — `DidBeginTransmitting`
and `DidEndTransmitting` gain bodies. The subtle part is that
`DidActivateAudioSession` is shared between receive and transmit and today
routes unconditionally to a pending wake; it must branch on whether a
transmission is in flight.

**`WalkieTalkieSession.HandleTransmit`** *(extends existing)* — the
boot-and-resolve block currently inlined in `HandleWake` is extracted so
both use it: live WebView scope, else headless, else the lost-the-race
retry. Keeping this in `WalkieTalkieSession` rather than a new type is
deliberate; E3 showed that restating the scope-resolution rules elsewhere
is what goes wrong.

**`PttPreRoll`** *(new)* — a process-level `AVAudioEngine` input tap holding
raw hardware-format PCM with its format and a per-transmission token,
bounded in seconds and bytes. It must stop its own engine before
`AudioEngines.Recording` starts; two engines on the input node with voice
processing enabled is not to be attempted. The bounded-ring-and-token core
is a pure type; only the tap is native.

**`AppleAudioCapture` pre-roll drain** *(existing)* — `CaptureInternal`
yields buffered frames before live ones, resampling them through the normal
`ResamplerFactory` once the scope exists. This is why the buffer stores raw
hardware format: resampling is a scoped concern and must not be duplicated
natively.

**Reply target for a deliberate press** *(existing, extended)* —
`RequestReply` resolves through `ReplyTargetResolver` bounded by
`WalkieTalkieReplyRecencyWindow`, so it would refuse after the window.
It gains an optional recency-window override: bounded for gestures and the
Android headset button, unbounded for the system Talk button. The "nothing
resolves" branch already plays `WalkieReplyNothingHeard`.

**`AudioSessionOwner`** *(new)* — `AudioSession.IsExternallyActivated`
becomes a typed owner (`App` / `PttPlayback` / `PttTransmit`). This is not
tidying: today `Reconfigure` still calls `ConfigureUnsafe` while externally
activated, so the recorder acquiring `AudioFocusMode.Recording` would set
category and mode underneath a session the framework owns. Under transmit,
configuration must be skipped entirely, and a bool cannot express that
difference.

**`IosPushToTalkUI` becomes mode-aware** *(existing)* — it watches
`GetPttChatIds` today and joins or leaves. It must also read the new
setting and set `FullDuplex` versus `ListenOnly` reactively, including when
the setting changes while joined; `DidJoinChannel` currently hardcodes
`ListenOnly`.

**`UserWalkieTalkieSettings.IsPttTransmitEnabled`** *(existing type,
extended)* — nullable bool defaulting to on, plus a toggle in
`PushToTalkSettings.razor` rendered only on iOS. Note that E3's "Headset
button" toggle (`PushToTalkSettings.razor:54`) is *not* platform-gated and
currently renders on iOS and web, where it controls nothing; E4 should gate
it to Android while adding its own.

## Error Handling

**Stale pre-roll attaching to the wrong recording.** If `HandleTransmit`
dies — no target, boot timeout, no scope — a buffer full of PCM is left in a
process-level static, and the next recording, possibly minutes later and in
a different chat, would prepend a stranger's audio to a message. This is
E3's `AndroidAudioWidget._instance` bug in a new place. The pre-roll carries
a token issued per transmission; `AppleAudioCapture` drains only a matching
token and discards anything else.

**Teardown racing a hot transmit.** `WatchTeardown` treats "no listening
chats and no replay" as idle and disposes the headless scope after two
checks. It does not look at `IsRecording`. A transmit started into a
headless scope that is not replaying anything is exactly that state, so the
watcher would dispose the scope out from under an open mic within ~10
seconds. `StopAndDispose` would close the reply cleanly so nothing
corrupts, but the user is cut off mid-sentence for no reason. The watcher
must count recording as activity.

**Release before the recorder exists.** On a cold start a short press ends
long before the scope is up, so `DidEndTransmitting` arrives with nothing
recording. Per decision 7 this is not an error: the boot continues, the
pre-roll is delivered as the message if it clears the minimum-duration
floor, and the scope tears down normally afterwards. Below the floor the
buffer is discarded silently.

**Talk pressed while already recording.** `RequestReply` is idempotent and
returns early, so the transmission never owns that recording — but
`DidEndTransmitting` would then call `StopReply` and kill a reply it did not
start. A transmission may only stop what it started.

**Microphone permission from a locked screen.** `RequestReply` calls
`MicrophonePermission.CheckOrRequest`, and no prompt can be shown with no
UI. If permission is not already granted, fail fast and stop transmitting
rather than block against the boot cap.

**Ownership getting stuck.** `AudioSession`'s existing comment warns that a
stuck flag permanently disables the app's own session activation; three
states make that worse. Ownership reverts to `App` on
`DidDeactivateAudioSession`, `DidEndTransmitting` and `DidLeaveChannel`,
plus a watchdog that reverts it if no PTT callback has arrived while
nothing is playing or recording.

**Practice mode never transmits**, the same rule as E3. A channel leave or
the setting being turned off mid-transmit stops the reply and discards the
buffer.

## Testing

E4 is almost entirely platform code, and unlike E3 there is no partial
coverage to fall back on: `App.Maui.csproj` is outside
`ActualChat.CI.slnf`, and iOS additionally cannot link off macOS. E2 and E3
produced five defects of exactly this shape. The design therefore pushes
logic off the platform rather than planning to test it there.

**Testable on a build machine:**

- The pre-roll's bounded ring, eviction, and token matching — a pure type
  wrapping `BlockRingBuffer<T>`; only the tap is native.
- `AudioSessionOwner` as a pure state machine, including every
  revert-to-`App` path.
- The unbounded-window resolve, a `ReplyTargetResolver` argument and
  already a pure unit from E1.
- The settings round-trip, including `UserSettings.KeyToType` — the E2
  defect that meant no walkie setting had ever round-tripped on the client
  path.

This is E3's lesson applied up front: `HeadsetButtonPolicy` was pure by
construction, which is why it carried real tests while everything around it
was unverifiable.

**`scripts/csc-ios-probe.sh`** — the Android probe pointed at
`Microsoft.iOS.Ref` instead of `Mono.Android`, with the same honest limits:
no analyzers, no linking, no device, hand-tuned per file. Worth building
because it is the only thing that will compile `IosPushToTalk` before a Mac
does.

**Sweep discipline** — E3 found `GestureUITest` silently red since E2
because no plan's verification ran `Chat.UI.Blazor.IntegrationTests`. E4's
final sweep names its projects explicitly, and carries E3's open follow-up:
diff the CI test-project list against what plans actually invoke.

**Device verification** has a precondition that outranks every item in it:
**sub-project C's iOS wake path has never been compiled or run.** E4 sits
entirely on it, and until it is known good a transmit failure and a wake
failure are indistinguishable — the same trap E3 flagged for B and C. It
also needs the APNs `.p8` key provisioned; the PTT entitlement is already
in both plists.

## Reuse

**Existing abstractions to reuse.** `WalkieTalkieSession` (scope
resolution, teardown watcher), `AppScopeAccessor`, `HeadlessBlazorScope`,
`WalkieTalkieReplyUI.RequestReply`/`StopReply`, `ReplyTargetResolver`,
`IncomingVoiceActivityUI`, `ChatAudioUI.SetRecordingChatId`,
`AppleAudioCapture` / `AudioEngines` / `ResamplerFactory`,
`AppleAudioFocusUI` / `AudioSession`, `BlockRingBuffer<T>`
(`Core/Collections/BlockRingBuffer.cs`), `UserWalkieTalkieSettings` +
`UserSettingsAccessor`, `TuneUI` with the existing `Tune.WalkieReply*`
cues, `PushToTalkSettings.razor`, `IosPushToTalk` / `IosPushToTalkUI`.

**Reusability of new components.** The **pre-roll core** goes in
`ActualChat.Core` beside `BlockRingBuffer` — it is a generic bounded
pre-roll with session tokens, it must be testable off-platform to be worth
anything, and Android will want the same thing if a transmit-from-wake path
ever lands there. `AudioSessionOwner` stays in `MaciOS/Audio`: it describes
`AVAudioSession` ownership specifically and generalises to nothing. The
recency-window override is a change to an existing platform-free type, not
a new component.

## Risks

- **Nothing here compiles on this machine, and iOS cannot link off
  macOS.** The strongest mitigation is the off-platform split above; the
  probe script is second.
- **Sub-project C is unverified.** Every risk below is conditional on a
  wake path that has never run.
- **The pre-roll handover must be right on the first device build.** It is
  the largest piece of untestable native work, and the token discipline
  guards a failure — audio landing in the wrong chat — that is worse than
  losing it.
- **Full duplex leaves session configuration entirely to the framework.**
  Speakerphone echo is the plausible failure, and `ConfigureUnsafe` being
  skipped under `PttTransmit` is exactly what removes our ability to
  correct it.
- **A stuck `AudioSessionOwner` permanently disables the app's own session
  activation.** The existing bool already carried this hazard; three
  states and more call sites widen it.
- **Targeting after a long silence.** Decision 3 removed the window gate,
  so the descriptor's chat title is the only thing telling a user where
  their voice is about to go.

## Open Questions (to resolve during planning)

- **Pre-roll bounds and the boot cap.** `HandleWake` uses a 20 s
  `StartupTimeout`; transmit needs its own, shorter cap plus byte and
  second ceilings on the buffer. Pick concrete numbers against the
  recorder's format, not by analogy.
  **Resolved:** both the pre-roll capacity and the transmit boot cap are
  8 s, pinned to `AppleAudioCapture`'s `outBuffer` (`RecordingSampleRate *
  10` samples) so a full pre-roll can be resampled into it in one go. The
  8 s boot cap is shared across `WhenAppReady`, `SessionTask` and
  `RequestReply` combined, not 8 s each — see the plan's "Execution
  outcome" for why.
- **The minimum-duration floor** below which an early-released pre-roll is
  discarded rather than sent (decision 7). Duration is a proxy for intent,
  not for voice — the real VAD (`CoreMLVoiceActivityDetector`) is a scoped
  service and is not available while the buffer is filling.
  **Resolved:** 0.4 s.
- **Live descriptor updates.** Decision 8 assumes the channel descriptor
  can be re-set while joined so the system sheet names the last-active
  chat. Confirm the API and whether an update while transmitting is
  allowed; if it is not, the title is only refreshable between
  transmissions.
  **Resolved:** `PTChannelManager.SetChannelDescriptor(PTChannelDescriptor,
  NSUuid, Action<NSError>)` exists and compiles — live descriptor updates
  are possible.
- **Whether `Microsoft.iOS.Ref` is obtainable and usable on Linux** for
  `scripts/csc-ios-probe.sh`. The Android probe works because the ref pack
  restores cross-platform; verify before promising the probe.
  **Resolved:** `Microsoft.iOS.Ref.net11.0_26.2` version
  `26.2.11588-net11-p3` restores as a plain NuGet package on Linux and
  yields `ref/net11.0/Microsoft.iOS.dll`, which `csc` compiles
  `PushToTalk`/`AVFoundation`/`UIKit` code against. This is what
  `scripts/csc-ios-probe.sh` uses.
- **Half duplex semantics** — whether it blocks a transmit request or
  preempts the active remote participant (decision 5). Not needed to
  implement full duplex, but it bounds how far iOS would diverge if we
  ever reverse that decision. **Left open** — not answerable without a
  device.
- **Whether `didBeginTransmitting` can arrive before
  `PTChannelManager.Create`'s completion handler has assigned `_manager`.**
  The delegate is passed into `Create`, so callbacks should not precede it,
  but the transmit path dereferences the manager and the cold-launch
  ordering is unverified. **Left open** — not answerable without a device.
- **`PTChannelTransmitRequestSource` has exactly three members**
  (`Unknown`, `UserRequest`, `HandsfreeButton`), confirmed by compiling
  against the ref assembly; there is no `PlayButton`, `Siri`, or `CarPlay`.
  The transmit-failure delegate override is
  `FailedToBeginTransmittingInChannel`, not `DidFailToBeginTransmitting` —
  the latter does not exist.

## Corrections to this spec found during implementation

- **The pre-roll does not wrap `BlockRingBuffer<T>`.** The Reuse section
  below originally proposed it, but `BlockRingBuffer<T>`
  (`Core/Collections/BlockRingBuffer.cs`) is a blocking
  single-producer/single-consumer *streaming* buffer: it drops the
  *newest* data on overflow and requires exact-length reads. A pre-roll is
  a one-shot capture drained once, where overflow must void the *whole*
  buffer instead of dropping a fragment. Wrapping `BlockRingBuffer<T>`
  would have meant fighting three of its four behaviours, so
  `PreRollBuffer` is a standalone type in `src/dotnet/Core.Audio/`.
- **`AudioSessionOwner` lives in `src/dotnet/UI.Blazor/Services/
  AudioSessionOwnership.cs`, not `MaciOS/Audio`.** The spec's Components
  section said `MaciOS/Audio`, but `App.Maui` compiles in no test host, and
  the entire point of the type is that its ownership transitions are
  verifiable off-platform. It lives beside `AudioFocusMode` in
  `UI.Blazor/Services` instead.
