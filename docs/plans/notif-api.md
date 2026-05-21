# Notifications redesign — `feat/notif-api`

**Status:** in progress · **Branch:** `feat/notif-api` · **Started:** 2026-05-20

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

`NotificationItem` — union with an abstract-record base (MessagePack `[Union]`).
These types are **MessagePack-only — no MemoryPack**. The base carries the
identity/dedup key (`NotificationId` → `SimilarityKey`), `ChatId`, `Title`, `Text`,
`CreatedAt`, and the abstract **`ReadEntryLid`** — the read-detection anchor.
Members: `ChatNotificationItem` (`EntryLid`, `AuthorId`),
`AttentionNotificationItem` (`CallerId`, `LastEntryLid`). Minimum data stored;
richer data (icon, reaction emoji) pulled at send time. `Text` *is* stored (tiny).

`UserNotificationInfo` — one small blob per user: `Displayed: ApiArray<NotificationItem>`
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

### 3.8 Reconciliation layers

1. **Population re-check** — `GetUserNotificationInfo` re-checks each item's read
   state against `IChatPositionsBackend`; read items move to `DismissedIds` and a
   silent push is scheduled. Once populated, the set is authoritative.
2. **Client-side** — the running app queries `ListActive` and reconciles device
   notifications (e.g. ensures every unread chat has one).

## 4. Implementation plan

- **Phase 1 — Contracts & data model.** `NotificationItem` union, `UserNotificationInfo`
  / `NotificationDelta`, extend `INotifications`/`INotificationsBackend`,
  `DbUserNotifications` + migration.
- **Phase 2 — `NotificationsBackend` core.** Rebase on `ShardedDbServiceBase`;
  `GetUserNotificationInfo` (population re-check), `OnNotify` (hard/soft),
  `OnProcess` (deferred tick), soft buffer, dormancy.
- **Phase 3 — FCM.** `IFirebaseMessagingClient` interface + real impl + test sink;
  `Aps.Badge`; silent dismissal push.
- **Phase 4 — Fan-out rewrite.** Event handlers → cheap fan-out → `OnNotify`.
  Delete `NotificationFlow`.
- **Phase 5 — Read / dismiss.** Engagement/read signals → dormancy clear +
  clear-on-read (via the population re-check).
- **Phase 6 — Client.** `ListActive` consumer, iOS badge simplification, silent
  dismissal handling, client-side reconciliation.
- **Phase 7 — Tests, cleanup, docs.** Replace `NotificationFlowTest`; retire the old
  `DbNotification` write path; flesh out this doc.

Tests rely on the FCM test sink (Phase 3) — `IFirebaseMessagingClient` is replaced
with a logging + callback sink in integration tests so sends are observable without
hitting Firebase.

## 5. Reuse

**Reused:** `ShardedDbServiceBase`, `ShardScheme.NotificationBackend`,
`DbHub.CreateOperationDbContext` / `Operation.MustStore(false)` / `Invalidation.Begin()`
(the `UserPresencesBackend` pattern); `IChatPositionsBackend`, `IUserPresences`,
`ListSubscribedUserIds`, `FilterByNotificationMode` / `FilterByFollowThreadStatus`,
`NotificationHelper`, `MentionExtractor`, `NotificationId` / `SimilarityKey`,
`FirebaseMessagingClient`, `VersionedByteSerializer`, MessagePack `[Union]` (per
`StoredSettings`), `ApiArray`, `DbDevice` / `DbExplicitNotification` (untouched).

**Removed:** `NotificationFlow`.

**New components** are all notification-specific → `Api/Notification/` and
`Notification.*`. No new shared abstractions in `Core` / `Core.Server`.

## 6. Open details (resolved as we build)

1. Cleanest shard-ownership-gain hook for the optional warm-up scan — v1 relies on
   the next `Notify`/`ListActive` instead.
2. Exact `[ComputeMethod]` retention knob keeping `GetUserNotificationInfo` alive
   ≥ the throttle window.
3. Web client consumers of `Get` / `ListRecentNotificationIds` — confirm before
   retiring them in Phase 7.
