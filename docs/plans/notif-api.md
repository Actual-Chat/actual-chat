# Notifications redesign — `feat/notif-api`

**Status:** Phases 1–7 complete; client-side reconciliation (prune + create on web/Android, iOS prune-only) shipped — see §8 ·
**Branch:** `feat/notif-api` · **Started:** 2026-05-20

## 1. Background

The work started from a concrete bug: the **app-icon badge count never updates on
iOS** when the app is backgrounded or terminated. Root cause — on iOS the badge of
a non-foreground app can only be changed by the `aps.badge` field of the push
payload (or a Notification Service Extension); the server never sends a badge, and
there is no NSE. The in-app `AppIconBadgeUpdater` only runs while the process is
alive.

Fixing only the badge would require computing a per-recipient unread count at
FCM-send time — which the current "push on event" pipeline has no place for. So the
badge fix is folded into a broader redesign that also gives us a server-side,
queryable notification state.

### Current system (what we move away from)

Imperative pipeline: a chat event handler computes recipients, renders text and
calls `FirebaseMessagingClient.SendMessage` synchronously. Per-notification rows in
`DbNotification`. Per-`(user, chat)` `NotificationFlow` re-checks online users
after a delay. No server-side notion of "what is currently on the device" and no
count.

## 2. Goals & non-goals

**Goals**
- Server-side, per-user **desired notification state** — the set that *should* be
  on the device — with a precise count.
- Fix the iOS badge: every push carries `aps.badge`.
- A queryable `INotifications.ListActive` API for web / Windows / native-less
  platforms and for client-side reconciliation.
- Truly distributed: sharded by `UserId`, no operations-framework invalidation for
  the real-time path (model: `UserPresencesBackend`, live-audio/video backends).
- Coalesced, throttled, fair: noisy chats and non-readers cost ~nothing.

**Non-goals (v1)**
- Full notification-removal logic beyond clear-on-read.
- Reworking device registration (`DbDevice`) or `ExplicitNotification`.

## 3. Design

### 3.1 Reconciliation model

Move from "push on event" to **desired-state reconciliation**. The server keeps,
per user, the set of notifications that should be on the device; chat events become
cheap *submissions*; reconciliation diffs desired vs. delivered and sends the delta.
Idempotent — a missed or duplicated run converges on the next pass.

### 3.2 Components

- **`NotificationsBackend`** — `ShardedDbServiceBase<NotificationDbContext>`,
  sharded by `UserId` (`ShardScheme.NotificationBackend`). State owner + brain.
  Holds the per-user state primed in memory on the owning node; DB blob for
  durability. Exposes `GetUserNotificationInfo(UserId)` (compute method,
  in-process invalidation — no op-log).
- **`INotifications`** — thin client API. `ListActive(Session)` projects the
  displayed set; calling it doubles as a dormancy-clearing engagement signal.
- **`FirebaseMessagingClient`** — gains an `Aps.Badge` on every push and a
  silent-push path for dismissals; hidden behind an interface so tests get a sink.
- **Hint emitters** — the chat/reaction event handlers render a candidate once,
  fan out to subscribers and call `OnNotify` per subscriber.

### 3.3 Data model

`Notification` — union with an abstract-record base (MessagePack `[Union]`).
These types are **MessagePack-only — no MemoryPack**. The hierarchy:

- `Notification` (abstract) — carries only the identity/dedup key
  (`NotificationId`, which encodes `UserId`/`Kind`/`SimilarityKey`), `Title`, `Text`,
  `CreatedAt`. It makes **no** assumption that a notification is chat-related.
- `ChatNotification` (abstract) — for chat-related notifications: adds `AuthorId`.
  `ChatId` is **derived** (computed), not stored: `ChatId.Parse(SimilarityKey)` for
  chat-keyed notifications. Two sub-bases refine this:
  - `ChatEntryRelatedNotification` — keyed by chat, **stores `EntryLid`** (the entry
    the notification points at and the read-detection anchor); `EntryId` is computed
    from `ChatId`+`EntryLid`. Used by `Message`/`Reply`/`Thread`.
  - `ChatEntryNotification` — keyed by the entry (`SimilarityKey` is a `ChatEntryId`),
    so `ChatId`/`EntryLid` are derived from `EntryId`. Used by `Mention`/`Reaction`/
    `Attention`.
- One concrete record **per `NotificationKind`** — `MessageNotification`,
  `ReplyNotification`, `InvitationNotification`, `MentionNotification`,
  `ReactionNotification`, `AttentionNotification`, `ThreadNotification` — union tags
  match the `NotificationKind` enum values.

Minimum data stored; richer data (icon, author, reaction emoji) pulled at send time.
`Text` *is* stored (tiny).

