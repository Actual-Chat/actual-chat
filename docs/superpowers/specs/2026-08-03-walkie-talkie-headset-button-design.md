# Walkie-Talkie: Headset Button + Headless Reply Pipeline (Sub-Project E3)

Date: 2026-08-03
Status: Implemented (device verification pending — see plan Task 7)
Depends on: E1 (`2026-07-20-walkie-talkie-reply-to-voice-design.md`) for
`WalkieTalkieReplyUI.RequestReply`/`StopReply` and `ReplyTargetResolver`;
E2 (`2026-07-26-walkie-talkie-ptt-settings-design.md`) for `PttChatIds`,
the answer window, and `GestureActivationPolicy`.

## Background

Sub-projects A–D deliver the receive half of walkie-talkie: the server
pushes a speech-start wake, Android and iOS play the utterance headlessly
from a killed process, and the listener's playback reports a `Heard`
watermark back. E1 added the reply pipeline plus an on-screen button, and
E2 added an explicit PTT opt-in and two motion gestures.

Two gaps remain on Android, and they turn out to be one problem.

The first is the obvious one: there is no hardware trigger. A user with
earbuds in and the phone in a pocket still has to take the phone out to
reply, which defeats the premise.

The second was found while designing the first, and it is the more serious
of the two. **E2's gestures do not work after a wake.**
`AppScopedServiceStarter` runs from `AppBase.razor`'s render lifecycle
(`AppBase.razor:111`), so it starts `GestureUI` and
`IncomingVoiceActivityUI` only in the WebView scope. The wake path creates
a `HeadlessBlazorScope` that never renders and deliberately marks its
`SafeJSRuntime` disconnected. So in the killed-then-woken case — phone in
pocket, screen off, which is the entire scenario the feature exists for —
no trigger is armed at all. Shake and flip work foregrounded and
backgrounded-but-alive, and silently do nothing after a push wake.

Adding a hardware button on top of that would produce a third trigger dead
in the same place. So this sub-project fixes the pipeline first and adds
the button second.

## Goals

- Start the walkie-talkie reply pipeline in the headless wake scope, so
  every trigger works from a killed process — the button, and E2's shake
  and flip as a side effect.
- Add a headset/Bluetooth media-button trigger that opens a reply without
  touching the screen.
- Leave existing media-button playback control working unchanged whenever
  a reply is not plausible.

## Non-Goals

- **E4** — iOS Apple PTT transmit. Untouched, though it will benefit from
  the same scope-startup change.
- Vendor PTT accessories (rugged hand mics, Bluetooth PTT pucks). They
  vary by brand — HID keyboard events, media buttons, per-vendor SDKs —
  and cannot be developed against without the hardware in hand.
- Volume keys as PTT. Intercepting them system-wide is heavily restricted
  on modern Android and is unreliable exactly when the app is not
  foreground, which is when PTT matters.
- Zero-loss handover of an in-flight recording when the user opens the app
  mid-reply. See decision 7.
- The `WalkieTalkieSession` de-static refactor, still deferred to E4.
- Any change to the receive/wake path (A/B/C) or heard receipts (D).

## Key Decisions (with rationale)

1. **Standard media buttons only.** Ordinary Bluetooth headsets, earbuds
   and car head units speak AVRCP and send `KEYCODE_HEADSETHOOK` or
   `KEYCODE_MEDIA_PLAY_PAUSE`, which already reach the app through the
   `MediaSessionCompat` that `AndroidAudioWidgetForegroundService` owns.
   The whole problem is therefore disambiguation, not device support.
   (Considered and rejected: vendor PTT accessories — no single API covers
   them and they need physical hardware to build against.)

2. **A single tap is the only press to build on.** Long-press is usually
   consumed by the headset firmware for the voice assistant, and
   double-tap is frequently next-track in firmware — both would work on
   some hardware and silently not on the rest, with no way for the user to
   tell why. This rules out hold-to-talk on this transport.

