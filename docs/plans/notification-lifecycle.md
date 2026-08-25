---
title: Notification lifecycle
description: Expiry, reliable dismissal, and view-based clearing for the per-user notification set.
---

# Notification lifecycle: expiry, reliable dismissal, view-based clearing

## Goal

Make every notification that reaches `DbUserNotifications.Data` reach a terminal
state, and make its dismissal as reliable as its delivery. Today a notification
can be stranded in the active set forever, and a notification hidden by the
compute-time filter can stay on the device forever — the two failure modes are
independent and both are silent.

## Why now

A user-reported "Windows taskbar badge stuck at 1" was traced to a single
`CallNotification` from 2026-08-19, still in the active set 5½ days later. The
badge counted it correctly; nothing could ever clear it.

Confirmed against production via `App.ConsoleClient -notifications`:

```
IncomingCall | CallNotification | hjp639qb6bp1 9:p-hjp639qb6bp1-jo4vst32iijq:3009
{ "HasVideo": false, "SentAt": "2026-08-19T08:09:08Z", "HandledAt": null, "Version": 0 }
```

`GetReadAnchor` returns `(null, 0)` for a `CallNotification`, so `IsRead` can
never clear it; `IsSuppressedByMode` exempts ringers, so that can't either. The
only remaining exit is `NotificationsBackend_CancelCall`, which
`LiveSessionsBackend` itself documents as missable ("a ring can vanish via its
RingTtl before this catches it"). Both existing filters are, by design, blind to
exactly this shape.

The same investigation found that a reaction is dropped before delivery whenever
the recipient's Read position already covers their own message — which is the
normal state of any chat they actually read — and that the client-side badge
worker can die silently and permanently.

## Reuse

### Existing abstractions to reuse

- **`PeriodicFlow` + `FlowHub.NewResumeEvent<T>(key)`** (`Core.Server/Flows`) —
  the cleanup and dismissal-drain flows are the same shape as
  `Notifications.Flows.MentionReminderFlow`: one flow per user, keyed by user id,
  resumed by an event, returning `Moment.MaxValue` to park.
- **The transactional outbox** — `context.Operation.AddEvent(...)`, already used
  by `ApplyHardUpdate` for `NotificationsBackend_Push` /
  `NotificationsBackend_PushDismissal`. No new delivery mechanism is needed.
- **`IQueues.Enqueue` + `SetDelayBy` bucketing** — the collapse pattern from
  `ChatPositionsBackend.OnSet`'s `ReadPositionChangedEvent`.
- **`AsyncChain.From(...).Log(...).RetryForever(...).CycleForever().RunIsolated(ct)`**
  — the resilient-loop idiom already used by `ActivatedWorkerBase` and
  `KeepWebViewAliveUI`. The notification workers get it verbatim.
- **`ChatViewItemVisibility.VisibleTextEntryIds`** — the "these entries were on
  screen" signal already maintained by `ChatUI`; placeholders excluded. No new
  visibility tracking.
- **`Notifications_Dismiss`** (renamed from `Notifications_Handle`) — the chat
  view knows the notification id from `ListActive`, so view-clearing needs no new
  command.
- **`NotificationHelper`** — the existing home for per-kind policy
  (`GetImportance`, `IsDeliverable`); expiry and dismiss-mode policy join it or
  sit beside it as virtual members on `Notification`.
- **`SystemJsonSerializer.Pretty`** — already used by `/test/notifications` and
  `App.ConsoleClient -notifications`; both stay the diagnostics surface.

No existing abstraction covers "a persisted set of pending dismissals", so that
one is new.

### Reusability of new components

- **`Computed.InvalidateSafely(delay[, maxDelay])` extension** — clamped
  self-invalidation. There are four hand-rolled variants today
  (`Invites`, `InvitesBackend`, `LiveAudioBackend`, `LiveTime`), one of them
  unclamped. **Recommended placement: `ActualChat.Core`** (next to the other
  Fusion helpers), not `Notifications.Service` — it is not a notifications
  concern and it has at least four existing callers.
- **`NotificationDismissMode` enum + `Notification.DismissMode` / `ExpiresAt`
  virtual properties** — feature-specific by nature; they belong in
  `ActualChat.Api/Notifications` next to the union they describe. Not shared.
- **`PendingDismissal` record** — lives inside `UserNotificationInfo`, same
  assembly. Not shared.

## The model

Three orthogonal per-kind properties on `Notification`, all computed (no stored
fields, no migration — the blob is MessagePack and these carry the standard
`[JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]` set):

```csharp
public enum NotificationDismissMode {
    Explicit = 0,  // only an explicit dismissal or expiry - the safe default
    OnRead,        // read position passes its entry
    OnView,        // its entry was actually on screen
}

// Notification
public virtual NotificationDismissMode DismissMode => NotificationDismissMode.Explicit;
public virtual Moment? ExpiresAt => null;
```

| Kind | DismissMode | ExpiresAt |
|---|---|---|
| Message, Reply, Thread, Mention, Conversation | `OnRead` | none |
| Reaction | `OnView` | `SentAt + 1 day` |
| IncomingCall | `Explicit` | `SentAt + RingTimeout + 5s` |
| Attention | `OnRead` | none |

`IsRead` is gated on `DismissMode == OnRead`. That single gate is what unbreaks
reactions: a `ReactionNotification` anchors at the reacted-to entry — the
recipient's own message — so once their Read position covers it, `Reconcile`
drops the notification at commit and it never reaches a device.

The precondition is "their Read position covers their own entry", not "always":
`Chats.OnUpsertTextEntry` advances it on send only for an author who is already
caught up (`Chats.cs:552`), so in a chat with no read position yet — which is
what every pre-existing test sets up — reactions did get through. That's why the
suite never caught it. `NotificationDismissModeTest` sets the read position
explicitly and fails without the gate.

`OnRead` is **opt-in**, declared on exactly the three types `GetReadAnchor` can
resolve — `ChatEntryRelatedNotification`, `ChatEntryNotification` and
`ConversationNotification` — with `ReactionNotification` overriding to `OnView`.
Anchorless kinds fall through to `Explicit`. That way a kind that declares
nothing can't be filtered by a read position that doesn't apply to it: the
failure mode is a notification that lingers (visible, and expiry-governed) rather
than one silently dropped before delivery, which is the bug this whole plan came
from.

`GetReadAnchor` goes back to answering only "which chat/entry is this about". It
stops doubling as "what clears it", which is how the call ring slipped past both
existing filters.

### Storage

`UserNotificationInfo.Displayed` → **`Items`** (the old name was never true —
nothing rendered it, and the compute filter hides entries still present in it).
Wire-safe: `[DataMember(Order = 2), Key(2)]`, MessagePack keys by ordinal.

New sibling in the same blob:

```csharp
[DataMember(Order = 6), Key(6)]
public ApiArray<PendingDismissal> PendingDismissals { get; init; }

public sealed partial record PendingDismissal(NotificationId Id, string Tag, Moment QueuedAt);
```

Same blob, same row, same transaction as the removal from `Items` — that
atomicity is the whole point, and the reason this is not a separate table.

The `Tag` must be captured at removal time: `GetPushTag()` derives from the
notification instance, which no longer exists once it leaves `Items`, and
`SendDismissal` needs both `DismissedIds` and `DismissedTags`
(`FirebaseMessagingClient.cs:171-181`). The `bannersToClose` survivorship
decision (`NotificationsBackend.cs:1487`) is likewise resolved at removal time
and recorded, not recomputed later.

`PendingDismissals` is excluded from `ListActive` and from the `IsDormant` count.
Bounds: **TTL 1 day** (matches the FCM `TimeToLive` already set on the dismissal
push — beyond that it cannot be delivered anyway) and **cap 256, oldest dropped
first**. The client-side `NotificationReconciler` is the backstop for anything
that falls off: it prunes whatever is missing from `ListActive` on every
active-set change and on foreground resume.

### The two removal paths

Today there are two, and only one dismisses:

- `ApplyHardUpdate` — commits the removal and emits a dismissal via the outbox.
  Reliable.
- `GetUserNotificationInfo`'s filter — hides entries with **no commit and no
  dismissal** (`NotificationsBackend.cs:117-124`). A notification can be absent
  from `ListActive`, still in the blob, and still in the device tray, with
  nothing scheduled to close it.

After this work, every filter hit — `IsRead`, `IsSuppressedByMode`, `IsExpired` —
enqueues a resume of the cleanup flow, which commits the removal and appends to
`PendingDismissals`. The read path stays cheap because the flow's `DelayQuanta`
bounds the work, the resume is idempotent per flow id, and a recompute only
re-triggers while the flow still has something to do.

## Work

One branch — `feat/notification-fixes` — reviewed and merged as a whole. The
steps below are one commit each, in roughly dependency order; each is meant to
stand on its own at review time, so they're amended or squashed rather than
followed by fix-ups.

### 1 — diagnostics (done)

`/test/notifications`, `App.ConsoleClient -notifications`, removal of the never-
rendered `NotificationStack`/`NotificationEntry`, JSON-write coverage for all
union subtypes. Extend with `DormancyThreshold` 64 → 100 and the dead
`similarityKey` parameter on `EnqueueMessageRelatedNotifications`.

### 2 — clamped self-invalidation (done)

`Computed.InvalidateSafely(delay)` (30 min cap) plus an explicit-`maxDelay`
overload, in `ActualChat.Core`,
replacing the four local variants. A pending invalidation pins the `Computed<T>`
until it fires, so a far-future delay leaks memory proportional to the method's
parameter cardinality; re-arming early is free, since the method recomputes and
schedules the next one. Clamping changes only how often a method recomputes,
never its result.

Audit result:

| Site | Bound | |
|---|---|---|
| `Invites.cs:257` | clamped [1s, 10min] | ok |
| **`InvitesBackend.cs:265-269`** | **unclamped `expiresOn - now`** | **fix** |
| `LiveAudioBackend.cs:163` | clamped to `StreamTtl` | ok |
| `ChatsBackend.ContentItems.cs:151` | `Min(MaxYearRecheckPeriod, …)` | ok |
| `LiveTime.cs:33` | `TrimInvalidationDelay` | kept local |
| `LocationUI.cs:122` | `Min(RemainingTextUpdatePeriod, …)` | kept local |
| `ChatUI.Tiles.cs:1645` | bounded by `StreamingEntryGap` | ok |

`InvitesBackend` is the live instance of the pathology: invite lifetimes run to
days and `GetInviteChatLinkPreview` is keyed `(accountId, inviteId)`. Its sibling
in the same feature is clamped, and carries the rationale in a comment.

`ChatUI.Tiles.cs:1645` turned out to be bounded after all — the loop only reaches
the invalidation for entries still inside `StreamingEntryGap`, so the delay can't
exceed it. `LiveTime` and `LocationUI` keep their local clamps: both pass
`usePreciseTimer: false` and need sub-second delays, which the helper's 1s floor
would round up.

### 3 — `Handle` → `Dismiss` rename (done)

Mechanical, no behavior change. "Dismiss" is already the vocabulary downstream:
`Reconcile` builds a `dismissed` list, the push is `SendDismissal`, the payload
keys are `DismissedIds`/`DismissedTags`.

| Now | After |
|---|---|
| `Notifications_Handle` / `_HandleAll` | `Notifications_Dismiss` / `_DismissAll` |
| `INotifications.OnHandle` / `OnHandleAll` | `OnDismiss` / `OnDismissAll` |
| `NotificationsBackend_Handle` / `_HandleAll` | `NotificationsBackend_Dismiss` / `_DismissAll` |
| `INotificationsBackend.OnHandle` / `OnHandleAll` | `OnDismiss` / `OnDismissAll` |
| `ApplyHardUpdate(… handledIds, handleAll …)` | `dismissedIds`, `dismissAll` |
| `UserNotificationInfo.Displayed` | `Items` (62 usages) |

No deprecated aliases: nothing has ever sent `Notifications_Handle` from a client
(`NotificationStack` was its only caller and was never rendered), and a queued
`NotificationsBackend_Handle` in flight across a deploy is accepted as a
negligible risk.

`Notification.HandledAt` is **deleted** rather than renamed: every assignment in
`src/` sets it to `null` (`Notification.cs:66`, `ChatEntryRelatedNotification.cs:91`)
— dismissal removes the entry rather than stamping it — so `IsActive => HandledAt
== null` is a constant `true`. `PendingDismissals` covers the tombstone role.

Also in this PR: correct the "single source of truth" enumerations
(`NotificationsBackend.cs:109`, `docs/notifications.md:57`) to drop the in-app
list and add the sentence distinguishing the two concepts — unread counts on
chats and places drive the navbar and the bell panel and are deliberately a
different calculation; `ListActive` drives the app-icon badge and the OS-level
surfaces. Two concepts, one source of truth each.

### 4 — expiry, dismiss modes, and the cleanup flow (done)

1. `NotificationDismissMode` + `DismissMode` / `ExpiresAt` virtual properties;
   `IsRead` gated on `DismissMode == OnRead`.
2. Hoist `RingTimeout` to a shared constant. `LiveSessionsBackend.cs:31` says
   20s; `IncomingCallNotifications.cs:19` claims to mirror it and says **40s**.
   One constant, both sides.
3. `IsExpired` joins the `GetUserNotificationInfo` filter. The predicate has **no
   importance term** — ringers are the most expirable kind, not the least, and
   the instinct to special-case them (as `IsDeliverable` does) is exactly what
   would re-create the bug.
4. Clamped self-invalidation on the nearest `ExpiresAt`, via the step 2 helper.
5. Every filter hit enqueues a resume of `NotificationConvergeFlow`. The read
   **fails** unless the enqueue succeeds, so a retried read retries the trigger.
6. `NotificationConvergeFlow` (`DelayQuanta = 3`, one per user): commits removals
   from `Items`, appends `PendingDismissals`, parks at `Moment.MaxValue` when
   there is nothing left.
7. Fix `Version` / `CreatedAt` stamping — the observed stranded ring has
   `Version: 0` and an epoch `CreatedAt`, so some write path skips them. Trace it
   before expiry keys off timestamps.

### 5 — reliable dismissal drain (done)

The drain half of `PendingDismissals`: a flow (or a second phase of the cleanup
flow) that sends the dismissal push and removes the entry only after a
successful send pass. Retry with backoff (3s → ×2 → 5 min ceiling) until TTL.
Per-entry acking, not per-device.

Closes the gap where `OnPushDismissal` consumes its queue message and then fails
in `SendDismissal` — the entry is already out of `Items` at that point, so
nothing can re-derive it.

As built, cleanup and the send are **one command**, `NotificationsBackend_Converge`:
it runs `ApplyHardUpdate` to commit the removals, then sends whatever is pending
from outside the row lock. Splitting them gave the flow two calls that raced the
outbox event, and there was no caller that wanted cleanup without delivery —
which is the invariant, not an option. It's idempotent (a removed notification
can't come back), so the event `ApplyHardUpdate` emits and the flow's retry can
both run it without coordinating; the second pass dismisses nothing, emits no
event, and terminates.

The command carries no payload: `OnConverge` reads what to send from
`PendingDismissals`. Clearing entries after a successful send is its own command
(`NotificationsBackend_ClearDismissals`) so the send stays outside the lock.
`SendDismissal` takes `IReadOnlyCollection<PendingDismissal>` rather than
notifications — by the time it runs, the notifications are gone.

### 6 — client resilience and diagnosability (done)

1. `AppIconBadgeUpdater` and `NotificationReconciler` wrap their loops in
   `AsyncChain…RetryForever…CycleForever`. `lastCount` is a local, so a restart
   resets it and the first emission re-asserts unconditionally — the behavior we
   want after a failure. `NotificationReconciler`'s `_lastActiveTags` /
   `_isInitialized` are fields and need an explicit reset on restart, or the
   create-suppression baseline replays old tags as new.
2. `UIWorkerBase.Run`'s bare `catch { }` (`UIWorkerBase.cs:34-37`) logs instead
   of swallowing. Shared by **33 workers**, any of which can die invisibly today.
   Control flow unchanged — still no restart; per-worker retry stays each
   worker's business.
3. `AppIconBadgeUpdater` added to `LoggingExt.NotificationLogCategories`, or the
   new lines are dropped in Release.
4. `WindowsAppIconBadge` gets a logger; today it catches `COMException`, latches
   `static volatile _isUnsupported` for the process, and says nothing.
5. **Serilog rolling on MAUI** (`MauiDiagnostics.cs:94`): `WriteTo.File` uses
   `fileSizeLimitBytes: 10_000_000` with no `rollOnFileSizeLimit`. The Windows
   log has been at exactly 10,000,003 bytes and silent since **2024-05-14**.
   This is a prerequisite for any of the above being observable in the field.

On Windows this matters more than elsewhere: `MauiProgram.Windows.cs` registers
no `IDeviceNotifications` (`BlazorUIAppModule.cs:210` gates `WebDeviceNotifications`
to `IsServerOrWasmApp()`) and `WindowsDeviceTokenRetriever` returns `null`, so
there are no toasts and no pushes. The app-icon badge is the *entire* notification
surface, and `UpdateOnActiveChanges` is its only driver.

### 7 — reaction view-clearing (client)

The chat view intersects `ChatViewItemVisibility.VisibleTextEntryIds` against
`ListActive`, and sends `Notifications_Dismiss` for any `DismissMode == OnView`
notification whose entry is on screen. No interim fallback ships — reactions go
straight to view-clearing.

## Decisions taken

- Reactions clear by *view*, not by read position: read position is set at send
  time for the recipient's own message, so no lid-based scheme can work.
- The bell panel stays unread-chats. That divergence is deliberate and is not a
  bug; the notification set gets its own clearing rules instead.
- `NotificationKind.Invitation` stays as-is, unreached, to be implemented later.
- Dormancy's early return in `OnNotify` is correct behavior; only the threshold
  moves (64 → 100).
- No backstop expiry for `Message`/`Reply`/`Thread`/`Mention`/`Conversation` —
  read-clearing is sufficient for them.
- The stranded production ring is not cleared manually; the next deploy's expiry
  handles it.

## Not in scope

- A user-reachable "dismiss all" affordance. `Notifications_DismissAll` still has
  no caller; expiry plus view-clearing may make one unnecessary. Revisit after
  step 4 lands.
- Anything about how unread chat/place counts are computed.