`UserNotificationInfo` — one small blob per user: `Displayed: ApiArray<Notification>`
(converged set, one item per `SimilarityKey`), `UnsentDelta: NotificationDelta`
(`Upserts` + `DismissedIds`), `LastPushAt`, `IsDormant`, `Version`.

`DbUserNotifications` — one row per user: `{ UserId PK, Version, Data blob,
HasUnsentDelta, IsDormant }`. `Data` = `VersionedByteSerializer`-encoded committed
`UserNotificationInfo`. No Redis.

### 3.4 Hard vs. soft updates

| | Hard | Soft |
|---|---|---|
| When | first notif for a key / urgent / outside silence period | similar, low-urgency notif during the silence period |
| Committed state | mutated | untouched |
| DB | written | not written |
| Compute method | invalidated + re-primed | untouched |
| Push | sent (sets `LastPushAt`) | deferred tick scheduled once |
| Crash | durable | lost — accepted |

Soft updates accumulate in an **in-memory soft buffer** (a sibling per-`UserId` map,
TTL ≥ throttle window) holding lightweight "what to re-check" descriptors plus an
`IsProcessScheduled` flag. The deferred tick drains the buffer and does one batched
hard update. A busy chat → ~1 DB write + 1 push per ~10 s window.

### 3.5 Distribution & scheduling

DB writes use the `UserPresencesBackend` pattern: `DbHub.CreateOperationDbContext()`
+ `context.Operation.MustStore(false)` (no `DbOperation` row) + a completion handler
that invalidates the compute method via `Invalidation.Begin()`.

**No durable event scheduling.** Soft-tick and hard-push timers are plain in-memory
delays on the owning shard — consistent with soft buffers dying on crash, and a
committed-but-unpushed `UnsentDelta` is recovered by the population re-check on the
next `Notify`/`ListActive`.

### 3.6 Dormancy

`IsDormant` per user. Dormant ⇒ `OnNotify` returns immediately (zero work, zero DB
writes, no push). Set when `Displayed` crosses a threshold with no engagement;
cleared by any engagement signal (`ListActive`, `Handle`, read), which triggers a
from-source-of-truth recompute. This is the hard form of throttling for non-readers.

### 3.7 iOS badge

Every push carries `aps.badge` = unmuted `Displayed` count, computed by the push
handler. Dismissals (clear-on-read) lower the count via a silent push.

**Client backstop (foreground re-assert).** The `aps.badge` on the silent
dismissal push is the *only* thing that clears a backgrounded iOS app's badge —
and iOS throttles/drops background (`content-available`) pushes, so a badge set
by an earlier alert push can linger after the chat is read on another device.
`AppIconBadgeUpdater` therefore watches **two** signals: the `ListActive` count
(as before) *and* a background→foreground transition (`BackgroundStateTracker`).
On resume it re-asserts `SetBadgeCount(ListActive.Count)` **unconditionally** —
bypassing the `lastCount` de-dupe, because the OS badge may have drifted
out-of-band while `ListActive` itself was unchanged. This makes the badge
client-authoritative on every resume rather than dependent on a deliverable
silent push.

### 3.8 Reconciliation layers

1. **Population re-check** — `GetUserNotificationInfo` re-checks each item's read
   state against `IChatPositionsBackend`; read items move to `DismissedIds` and a
   silent push is scheduled. Once populated, the set is authoritative.
2. **Client-side** — the running app queries `ListActive` and reconciles device
   notifications (e.g. ensures every unread chat has one).

### 3.9 Redelivery idempotency

The queue is at-least-once, so `OnNotify` can be redelivered and events can
arrive out of order. `Reconcile` decides whether a merge changed anything by
**reference equality** (`ReferenceEquals(before, after)`): a no-op merge must
return the *existing* instance so no duplicate push (and, for ringers, no
re-ring) is emitted. This makes `Notification.MergeWith` idempotency load-bearing:

- **Coalescing kinds** (`ChatEntryRelatedNotification`) return the existing
  instance when the incoming entry is already inside the merged window.
- **Individually-seen kinds** (mention / reaction / attention / call) don't
  coalesce, so the **base** `Notification.MergeWith` returns the existing instance
  whenever the incoming event is *not newer* (`SentAt <= existing.SentAt`) — a
  redelivery (equal `SentAt`) or an out-of-order older event. A strictly newer
  same-key event still updates in place.

## 4. Implementation plan

- **Phase 1 — Contracts & data model.** `Notification` union, `UserNotificationInfo`
  / `NotificationDelta`, extend `INotifications`/`INotificationsBackend`,
  `DbUserNotifications` + migration.
