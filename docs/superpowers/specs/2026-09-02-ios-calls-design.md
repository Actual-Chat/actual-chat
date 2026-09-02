# iOS Calls — Design

Issue: #3296. Branch: `feat/3296-ios-calls` (reset onto `dev` at `352627c60b`).
Pre-reset tip preserved at `backup/3296-ios-calls-pre-rebase` (`369256c3ea`).

Target: **CallKit for incoming *and* outgoing calls**, delivered by **PushKit VoIP
pushes over direct APNs**. Dev-signed builds first; prod entitlement and App Review
are a separate task.

---

## 1. Why the old branch is not rebased

The branch's 8 commits sat on a merge-base `dev` has since moved 2280 commits past.
Of the 41 files it touched, 17 no longer exist — and they are almost exactly the
server-side ones. Rebasing would mean resolving delete/modify conflicts to produce
code we would then delete.

| Branch introduced | Status on `dev` today |
|---|---|
| `Notification.Service/Apns.cs` (dotAPNS) | Superseded by `ApnsClient` — hand-rolled, ES256 JWT with caching, dead-token pruning, HTTP/2, already sending `apns-push-type: pushtotalk` |
| `NotificationSettings.Apns.*` | Superseded by `NotificationsSettings.ApplePush{KeyId,TeamId,BundleId,PrivateKeyPath,UseSandbox}` |
| `NotificationChannel` enum on `Device` | Superseded — token kinds are discriminated by `DeviceType` (`iOSApp` = FCM, `iOSPttApp` = PushKit PTT) |
| `CallToggle.razor` | Superseded by `ChatHeaderCallButton`, `IncomingCallModal`, `IncomingCall{,Over Lock}Banner`, `IncomingCallUI`, `CallNotification`, `CallInvite`/`CallState` |
| `Notification.{Contracts,Service}` projects | Renamed to `Notifications.*` |
| `IosCalls.cs`, `IosVoipPushes.cs` | Still unique — `dev` has no CallKit/PushKit-VoIP code at all |

**Salvaged from the old branch** (four items, all rewritten against `dev`):

1. `IosVoipPushes.cs` — the PushKit registry. Its sign-in wait and `AsyncLock` around
   token refresh were real bug fixes (commits `f01892f83c`, `cd3504d44a`) and are kept.
2. `IosCalls.cs` — the `CXProviderDelegate`. Substantially rewritten (see §4).
3. `aps-environment` in `Entitlements.dev.plist`.
4. `docs/plans/ios-call-audio-session.md` — its §1 (AVAudioSession rules) is still
   accurate and load-bearing. Its §2–4 describe code that no longer exists; the
   findings they raise are re-stated against `dev` in §5 below.

Everything else is dropped.

## 2. FCM cannot deliver VoIP pushes

Established externally and, more usefully, inside this codebase already:
`ApnsClient` exists precisely because "FCM cannot deliver `apns-push-type=pushtotalk`".
VoIP is the same restriction — Firebase supports no VoIP Services Certificate and
only the `alert` and `background` push types.

So iOS calls need a **second push token per device** (PushKit VoIP, distinct from the
FCM token) and a **direct APNs send**. Both already have a proven precedent in the
PTT work; this design follows it rather than inventing a parallel one.

Today an iOS device receives a call ring as a plain FCM alert banner
(`apns-push-type: alert`, priority 10, `attention_ringtone.caf`) — a banner, never a
full-screen system call UI, and nothing at all when the app is killed.

## 3. Server: VoIP push delivery

### 3.1 New device type

```
DeviceType.iOSVoipApp = 5
```

A PushKit VoIP token: direct-APNs only, must never be handed to FCM. Mirrors the
`iOSPttApp` precedent, including its comment.

**Hardening (required, not optional).** FCM device lists are filtered today by
open-coded exclusions — two sites in `NotificationsBackend` (the `OnPush` fan-out and
the `OnConverge` dismissal send) each say `d.DeviceType != DeviceType.iOSPttApp`.
Adding a second push-only type to that
pattern means every future one is a silent leak of a PushKit token into an FCM
batch. Replace the exclusions with a single predicate (`DeviceTypeExt.IsFcm`, in
`Api/Notifications/` next to the enum) so the enum and the filter cannot drift.

### 3.2 The ring push

`IApnsClient` gains:

