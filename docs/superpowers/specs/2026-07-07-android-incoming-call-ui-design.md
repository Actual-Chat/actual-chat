# Android Incoming-Call UI — Design

Date: 2026-07-07
Branch: `feat/1xxx-android-calls`
Status: approved in brainstorming; ready for implementation planning.

## Context

The server-side call subsystem is complete: `LiveSession` with `Kind=Call`,
`CallInvite` (`Ringing/Accepted/Declined/Missed`, 40s ring timeout), the full
`ILiveSessions` ring lifecycle (`StartCall/AcceptCall/DeclineCall/CancelCall/
LeaveCall`), and FCM pushes — `CallNotification` (kind `IncomingCall`) on ring
start plus a dismissal push (`DismissedTags`) on ring dismissal.

The Android client side is missing:
- `FirebaseMessagingService` does not special-case `NotificationKind.IncomingCall`
  — a call push renders as an ordinary chat banner.
- `IncomingCallModal` is a stub (profile-oriented model, no Accept/Decline
  handlers).
- The only working accept flow is the admin-only `VoiceCallTestPage`, which
  polls all chats.
- No call notification channel, no notification actions, no telecom /
  full-screen-intent integration.

## Scope and staging

This design is **stage B** of a two-stage path:

- **Stage B (this design)**: in-app incoming-call UI when the app is running,
  plus a ringer notification with Accept/Decline actions when it is not.
  Strictly Android: shared Blazor code is touched minimally and all new
  behavior is gated to the Android app.
- **Stage A (future)**: full killed-app / lock-screen experience —
  `CallStyle` notification, full-screen intent, possibly a foreground service.
  Stage B must not dead-end that path.

Non-goals for stage B: web/iOS/desktop in-app call UI, `ConnectionService` /
telecom integration, DND breakthrough beyond channel importance, looping
ringtone in the background notification, caller-side UI changes.

## Approach (decision)

**Push is the trigger; the Fusion computed is the source of truth.**

The FCM `IncomingCall` push (it carries `ChatId`) only wakes the client up.
While the app UI is alive, the incoming-call state is driven by the already
existing reactive `ILiveSessions.Get(session, chatId)` (`[RemoteComputeMethod]`,
surfaced via `LiveSessionUI.Get`): the banner/modal/ringer live while my
`CallInvite` is `Ringing` and disappear on any status change — cancel, timeout,
decline, or accept on another device — even if the dismissal push is lost.

Rejected alternatives:
- *Push-only*: fragile — FCM has no delivery/order guarantees, UI cannot
  self-heal.
- *New server computed (`ListIncomingRings(session)`)*: always-consistent and
  cross-platform, but new server API + per-user invalidation work that is out
  of scope for the strictly-Android stage; reconsider for stage A or other
  platforms.

## Architecture

### New: `IncomingCallUI` (scoped service, `UI.Blazor.App/Services`)

Owns the "someone is calling" state; Android-only activation (gated via
`HostInfo`). Everything else (banner, modal, ringer, notification-accept
routing) reflects this state.

- Keeps a list of active rings (`ChatId` + subscription to
  `LiveSessionUI.Get(chatId)`); exposes `IState<IncomingCallState?>` for the
  topmost (most recent) ring: `ChatId`, caller `Author`, `HasVideo`, invite
  status.
- Entry points:
  - `OnRing(chatId)` — from the FCM bridge (foreground push) and from
    reconciliation.
  - `Accept()` / `Decline()` — shared by banner, modal, and
    notification-accept routing.
- A ring is ignored when `AmIInLiveConversation(chatId)` is true or I am the
  session host (guards against pushes hitting another device of the same
  user).
- While the topmost ring is `Ringing`: shows the banner via `BannerUI.Show`
  and starts `IIncomingCallRinger`. When it stops being `Ringing`: hides the
  banner, closes the modal, stops the ringer; the next live ring (if any) is
  shown.

### New: `IIncomingCallRinger`

Interface next to `IncomingCallUI` (no-op default registration); Android
implementation in `App.Maui/Platforms/Android`: looping system ringtone
(`RingtoneManager`) + vibration pattern, respecting the device ringer mode
(silent/vibrate → no sound). Registered in Android DI.

### Changed: `FirebaseMessagingService.OnMessageReceived`

New branch for `NotificationKind.IncomingCall`:

- **Foreground + Blazor scope alive** (`AndroidUtils.IsAppForeground()` +
  `AppServicesAccessor.TryGetScopedServices` — the same pattern already used
  for message pushes): `DispatchToBlazor` → `IncomingCallUI.OnRing(chatId)`.
  No system notification is posted — sound and UI are fully in-app, no double
  ringing.
- **Otherwise**: post the ringer notification (below).

Dismissal pushes keep working unchanged: the call notification reuses the
`NotificationId`-based tag from the payload, so the existing `DismissedTags`
flow cancels it.

### New: ringer notification (background / killed app)

- Channel `incoming_calls`: `Importance.High`, vibration, ringtone — reuse the
  existing `attention_ringtone` raw resource for now (a dedicated call
  ringtone asset can be swapped in later); `AudioUsageKind.NotificationRingtone`.
  Modeled on `NotificationHelper.EnsureAttentionNotificationChannelExist`.
- Notification: `CATEGORY_CALL`, caller avatar/name + chat title, two actions,
  `SetTimeoutAfter(40s)` so it self-destructs at ring timeout even offline.
  Plain heads-up style; `CallStyle` is deliberately deferred to stage A (it
  requires a foreground service or full-screen intent — exactly the B/A
  boundary).
- **Decline action**: broadcast `PendingIntent` → new `CallActionReceiver`
  (`[BroadcastReceiver]`, modeled on `AlarmReceiver`/Snooze): reads `Session`
  via `MauiSession.Read()`, resolves the `ILiveSessions` RPC client from the
  root (non-scoped) container, calls `DeclineCall` — without bringing up
  Blazor. On failure (no session / no network): log and give up; the
  notification times out anyway and the server marks the invite `Missed`.
  Implementation note: verify the `ILiveSessions` client is resolvable from
  the root container; if not, fall back to `WhenAppServicesReady`.
- **Accept action and body tap**: activity `PendingIntent` with URL
  `Links.Chat(chatId)` + an accept marker (query param), routed through the
  existing `NotificationHandler → AppNavigationQueue` chain. When Blazor is
  ready, `IncomingCallUI` re-checks the ring via `LiveSessionUI.Get` and only
  then accepts; body tap navigates without accepting.

### New: reconciliation on scope start

If a call push arrived in the background and the user opened the app from the
launcher (not from the notification), no `OnRing` signal exists. On Blazor
scope start, `IncomingCallUI` queries `NotificationManager` for active
notifications on the `incoming_calls` channel, extracts their `ChatId`s, and
calls `OnRing` for each. No new server API needed.

## UI components

### `IncomingCallBanner` (new, `UI.Blazor.App/Components/Banners`)

Plugged into the existing `BannerUI.Show` + `IBannerView` pipeline
(`TypeMapper` registration in `BlazorUIAppModule`), rendering in the standard
SubHeader banner slot. Content: caller avatar, caller name, chat title (for
group chats), "Incoming call" / "Incoming video call" label, **Accept**
(green, phone icon) and **Decline** (red) buttons. Tapping the banner body
(not the buttons) expands the modal.

Known stage-B limitation (accepted): banners render on chat pages; an open
modal covers it. Stage A's full-screen UI removes this.

### `IncomingCallModal` (rework of the stub)

- Model changes from `AuthorId` to the `IncomingCallUI` state (`ChatId`,
  caller, `HasVideo`) — the current stub is profile-oriented.
- Large avatar, caller name, chat title, wired Accept/Decline.
- For video calls: camera preview following the `JoinVideoCallModal` pattern
  (`ChatVideoUI.StartCameraWarmup`) with a toggle that is **off by default**.
  If the user enables the preview before accepting, the warmup recorder is
  claimed on accept (`TryClaimCameraWarmupRecorder`) and the camera streams
  immediately; otherwise the camera stays off and can be enabled in-call via
  the existing `VideoToggle`.
- Auto-closes when the ring stops being `Ringing`.

### Accept flow (`IncomingCallUI.Accept`, shared by all entry points)

1. `LiveSessionUI.AcceptCall(chatId)` — flips the invite status server-side.
2. Navigate to the chat (`History`/`AutoNavigationUI`).
3. `ChatAudioUI.SetRecordingChatId(chatId)` + `SetListeningState(chatId, true)`
   — this is what actually joins the call: mic open, audio flowing, and
   `LiveSessionUI`'s participation sync picks it up. (Key finding:
   `AcceptCall` alone starts no audio — `VoiceCallTestPage` demonstrates this
   gap.)