- **Phase 2 — `NotificationsBackend` core.** ✅ Done. Rebased on `ShardedDbServiceBase`;
  `GetUserNotificationInfo` (reads the `DbUserNotifications` blob), `OnNotify` (hard/soft),
  `OnProcess` (deferred coalescing tick), in-memory soft buffer, dormancy set+honor.
  `OnNotify` was rewritten in place, so the existing fan-out feeds the new path right away
  (Phase 4 only makes that fan-out cheap). The population re-check (read-state
  reconciliation) is deferred to Phase 5; `GetUserNotificationInfo` is just a blob read
  for now. Dormancy *clear* is Phase 5. Legacy `DbNotification` / `OnUpsert` / `Get` /
  `ListRecentNotificationIds` still coexist — retired in Phase 7.
- **Phase 3 — FCM.** ✅ Done. `IFirebaseMessagingClient` interface; `FirebaseMessagingClient`
  implements it. Every `SendMessage` carries `aps.badge` = `Displayed.Count` (the iOS
  badge fix). Added `SendDismissal` — a silent background push (`content-available` +
  `aps.badge`, `dismissedIds` data key); its caller is wired in Phase 5.
  `FirebaseMessagingTestSink` (in `Testing.Host`) replaces the client in **every** test
  host via `TestAppHostFactory`, so tests never hit Firebase and can assert on sends.
  Phase 2 follow-up done here: `DbUserNotifications` got `ConflictStrategy.DoNothing` +
  `ApplyHardUpdate` re-reads under lock on a lost create race (no notification dropped).
- **Phase 4 — Fan-out rewrite.** ✅ Done. Event handlers fan out straight to `OnNotify`:
  removed the presence check and the `NotificationFlow`-based deferral of online users
  (reconciliation + soft/silence coalescing replace it), and removed the redundant
  `_recentChatsWithNotifications` chat-level throttle. Deleted `NotificationFlow`, its
  module registration, `NotificationFlowTest`, `Flows/SerializationTests`, and the now-dead
  `Constants.Notification.OnlineCheckDelay`. `EnqueueMessageRelatedNotifications` still
  renders `Title`/`Text`/`IconUrl` once and enqueues one `NotificationsBackend_Notify`
  per subscriber.
