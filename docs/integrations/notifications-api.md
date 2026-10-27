---
title: "Integrations: notification history"
description: The per-user notification log, its read API and the MCP tools for active notifications.
---

# Integrations: notification history

The active notification set (`INotifications.ListActive`) is the converged set of
banners on the user's devices, and a banner is gone the moment the chat is read on
any device. The notification history is the log behind it: it keeps what was
addressed to the user - mentioned, replied to, reacted to, pinged - after the
active set dropped it. The in-app notifications panel's history section (the dimmed
rows under each tab's divider) is backed by the log. Agents use the active set
through the [MCP tools](#mcp-tools) below.

## What is logged

Every notification of an *addressed* kind, at the moment it fires:

| `kind` | Fires when |
|---|---|
| `mention` | Someone mentions you |
| `reply` | Someone replies to your message |
| `reaction` | Someone reacts to your message |
| `attention` | Someone pings you or the whole chat ("notify members") |
| `thread` | A thread is started in a chat you follow |
| `invitation` | You are added to a chat |
| `conversation` | A voice conversation in a chat you follow starts, gets a title, or ends |
| `incomingcall` | You are rung |

`reply` and `invitation` are defined `kind`s and valid `kinds` filter values, but
no current server code path emits either one, so neither will appear in history
until the server starts producing them.

`message` (a row per incoming message in every chat you are in) and
`speechstarted` are chat traffic, not something addressed to you, and are never
logged. `attention` and `incomingcall` are ringers (`NotificationHelper.GetImportance`)
and log regardless of the chat's notification mode, muted included. Every other
logged kind follows the chat's mode exactly as a push would: a muted chat logs
none of them, and "important only" mode logs the Important kinds (`mention` is
the only one currently live) but not the Ordinary ones (`reaction`, `thread`,
`conversation`).

A row is what the notification looked like when it fired: the kind, the chat,
the entry it anchors at, the author, and the title and text of the banner. It is
kept for **30 days** regardless of reads, dismissals or the chat being left, then
pruned.

## Read API

```csharp
Task<ApiArray<NotificationHistoryItem>> INotifications.ListHistory(
    Session session, NotificationHistoryQuery query, CancellationToken cancellationToken);
```

`NotificationHistoryQuery`:

| Field | Default | Meaning |
|---|---|---|
| `Kinds` | empty = all logged kinds | `ApiArray<NotificationKind>`; unlogged kinds never match |
| `AfterSeq` | 0 = start of the walk | Cursor: return rows after this `Seq` in walk order |
| `Limit` | 64 | Capped at 256 |
| `IsNewestFirst` | false | Walk from the newest row towards older ones |

`NotificationHistoryItem` carries `Seq` (the cursor), `Kind`, `SentAt`, `ChatId`,
`EntryId`, `AuthorId`, `Title`, `Text` and `NotificationId`. It is not a compute
method: nothing reactive depends on it, and every logged notification would
otherwise invalidate every cursor variant.

```csharp
Task<long> INotifications.GetHistoryVersion(Session session, CancellationToken cancellationToken);
```

`GetHistoryVersion` is the reactive seam `ListHistory` lacks: a compute method
returning the `Seq` of the caller's newest logged row (0 when the log is empty),
bumped by every logged notification and by account removal. A UI depends on it
and re-reads `ListHistory` when it changes; a polling agent has the cursor and
needs neither.

Several reactions on the same entry coalesce into one notification, so they share
one `NotificationId` — the in-app panel hides all of them while any is still in
the active set.

## MCP tools

The MCP server exposes the **active** set, not the history: an agent consumes
notifications as they arrive and dismisses them. A mention disappears from the set
once its chat is read, on any device. The history log stays behind
`INotifications.ListHistory` for the in-app panel.

| Tool | Does |
|---|---|
| `list_notifications` | The active notifications and their `version`. Optional `chatId` or `placeId` limits the set; a contact is its peer chat, so pass the peer chat id. |
| `wait_for_notifications` | Blocks until the set differs from `version`, then returns the new set. Same optional `chatId` / `placeId`. `timeoutSeconds` defaults to 30, capped at 55. |
| `dismiss_notification` | Dismisses one notification by `id`. |
| `dismiss_all_notifications` | Dismisses all of them. |

Result of the first two:

```json
{
  "items": [{
    "id": "…", "kind": "mention", "at": 1758470400000,
    "chatId": "…", "entryId": 42, "authorId": "…",
    "title": "Dima mentioned you in Review Requests", "text": "…",
    "message": { /* ExternalMessage, or null */ }
  }],
  "version": "9f2c4a1b7e30d5a8"
}
```

`message` is the anchored message in the same `ExternalMessage` shape `list_messages`
and the web hook `notification` event use, re-resolved through the caller's session
at read time - `null` for a chat the caller has left or a removed entry.

`version` is a hash of the returned set (ids, versions, send times, titles and
texts). Equal versions mean an equal set.

**Waiting.** Keep the `version` of the last set you processed and pass it to
`wait_for_notifications`; the call returns when the set differs. On a timeout it returns
the unchanged set and the same `version` - call again. The server holds no per-client
state, so a broken connection is resumed by calling again with the saved `version`
(an empty or stale one returns the current set at once). The wait runs on the
compute method `INotifications.ListActive`, so it wakes on invalidation rather than polling.
Changes outside the `chatId` / `placeId` scope wake it too, but leave the filtered version
unchanged, so the call keeps waiting.

Web hooks ([`web-hooks.md`](./web-hooks.md)) push the same events.

Notifications are not a feed of every message: a message may produce none. An agent that must see
every new message uses `wait_for_changes` ([`chat-activity-api.md`](./chat-activity-api.md)).