```
Task SendCallRing(
    ConversationId conversationId, AuthorId caller, string callerName,
    bool hasVideo, IReadOnlyCollection<Symbol> deviceIds, CancellationToken ct);
```

- `apns-push-type: voip`, `apns-topic: <ApplePushBundleId>.voip`, `apns-priority: 10`.
- `apns-expiration` = `Constants.Call.RingTimeout` (20s). A ring that outlives its own
  window must not wake a phone.
- Payload: `ConversationId`, caller `AuthorId`, caller display name, `HasVideo`, and
  the derived call id (§4.2). `ChatId` is carried too, redundantly — it is derivable
  as `ConversationId.ChatId`, but the push handler runs before any Blazor scope and
  should not have to parse to route a ring.
- All JWT caching, dead-token pruning (`NotificationsBackend_RemoveDevices`) and HTTP/2
  handling are inherited from `ApnsClient` unchanged.

### 3.3 There is no cancel push

A VoIP push that does not result in a reported call gets the app killed by iOS and
its VoIP delivery revoked. Cancel therefore must not be a VoIP push — and does not
need to be:

- After a ring the app is alive holding an active CallKit call, so `IncomingCallUI`'s
  reactive `LiveSessionUI` path ends it (the same path that already handles cancel,
  timeout, decline, and answered-on-another-device).
- `NotificationsBackend.OnCancelCall` already enqueues `Dismiss`, which emits a silent
  FCM dismissal. iOS maps that onto `CXProvider.ReportCall(...Ended)` as an
  independent second path.

Two paths, neither of them a VoIP push.

### 3.4 Double-ring suppression

A phone holding both tokens would otherwise get a CallKit screen *and* an FCM banner
for one call. `DbDevice.SessionHash` identifies the app installation, so it is the
join key between an `iOSApp` row and its sibling `iOSVoipApp` row.

For a `CallNotification`, an `iOSApp` device is excluded from the FCM fan-out when a
`iOSVoipApp` row shares its `SessionHash`.

This needs `SessionHash` projected onto the `Device` contract model — it exists on
`DbDevice` and is filterable in `ListDevices`, but is not on `Device` today. Add it to
`Device` and `DbDevice.ToModel`.

The split lives in the `OnPush` device fan-out, structured the way `SendPttWake`
already splits FCM vs PTT devices.

## 4. Client: PushKit + CallKit

### 4.1 Both are headless statics

`IosVoipPushes` and `IosCalls` are static singletons initialised from
`FinishedLaunching`, never from the Blazor scope — the same shape as `IosPtt`.

This is not stylistic. A VoIP push arrives with no WebView alive, and anything
reachable only from `AfterFirstRender` is dead on that path. The scoped
`IIncomingCallsBridge` implementation (§6) is a thin adapter onto these statics, not
their owner.

- `IosVoipPushes`: `PKPushRegistry` with `DesiredPushTypes = voip`;
  `DidUpdatePushCredentials` → `MauiNotifications.RefreshNotificationToken(token,
  DeviceType.iOSVoipApp)`; `DidReceiveIncomingPush` → `IosCalls.ReportIncomingCall(...)`
  synchronously, invoking `completion()` from the report callback.

### 4.2 `IosCalls` — changes from the old branch version

| Old branch | This design | Why |
|---|---|---|
| `SupportedHandleTypes = PhoneNumber, EmailAddress`; handle built from the caller's phone/email | `Generic` only | We are not a telephony app. The old shape leaks a contact detail into the system call log and misroutes a handle that is not a dialable number. |
| Random `NSUuid` per push + `LruCache<NSUuid, ChatId>` | Call id derived deterministically from `ConversationId` (name-based UUID) | Push, cancel, reactive state and app restart all agree on one id with no lookup table to lose. The cache dies with the process; the ring does not. |
| No `DidDeactivateAudioSession` | Implemented; audio starts there and stops there | CallKit owns activation (§5) |
| No route-change observer | `AVAudioSession` route-change observer | Headset/Bluetooth/speaker changes mid-call |
| `PerformStartCallAction` reports connected immediately | Driven by real `CallState` (§7) | Reporting connected while still dialing lies to the system UI and the call log |

The id derivation is a private helper unless a second caller appears; `Core`'s hashing
is the place for it if one does.

## 5. Audio-session ownership

The old branch's investigation doc raised four findings. Two of them are already
solved structurally on `dev` — by the PTT work, which faced the identical problem of
a system framework owning `AVAudioSession` activation.

