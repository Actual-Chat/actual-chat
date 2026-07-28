# Incoming-Call UI — Implementation Plan (executed)

> **Status: done.** Every task below except Task 8 shipped on
> `feat/1xxx-android-calls`. This document was rewritten on 2026-07-28 to match
> the code that actually landed: the original plan carried full source listings
> for code that has since evolved, so the listings were replaced by the shipped
> shape plus file pointers. Only snippets that capture a non-obvious contract are
> kept verbatim.

**Goal:** in-app incoming-call banner + modal (push-triggered, computed-driven),
a `CallStyle` ringer notification with Accept/Decline when the app is backgrounded
or killed, and a lock-screen call screen with accept-over-keyguard.

**Architecture:** the FCM `IncomingCall` push only wakes the client. The scoped
`IncomingCallUI` service tracks candidate rings and derives the visible ring from
the reactive `LiveSessionUI.Get(chatId)` — cancel/timeout/answer-elsewhere all end
the ring without any further push. Android platform code contributes the FCM
branch, the silent `incoming_calls` channel, the `CallStyle` notification with
a full-screen intent, a Decline broadcast receiver that works without Blazor, the
looping ringer, and the keyguard lifecycle. Web/desktop get the same in-app ring
through the service worker and `INotifications.ListActive`.

**Tech Stack:** .NET 10 MAUI (Android head), Blazor Hybrid (`UI.Blazor.App`),
ActualLab.Fusion compute services, Firebase Cloud Messaging (data messages),
AndroidX `NotificationCompat` (`CallStyle`).

**Spec:** `docs/superpowers/specs/2026-07-07-android-incoming-call-ui-design.md`

## Global constraints

