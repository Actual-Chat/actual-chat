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
the per-user blob (`Items` set + `PendingDismissals` + `LastPushAt` + `IsDormant`).

**Lifecycle.** Each kind carries a `DismissMode` (`OnRead` / `OnView` /
`Explicit`) and an optional `ExpiresAt`, both computed per-kind rather than
stored. `GetUserNotificationInfo` hides read, mode-suppressed and expired items
and resumes `NotificationConvergeFlow`, which commits those removals — hiding
alone would leave the notification on the device with nothing scheduled to close
it. Every removal appends to `PendingDismissals` in the same commit, and an entry
is cleared only once its dismissal push has actually gone out, so a failed send
is retried rather than lost.

**Throttling.** Hard vs. soft updates: the first/urgent notification for a key
commits + pushes; similar low-urgency ones during the silence window accumulate
in an in-memory soft buffer and drain as one batched push (a busy chat costs
~1 DB write + 1 push per window). `IsDormant` per user is the hard cap for
non-readers — dormant users cost zero work until any engagement clears it.

**Composing what a merged banner says.** The reconcile pass that decides alerting also
recomposes the text of everything the merge changed, because only there is the recipient's
localizer in hand. A coalescing chat notification gets a transcript of its window, oldest
message first — the order they have in the chat — with the messages that fell out of the
window counted on the line above it. A reaction coalesces per entry instead, so its body
lists the emoji it accumulated (up to `MaxShownReactionEmojis`, then `…`) and its *sender*
carries the reactor count: `"Dima +2 more @ Team"`, the newest reactor plus the others. One
reactor with one emoji composes exactly what the send path already wrote, which is also every
peer chat — reactions are one per author per entry (`DbReaction.Id` is `(entryId, authorId)`)
and your own never notify, so a peer chat's reactor count is always 1.

Two details that are easy to get wrong there:

- **The count rides on `ReactionNotification.DisplaySenderName`, not only in `Title`.** Android
  renders a chat banner with `MessagingStyle`, which hides the content title and names its
  `Person` from the sender — so a count that lived only in `Title` would be invisible on Android.
  `NotificationExt.GetSenderName` is what the push payload and the client reconciler both read.
  `SenderName` itself stays the raw newest reactor: a merge can carry an existing notification's
  copy of it forward (two reactions in one coarse-clock tick already do), and recomposing from an
  already-composed value would append the count twice.
- **`Emojis` accumulates and never drops one.** A reactor who switches emoji leaves the old one
  behind, and a removal doesn't notify at all — so the stored set outgrows what the message
  actually carries. There can't be more current emoji than reactors and the stale ones are the
  oldest, so the body shows only the newest `AuthorIds.Count` of them.

**Alerting.** Every change pushes; only some pushes alert. `IsSilent` carries
that, and `NotificationBeepPolicy` decides it: a spoken message alerts when its
speaker changes and then at most once per `VoiceReAlertInterval` (10 min), so a
monologue is one alert however long it runs; typed messages back off along
`BeepBackoff`. The banner keeps updating silently to the newest message either
way. A read or a dismissal removes the notification but not its beep state:
`UserNotificationInfo.BeepMemories` keeps it until the lull would have reset it,
and the next message under the same id inherits it (`NotificationBeepPolicy.Inherit`),
so reading a chat on one device does not turn every following message into a
first alert on the others. Clients render every banner themselves — no
`webpush.notification`, no `android.notification` — so `IsSilent` means the same
thing on all three. The web service worker applies a silent update only to a
banner still on screen, replacing it by tag in place; an audible push closes and
re-shows it.

## Client side

- **Reconciliation** — `src/dotnet/UI.Blazor.App/Services/NotificationReconciler.cs`
  prunes stale and creates missing notifications from `ListActive` (prune+create
  on web/Android, prune-only on iOS).
