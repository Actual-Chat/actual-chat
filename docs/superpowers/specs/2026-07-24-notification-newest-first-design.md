# Newest-First Coalesced Notifications + Android Transcript — Design

**Status:** Approved (2026-07-24)
**Author:** Alexey Kochetov (with Claude)
**Area:** Notifications (`Api/Notifications`, `Notifications.Service`, Android `App.Maui` notification rendering)

## Problem

Chat notifications coalesce per chat (one banner per chat, replaced in place via
Android `tag` / `apns-collapse-id`). The body is anchored on the **first unread**
message (`LeadText`, with a second short message rolled in), and every later
message only bumps a `+N more messages` tail. The visible text goes stale the
moment a second message arrives: the user stares at the oldest message while the
counter climbs.

Best-in-class messengers do the opposite:

- **WhatsApp / Telegram / Signal on Android** use `MessagingStyle` — one
  notification per chat that appends each message as its own line (sender +
  text), newest at the bottom of the expanded view; the collapsed view shows the
  newest message with a count.
- **iOS apps** stack per-thread notifications so the newest is always on top.
- **Web push** (Telegram Web, Slack) replaces by tag with the newest message as
  the body and the count as secondary text.

The common thread: **the newest message is always visible, the count is
secondary, and older messages appear as history when capacity allows — never as
the headline.**

## Goal

1. Server-composed body becomes **newest-first**: newest message on top (that is
   what collapsed banners show), earlier still-unseen messages below it, then a
   `+N earlier messages` tail. Benefits every platform at once.
2. Android renders a **true `MessagingStyle` transcript**: one line per message
   with real sender `Person`s and timestamps, driven by structured per-message
   data in the push payload.

Non-goals: iOS per-message notifications (kept as replace-by-tag), beep/silent
policy changes, tap-target changes (still the first unread entry).

## Reuse

- `ChatEntryRelatedNotification.MergeWith` — the existing single merge point;
  all changes to coalescing happen there.
- `NotificationHelper.GetAggregatedText` / `NotificationsBackend.ComposeAggregatedText`
  — the existing composition seam; rewritten, not relocated.
- `NotificationHelper.ShowChatNotification` (Android) — already the shared
  render path for the FCM receive path and the reconciler heal path; already
  builds a (single-message) `MessagingStyle`. Extended, not duplicated.
- `NotificationReconciler` / `IDeviceNotifications` — the heal path already
  carries full notification records; no new plumbing needed.
- `ApiArray<T>`, `Constants.Notification.*` — existing containers/constants.

New component placement: `NotificationMessage` is a shared API contract and goes
in `src/dotnet/Api/Notifications/` next to the notification records (it is part
of the `ChatEntryRelatedNotification` wire format, so `Api` is its natural and
shared home; no `ActualChat.Core` placement applies).

## Data model (`Api/Notifications`)

New value type:

```csharp
[DataContract, MessagePackObject]
public sealed partial record NotificationMessage(
    AuthorId AuthorId,      // Key(0)
    string AuthorName,      // Key(1) — snapshot at send time; no resolve at compose/render
    string Text,            // Key(2) — truncated to MaxRecentMessageTextLength
    long EntryLid,          // Key(3)
    Moment SentAt);         // Key(4)
```

`ChatEntryRelatedNotification` gains:

```csharp
[DataMember(Order = 18), Key(18)]
public ApiArray<NotificationMessage> RecentMessages { get; init; }
```

- Capped at `MaxRecentMessages`, ordered **oldest → newest**.
- `LeadText` / `LeadCount` remain on the wire for one rolling-deploy window:
  new code keeps writing `LeadText` = newest message text, `LeadCount` = 1, so
  old nodes/clients compose something sane from new blobs. Removed in a
  follow-up release.

New constants (`Constants.Notification`):

- `MaxRecentMessages = 5`
- `MaxRecentMessageTextLength = 200` (replaces `LeadRollInThreshold`)

Payload budget: 5 × (200 chars text + name + ids) stays far under FCM's 4 KB
message limit.

## Merge logic

**Creation** (`EnqueueMessageRelatedNotifications`): seed `RecentMessages` with
the single incoming message — `changeAuthor.Avatar.Name` is available at that
site.

**`MergeWith`**:

- The existing idempotence guard (entry already inside the merged window →
  return the existing instance so no beep/push fires) is untouched, as are
  `UnreadCount`, `AuthorIds` tracking, window anchors (`StartEntryLid` /
  `EntryLid` min/max), and the beep back-off / lull reset.
- Insert the incoming message into `RecentMessages` sorted by `EntryLid`
  (out-of-order events land in the right slot), then drop the oldest entries
  beyond `MaxRecentMessages`.
- **Legacy seed**: if the existing record has empty `RecentMessages` but a
  non-empty `LeadText`/`Text`, synthesize one `NotificationMessage` from it
  (empty `AuthorName` → that line renders without a sender prefix — acceptable
  transition-window degradation).
- **Title/icon follow the newest message**: `Title`/`IconUrl` must come from the
  message with the max `EntryLid`. (Today an out-of-order *earlier* message that
  extends the window start wins the title — backwards for newest-first.)

## Text composition

`ComposeAggregatedText` / `GetAggregatedText` (rewritten):

```
<newest message>
<next-newest message>
...
+N earlier messages
```

- Lines = `RecentMessages` reversed (newest first).
- Group/place chats prefix each line with `AuthorName: `; peer chats don't (the
  title already names the sender). Chat kind is derived from `ChatId` — no
  lookup.
- Tail `+N earlier messages` with `N = UnreadCount − RecentMessages.Count`;
  omitted when `N ≤ 0`. Author names are dropped from the tail (senders are on
  the lines now); the author-name resolution loop in `ComposeAggregatedText` is
  deleted.

**`ReAnchor`** (partial read): filter `RecentMessages` to `EntryLid > read`
instead of re-reading the lead entry from `ChatsBackend`. Only when the filter
empties the list does it fall back to fetching the entry at the new anchor
(resolving the author name there). `UnreadCount` approximation unchanged.

## Android transcript

**Push payload**: new data key `messages`
(`Constants.Notification.MessageDataKeys.Messages`, added to `ValidKeys`) —
compact JSON array, oldest → newest:

```json
[{"n": "Alice", "t": "ok let's ship it", "ts": 1753350000000}, ...]
```

Written by `FirebaseMessagingClient` from `RecentMessages` for
`ChatEntryRelatedNotification` pushes. `Body` keeps the composed newest-first
text as the universal fallback.

**Rendering** (`NotificationHelper.ShowChatNotification` on Android): gains an
optional parsed-messages parameter.

- When present, `CreateStyle` adds one `MessagingStyle` message per entry — a
  `Person` per distinct sender name, the newest sender gets the avatar bitmap
  (`largeImage`), real timestamps from `ts`. Group conversation flag/title as
  today (from the `" @ "` title split).
- When absent (old server, non-chat kinds), the current single-message fallback
  stays.

**Callers** (both updated, no third path exists):

1. FCM receive path (`FirebaseMessagingService`) — parses the `messages` key.
2. Reconciler heal path (`AndroidDeviceNotifications`) — already holds the full
   notification record; passes `RecentMessages` directly.

**iOS and web need zero code changes** — they display the composed body, which
is newest-first by construction. iOS keeps `apns-collapse-id` replacement;
alerting stays governed by the existing beep/silent policy.

## Compatibility

- **Old blobs → new code**: `RecentMessages` deserializes empty; the legacy
  seed in `MergeWith` (and the `Text` fallback in composition) covers it.
- **New blobs → old code**: keys ≤ 17 unchanged; old code ignores key 18 and
  still finds a fresh `LeadText` (kept = newest message during the transition).
- **Old Android app / new server**: extra `messages` data key is ignored by the
  old parser (it reads known keys only); body fallback renders.
- **New Android app / old server**: `messages` key absent → single-message
  fallback path.

## Testing

Unit tests (server, `MergeWith` + composition are pure or near-pure):

- Append order, capacity eviction at 5, out-of-order insert lands sorted,
  redelivery is a reference-equal no-op, legacy-blob seeding, title/icon follow
  the newest `EntryLid`.
- Composition: newest-first ordering, peer vs group sender prefixes, tail count
  (present/omitted), text truncation.
- `ReAnchor`: list filtering, empty-list fallback to entry fetch, unread
  recomputation.

Android `MessagingStyle` rendering is verified manually on a device (transcript
lines, senders, collapsed view showing the newest message, silent same-tag
updates).