- `docs/CODING_STYLE.md` and `docs/development/ui-components.md` apply: no `Async`
  suffix; no XML docs on members (short type-level `<summary>` only where the name
  isn't obvious); Allman braces for methods/types, K&R elsewhere;
  `.ConfigureAwait(false)` in service code, `.ConfigureAwait(true)` in UI code that
  touches instance state after `await`; no inline Tailwind in `.razor` (`c-`
  classes + `@apply`); max line length 120.
- Comments: only where they mark a non-obvious constraint.
- Server ring timeout is **40 s** (`LiveSessionsBackend.RingTimeout`); the client
  mirrors it only in `SetTimeoutAfter` on the Android notification.
- Build verification: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
  for shared code; `dotnet build src/dotnet/App.Maui/App.Maui.csproj -f net10.0-android`
  for Android code (requires the `maui-android` workload); `npm run build:Verify`
  for the TypeScript added in Tasks 13/15.

## Reuse

| Existing | Used for |
|---|---|
| `LiveSessionUI.Get` / `.AcceptCall` / `.DeclineCall` / `.LeaveCall` / `.AmIInLiveConversation` | ring source of truth, call actions |
| `ChatAudioUI.SetRecordingChatId` / `.SetListeningState` | actually joining the call audio |
| `INotifications.ListActive` | push-free ring discovery; dropped-push safety net |
| `NotificationReconciler` + `IDeviceNotifications` | re-showing a ring whose push was dropped |
| `Chat.Rules.CanWriteAudio()` | peer-call anti-spam gate, client and server |
| `Banner` component + `Banners.razor` always-on slot (like `ReconnectBanner`) | banner rendering |
| `NotificationHelper` (`CreateViewIntent`, `RequestCodeProvider`, `GetImage`) | call intents and avatars |
| `ChatAttentionService` + `AlarmReceiver` action pattern | Decline broadcast receiver |
| `AppServicesAccessor.DispatchToBlazor` / `TryGetScopedServices` | FCM → Blazor bridge |
| `IntentHandler` → `NotificationHandler` → `AppNavigationQueue`, `Links.Chat` | notification tap / accept routing |
| `MauiSession` (secure-storage session) | Session for Blazor-less Decline |
| `AudioRecorder.MicrophonePermission.CheckOrRequest` | mic permission on accept |
| `attention_ringtone` raw asset | the web ringtone |

Placement: `IncomingCallUI`, `IIncomingCallsBridge`, `IncomingCall` →
`UI.Blazor.App/Services` (shared — they drive Android, web and desktop alike);
`IncomingCallBanner` → `UI.Blazor.App/Components/Banners`;
`IncomingCallOverLockView` → `UI.Blazor.App/Components/IncomingCallModal`;
`IncomingCallRingtone` / `OutgoingCallRingback` → `UI.Blazor.App/Services/*.ts`;
Android-only `IncomingCallNotifications`, `IncomingCallRinger`,
`CallActionReceiver`, `AndroidIncomingCallsBridge` → `App.Maui/Platforms/Android`.

**Deviations from the spec (deliberate):** the banner is an always-on component
in `Banners.razor` (like `ReconnectBanner`) driven by `IncomingCallUI` state, not a
`BannerUI.Show` / `IBannerView` dynamic banner — the dynamic pipeline's dismiss
semantics don't fit a state-driven call banner. The spec's `IIncomingCallRinger`
was merged into the broader `IIncomingCallsBridge` — one platform hook instead of
two.

---

## Task 1: server call-scoped push tag

`CallNotification.SimilarityKey` is a `ConversationId` (`"{chatId}:{lid}"`), which
`ChatId.TryParse` rejects, so `GetPushTag()` fell through to `null`: the ring push
went out with tag `"topic"` and the dismissal push carried no tag at all, leaving
the Android client unable to cancel the call banner.

**Shipped on `dev`** (commit `c3c9de8185`), not on this branch:

- `Constants.Notification.CallTagPrefix` = `"call-"`.
- `NotificationExt.GetPushTag()` → `"call-{chatId}"` for `CallNotification`;
  `GetChatTag()` handles `CallNotification` explicitly, before the generic
  `ChatNotification` arm.
- `tests/Notifications.IntegrationTests/CallNotificationTagTests.cs`.

- [x] Implemented and tested (`dotnet test tests/Notifications.IntegrationTests --filter CallNotificationTag`)

---

## Task 2: `IncomingCallUI` + `IIncomingCallsBridge` + registration

**Files:**
- `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs` (new)
- `src/dotnet/UI.Blazor.App/Services/IIncomingCallsBridge.cs` (new)
- `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` — `fusion.AddService<IncomingCallUI>(ServiceLifetime.Scoped)`
- `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` — `IncomingCallUI` property
- `tests/Chat.UI.Blazor.UnitTests/IncomingCallUITest.cs` (new)

**Public shape as shipped:**

- `record IncomingCall(ChatId ChatId, AuthorId Caller, bool HasVideo)`
- `void OnRing(ChatId chatId, bool showOverLockScreen = false)`
- `void OnCallDismissed(ChatId chatId)`, `void OnOverLockScreenRendered()`
- `[ComputeMethod] Task<IncomingCall?> GetIncomingCall(CancellationToken)`
- `[ComputeMethod] Task<IncomingCall?> GetRingingCall(ChatId, CancellationToken)`
- `Task Accept(ChatId)`, `Task Decline(ChatId)`, `Task GoToChat(ChatId)`, `Task HangUp(ChatId)`
- `IState<ChatId?> OverLockChatId`
- `static IncomingCall? FindRingingCall(LiveSession? live, AuthorId ownAuthorId)` — pure, unit-tested

Candidate rings live in a `MutableState<ImmutableList<ChatId>>`; `GetRingingCall`
is the confirmation against `LiveSessionUI.Get` + `Authors.GetOwn`. Workers
(`OnRun`): `SyncRings` (reconcile from the bridge, drive the ringer, prune dead
candidates), `SyncActiveCallNotifications` (Task 13), `ResetOverLockScreen`
(Task 11).

The bridge interface grew over Tasks 10–11 to its final seven members — see
`IIncomingCallsBridge.cs`; the base four are:

```csharp
    void StartRinging();
    void StopRinging();
    Task<ChatId[]> ListActiveCallChatIds(CancellationToken cancellationToken);
    void DismissCallNotification(ChatId chatId);
```

- [x] Unit test written first, verified failing, then green (3 tests)
- [x] Service + bridge implemented, registered, `dotnet build UI.Blazor.App` OK
- [x] Commit `0f1c27635b`

---

## Task 3: `IncomingCallBanner`

**Files:**
- `src/dotnet/UI.Blazor.App/Components/Banners/IncomingCallBanner.razor` (new)
- `src/dotnet/UI.Blazor.App/Components/Banners/Banners.razor` — next to `<ReconnectBanner/>`
- `src/dotnet/UI.Blazor.App/Components/Banners/banners.css`

A `ComputedStateComponent` over `IncomingCallUI.GetIncomingCall`; renders nothing
when there is no ring. Final content (after the Task 16 tidy-up) is just an
`AuthorBadge` for the caller plus **Accept** / **Decline**; the badge area opens
`IncomingCallModal`. Task 13 also renders it from `LeftPanelContent` on narrow
screens.

- [x] Component, always-on rendering, styles, `dotnet build UI.Blazor.App` OK
- [x] Commit `0f1c27635b` (tidy-up in `52840dc9ba`)

---

## Task 4: rework `IncomingCallModal`

**Files:**
- `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallModal.razor`

`Model(AuthorId CallerId)` stays a positional single-`AuthorId` record so existing
call sites keep compiling. The modal computes the caller `Author` plus
`GetRingingCall(chatId)`, wires Accept/Decline to `IncomingCallUI`, and closes
itself once the ring ends:

```razor
    if (m.Author is not { } author || m.Call is not { } call) {
        // The ring ended (cancel / timeout / answered elsewhere) — close ourselves.
        if (!_isAutoClosed) {
            _isAutoClosed = true;
            Modal.Close();
        }
        return;
    }
```

`IncomingCallModalHeader.razor` is unchanged. The dev-only "Show call" stub button
in `AuthorModalHeader` was later deleted (Task 16), so nothing opens this modal
outside a real ring anymore.

- [x] Rewritten, `dotnet build UI.Blazor.App` OK
- [x] Commit `0f1c27635b`

---

## Task 5: Android channel + call notification + FCM branch

**Files:**
- `src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs` (new)
- `src/dotnet/App.Maui/Platforms/Android/CallActionReceiver.cs` (new, stubbed here)
- `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs`

Produced (consumed by Tasks 6, 7, 10): `ChannelId`, `DeclineAction`,
`ChatIdExtraKey`, `AcceptExtraKey`, `Show(NotificationData)`, `Dismiss(ChatId)`,
`ListActiveCallChatIds()`, `CallTag(chatId)` → `"call-{chatId}"`.

Initial version: a plain heads-up notification on the `incoming_calls` channel
with its own ringtone and manual Accept/Decline actions, shown only when the app
was **not** in the foreground with a live Blazor scope. Task 10 replaced all three
of those decisions. The FCM branch:

```csharp
        if (data.NotificationKind == NotificationKind.IncomingCall) {
            HandleIncomingCall(data);
            return;
        }
```

- [x] Implemented, `dotnet build App.Maui -f net10.0-android` OK
- [x] Commit `aa6fea1621`

---

## Task 6: Decline receiver + Accept routing + `MauiSession.ReadStored`

**Files:**
- `src/dotnet/App.Maui/Services/MauiSession.cs` — `ReadStored()`
- `src/dotnet/App.Maui/Platforms/Android/CallActionReceiver.cs`
- `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationHandler.cs`
- `src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs` — `HandleViewIntent`

`CallActionReceiver` declines from the notification without bringing up Blazor:
`GoAsync()` to survive the receiver's 10 s budget, `MauiSession.ReadStored()` for
the Session, `ILiveSessions` from the root container. Task 16 put a live-scope
fast path in front of it (the root container may lack the Fusion client stack) and
made `ReadStored()` lock-guarded so the stored session is read exactly once.

`HandleViewIntent` re-verifies the ring through `IncomingCallUI.Accept` once
Blazor is up, so a stale tap yields a "Call ended" toast rather than a phantom
join; a body tap only navigates. Task 10 extended it to register the ring
(`OnRing`) for the full-screen path.

- [x] Implemented, `dotnet build App.Maui -f net10.0-android` OK
- [x] Commit `aa6fea1621` (fixes in `771b0c8615`, `e31e2e07e2`)

---

## Task 7: `AndroidIncomingCallsBridge` + DI

**Files:**
- `src/dotnet/App.Maui/Platforms/Android/AndroidIncomingCallsBridge.cs` (new)
- `src/dotnet/App.Maui/MauiProgram.Android.cs` — next to `IDeviceNotifications`:

```csharp
        services.AddScoped<IIncomingCallsBridge>(_ => new AndroidIncomingCallsBridge());
```

Scoped lifetime matters: the bridge is disposed with the Blazor scope, so a
mid-ring scope teardown stops the ringer (`IDisposable.Dispose` → `StopRinging`).

The bridge started out owning a `Ringtone`-based ringer inline; Task 10 moved the
sound into the static `IncomingCallRinger` and left the bridge a thin adapter,
then Task 11 added the keyguard members.

- [x] Implemented, registered, `dotnet build App.Maui -f net10.0-android` OK
- [x] Commit `aa6fea1621`

---

## Task 8: camera preview toggle in `IncomingCallModal` — NOT IMPLEMENTED

Deliberately dropped. Accepting a video call joins with the camera off; it can be
enabled in-call via the existing `VideoToggle`.

Still open if it is picked up later: extract `IVideoSession` /
`WarmupRecorderVideoSession` / `CameraState` out of `JoinVideoCallModal.razor`
into a shared `VideoSessions.cs`, make `ChatVideoUI.StartVideoStreaming`
`internal`, add a `withCamera` parameter to `IncomingCallUI.Accept` (which
`StartVideoStreaming` turns into a `TryClaimCameraWarmupRecorder` claim, so the
previewed camera keeps streaming without a re-acquire), and add the preview +
toggle to the modal following `JoinVideoCallModal`'s JS-module lifecycle.

- [ ] Not done — deferred

---

## Task 9: AOT regen, full build, tests

**Files:** `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs` (generated —
do not hand-edit).

New Razor components must appear in the generated keep-list
(`dotnet run --project src/dotnet/App.AotHelper -- -g`, see `docs/native-aot.md`).

- [x] `CodeKeeper.Keep<…IncomingCallBanner>()` regenerated — commit `fa313a4aa0`
- [x] `dotnet build UI.Blazor.App`, `dotnet build App.Maui -f net10.0-android`,
      `dotnet test tests/Chat.UI.Blazor.UnitTests`,
      `dotnet test tests/Notifications.IntegrationTests --filter "CallNotificationTag|NotificationSerializationTests"`
- [ ] `IncomingCallOverLockView` keep-list entry — verify on the next regen if the
      over-lock screen ever fails to render in an AOT build

---

## Task 10: lock-screen ring, `CallStyle`, single ring source

Absorbed the original "stage A" ring surface.

**Files:**
- `IncomingCallNotifications.cs`, `IncomingCallRinger.cs` (new),
  `AndroidIncomingCallsBridge.cs`, `FirebaseMessagingService.cs`,
  `AndroidManifest.xml`, `Audio/AndroidAudioFocusHelper.cs`

What shipped:

- The `incoming_calls` channel became **silent and non-vibrating**,
  `Importance.High`. The in-app ringer is the only sound source, so the channel
  must not ring on top of it.
- `NotificationCompat.CallStyle.ForIncomingCall(caller, decline, accept)` with a
  `Person` carrying the caller name and avatar; `SetOngoing(true)`,
  `VisibilityPublic`, `CategoryCall`, `SetTimeoutAfter(40 s)`.
- `SetFullScreenIntent(fullScreenPendingIntent, true)` + the
  `USE_FULL_SCREEN_INTENT` permission — surfaces the ring over the lock screen /
  with the screen off, degrades to a heads-up banner when unlocked.
- The notification is now posted in **every** app state, including the foreground;
  when a Blazor scope is alive the ring is additionally dispatched to
  `IncomingCallUI.OnRing`.
- `ClearForegroundCallRings` — a dismissal push with a `call-` tag routes to
  `IncomingCallUI.OnCallDismissed`, so a foreground ring (which lives in the
  banner, not a system notification) clears at once.
- `IncomingCallRinger` — static, looping `MediaPlayer` on the system default
  ringtone URI plus waveform vibration, respecting the ringer mode. `MediaPlayer`
  rather than `Ringtone`: `Ringtone.Play()` is unreliable on its first invocation.
- `AndroidAudioFocusHelper.WarmUpAudioMode` bails out while
  `IncomingCallRinger.IsPlaying` — switching to `Mode.InCommunication` reroutes
  `STREAM_RING` to the earpiece and audibly drops the ring.

- [x] Commit `68faf626bd`

---

## Task 11: over-lock call screen and the keyguard lifecycle

**Files:**
- `src/dotnet/UI.Blazor.App/Components/IncomingCallModal/IncomingCallOverLockView.razor` (new)
- `.../IncomingCallModal/incoming-call-modal.css`
- `src/dotnet/UI.Blazor.App/Components/AlwaysVisibleComponents.razor`
- `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs`
- `src/dotnet/App.Maui/Platforms/Android/MainActivity.cs`, `IntentHandler.cs`,
  `Notifications/NotificationHandler.cs`, `AndroidIncomingCallsBridge.cs`
- `src/dotnet/App.Maui/Platforms/Android/Audio/AndroidAudioWidget.cs`,
  `Audio/AndroidAudioWidgetForegroundService.cs`

Bridge members added:

```csharp
    Task<bool> OnCallHandled(bool accepted);
    void RevealCallScreen();
    void MoveBehindLockScreen();
```

- `IncomingCallOverLockView` — a full-screen Blazor view rendered from
  `AlwaysVisibleComponents` while `IncomingCallUI.OverLockChatId` is set, with a
  **Ringing** phase (Decline / Accept) and an **InCall** phase (go-to-chat /
  hang-up).
- `MainActivity`: `EnableShowWhenLocked` / `DisableShowWhenLocked`, the
  splash-colored `ShowCallCover` / `HideCallCover`, and `DismissKeyguardForCall`
  with a `KeyguardDismissCallback` reporting whether the screen ended up unlocked.
- `NotificationHandler.HandleIntent(intent, isColdStart)` — cold start brings the
  app over the keyguard eagerly behind a cover (the WebView would otherwise flash
  its restored route); a warm start renders the call screen first and reveals it
  through `RevealCallScreen`. `OnOverLockScreenRendered` waits ~200 ms because the
  render callback fires before the WebView paints.
- Accept over the lock screen does **not** unlock: the over-keyguard activity
  counts as foreground, so the mic FGS may start and audio flows while locked. The
  chat opens only via go-to-chat (PIN first; a cancelled PIN keeps the in-call
  screen). `_isAccepting` holds the over-lock session "active" across the accept
  transition so `ResetOverLockScreen` can't tear it down mid-accept.
- The audio-widget FGS start paths now log instead of crashing — some OEMs reject
  a microphone FGS started over the keyguard.

- [x] Commits `94ba4a91ce`, `bdcb44f60f`, `8f20ae6aa8`, `4df39212bd`

---

## Task 12: recognize the ring in the `Dialing` phase + gated tracing

`FindRingingCall` originally matched `LiveSessionKind.Call`, which never matches an
*unanswered* ring — the server keeps the session in `Dialing` until someone
answers and only then promotes it. Fixed to match `Dialing`; the unit test was
updated accordingly.

Added `Constants.DebugMode.AndroidIncomingCalls` (off by default) and `CALL_TRACE:`
logging behind it across `IncomingCallUI`, `IncomingCallNotifications`,
`NotificationHandler`, `FirebaseMessagingService`, `MainActivity`,
`AndroidIncomingCallsBridge` and the over-lock view.

- [x] Commit `151c79cf96`

---

## Task 13: web / desktop incoming calls

**Files:**
- `src/dotnet/UI.Blazor/ServiceWorkers/service-worker.ts`
- `src/dotnet/UI.Blazor.App/notification-ui.ts`, `NotificationUI.cs`
- `src/dotnet/UI.Blazor.App/Services/incoming-call-ringtone.ts` (new),
  `exports.ts`, `src/nodejs/src/logging.ts`
- `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs`
- `src/dotnet/UI.Blazor.App/Components/LeftPanel/LeftPanelContent.razor`
- `src/dotnet/Notifications.Service/NotificationsBackend.cs`

- The service worker posts `INCOMING_CALL` / `INCOMING_CALL_CANCELLED` to every
  open window (the `call-` tag prefix is duplicated as a literal — a service worker
  can't import `AppConstants`) and shows an OS notification; foreground FCM does
  the same through `onIncomingCallPush`.
- `NotificationUI.OnIncomingCall` / `OnIncomingCallCancelled` (`[JSInvokable]`)
  forward to `IncomingCallUI.OnRing` / `OnCallDismissed`.
- `SyncActiveCallNotifications` — follows the reactive `Notifications.ListActive`
  and seeds `OnRing`, so a connected client discovers a ring with no push at all.
  Shipped as non-Android only; Task 17 extended it to Android as a safety net.
- `IncomingCallRingtone` — a looping `HTMLAudioElement` on `attention_ringtone`,
  used by `IncomingCallUI` whenever no `IIncomingCallsBridge` is registered.
- The banner also renders from `LeftPanelContent` on narrow screens.
- `NotificationsBackend.OnCancelCall` enqueues `NotificationsBackend_Handle`
  instead of a raw `PushDismissal`, so a cancelled ring leaves the active set
  (otherwise `ListActive` discovery resurrects it) while `ApplyHardUpdate` still
  emits the dismissal push.

- [x] Commits `10b07117d5`, `29f1f93a81`, `ce19dbfd45`, `faba3ec417`
- [x] `npm run build:Verify`

---

## Task 14: peer-call anti-spam gate

**Files:**
- `src/dotnet/Streaming.Service/Services/LiveSessions.cs`
- `src/dotnet/UI.Blazor.App/Components/ChatHeaderCallButton.razor`,
  `Components/components.css`
- `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`
- `tests/Chat.IntegrationTests/PeerCallGateTest.cs` (new)

The same signal that gates peer messaging gates peer calls: in a peer chat the
stream permissions are stripped unless the recipient stored the caller's contact
or replied (a block leaves the contact non-regular too), so
`chat.Rules.CanWriteAudio()` is the reused check.

```csharp
        if (chatId is PeerChatId && !chat.Rules.CanWriteAudio())
            throw StandardError.Constraint(
                "You can call this user only after they add you to their contacts or reply to you.");
```

Client side: `ChatHeaderCallButton` disables the button with an explanatory
tooltip when the gate is closed, and `LiveSessionUI.StartCall` toasts on failure —
only `StandardError.Constraint` carries user-facing text.

- [x] Commits `71816a995a`, `583d0db7c2`
- [x] `dotnet test tests/Chat.IntegrationTests --filter PeerCallGate`

---

## Task 15: caller-side ringback and dialing-aware UI

**Files:**
- `src/dotnet/UI.Blazor.App/Services/outgoing-call-ringback.ts` (new), `exports.ts`
- `src/dotnet/UI.Blazor.App/Services/LiveSessionUI.cs`
- `src/dotnet/UI.Blazor.App/Services/ChatActivityUI.cs`
- `src/dotnet/UI.Blazor.App/Components/ChatHeaderCallButton.razor`
- `tests/Chat.IntegrationTests/CallNotificationFlowTest.cs` (new)

- `OutgoingCallRingback` — a synthesized European ringback (425 Hz, 1 s on /
  4 s off) encoded to a WAV blob and looped through an `HTMLAudioElement`; media
  playback survives tab backgrounding, a Web Audio graph would not. Started and
  stopped by `WatchOutgoingCall` for as long as the session is `Dialing`, with a
  single-owner guard so a restarted watch takes the tone over instead of
  silencing it.
- `ChatActivityUI` gained `IsDialing` and excludes the dialing host from the
  participant count — a chat that is merely ringing must not show "1 · live".
- The call button treats `IsDialing` as busy: offering it again would re-notify
  every invitee and extend the ring window.

- [x] Commits `7d0bdb3d9c`, `1d05905d32`, `20ee61215c`
- [x] `npm run build:Verify`, `dotnet test tests/Chat.IntegrationTests --filter CallNotificationFlow`

---

## Task 16: pre-landing review fixes

- `CallActionReceiver` prefers the live Blazor scope over the root container.
- `MauiSession.ReadStored()` is lock-guarded — the stored session is read once.
- SCO receiver: no inline continuations on the receiver thread
  (`TaskCompletionSourceExt.New`).
- Dead "Show call" preview button removed from `AuthorModalHeader`.
- Incoming-call banner tidied down to the caller badge + two buttons.

- [x] Commits `4299e100e5`, `4826758a27`, `e31e2e07e2`, `b48c1cc1d5`, `771b0c8615`

---

## Task 17: dropped-push safety net on Android

**Files:**
- `src/dotnet/App.Maui/Platforms/Android/Notifications/AndroidDeviceNotifications.cs`
- `src/dotnet/App.Maui/Platforms/Android/Notifications/IncomingCallNotifications.cs`
- `src/dotnet/UI.Blazor.App/Services/IncomingCallUI.cs`

`NotificationReconciler` was already subscribed to `Notifications.ListActive` on
Android (it needs only `IDeviceNotifications`), so it already re-created a ring
whose push was dropped — but through `NotificationHelper.ShowChatNotification(…,
silent: true)`, i.e. as a mute chat banner with no `CallStyle`, no action buttons
and no full-screen intent. A restored call looked like a silent message.

- `AndroidDeviceNotifications.Reconcile` now routes `call-` tags to
  `IncomingCallNotifications.Show`, so a healed ring comes back as a real ring.
  `Show` gained an overload taking the fields of an `ActiveNotificationInfo`
  instead of a push `NotificationData`, and the tag parsing moved into the shared
  `IncomingCallNotifications.TryParseCallTag`.
- `SyncActiveCallNotifications` lost its `if (Bridge is not null) return;`, so the
  in-app banner and ringer also recover from a dropped push.

Costs nothing extra: the `ListActive` computed was already being captured by
`NotificationReconciler`, so Fusion reuses it. Covers a live Blazor scope only —
a push dropped while the app is killed remains unrecoverable.

- [x] `dotnet build App.Maui -f net10.0-android`, `dotnet test tests/Chat.UI.Blazor.UnitTests --filter IncomingCallUITest`
- [ ] Device check: drop a ring push (airplane mode during `StartCall`, then
      reconnect) with the app foregrounded and backgrounded

---

## Manual E2E matrix

Caller: `/test/voice-call` (admin-only) on web, or the chat-header call button.
Callee: an Android device/emulator, plus a second browser for the web path.

| # | App state | Action | Expected |
|---|---|---|---|
| 1 | Foreground | Caller starts call | Banner appears in-app, device rings (looping) + vibrates; the silent call notification is also posted |
| 2 | Foreground | Tap banner body | Modal opens (caller avatar, Accept/Decline) |
| 3 | Foreground | Accept (banner or modal) | Ring stops, navigates to chat, mic on, participation visible on the caller side |
| 4 | Foreground | Decline | Ring stops, banner gone, caller sees invite Declined |
| 5 | Foreground | Caller cancels | Banner + ringer stop by themselves |
| 6 | Foreground | Wait 40 s | Same — ring self-expires |
| 7 | Background | Caller starts call | `CallStyle` notification with Answer/Decline, in-app ringer sound |
| 8 | Background | Tap Decline on notification | Notification gone WITHOUT opening the app; caller sees Declined |
| 9 | Background | Tap Answer on notification | App opens → chat opens → joined with mic on |
| 10 | Killed (swipe from recents) | Caller starts call | Same as 7 |
| 11 | Killed | Tap notification body | App opens on the chat; banner shows if still ringing |
| 12 | Background, push arrived | Open app from launcher (not notification) | Banner appears via reconciliation; notification dismissed on accept/decline |
| 13 | Background | Caller cancels | Dismissal push removes the notification |
| 14 | Any | Accept on ANOTHER device, watch this one | Banner/notification clears itself |
| 15 | Locked screen, killed | Caller starts call | Full-screen call screen over the keyguard, no flash of app content |
| 16 | Locked screen, running | Caller starts call | Same, via render-first-then-reveal |
| 17 | Locked screen | Accept | Stays locked, audio starts; go-to-chat asks for the PIN and then opens the chat |
| 18 | Locked screen | Decline / hang up / remote end | Screen closes, app goes behind the lock screen |
| 19 | Any | Tap Answer on a stale notification (after ring end, within 40 s) | App opens, "Call ended" toast, no join |
| 20 | Web tab open | Caller starts call | Banner + looping web ringtone; also discovered with no push (`ListActive`) |
| 21 | Caller side | Start a call | Ringback tone while dialing; call button hidden while dialing; no "1 · live" in the chat list |
| 22 | Caller side, peer chat with a non-contact | Try to call | Button disabled with a tooltip; the RPC also refuses |
| 23 | Foreground / background, ring push dropped | Caller starts call | Ring still appears via `ListActive`: in-app banner + ringer, and the system notification as a full `CallStyle` ring, not a silent banner |

---

## Execution notes

- Task 1 landed independently on `dev`; Tasks 2–7 and 9 were the original stage-B
  batch; Tasks 10–16 grew out of device testing and review.
- The server must be redeployed (or `/server-loop` restarted) for the push-tag and
  `OnCancelCall` changes to affect pushes during manual testing.
- Task 8 (camera preview) is the only planned item not shipped.