- **Clearing an `OnView` kind** — `SeenNotificationDismisser.cs` dismisses reactions
  and attention pings once their anchor entry is on screen. It runs off both
  `ChatUI.ItemVisibility` *and* `ListActive`: a reaction to an entry the reader is
  already looking at changes nothing the visibility state depends on. The reactions
  tab dismisses on tap too — a tap doesn't guarantee the entry ends up visible.
- **In-app feed** — only the reactions tab of the notifications panel
  (`ChatList/ReactionNotifications/`) renders part of the active set. Everything else
  surfaces as OS notifications, the app-icon badge, and incoming-call rings.
  `/test/notifications` dumps the whole set as JSON for diagnostics.

  Note what this set is *not*: unread counts on chats and places drive the navbar
  and the bell panel, and are deliberately a different calculation. `ListActive`
  drives the app-icon badge and the OS-level surfaces. Two concepts, one source of
  truth each — not two sources for one thing.
- **One row, one notification at a time** — the panel's other tabs list chats, not
  notifications, so a chat holding several gets one row. `NotificationExt.ListNavigable`
  orders that chat's entry-anchored notifications — ping, then mention, then reaction,
  oldest entry first within a kind — and `NotificationsUI.GetNavigationTarget` projects the
  head of that list down to a value-compared `ChatNotificationTarget` (a `Notification`'s
  `ApiArray` members compare by *reference*, so handing one to a row's model would re-render
  every row on every active-set change). `ChatListItem` binds to it: its link is that
  notification's entry rather than the chat, and its badge shows that notification's symbol.
  Tapping dismisses an `OnView` target (the `NavigateToUnreadReaction` pattern: a tap doesn't
  guarantee the entry ends up on screen) and the row rebinds to the next, so repeated taps
  walk the chat's notifications.

  Three rules the binding follows:

  - An `OnRead` target (a mention) is *not* dismissed on tap. A requested dismissal advances the
    read position to the notification's anchor (`GetReadAdvances`), and jumping to a mention must
    not declare everything before it read. Reading it there clears it anyway.
  - A **reaction waits behind unread messages** — those are what the row is in the list for, and
    the walk reaches the reaction as soon as they are read. A ping or a mention doesn't wait; its
    `@` already outranks the count in `UnreadCount`.
  - The **Mentions tab never binds to a reaction**: that tab means own-mentions and attention
    pings, and reactions lift a chat onto the other tabs instead (`ListUnorderedForDisplay`).

  The badge deliberately ignores the `IsReadingTail` gate `ChatUI.GetUnreadState` applies: the
  row you just tapped into is the one that still has to show what's left.
- **Banner rendering** — Android builds its own banner from the data message
  (`Platforms/Android/Notifications/NotificationHelper.cs`, `MessagingStyle` with
  the avatar as the sender's icon). iOS renders `aps.alert` itself, and
  `src/swift/VoxtNotificationService` (a `UNNotificationServiceExtension`, Swift
  because of its 24 MB memory limit) rewrites it into a *communication
  notification* so the chat avatar replaces the app icon. Its
  `conversationIdentifier` must stay equal to the thread id the dismissal path
  matches on — see that project's README.
- **Who a banner is named after** — the chat, on both mobile platforms; the
  author of each message is named by its body line instead. `Title` still ships
  the composed `"<author> @ <chat>"` string (web renders it, and it's what an
  iOS banner falls back to), but a client never splits it back apart — `" @ "`
  occurs in real names. `ChatNotification.SenderName`/`GroupTitle` carry the two
  halves, and an empty `GroupTitle` means "not a group", which is what keeps a
  peer chat from becoming an Android `SetGroupConversation(true)`.
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
| Active set looks wrong | `/test/notifications` — dumps `INotifications.ListActive` as JSON |
| iOS banner shows the app icon, not the chat avatar | is `VoxtNotificationService.appex` in the bundle's `PlugIns/`, and does the build have the `com.apple.developer.usernotifications.communication` entitlement on both the app and the extension? |
