---
title: Notifications
description: Current architecture of the notifications subsystem — desired-state reconciliation, sharded per user, iOS badge handling.
---

# Notifications

Push and in-app notifications run on a **desired-state reconciliation** model
(not "push on event"). The server keeps, per user, the set of notifications
that *should* be on the device; chat/reaction events are cheap *submissions*;
reconciliation diffs desired-vs-delivered and sends only the delta. It is
sharded by `UserId` with in-process invalidation — no operations-framework
op-log on the real-time path (same model as `UserPresencesBackend` and the
live audio/video backends).

This replaced the older imperative pipeline (per-event `SendMessage`, per-row
`DbNotification`, per-`(user, chat)` `NotificationFlow`). The redesign was
driven by the iOS app-icon badge never updating while backgrounded.

## Server side

| Piece | Where | What it does |
|---|---|---|
| `NotificationsBackend` | `src/dotnet/Notifications.Service/NotificationsBackend.cs` | State owner + brain. `ShardedDbServiceBase<NotificationDbContext>`, sharded by `UserId` (`ShardScheme.NotificationBackend`). Per-user state primed in memory on the owning node; `GetUserNotificationInfo(UserId)` is the invalidated compute method. |
| `INotifications` / `NotificationsService` | `src/dotnet/Api.Contracts/Notifications/INotifications.cs`, `Notifications.Service/NotificationsService.cs` | Thin client API. `ListActive(Session)` projects the displayed set and doubles as an engagement (dormancy-clearing) signal. |
| `FirebaseMessagingClient` | `Notifications.Service/FirebaseMessagingClient.cs` (`IFirebaseMessagingClient`) | Sends FCM pushes; every push carries `aps.badge`; silent-push path for dismissals. |
| `MentionReminderFlow` | `Notifications.Service/Flows/MentionReminderFlow.cs` | Re-reminder for unread mentions. |
| Persistence | `Notifications.Service/Db/DbUserNotifications.cs` | One row per user (`Data` blob = committed `UserNotificationInfo`). No Redis. |

Data model (`src/dotnet/Api/Notifications/`): `Notification` is a MessagePack
`[Union]` (one concrete record per `NotificationKind` — Message, Reply,
Mention, Reaction, Invitation, Attention, Thread). `UserNotificationInfo` is
the per-user blob (`Displayed` set + `UnsentDelta` + `IsDormant`).

**Throttling.** Hard vs. soft updates: the first/urgent notification for a key
commits + pushes; similar low-urgency ones during the silence window accumulate
in an in-memory soft buffer and drain as one batched push (a busy chat costs
~1 DB write + 1 push per window). `IsDormant` per user is the hard cap for
non-readers — dormant users cost zero work until any engagement clears it.

## Client side

- **Reconciliation** — `src/dotnet/UI.Blazor.App/Services/NotificationReconciler.cs`
  prunes stale and creates missing notifications from `ListActive` (prune+create
  on web/Android, prune-only on iOS).
- **In-app feed** — `UI.Blazor.App/Components/Notifications/NotificationStack.razor`.
- **App-icon badge** — `AppIconBadgeUpdater.cs` (single source of truth) plus
  native `AppIconBadge` on iOS (`App.Maui/MaciOS/AppIconBadge.cs`) and Windows
  (`Platforms/Windows/WindowsAppIconBadge.cs`). On iOS the badge of a
  backgrounded app can only change via `aps.badge`, so every push sets it and a
  silent dismissal push lowers it; the client re-asserts on foreground resume.

## Where to look when something is wrong

| Symptom | First place |
|---|---|
| iOS badge count wrong/stale | `aps.badge` computed at push time in `NotificationsBackend`; foreground re-assert in `AppIconBadgeUpdater` |
| Notification not delivered | `NotificationsBackend` submission → reconciliation; is the user `IsDormant`? |
| Notification lingers after read | clear-on-read → silent dismissal push; `NotificationReconciler` prune |
| Duplicate / noisy pushes | soft-buffer coalescing + throttle window in `NotificationsBackend` |
| Missing in-app feed item | `INotifications.ListActive` projection; `NotificationStack.razor` |