- **Phase 5 — Read / dismiss.** ✅ Done. `GetUserNotificationInfo` now does the
  population re-check: it calls `IChatPositionsBackend.Get` per displayed item and hides
  ones whose entry the user has read — and because that's a compute-method dependency,
  Fusion re-invalidates it whenever a read position advances. `ApplyHardUpdate` is now the
  one DB-write+push path for both adds and removals: it reconciles (adds new, drops
  read/handled), pushes the new notification via `Send`, and pushes a silent badge update
  via `SendDismissal` for the dropped ones. `OnHandle` was rewritten as a backend command
  (`NotificationsBackend_Handle`) that dismisses one notification. Dormancy is re-derived
  from the effective count (no latch) — reading clears it. There is no eager read-position
  trigger; reconciliation converges on the next `OnNotify`/`OnProcess`/`OnHandle` (and
  Phase 6's `ListActive`). Verified by `NotificationReadReconciliationTest`.
- **Phase 6 — Client.** ✅ Done (core). `INotifications.ListActive(Session)` projects
  `GetUserNotificationInfo(...).Displayed` (no per-id `Get` loop; `MinCacheDuration = 30`
  keeps it alive ≥ the throttle window — resolves open detail #2). `NotificationStack`
  reads `ListActive` instead of `ListRecentNotificationIds` + `Get`. Badge has a single
  source of truth: `AppIconBadgeUpdater` watches the `ListActive` count so the foreground
  badge matches the server push `aps.badge`, and the iOS receive handler no longer overrides
  the badge with the unread-chat count. Silent dismissal is handled on all three platforms:
  the server carries the dropped notifications' **tags** (chatId) in the silent push
  (`SendDismissal` + `DismissedTags`; iOS matches by `ThreadIdentifier`), and web /
  Android / iOS drop the matching delivered notification by tag.
  **Client-side reconciliation (`NotificationReconciler`)** — see §8.
- **Phase 7 — Tests, cleanup, docs.** ✅ Done. `NotificationContentTest` and the
  `WaitForChatEntryNotification` helper (used by `Retranscribe*NotifyFlowTest`) now read
  `GetUserNotificationInfo().Displayed` (open detail #6). The legacy `DbNotification` path
  is deleted — `Get` / `ListRecentNotificationIds` / `PseudoListRecentNotificationIds` /
  `OnUpsert`, the `DbNotification` entity + `DbSet` + resolver registration, and the
  `notifications` table (migration `DropNotificationsTable`); `OnRemoveAccount` now clears
  the `user_notifications` blob. `_softBuffers` is now evicted on drain (open detail #4).
  `OnUpsertExplicitNotification` / `GetExplicit` / `DbExplicitNotification` are kept (non-goal).
  Verified: full `Notifications.IntegrationTests` suite green.

Tests rely on the FCM test sink (Phase 3) — `IFirebaseMessagingClient` is replaced
with a logging + callback sink in integration tests so sends are observable without
hitting Firebase.

## 8. Client-side reconciliation (`NotificationReconciler`)

The push transport is best-effort (FCM/APNs can drop; the silent dismissal push is
throttled). `NotificationReconciler` (a shared `UIWorkerBase` in `UI.Blazor.App`,
started like `AppIconBadgeUpdater`) is the client backstop that keeps the device's OS
notifications in sync with the server's `ListActive`. It reacts to two signals — the
`ListActive` compute changing, and a foreground resume (`BackgroundStateTracker`) — and
calls a per-platform `IDeviceNotifications`:

- **Prune** (all platforms): remove any shown notification whose tag (= chatId) is no
  longer active. Heals a lost silent-dismissal push or a read on another device. Idempotent.
- **Create** (web + Android): re-show a notification that **newly entered** the active set
  but isn't on the device. Heals a dropped delivery push. The reconciler tracks the previous
  active-tag set and passes only **newly-added** tags as create candidates, so dismissing a
  banner (which doesn't change the active set) never resurrects it — no per-platform
  dismissal tracking needed. Web additionally suppresses create while a tab is visible (the
  in-app UI covers it). Tag content (title/text/icon/absolute deep-link via
  `NotificationExt.GetChatLink` + `UrlMapper`) rides in `ActiveNotificationInfo`.

`GetChatTag` was lifted to `NotificationExt` so the FCM send path and the client reconciler
derive the device tag identically.

### iOS create-missing / NSE (deferred decision)

iOS is **prune-only** — it ignores `createTags`. Reason: iOS gives no user-dismissal
callback and renders alert pushes without running our code, so we can't distinguish a
*dropped* push from a banner the user *swiped away*; create-missing would resurrect
dismissed notifications. The clean fix is a **Notification Service Extension** that records
delivered tags into an **App Group**, giving the "delivered on this device" signal iOS
withholds — then iOS create-missing becomes `active − delivered − shown`, like web/Android.
An NSE also unlocks rich (image) and E2E-encrypted notification content. We deferred it:
the standalone payoff (a missed banner reappearing on reopen) is modest versus adding an
NSE target + App Group. **Revisit when we want rich or E2E-encrypted iOS notifications** —
fold create-missing in then. (A silent-dismissal NSE does *not* help: the NSE only runs for
alert pushes, not the `content-available` dismissal push.)

Not buildable/testable in Docker: the MAUI iOS/Android `IDeviceNotifications` impls and the
web service-worker create path (no browser) — verified by build only.

## 5. Reuse

**Reused:** `ShardedDbServiceBase`, `ShardScheme.NotificationBackend`,
`DbHub.CreateOperationDbContext` / `Operation.MustStore(false)` / `Invalidation.Begin()`
(the `UserPresencesBackend` pattern); `IChatPositionsBackend`, `IUserPresences`,
`ListSubscribedUserIds`, `FilterByNotificationMode` / `FilterByFollowThreadStatus`,
`NotificationHelper`, `MentionExtractor`, `NotificationId` / `SimilarityKey`,
`FirebaseMessagingClient`, `VersionedByteSerializer`, MessagePack `[Union]` (per
`StoredSettings`), `ApiArray`, `DbDevice` / `DbExplicitNotification` (untouched).

**Removed:** `NotificationFlow`.

**New components** are all notification-specific → `Api/Notifications/` and
`Notifications.*`. No new shared abstractions in `Core` / `Core.Server`.

## 6. Open details (resolved as we build)

1. Cleanest shard-ownership-gain hook for the optional warm-up scan — v1 relies on
   the next `Notify`/`ListActive` instead. **Still v1-deferred.**
2. ✅ **Resolved.** `GetUserNotificationInfo` is kept alive ≥ the throttle window via the
   client `ListActive` wrapper's `[ComputeMethod(MinCacheDuration = 30)]` (mirrors
   `IUserPresences.Get`).
3. ✅ **Resolved.** The only web consumer was `NotificationStack`, now migrated to
   `ListActive`; the legacy `Get` / `ListRecentNotificationIds` were removed in Phase 7.
4. ✅ **Resolved.** `_softBuffers` is evicted on drain (`DrainSoftBuffer` + `IsRemoved`
   tombstone for the enqueue/drain race); the map is bounded to users with in-flight
   soft updates.
5. `UnsentDelta` is still unused: Phase 5 sends dismissal pushes eagerly inside
   `ApplyHardUpdate` (consistent with how new-notification pushes work). The field
   remains available for a future crash-recovery refinement (committed-but-unpushed).
   **Intentional, unchanged.**
6. ✅ **Resolved.** `NotificationContentTest` and the `WaitForChatEntryNotification`
   helper now read `GetUserNotificationInfo().Displayed`.
