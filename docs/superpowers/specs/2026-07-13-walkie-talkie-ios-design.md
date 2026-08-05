# Walkie-Talkie: iOS via Apple Push to Talk (Sub-Project C)

Date: 2026-07-13
Status: Implemented (device verification pending)
Depends on:
- Sub-project A (`2026-07-13-walkie-talkie-server-trigger-design.md`) —
  the `SpeechStartedEvent` → `NotificationsBackend.SendWalkieTalkieWake`
  pipeline with its armed check, wake-pending TTL, and feature gate.
- Sub-project B (`2026-07-13-walkie-talkie-android-design.md`) — the
  client playback core this sub-project extracts and reuses:
  `HeadlessBlazorScope`, `ChatAudioUI.StartWalkieTalkieReplay`,
  `IsWalkieTalkieHeadless`, the background idle watcher, the
  stale-wake rule.

## Background

Sub-projects A and B deliver walkie-talkie wake-to-hear on Android via
high-priority FCM data pushes. iOS cannot use that path: FCM cannot
deliver Apple Push to Talk pushes, and ordinary pushes cannot start
audio from a killed app. Apple's sanctioned mechanism is the
**PushToTalk framework** (iOS 16+): the app joins a PTT *channel*;
while joined, the system shows its PTT UI (status pill / lock-screen
card with a leave button), delivers `apns-push-type: pushtotalk`
pushes to the app even from termination, and manages the audio
session for playback.

Verified platform facts:

- Microsoft.iOS ships PushToTalk bindings (`PTChannelManager`,
  `PTChannelManagerDelegate`, `PTChannelRestorationDelegate`,
  `PTPushResult`) — no binding project needed. One historical
  dotnet/macios issue (#16792, delegate construction segfault) must be
  re-verified on the current SDK during implementation.
- App `MinimumOSVersion` is 16.4 ≥ PTT's 16.0 floor. Mac Catalyst has
  no PTT framework — all PTT code is `#if IOS` (not Catalyst).
- The `com.apple.developer.push-to-talk` entitlement is NOT in
  `Entitlements.dev.plist`/`Entitlements.prod.plist` and the
  `push-to-talk` background mode is not in `Info.plist` — both must be
  added, and provisioning profiles regenerated (manual Apple-portal
  action).
- PTT pushes require **direct APNs** (HTTP/2 + ES256 JWT auth to
  `api.push.apple.com`, topic `<bundleId>.voip-ptt`) and a **PTT
  device token** distinct from the FCM token, obtained from
  `PTChannelManager` per channel join (ephemeral, rotates).
- The repo has no direct-APNs client. .NET `HttpClient` supports
  HTTP/2 natively; the `SocketsHttpHandler` +
  `EnableMultipleHttp2Connections` pattern already exists
  (`ApiContractsModule`). ES256 signing needs only BCL
  (`ECDsa.ImportPkcs8PrivateKey`).
- An APNs auth key is a separate `.p8` from the Apple Sign-In key
  (same shape, different portal capability) — new key required
  (manual Apple-portal action).
- Today `AudioSession`/`AppleAudioFocusUI` own `AVAudioSession`
  activation end-to-end; under PTT the `PTChannelManager` delegate
  owns activation — an ownership handoff is required.

## Goals

- **iOS wake-to-hear**: with walkie-talkie mode armed, an iOS user
  hears an utterance from its first word within seconds — even from a
  killed app — via a PTT push and the existing replay pipeline.
- **Receive-only v1**: the system talk button is not wired; users
  reply by opening the app.
- **Server APNs PTT sender**: minimal hand-rolled `ApnsClient`, fed by
  the same trigger, gates, and dedup as the Android path.
- **Shared client core**: extract sub-project B's portable
  wake-playback core into a platform-neutral `WalkieTalkieSession`;
  Android is refactored onto it with no behavior change.

## Non-Goals

- Full duplex (system talk button → recording) — see Future Extension.
- Mac Catalyst / web / Windows (no PTT framework there).
- Heard receipts (sub-project D).
- Any change to the Android wake semantics delivered by B.
- Fallback *live* delivery when PTT fails — the regular message
  notification (existing FCM path) remains the safety net; iOS v1
  accepts "notified, not live" as the degraded mode.

## Key Decisions (with rationale)

1. **Hand-rolled minimal `ApnsClient`** (rejected: third-party APNs
   libraries). One push shape, BCL-only crypto and HTTP/2, ~150 lines
   with a cached (~50 min) ES256 JWT. A dependency would be vetted for
   exactly the same code.

2. **PTT token = new `DeviceType.iOSPttApp` member (appended, value
   4)** riding the existing `RegisterDevice`/`RemoveDevices`/
   `DbDevice` machinery. Exclusions required: `OnPush` /
   `OnPushDismissal` must skip `iOSPttApp` rows (they currently pass
   every device token to FCM); APNs `410 Unregistered` /
   `BadDeviceToken` prunes the row (FCM-pruning analog).

3. **Single aggregate channel ("Voxt")** — forced: PTT allows one
   active channel per app; the user's ≤3 armed chats share it. The
   incoming push carries `chatTitle` so the system UI can name the
   speaking chat as the active participant without any RPC.

4. **Armed = joined.** `IosPushToTalkUI` joins the channel when
   `AlwaysListenedChatIds` becomes non-empty (joins must happen in
   foreground — satisfied, since arming is an in-app toggle) and
   leaves when it empties. `PTChannelRestorationDelegate` restores the
   join across reboot/kill — this replaces Android's
   FCM-wake-from-dead-process. Consequence (inherent, accepted): the
   system PTT pill is visible the whole time the mode is armed, not
   only while audio plays.

5. **Transmission lifetime = hot window.** Apple models a PTT
   transmission as one utterance; our hot window is longer. Clearing
   the active remote participant after each utterance would
   deactivate the audio session, and a hot listener is a live-session
   participant whom the server deliberately does not re-wake (the
   invariant from A) — the next utterance would be silently missed.
   Therefore the active-remote-participant state is held for the whole
   hot window and cleared at the idle drop (`SetActiveRemoteParticipant
   (null)` → system deactivates the session → armed/joined). The
   system UI shows the channel as receiving for up to 5 quiet minutes
   — conservative but truthful ("live-listening"), and the only
   mapping that never drops audio.

6. **"Externally activated" audio-session mode.** While a PTT
   transmission is active, `AudioSession`/`AppleAudioFocusUI`
   configure category/routes but never call `SetActive` — activation
   and deactivation belong to the PTT delegate
   (`DidActivateAudioSession`/`DidDeactivateAudioSession`), and the
   interruption-recovery loop defers to it. This is the only surgical
   change to existing audio code.

7. **Shared `WalkieTalkieSession`** (App.Maui/Services,
   platform-neutral) extracted from B's Android handler: wake
   handling (app-ready + session waits, live-vs-headless scope
   resolution, `StartPlayback` with cue / stale-vs-replay branch /
   armed restore set) and the teardown watcher (2×5 s idle
   confirmation → dispose headless scope → platform callback).
   Platform shells: Android keeps FGS show/update/hide + the FCM
   entry point; iOS keeps PTT delegate plumbing +
   `SetActiveRemoteParticipant(null)` as its teardown callback. The
   Android refactor is a pure extraction pinned by B's review record.

## Architecture & Data Flow

Server:

```
NotificationsBackend.SendWalkieTalkieWake        (existing, from A)
  ├─ armed check + wake-pending TTL              (unchanged; covers both transports)
  ├─ devices = ListDevices(userId)
  ├─ AndroidApp → FirebaseMessagingClient.SendSpeechStartedWake   (existing)
  └─ iOSPttApp → IApnsClient.SendPushToTalkWake                    NEW
       — POST https://api.push.apple.com/3/device/{pttToken}  (HTTP/2)
         headers: apns-push-type=pushtotalk, apns-topic=<bundleId>.voip-ptt,
                  apns-priority=10, apns-expiration=now+60s, ES256 JWT auth
         payload: { kind: SpeechStarted, chatId, timestamp, chatTitle }
         (chatTitle via ChatsBackend.Get — Fusion-cached)
```

iOS client:

```
Arm ("Keep listening" toggled on, foreground)
  └─ IosPushToTalkUI: PTChannelManager.JoinChannel("Voxt")
       └─ delegate.ReceivedEphemeralPushToken → RegisterDevice(iOSPttApp)
Reboot / app kill → PTChannelRestorationDelegate restores the join

APNs pushtotalk push (chatId, timestamp, chatTitle)
  └─ delegate.IncomingPushResult → ActiveRemoteParticipant(chatTitle)
       └─ system activates AVAudioSession
            └─ delegate.DidActivateAudioSession
                 └─ WalkieTalkieSession.HandleWake(chatId, startedAt, isForeground)
                      — shared core: scope resolution (live vs HeadlessBlazorScope),
                        Enable, cue, stale-vs-replay branch, armed-set restore;
                        foreground wake only ensures listening (no hijack)
… hot: listening continues; idle watcher (now gated Android|Ios) after
5 background-silent minutes → ClearListeningChats → teardown watcher →
dispose headless scope + SetActiveRemoteParticipant(null) → system
deactivates the session → ARMED (still joined, pill visible)
Disarm → leave channel → RemoveDevices(pttToken) → pill gone
```

## Components

1. **`ApnsClient` + `IApnsClient`** (Notifications.Service) — the
   direct-APNs sender described above; named `HttpClient` with
   HTTP/2; ES256 JWT cached ~50 min; response handling prunes dead
   tokens. Config on `NotificationsSettings`: `ApplePushKeyId`,
   `ApplePushTeamId`, `ApplePushBundleId`, `ApplePushPrivateKeyPath`,
   `ApplePushUseSandbox`. Missing config → branch no-ops with one
   startup log line.
2. **`DeviceType.iOSPttApp`** (Api) + `OnPush`/`OnPushDismissal`
   exclusions + wake-sender branch (NotificationsBackend).
3. **`WalkieTalkieSession`** (App.Maui/Services, shared) — extraction
   per Key Decision 7; Android's `WalkieTalkieWakeHandler` becomes a
   thin shell over it.
4. **`IosPushToTalkUI`** (`Platforms/iOS`, `#if IOS`) — channel
   join/leave driven by the armed set, restoration delegate, token
   registration, and the channel-manager delegate: `IncomingPushResult`
   (parse payload → participant), `DidActivateAudioSession` (start
   shared playback), `DidDeactivateAudioSession` (stop engines).
5. **Audio-session ownership mode** (`MaciOS/Audio/AudioSession.cs`,
   `AppleAudioFocusUI.cs`) — per Key Decision 6.
6. **Entitlement + plist changes** — `com.apple.developer.push-to-talk`
   in both entitlements files; `push-to-talk` added to
   `UIBackgroundModes`. Requires regenerated provisioning profiles
   (manual).
7. **Idle-watcher gate** (`ChatAudioUI.StateSync.cs`) — extend the
   Android-only gate to `AppKind.Android or AppKind.Ios`.

## Reuse

| Need | Existing abstraction |
|---|---|
| Trigger, gates, dedup | A's `SendWalkieTalkieWake` pipeline (unchanged above the device split) |
| Device token storage | `DbDevice` / `RegisterDevice` / `RemoveDevices` (new enum member only) |
| HTTP/2 client pattern | `AddHttpClient` + `SocketsHttpHandler` w/ `EnableMultipleHttp2Connections` (`ApiContractsModule`) |
| Apple key config shape | `UsersSettings.Apple*` pattern (new key, same structure) |
| Headless runtime | `HeadlessBlazorScope` (already platform-neutral) |
| Playback from a moment | `ChatAudioUI.StartWalkieTalkieReplay` + restore-set mechanics (B) |
| Wake cue | `Tune.NotifyOnNewAudioMessageAfterDelay` via `AppleTuneUI` (native) |
| Idle drop | B's `StopListeningWhenIdleInBackground` (gate extension only) |
| Stale-wake rule | `WalkieTalkie.IsStaleWake` |
| Token pruning pattern | FCM `HandleBatchResponse` → `RemoveDevices` analog |

New components' reusability: `WalkieTalkieSession` is deliberately
shared (App.Maui/Services); `ApnsClient` stays in Notifications.Service
(single consumer today; promotable if APNs gains other uses);
`IosPushToTalkUI` is inherently platform-specific.

## Error Handling

- APNs `410`/`BadDeviceToken` → prune device row; other send errors →
  log only, no per-wake retry (server wake-pending TTL paces retries
  across utterances, as in A).
- Failed/undelivered PTT wake → no live audio, but the regular message
  notification via the existing FCM path still arrives once the
  transcript lands. Accepted v1 degraded mode (documented guarantee
  difference vs Android, which has an in-handler fallback
  notification).
- Missing APNs config or entitlement → server branch no-ops (startup
  log); client join failure → logged by `IosPushToTalkUI`, mode stays
  Android-style notification-only.
- Ephemeral token rotation → old token 410s and is pruned; the new
  token registered at rotation time.
- `IncomingPushResult` must return synchronously and fast — payload
  parse only; all real work happens after `DidActivateAudioSession`.

## Testing

- **Server (automated, primary):** integration tests with a recording
  `IApnsClient` fake (the `FirebaseMessagingTestSink` pattern): armed
  user with an `iOSPttApp` device receives a PTT wake; dual-device
  user fires both transports; `OnPush` excludes PTT rows; `410`
  prunes. `ApnsClient` unit tests with a faked `HttpMessageHandler`:
  JWT header/claims/signature (verified against the public key),
  exact headers/payload, JWT cache expiry (virtual clock).
- **iOS (manual device script, requires entitlement + APNs key):** arm
  → pill appears; kill app → speak from second account → system PTT UI
  shows the chat title + first-word playback; 5-min silence →
  transmission ends, pill remains (armed); disarm → pill gone; reboot
  → join restored; foreground wake → listening ensured, no hijack.
- **Not automatable in this environment:** any iOS build
  (macOS-only TFM) — all iOS code is host/CI-verified.

## Future Extension: Full Duplex (documented, out of scope)

Wire the system talk button via the channel-manager delegate's
transmit callbacks (`ChannelManagerDidBeginTransmitting` /
`...DidEndTransmitting` and the transmit-mode audio-session
activation) to `SetRecordingChatId(chatId, isPushToTalk: true)`,
targeting the **last-active chat** of the aggregate channel (the last
one that woke or played). The receive-only design keeps every seam
this needs: the externally-activated audio-session mode, the shared
`WalkieTalkieSession`, and the aggregate-channel model.