`AudioSessionOwnership` (in `UI.Blazor/Services/`, deliberately outside the platform
projects so it is testable) already encodes the rule:

```
MayActivate(owner)  => owner == App          // the app must not SetActive
MayConfigure(owner, mode)                    // what the app may still reconfigure
```

Changes:

- Add `AudioSessionOwner.CallKit`. `MayActivate(CallKit)` is `false`.
  `MayConfigure(CallKit, mode)` mirrors the `PttPlayback` rule: raising to `Recording`
  is compatible with a live call, lowering to `Playback`/`Ambient` cuts the incoming
  voice out and is refused.
- Set `AVAudioSession.Mode` to `VoiceChat` (`VideoChat` for video) for the duration of
  a call, via an **iOS-local in-call flag on `AudioSession`** rather than a new
  `AudioFocusMode` member. `AudioFocusMode` is shared with Android, which has no
  meaning for a VoIP session mode; a new member would force a change there for nothing.
- Do not use `DefaultToSpeaker` + a forced speaker override for calls. `VoiceChat`
  implies receiver-first routing with an explicit speaker toggle, which is the
  phone-like behaviour; the existing speaker override exists for recording *outside*
  a call and must stay scoped to that.

Findings #1 (double activation) and #2 (mode never set) are closed by the above.
Finding #3 (no deactivate/route handlers) is closed in §4.2. Finding #4 (outgoing
bypasses CallKit) is closed in §7.

## 6. Bridging CallKit into `IncomingCallUI`

iOS implements the existing **`IIncomingCallsBridge`**. All of `dev`'s reconciliation
logic — ring pruning, dismissal, active-call recovery after an app kill, the reactive
`LiveSessionUI` source of truth — then keeps working untouched.

| Member | iOS |
|---|---|
| `ListActiveCallChatIds` | CallKit's active calls — covers "push landed while killed, user opened from the launcher" |
| `DismissCallNotification` | `CXProvider.ReportCall(...Ended)` |
| `StartRinging` / `StopRinging` | No-op — see below |
| `OnCallHandled`, `RevealCallScreen`, `MoveBehindLockScreen` | Documented no-ops; Android keyguard choreography with no iOS analogue (`OnCallHandled` returns `true`) |

**One member is added to the interface: `bool OwnsRinging => false`.**

CallKit rings by itself the moment the call is reported. Without this flag
`IncomingCallUI.StartRinging` would, on iOS, both play the web ringtone *and* call
`AudioFocusUI.YieldCommunicationMode` — double-ringing and fighting CallKit for the
audio session it owns. When `OwnsRinging` is true, `StartRinging`/`StopRinging` skip
both the web ringtone and the communication-mode yield. Android takes the default and
is unchanged.

Considered and rejected: splitting `IIncomingCallsBridge` into a narrow `ISystemCallUI`
plus an Android-only remainder. Cleaner in the abstract, but a larger edit to a mature,
carefully-commented file for no behavioural gain. Revisit if a third platform lands.

Direction of control:

- **Push → CallKit**, directly and synchronously. It cannot wait for a Blazor scope.
- **CallKit → app**: `PerformAnswerCallAction` / `PerformEndCallAction` reach
  `IncomingCallUI.Accept` / `Decline` via `AppServicesAccessor.DispatchToBlazor`.
- **Reactive state → CallKit**: when `LiveSessionUI` says the ring is over,
  `DismissCallNotification` ends the CallKit call.

## 7. Outgoing calls through CallKit

Hooked at `LiveSessionUI.StartCall` — the single choke point, so `ChatHeaderCallButton`,
`NotifyCallPanel` and any future caller are covered without touching each.

- `CXStartCallAction` on start.
- `ReportConnectingOutgoingCall` / `ReportConnectedOutgoingCall` / `ReportCallEnded`
  driven by the real `CallState` machine (`Dialing → Accepted | Declined | NoAnswer`),
  not fired eagerly.
- Registered through a default-no-op interface resolved in `BlazorUIAppModule`, the
  pattern `IFullScreenCallsAvailability` / `DefaultFullScreenCallsAvailability`
  already uses.

This is what puts caller and callee on one audio-session activation regime, closing
the old doc's finding #4.

## 8. Entitlements and scope

Dev plist only, this iteration:

- `aps-environment = development` in `Entitlements.dev.plist`.
- `voip` is **already** in `UIBackgroundModes` on `dev`, alongside `push-to-talk`.
- **CallKit needs no entitlement.** Unlike PTT there is nothing further to add to
  prod beyond `aps-environment` when the time comes.

