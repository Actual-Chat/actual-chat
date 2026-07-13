# Walkie-Talkie: Android Armed/Hot Lifecycle (Sub-Project B)

Date: 2026-07-13
Status: Approved design, pre-implementation
Depends on: Sub-project A (`2026-07-13-walkie-talkie-server-trigger-design.md`),
merged as the speech-start FCM wake push
(`kind=SpeechStarted, chatId, authorId, timestamp` data payload,
high priority, TTL 60 s, per-chat collapse key).

## Background

Sub-project A made the server send a high-priority data-only FCM push when
someone starts speaking in a chat that a user keeps in walkie-talkie mode
("Keep listening" — `UserListeningSettings.AlwaysListenedChatIds` /
`ChatUserSettings.ListeningMode == Forever`). Sub-project B makes the
Android app act on it: wake even from a dead process, play the utterance
from its first word, and drop back to a battery-neutral "armed" state
after silence.

Established platform facts this design builds on (verified in source):

- A high-priority FCM data push **boots the .NET runtime**
  (`MainApplication.CreateMauiApp` → MAUI root container), even with no
  activity. The app's real service container (`BlazorWebViewApp` —
  RPC clients, session, audio modules; the name is historical, it is a
  DI container, not a WebView) is built only by
  `BlazorWebViewApp.EnsureStarted()`, today called solely from
  `MainActivity.OnCreate` / `CustomBlazorWebViewHandler`.
- Android playback is fully native: `ILiveAudioStreams` RPC →
  `OpusAudioCodec` → `AndroidAudioPlaybackEngine` (`Android.Media.AudioTrack`).
  No WebView/JS in the audio path.
- Orchestration (`ChatAudioUI`, `ChatListeningPlayer`/`ChatReplayPlayer`,
  `AudioTrackPlayerFactory`, `AudioWidget`) is **scoped** on `AppUIHub`;
  the only scope creator today is the WebView render.
- `ChatAudioUI.StartReplay(chatId, startAt, rewindOffset)` →
  `ILiveAudioStreams.GetReplayStream` plays from an exact moment and
  catches up to live (server-paced). The live-listening path trims to
  ≤3 s backlog and joins mid-utterance — it cannot deliver the first word
  after a 1–3 s wake latency; replay can.
- `SpeechStarted` pushes are currently dropped by
  `FirebaseMessagingService.OnMessageReceivedImpl` (no branch for the
  kind; `NotificationData` does not parse `authorId`/`timestamp`).
- Tune playback is already native on MAUI: `MauiTuneUI`
  (Plugin.Maui.Audio) on Android, `AppleTuneUI` (AVAudioEngine) on
  iOS/Mac. `WebTuneUI` is web-only. No WebView dependency.
- Starting a foreground service from a high-priority FCM handler is
  permitted (Android 12+ exemption); this codebase has not used it yet.
  All needed permissions (`FOREGROUND_SERVICE_MEDIA_PLAYBACK`,
  `POST_NOTIFICATIONS`, battery-optimization exemption flow) exist.

## Goals

- **Wake-to-hear:** on a `SpeechStarted` push with the app killed or
  backgrounded, play the utterance from its first word within a few
  seconds, with no user interaction ("silent headless play").
- **Wake lead-in cue:** when playback starts after a long silent
  period, play the app's existing new-audio-after-delay tune first, so
  speech doesn't startle the listener.
- **Armed/hot lifecycle:** hold the live subscription (hot) only while
  there is recent activity; after 5 minutes of background silence, stop
  players and the foreground service and become process-reclaimable
  (armed). FCM re-wakes us.
- **Never lose a wake:** any headless-path failure degrades to a
  visible notification, not silence.
- **No Blazor interop before the user opens the app.** Headless
  operation must not require the WebView; opening the app (icon or
  notification tap) hands over cleanly.

## Non-Goals

- iOS (sub-project C — Apple Push to Talk framework).
- Heard receipts (sub-project D).
- Recording/replying from the headless state (user opens the app to
  talk; existing flows).
- "Keep listening" UX placement rework and OEM task-killer guidance
  (later UX sub-project).
- Web/Windows clients (no walkie-talkie wake there).
- Changing foreground listening behavior (Forever chats keep listening
  continuously while the app is visible, as today).

## Key Decisions (with rationale)

1. **Headless scope over the real app container** (chosen over a
   dedicated minimal player and over invisible-activity launch).
   The push handler calls `BlazorWebViewApp.EnsureStarted()` (container
   only — no WebView, no renderer, no JS) and creates a DI scope on it,
   then drives the existing orchestration. This reuses the whole
   battle-tested stack: player keep-alive/reconnect, replay→live
   catch-up, idle watchers, native engine. A dedicated player would
   duplicate that orchestration (reuse-first rule); an invisible
   activity is illegal from background on Android 10+.

