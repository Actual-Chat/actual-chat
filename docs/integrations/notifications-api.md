---
title: "Integrations: notification history"
description: The per-user notification log, its read API and the MCP tool, for agents that poll instead of receiving web hooks.
---

# Integrations: notification history

An agent that talks to Voxt through the MCP server needs to know when it was
addressed: mentioned, replied to, reacted to, pinged. The active notification
set (`INotifications.ListActive`) cannot answer that — it is the converged set
of banners on the user's devices, and a banner is gone the moment the chat is
read on any device. The notification history is the log behind it.

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
`EntryId`, `AuthorId`, `Title` and `Text`. It is not a compute method: nothing
reactive depends on it, and every logged notification would otherwise
invalidate every cursor variant.

## MCP tool: `list_notifications`

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `kinds` | `string[]` | all | Any of the `kind` values above, case-insensitive; an unrecognized name, or a kind that exists but is never logged (`message`, `speechstarted`), is an error |
| `afterSeq` | `long` | none | Cursor, see below |
| `limit` | `int` | 64 | Capped at 256 |
| `newestFirst` | `bool` | false | Newest first |

Result:

```json
{
  "items": [{
    "seq": 1234, "kind": "mention", "at": 1758470400000,
    "chatId": "…", "entryId": 42, "authorId": "…",
    "title": "Dima mentioned you in Review Requests", "text": "…",
    "message": { /* ExternalMessage, or null */ }
  }],
  "nextAfterSeq": 1234
}
```

`message` is the anchored message in the same `ExternalMessage` shape
`list_messages` and the web hook `notification` event use, re-resolved through
the caller's session at read time — a chat the caller has since left yields
`null`, as does a removed entry.

**Polling.** Persist `nextAfterSeq` and pass it back as `afterSeq` on the next
call: each call then returns only what is new. When a page comes back empty,
`nextAfterSeq` is `null` — keep the cursor you already have rather than
overwriting it with `null`, since an absent `afterSeq` restarts the walk from
the beginning. Walk oldest-first for this; `newestFirst` is for "what happened
lately", where `afterSeq` continues towards older rows.

Web hooks ([`web-hooks.md`](./web-hooks.md)) push the same events; the history
is for agents that would rather poll.