3. **The answer window disambiguates.** Inside the answer window of a PTT
   chat — someone spoke within `WalkieTalkieReplyRecencyWindow` (150 s) —
   a press starts a reply; outside it, a press is playback exactly as
   today. This reuses E2's arming concept rather than inventing a second
   one, needs no new gesture, and works on the single tap every headset
   forwards. (Considered and rejected: a mode toggle that permanently
   claims the button — predictable, but it removes hardware playback
   control entirely while any chat is armed.)

4. **Inside the window, reply always wins — including mid-playback.** The
   incoming message is usually still playing when the user presses. One
   rule with no sub-cases beats a two-press sequence whose meaning changes
   a second apart, and it matches real walkie behaviour: you key up while
   the other person is still talking. Playback keeps running; the existing
   capture path already handles talking over incoming audio. (Considered
   and rejected: pause-then-reply — makes answering a two-press action
   exactly when speed matters.)

5. **On by default.** Matches how flip and shake ship in E2. The
   answer-window gating bounds exposure to 150 s after someone actually
   speaks to the user, and a press outside it is still plain playback. An
   opt-in default would be safer in the abstract but almost nobody would
   discover the feature. A per-user toggle exists for those who want it
   off.

6. **One container, one scope at a time.** The app has a single root
   container; scoped services live in either the WebView scope (created by
   Blazor, published via `MauiWebView.SetScopedServices`) or the headless
   scope, never both. This design keeps that. What changes is *when*
   startup runs: it becomes scope-driven rather than render-driven.
   (Considered and rejected: a long-lived app scope the WebView attaches
   to — cleanest model but fights `BlazorWebView`'s ownership of its own
   scope; and two coexisting scopes bridged by `CompositeServiceProvider`
   — framework-friendly but every service must be assigned to a side, and
   a mistake yields two silent instances of a service like `ChatAudioUI`.)

7. **The headless scope is created only when no UI is coming.** A push
   wake, a boot receiver, or notification handling starts the process
   without an Activity, so a headless scope is made. A normal launch
   creates only the WebView scope. This keeps the handoff off the common
   path entirely — it happens only when the user opens the app *during* a
   headless session.

8. **A handoff mid-recording closes the mic cleanly rather than
   preserving it.** When the user opens the app while a headless reply is
   recording, the headless scope stops its triggers and closes the mic
   through `StopReply` — the entry finalises and the cue plays — before
   being disposed. The user loses the tail of a sentence but never loses
   the message and never gets a silent failure. Preserving the recording
   across the swap would require two live scopes, which is exactly the
   service-identity problem decision 6 avoids, and would arm two
   `GestureUI` instances at once. Zero-loss handover is a separate design.

## Architecture & Data Flow

```
                        process starts
                              │
              ┌───────────────┴───────────────┐
        no Activity                      user launch
     (push wake / boot)                       │
              │                               │
   HeadlessBlazorScope.GetOrCreate     Blazor creates the scope
              │                               │
              └──────────► StartScopedServices ◄──────────┘
                       (IncomingVoiceActivityUI,
                        GestureUI, TuneUI, AudioWidget)
                              │
                        answer window opens
                        when incoming voice
                        arrives in a PTT chat
                              │
   headset hook ──► MediaSession ──► Callback.OnMediaButtonEvent
                                             │
                                    AppScopeAccessor
                                    (WebView ?? headless)
                                             │
                                     HeadsetButtonPolicy
                                       ┌─────┴─────┐
                                    Reply      PassThrough
                                       │             │
                    RequestReply / StopReply    base.OnMediaButtonEvent
                                                (play / pause as today)
```

**Alive app.** Someone speaks in a PTT chat, `IncomingVoiceActivityUI`
stamps it, the window opens. The hook press routes to the media session,
the policy says Reply, and `WalkieTalkieReplyUI.RequestReply` opens the
mic under the configured hot window. A second press stops it.