2. **Replay, not live-join, for the triggering chat.**
   `StartReplay(chatId, startedAt, rewindOffset)` delivers the first
   word despite wake latency; the live path would join mid-sentence.
   Other armed chats get plain `SetListeningState(true)` (live) — if
   someone speaks there, we are already connected.

3. **Hot ⇄ armed state machine, background only.**

   ```
                 speech / wake push               5 min background silence
   ARMED ─────────────────────────────► HOT ─────────────────────────────► ARMED
   (no FGS, no players,                 (FGS up, players live,             (players + FGS stopped,
    process killable,                    subscription held)                 process reclaimable)
    FCM wake re-arms)
   ```

   Dropping to armed stops players + FGS (+ disposes the headless scope
   when headless) but does not force-close the RPC socket — without the
   FGS, Doze/OOM reclaim the process naturally; FCM is the recovery
   path. In background the idle rule **includes `ListeningMode.Forever`
   chats** — that is the point of the feature: Forever must no longer
   mean "hold a socket in the pocket forever". Foreground behavior is
   untouched.

4. **Wake cue reuses `Tune.NotifyOnNewAudioMessageAfterDelay`.** The
   existing tune (asset already shipped in all formats) is the app's
   audio-after-a-lull cue: `ChatListeningPlayer` plays it automatically
   when a stream starts after `AudioSettings.IdleListeningNewMessageTrigger`
   (5 min) of quiet — so the hot path needs nothing new. The wake
   handler plays the same tune before starting the wake replay, because
   the replay path bypasses `ChatListeningPlayer`. Played through
   scoped `TuneUI` → native `MauiTuneUI` (no WebView); not played in
   foreground. iOS later inherits via `AppleTuneUI`. No new tune member
   or asset.

5. **Single scope owner at all times.** Headless scope exists only
   while no WebView scope does. `MainActivity.OnCreate` disposes the
   headless session before the WebView scope initializes; the WebView's
   existing `InitializeListening` then restores listening from
   settings. No double-playback window. Warm wakes (WebView scope
   alive in background) skip headless machinery entirely and forward to
   the live scope.

6. **FGS first, silent notification, tap-to-open.** The wake handler
   starts the existing `AndroidAudioWidgetForegroundService`
   (`TypeMediaPlayback`) synchronously within the FCM exemption window
   (placeholder notification satisfies the ≤5 s `startForeground` rule),
   then updates it to "Listening · <chat title>". The channel stays
   Low-importance (soundless). A content intent is added so tapping
   opens the app at that chat — Android mandates the notification for
   any FGS, so it doubles as the honest "app is listening" indicator.

## Architecture & Data Flow (cold wake)

```
FCM data push (kind=SpeechStarted, chatId, authorId, timestamp)
  └─ FirebaseMessagingService.OnMessageReceived        (existing, + SpeechStarted branch)
       └─ WalkieTalkieWakeHandler                       NEW (App.Maui, Android)
            1. startForegroundService(...)              — inside FCM exemption window
            2. BlazorWebViewApp.EnsureStarted()         — container only
            3. session ready? (MauiSession /
               TrueSessionResolver)                     — none → fallback notification, stop FGS
            4. WebView scope alive? → forward to it     — warm path, no headless work
               else create headless scope               — HeadlessScopeFactory NEW
            5. ChatAudioUI.Enable()
               + SetListeningState(true) per armed chat
            6. TuneUI.Play(Tune.NotifyOnNewAudioMessageAfterDelay) — wake lead-in cue
            7. StartReplay(chatId, startedAt,
               rewindOffset)                            — internal overload, no modal
            8. …listening continues (hot) …
            9. WalkieTalkieIdleWatcher: 5 min background
               silence → stop players, stop FGS,
               dispose headless scope → ARMED
       any step throws → fallback notification
       ("🔊 <Author> is talking in <chat>", default channel,
        tap opens chat) + stop FGS
```

## Components

1. **`FirebaseMessagingService` branch + `NotificationData` fields** —
   parse `authorId` and `timestamp` (epoch ms → `Moment`); on
   `NotificationKind.SpeechStarted`, delegate to the wake handler.
   (`src/dotnet/App.Maui/Platforms/Android/Notifications/`)
2. **`WalkieTalkieWakeHandler`** (new, App.Maui Android) — the
   orchestration above: FGS start, `EnsureStarted`, session check,
   warm-vs-headless routing, fallback notification, stale-wake rule.