Two things to verify on device rather than assume:

1. Whether `aps-environment` is genuinely absent today. FCM push works now, so the
   provisioning profile may already supply it; the old branch added it, which is
   evidence but not proof.
2. CallKit's mainland-China restriction. Apple has historically required CallKit be
   disabled for VoIP apps there. If it still holds, a regional gate is needed before
   prod — scoped out of this iteration but must not be discovered at review time.

## 9. Reuse

**Existing abstractions this builds on.** Nothing here needs a new framework:

`ApnsClient` / `IApnsClient` (+ `ApnsTestSink`, `ApnsClientTest`, `FakeHttpClientFactory`),
`NotificationsSettings.ApplePush*`, `DeviceType`, `DbDevice.SessionHash`,
`AudioSessionOwnership` / `AudioSessionOwner`, `AudioSession`, `AppleAudioFocusUI`,
`IncomingCallUI`, `IIncomingCallsBridge`, `LiveSessionUI`, `CallState` / `CallInvite`,
`CallNotification`, `NotificationsBackend_{NotifyCall,CancelCall,RegisterDevice}`,
`AppServicesAccessor.DispatchToBlazor`, `MauiNotifications`, and `IosPtt` as the
headless-static precedent.

**Placement of new components:**

| New | Placement | Rationale |
|---|---|---|
| `AudioSessionOwner.CallKit`, ownership rules | `UI.Blazor/Services/AudioSessionOwnership.cs` (existing, shared) | Already deliberately outside the platform projects so the transition rules are unit-testable |
| `IIncomingCallsBridge.OwnsRinging` | `UI.Blazor.App/Services/` (existing, shared) | Shared contract |
| `DeviceTypeExt.IsFcm` | `Api/Notifications/`, beside the enum | Shared; the whole point is that no caller can drift from the enum |
| `IApnsClient.SendCallRing` | Extends the existing client | A second APNs client would duplicate JWT + dead-token handling |
| `Device.SessionHash` | `Notifications.Contracts/Device.cs` | Shared contract |
| `IosCalls`, `IosVoipPushes` | `App.Maui/Platforms/iOS/Calls/` | Genuinely iOS-only. The `Calls/` subfolder matches the existing `Ptt/` and `Activities/` grouping |
| Call-id derivation | Private helper in `IosCalls`; promote to `ActualChat.Core` if a second caller appears | Not shared until it is |

## 10. Testing

**Off-device — the majority, and where the real invariants live:**

- `AudioSessionOwnership` transitions with the `CallKit` owner (pure, and the file
  already exists to be tested this way).
- Call-id derivation: stable across processes, distinct per `ConversationId`.
- The VoIP/FCM device split and `SessionHash` suppression, as a pure function.
- `DeviceTypeExt.IsFcm` — a regression test that a push-only device type never reaches
  an FCM batch.
- `SendCallRing` header and payload shape via `FakeHttpClientFactory`, mirroring the
  existing `SendPttWakeSendsCorrectRequest`.
- Ring and cancel flow end-to-end through `ApnsTestSink` in the integration host.

**On-device — unavoidable, needs a Mac and a dev-signed build:**

- App state × outcome: locked / unlocked / backgrounded / killed ×
  answer / decline / caller-cancel / ring timeout / answered-on-another-device.
- Audio routes: earpiece, speaker, Bluetooth, wired headset, and changes *mid-call*.
- Outgoing: connect, remote decline, no-answer, and the system call-log entry.
- Interaction with PTT: both frameworks claim the audio session, and
  `AudioSessionOwner` must arbitrate — a call arriving during a PTT session and vice
  versa is the highest-risk pair in this design.

## 11. Open risks

1. **PTT × CallKit audio-session contention** is the least-understood interaction and
   is only observable on device. `AudioSessionOwnership` is the arbitration point, and
   its owner-stuck watchdog currently has PTT-specific recovery
   (`SetOwnerWatchdogRecovery`) that a CallKit owner would need an analogue for.
2. **`aps-environment`** — verify before assuming it is the fix for anything.
3. **Mainland-China CallKit restriction** — verify before prod.
4. **App Review** (prod only, out of scope here): VoIP background mode plus CallKit
   draws 2.3.1/2.5.4 scrutiny. iOS PTT is dev-only today for exactly this reason.