**Killed then woken.** `WalkieTalkieWakeHandler.Handle` starts the FGS and
calls `BlazorWebViewApp.EnsureStarted`; `HeadlessBlazorScope.GetOrCreate`
now also runs `StartScopedServices`, so the trigger services are alive.
`WalkieTalkieSession.HandleWake` plays the incoming audio and the window
opens. The press takes the identical path from there.

One property makes the headless case work at all: the FGS is already
running with `TypeMediaPlayback | TypeMicrophone` from the wake, so **no
new foreground service starts at press time**. The app inherits microphone
access from the service that is already up, which sidesteps Android 14's
restriction on starting a microphone-typed FGS from the background. A
design that had to start a mic FGS on the press would not work.

## Components

1. **`StartScopedServices`** — `UI.Blazor.App/Services/`. The headless-safe
   subset of today's `AppScopedServiceStarter.AfterFirstRender`:
   `IncomingVoiceActivityUI`, `GestureUI`, `TuneUI`, `AudioWidget`. Called
   from `AppBase.razor` as today, and from `HeadlessBlazorScope.GetOrCreate`.
   The JS-bound work — `BrowserInit`, `BrowserInfo.WhenReady`, `ThemeUI`,
   `History`, navigation restore — stays render-driven and is *not* in this
   list. An explicit inclusion list, not a skip-list: the next reader
   should be able to see what runs headlessly without reasoning about what
   was disabled.

2. **`AppScopeAccessor`** — `App.Maui/Services/`. Returns the WebView scope
   if one is published, else `HeadlessBlazorScope.Current`, else null.
   Android's foreground service is a static component and needs to reach
   whichever scope is live; the pattern already exists ad hoc in
   `AndroidAudioWidget.Stop()`, which falls back to
   `WalkieTalkieWakeHandler.StopHeadlessSession()` when its instance is
   null. This generalises it and replaces that null-check.

3. **`HeadsetButtonPolicy`** — `UI.Blazor.App/Services/Gestures/`, pure.
   `(keyCode, isEnabled, hasAnswerWindow, isReplyHot) → Reply | Stop |
   PassThrough`. Lives beside `GestureActivationPolicy` and takes the same
   answer-window input that `ShouldSenseStartGestures` uses.

4. **`Callback.OnMediaButtonEvent`** — the existing nested `Callback` in
   `AndroidAudioWidgetForegroundService`. Extracts the `KeyEvent`, resolves
   the scope, asks the policy, and either calls `RequestReply`/`StopReply`
   and consumes the event, or returns `base.OnMediaButtonEvent(intent)` so
   the current play/pause/stop behaviour is untouched.

5. **`UserWalkieTalkieSettings.IsHeadsetButtonEnabled`** — a new member
   defaulting to `true`, with one row in the Push to Talk settings tab
   beside the gesture toggles.

6. **Handoff** — `MainActivity.OnCreate` currently disposes the headless
   scope outright. It gains a step: if a reply is recording, stop it
   through `StopReply` first, then dispose.

## Reuse

**Existing abstractions to reuse (verified 2026-08-03):**

| Need | Existing abstraction |
|---|---|
| Media button delivery | `MediaSessionCompat` with `FlagHandlesMediaButtons`, already created at `AndroidAudioWidgetForegroundService.cs:111` |
| Reply start/stop, target resolution, cold-start dead-man | `WalkieTalkieReplyUI.RequestReply`/`StopReply`, `ReplyTargetResolver` (E1) |
| Answer-window signal | `IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt` + `Constants.Audio.WalkieTalkieReplyRecencyWindow` (E1/E2) |
| Armed chat set | `ChatAudioUI.GetPttChatIds` (E2) |
| Pure trigger policy shape | `GestureActivationPolicy` (E2) |
| Settings record, storage, UI row | `UserWalkieTalkieSettings` + the Push to Talk tab (E2) |
| Headless DI scope | `HeadlessBlazorScope` (B) |
| Reaching a live scope from a static Android component | `AndroidAudioWidget`'s instance pointer + headless fallback (B) |
| Mic-capable foreground service | `[Service(ForegroundServiceType = TypeMediaPlayback \| TypeMicrophone)]`, `FOREGROUND_SERVICE_MICROPHONE` already in the manifest |
| Disconnected-JS degradation | `SafeJSRuntime.MarkDisconnected` and the `JSRuntimeDisconnected` the UI already tolerates |