4. Mic permission via the existing `AndroidRecordingPermissionRequester` flow.

Accept semantics per call kind (decided): audio call — instant join with mic
open; video call — instant join with mic open, camera initially off (or
streaming immediately if enabled in the modal preview).

### Decline flow

`LiveSessionUI.DeclineCall(chatId)`; banner/modal/ringer stop via the
computed.

## Edge cases

- **Accept on a stale ring** (expired/cancelled/answered elsewhere between tap
  and processing): every accept path re-checks `LiveSessionUI.Get` first; if
  the invite is not `Ringing` → "Call ended" toast, navigate to the chat
  without joining. An `AcceptCall` server error yields the same toast.
- **Cancel while the device is offline**: the dismissal push won't arrive;
  `SetTimeoutAfter(40s)` removes the notification locally, and the in-app
  banner dies via the computed once connectivity returns.
- **Second incoming ring during the first**: `IncomingCallUI` keeps a list;
  the banner shows the most recent ring; when it ends, the previous one shows
  if still live. No queueing/priorities beyond that.
- **Push lost in foreground**: the call is not shown and becomes `Missed`
  after 40s. Accepted stage-B limitation (inherent to the push-as-trigger
  approach).
- **Mic permission denied on accept**: join as listener only
  (`SetListeningState(true)`, no recording); the mic can be enabled later.
- **Own call / own other device**: ignored via the `AmIInLiveConversation` /
  host guard.
- **DND / silent mode**: the in-app ringer respects the ringer mode; the
  background notification relies on channel importance (DND breakthrough is
  stage-A territory).

## Testing

- **Unit tests**: "my ring" selection from `LiveSession.Invites` and
  `IncomingCallUI` state transitions — extracted into pure functions testable
  without Android.
- **Manual E2E matrix**: caller — `VoiceCallTestPage` on web (multi-login via
  debug-ui); callee — an Android device. Matrix:
  {foreground, background, killed} × {accept, decline, timeout, caller cancel,
  accept on another device}. Plus: the channel rings on a muted profile;
  Decline from the notification works without opening the app; tapping a
  notification after the ring ended shows the toast.
- **No Android UI autotests** (deliberate trade-off): the platform-specific
  part is covered by the manual matrix.

## Reuse

Existing abstractions used (no new server API, no polling):

| Piece | Reused for |
|---|---|
| `ILiveSessions.Get` / `LiveSessionUI.Get` (`[RemoteComputeMethod]`) | reactive source of truth for ring state |
| `LiveSessionUI.AcceptCall/DeclineCall`, `AmIInLiveConversation` | call actions and guards |
| `ChatAudioUI.SetRecordingChatId/SetListeningState` | actually joining the call |
| `ChatVideoUI.StartCameraWarmup` + `TryClaimCameraWarmupRecorder` (`JoinVideoCallModal` pattern) | camera preview in the modal |
| `BannerUI` + `IBannerView` + `Banners.razor` | banner pipeline |
| `NotificationHelper` channel/builder patterns (`EnsureAttentionNotificationChannelExist`, `attention_ringtone`) | the `incoming_calls` channel |
| `ChatAttentionService` action + `AlarmReceiver` pattern | `CallActionReceiver` for Decline |
| `AppServicesAccessor` (`TryGetScopedServices`, `DispatchToBlazor`), `AndroidUtils.IsAppForeground` | FCM → Blazor bridge |
| `NotificationHandler` → `AppNavigationQueue`, `Links.Chat` | notification tap / accept routing |
| `MauiSession.Read()` / `TrueSessionResolver` | Session for the Blazor-less Decline |
| `AndroidRecordingPermissionRequester` | mic permission on accept |

New components and their placement:

- `IncomingCallUI`, `IIncomingCallRinger`, `IncomingCallBanner`, reworked
  `IncomingCallModal` — `UI.Blazor.App` (Android-gated activation). They are
  potentially reusable for web/iOS in-app calls later, which is why the logic
  lives in shared Blazor code with only activation and the ringer
  implementation being platform-specific.
- `AndroidIncomingCallRinger`, `CallActionReceiver`, the `incoming_calls`
  channel and notification builder — `App.Maui/Platforms/Android`.
