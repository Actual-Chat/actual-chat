# PR #3892 review — `feat/notif-api` (Notifications redesign)

**Reviewed:** 2026-06-19 · **Branch:** `feat/notif-api` · **Base:** `dev`
**PR:** [#3892](https://github.com/Actual-Chat/ActualChat/pull/3892) — _feat: New INotifications/NotificationsBackend, NotificationItem union, etc._
**Size:** 90 files changed, +1825 / −930 · 13 commits
**Plan of record:** [`docs/plans/notif-api.md`](notif-api.md)

This is a status review: what the PR delivers against its own plan, and which
planned items are still outstanding. It is **not** a line-by-line correctness
review.

> **Update (2026-06-19):** the gaps in §3 have since been addressed on this branch.
> Phase 6 (core) shipped `INotifications.ListActive`, the `NotificationStack`
> migration, the single-source-of-truth badge, and tag-based silent dismissal on all
> three platforms. Phase 7 migrated the legacy-path tests, deleted the `DbNotification`
> read/write path (incl. the table-drop migration), and added soft-buffer eviction.
> Only **client-side device reconciliation** remains deferred. See `notif-api.md` for
> the current status; the analysis below reflects the branch state at review time.

---

## 1. What the PR does (summary)

Moves notifications from an imperative *"push on every chat event"* pipeline to a
**server-side desired-state reconciliation** model, sharded by `UserId`. Two
motivations: fix the iOS app-icon badge (it never updated while backgrounded
because no push carried `aps.badge`), and give the server a queryable,
per-user notification state with a precise count.

Core mechanism:
- The server keeps, per user, the set of notifications that *should* be on the
  device (`UserNotificationInfo.Displayed`).
- Chat events become cheap *submissions* (`OnNotify`) instead of synchronous
  FCM sends.
- A reconciliation pass diffs desired vs. delivered and pushes the delta;
  idempotent, so a missed/duplicated run converges on the next pass.
- Every push now carries `aps.badge` = displayed count (the original iOS bug fix).

---

## 2. Delivered — Phases 1–5 (✅ complete)

Each phase maps to one or more commits; all build green on `ActualChat.CI.slnf`
per commit messages.

| Phase | Scope | Status | Evidence |
|---|---|---|---|
| **1 — Contracts & data model** | `NotificationItem` union (abstract-record, MessagePack-only), `UserNotificationInfo` / `NotificationDelta`, `DbUserNotifications` row + migration | ✅ | `NotificationItem` hierarchy, `DbUserNotifications.cs`, migration `20260521030046_UserNotifications.cs` (table `user_notifications`) |
| **2 — Backend core** | `NotificationsBackend : ShardedDbServiceBase<NotificationDbContext>`; `GetUserNotificationInfo`, `OnNotify` (hard/soft), `OnProcess` deferred tick, in-memory soft buffer, dormancy set+honor | ✅ | `NotificationsBackend.cs:98` `GetUserNotificationInfo`, `_softBuffers` map at `:21` |
| **3 — FCM** | `IFirebaseMessagingClient` interface; `aps.badge` on every push; `SendDismissal` silent push; `FirebaseMessagingTestSink` in `Testing.Host`; `ConflictStrategy.DoNothing` + lost-create-race re-read fix | ✅ | commit `2e492ac50`; `NotificationTest` 2/2 |
| **4 — Fan-out rewrite** | Event handlers fan out straight to `OnNotify`; removed presence check + `NotificationFlow` deferral + `_recentChatsWithNotifications` throttle; **deleted `NotificationFlow`** + its test + module registration + `Constants.Notification.OnlineCheckDelay` | ✅ | commit `f9ba3b4ea`; `NotificationFlow` gone |
| **5 — Read / dismiss** | `GetUserNotificationInfo` does the population re-check via `IChatPositionsBackend.Get` (Fusion-invalidated on read-position advance); `ApplyHardUpdate` is the single DB-write+push path for adds & removals; `OnHandle` rewritten as backend command `NotificationsBackend_Handle`; dormancy re-derived from effective count (no latch) | ✅ | commit `84a7fbf15`; `NotificationReadReconciliationTest` passes |

Also done: project/namespace rename `Notification.*` → `Notifications.*`
(history preserved via `git mv`), legacy `Notification` record removed in favour
of the `NotificationItem` union, AOT sources regenerated after rebase onto `dev`.

---

## 3. Missing / outstanding from the plan

### Phase 6 — Client (❌ not started)

Plan §4: _"`ListActive` consumer, iOS badge simplification, silent dismissal
handling, client-side reconciliation."_

- **No `ListActive` API.** `INotifications`
  (`src/dotnet/Api.Contracts/Notifications/INotifications.cs`) still exposes only
  the legacy `Get` / `ListRecentNotificationIds` — no `ListActive(Session)`. This
  is a stated v1 goal (plan §2, §3.2) and the engagement signal that clears
  dormancy; it is absent. Backend `GetUserNotificationInfo` exists but is not
  surfaced through a session-scoped client method.
- **iOS badge client simplification not done.** `AppIconBadgeUpdater` is still
  present and wired (`UI.Blazor.App/Services/AppIconBadgeUpdater.cs`,
  `AppScopedServiceStarter.cs`, `BlazorUIAppModule.cs`). The server now sends
  `aps.badge`, but the in-app updater the redesign meant to retire is untouched.
- **No client-side reconciliation** of device notifications (e.g. "ensure every
  unread chat has one") — plan §3.8 layer 2.
- **Silent-dismissal handling on the client** — `SendDismissal` is emitted
  server-side (Phase 3/5) but the client-side consumer of the silent push is not
  wired.

### Phase 7 — Tests, cleanup, docs (❌ largely not done)

Plan §4: _"Replace `NotificationFlowTest`; retire the old `DbNotification` write
path; flesh out this doc."_

- **Legacy backend API still coexists.** `NotificationsBackend` /
  `NotificationsService` still carry `Get`, `ListRecentNotificationIds`,
  `OnUpsert`, `OnUpsertExplicitNotification`, and the `PseudoListRecentNotificationIds`
  invalidation shim (`NotificationsBackend.cs:44/80/182/244/541`). The plan
  schedules these for retirement in Phase 7; they are still present.
- **Legacy-path tests not migrated** (open detail #6). `NotificationContentTest`
  (`tests/Notifications.IntegrationTests/`) and `Testing.Host/NotificationOperations.cs`
  still read via `ListRecentNotificationIds`, which `OnNotify` no longer
  populates after Phase 2 — they must move to `GetUserNotificationInfo`.
- **Soft-buffer eviction missing** (open detail #4). `_softBuffers` (per-`UserId`
  map) is never evicted — no TTL / `EvictStale` / `MemoryCache` reuse found.
  Bounded only by active users per shard; plan flags this as a pre-Phase-7 must.
- **Doc not fleshed out.** `notif-api.md` is still the in-progress plan, not the
  final design write-up Phase 7 calls for.

### Open details still unresolved (plan §6)

1. Shard-ownership-gain warm-up hook — v1 deliberately relies on next
   `Notify`/`ListActive`; not addressed (acceptable for v1).
2. Exact `[ComputeMethod]` retention knob keeping `GetUserNotificationInfo` alive
   ≥ throttle window — not confirmed in code.
3. Confirm web client consumers of `Get` / `ListRecentNotificationIds` before
   retiring them — pending (blocks Phase 7 cleanup).
4. `_softBuffers` eviction — see above, **outstanding**.
5. `UnsentDelta` is still unused (dismissals push eagerly inside
   `ApplyHardUpdate`). Field retained for a future crash-recovery refinement —
   intentional, documented.

---

## 4. Non-goals (correctly out of scope)

Per plan §2, these are explicitly deferred and not expected in this PR:
- Full notification-removal logic beyond clear-on-read.
- Reworking device registration (`DbDevice`) / `ExplicitNotification`.

---

## 5. Bottom line

The **server-side core is complete** (Phases 1–5): contracts, sharded backend,
hard/soft reconciliation, FCM badge + dismissal, read/handle reconciliation, and
the `NotificationFlow` removal — all building green with passing integration
tests (`NotificationTest`, `NotificationReadReconciliationTest`).

**The PR cannot fully deliver its headline goals until Phase 6**, because the
`INotifications.ListActive` API — a primary v1 goal and the dormancy-clearing
engagement signal — does not yet exist, and the client still runs the old
`AppIconBadgeUpdater` rather than relying on the server's `aps.badge`.

**Before merge / before Phase 7 sign-off**, the highest-value gaps are:
1. `_softBuffers` eviction (unbounded growth risk).
2. Migrate `NotificationContentTest` / `NotificationOperations` off the dead
   `ListRecentNotificationIds` read path (they assert against state `OnNotify` no
   longer writes).
3. Decide whether to ship the legacy `Get`/`OnUpsert` surface as-is (coexisting)
   or gate Phase 7 retirement on confirming web-client consumers.