No sibling `ActualLab.Fusion` abstraction is needed beyond the compute
services already in use. `CompositeServiceProvider` was evaluated for the
two-scope topology and is deliberately not used — see decision 6.

**Reusability of new components.** `HeadsetButtonPolicy` is pure and has no
Android dependency, so it sits in `UI.Blazor.App` beside the other trigger
policies rather than in the MAUI project; that also keeps it unit-testable
on a build machine. `StartScopedServices` belongs in `UI.Blazor.App`
because the list it starts is app-scoped, not platform-specific — E4 will
want the same entry point on iOS. `AppScopeAccessor` is MAUI-bound by
definition.

## Error Handling

Every failure lands on **pass-through**, so the button behaves exactly as
it does today:

- **No live scope** (process starting, or mid-handoff) → pass through.
- **Setting off, window closed, or unrecognised key** → pass through.
- **No resolvable reply target** → E1's existing "nothing heard" cue; the
  mic never opens.
- **Microphone permission not granted** → it can be checked but not
  *requested* headlessly, so fail closed and log. The on-screen path can
  request it later.
- **FGS start denied by an OEM at wake time** → no scope exists, so the
  button passes through; playback still attempts as it does today.
- **Handoff while recording** → `StopReply` before disposal (decision 8).

One trap deserves naming because it is the classic media-button bug, and
because the obvious defence does not work here. The intent carries a
`KeyEvent` with both `ACTION_DOWN` and `ACTION_UP`, so handling both fires
a single press twice. E1's idempotence does **not** save us: by the time
the second edge arrives a reply is hot, so the policy maps it to `Stop` —
the mic opens and immediately closes, and the user sees a dead button. The
policy must therefore act on exactly one edge and ignore repeat counts,
and that is a correctness requirement rather than a tidiness one. It is
the first thing to unit-test.

## Testing

**Unit (`Chat.UI.Blazor.UnitTests`), which is most of the real coverage:**
- `HeadsetButtonPolicy` truth table — hook and play/pause inside the window
  with the setting on → Reply; setting off, window closed, and unknown
  keycode each → PassThrough; a press while a reply is hot → Stop.
- The down/up pair yields exactly one decision, and repeat counts are
  ignored.
- Settings round-trip for the new member, including that a blob written
  before it existed reads as enabled.

**Not testable here, stated plainly rather than mocked:**
- Whether a given service in `StartScopedServices` actually works without
  JS. The failure mode is a runtime hang or throw in a scope no test
  constructs, and "works without JS" is not expressible as an assertion.
  This is the design's main residual risk and it resolves on a device.
- The Android FGS and `MediaSession` path. `App.Maui.csproj` is not in
  `ActualChat.CI.slnf`, so nothing on a build machine compiles it.

**Device, in this order:**
1. Press **outside** the window and confirm play/pause still works. This is
   the regression that matters most — the change touches the existing
   playback controls.
2. Press inside the window → a reply records and sends. Press again → it
   stops.
3. The killed-process case: wake, then press. This is the point of the
   sub-project.
4. E2's shake and flip after a wake, which should now work for the first
   time.
5. Open the app mid-reply and confirm the handoff closes cleanly with a
   cue rather than dropping silently.

## Open Questions (to resolve during planning)

- The exact membership of `StartScopedServices`. The four named above are
  the walkie-talkie set; whether notification handling needs anything in a
  scope should be checked during planning rather than assumed.
- Whether `AndroidAudioWidget`'s existing instance-pointer fallback should
  be migrated onto `AppScopeAccessor` in this sub-project or left alone.
- Whether `OnMediaButtonEvent` sees presses when the app has no audio
  playing and the media session is inactive — session priority on Android
  depends on recent playback, and the answer decides whether the window
  alone is enough to receive the event.