3. **`HeadlessScopeFactory`** (new, App.Maui) — creates/disposes the
   headless DI scope on `BlazorWebViewApp.Current.Services`, publishes
   it via `AppServicesAccessor` semantics, and enforces the
   single-owner handover with the WebView scope. The headless scope
   audit: `InteractiveUI`/`ModalUI` must no-op or auto-approve when no
   JS channel exists (`SafeJSRuntime` pattern); `TuneUI`,
   `AudioFocusUI`, vibration are already native on MAUI.
4. **`ChatAudioUI` additions** — an internal `StartReplay` overload
   without the confirm-modal/pause-listening UX branch; an internal
   enable path not gated on ChatPage (`Enable()` is currently flipped
   by chat UI only).
5. **`WalkieTalkieIdleWatcher`** (new chain in `ChatAudioUI.StateSync`)
   — background-only: tracks last activity across listening chats
   (same server-activity signal as `StopListeningWhenIdle`), after
   `Constants.Audio.WalkieTalkieIdleTimeout` (5 min) of silence stops
   listening players + FGS (+ headless scope). Applies to Forever chats
   too, unlike the existing watcher. Foreground: inert.
6. **Wake cue** — nothing new to build; the wake handler plays the
   existing `Tune.NotifyOnNewAudioMessageAfterDelay`.
7. **FGS notification content intent** — tap opens `MainActivity` at
   the triggering chat (deep link, existing `Links.Chat` routing).
8. **Constants** — `Constants.Audio.WalkieTalkieIdleTimeout = 5 min`
   (client), documented as paired with the server invariant
   `WalkieTalkieWakeTtl (30 s) < client post-wake listening window`.
   `WalkieTalkieStaleWakeAge = 60 s` (matches the push TTL): older
   wakes skip replay-from-start and just go live.

## Reuse

| Need | Existing abstraction |
|---|---|
| App container headlessly | `BlazorWebViewApp.EnsureStarted()` + its factory (already idempotent) |
| Connection warmup | `AppNonScopedServiceStarter.StartNonScopedServices` (already runs post-build) |
| Session | `MauiSession` / `TrueSessionResolver` (singletons) |
| Playback from a moment | `ChatAudioUI.StartReplay` → `ILiveAudioStreams.GetReplayStream` |
| Live subscriptions | `ChatAudioUI.SetListeningState` + `KeepListeningPlayerAlive` |
| Armed-chat set | `ChatAudioUI.GetChatsYouNeedToKeepListeningTo` (reads the same settings the server gates on) |
| FGS + media notification | `AndroidAudioWidgetForegroundService` / `AndroidAudioWidget` |
| Wake lead-in cue | existing `Tune.NotifyOnNewAudioMessageAfterDelay` via `TuneUI` → `MauiTuneUI` (native) |
| Idle detection signal | same server-activity source as `StopListeningWhenIdle` |
| Background detection | `MauiBackgroundStateTracker` |
| Headless JS guard | `SafeJSRuntime` pattern |
| Notification fallback | existing chat-notification path in `FirebaseMessagingService` |

New components' reusability: `HeadlessScopeFactory` and the wake
handler are Android-specific by nature but placed so sub-project C can
mirror them (`App.Maui` shared vs `Platforms/Android` split decided at
planning: scope factory is platform-neutral → `App.Maui/Services`;
wake handler is Android-only → `Platforms/Android`).
The wake cue reuses `Tune.NotifyOnNewAudioMessageAfterDelay` from the
shared `UI.Blazor` tune stack — iOS reuses it as-is.

## Error Handling

- Any headless-chain failure (container build, no/expired session,
  scope creation, replay error) → fallback notification + stop FGS.
  A wake is never silently dropped.
- FGS start denied → fallback notification (needs no FGS).
- Stale wake (`now − startedAt > WalkieTalkieStaleWakeAge`) → live
  listening only, no replay-from-start.
- No retry within one wake; the next utterance's push retries
  naturally (server wake-pending TTL paces this).
- Headless scope disposal is idempotent and ordered before WebView
  scope init on app open.

## Testing

- **Unit tests** (plain C#, no device): `NotificationData` parsing of
  `authorId`/`timestamp`; stale-wake decision; `WalkieTalkieIdleWatcher`
  state machine with virtual clock (background+silence→drop;
  foreground→never; activity resets; Forever chats included in
  background only).
- **Manual device script** (documented with `adb logcat` markers per
  wake step):
  1. Kill app → speak from a second account → expect the new-audio cue + playback
     from first word within ~3 s; FGS notification shows
     "Listening · <chat>".
  2. Stay silent 5 min → FGS notification disappears (armed).
  3. Speak again → wake repeats (re-arm works).
  4. Open the app mid-headless-session → seamless handover, no double
     audio, chat UI reflects listening state.
  5. Sign out → wake produces fallback notification, no crash.
- Out of scope: automated end-to-end on-device wake tests (no such
  harness exists in-repo).
