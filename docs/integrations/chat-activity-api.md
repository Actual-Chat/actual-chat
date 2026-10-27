---
title: "Integrations: chat activity"
description: The MCP tools an agent uses to learn about new messages in all its chats - wait_for_changes, list_unread_chats, wait_for_unread and mark_read.
---

# Integrations: chat activity

An agent that answers messages needs to learn about every new message in every chat it is in.
Notifications ([`notifications-api.md`](./notifications-api.md)) can't do that: they coalesce per
chat, are held back for a reader who is caught up and present, and are skipped for a dormant user,
so a message may arrive with no notification at all. Dismissing a message notification also moves
the caller's read position. The tools here read the chats themselves and change no state except
through `mark_read`.

Code: `src/dotnet/Mcp/Tools/McpChatActivityTools.cs`, on top of two compute methods of `IContacts`
(see [The API behind the tools](#the-api-behind-the-tools)).

## Which chats

All four tools cover the same set: the caller's group and peer chats, the chats of every place
they are in, and the threads they follow (the thread contacts behind the app's thread list). An
optional `placeId` narrows the set to one place's chats and threads.

## Tools

| Tool | Does |
|---|---|
| `wait_for_changes` | Returns the chats that have messages after `cursor`, and a new cursor. Waits until there is one, or `timeoutSeconds` (default 30, capped at 55) passes. |
| `list_unread_chats` | The chats with messages after the caller's read position, and a cursor for the state the list was built from. |
| `wait_for_unread` | The same result as `list_unread_chats`, once it lists any chat. Waits until one has unread messages, or `timeoutSeconds` (default 30, capped at 55) passes, then returns no chats. |
| `mark_read` | Moves the caller's read position in a chat forward to `lastId`. It never moves back, and stops at the chat's last message. |

`wait_for_changes` result:

```json
{
  "changes": [{ "chatId": "s-1KCdcYy9z2-zu3UA03jvX", "afterId": 1771, "lastId": 1773 }],
  "cursor": "c1.…"
}
```

`afterId` is the chat's last message id in the cursor the caller passed. Pass it as `afterId` to
`list_messages`. It is null for a chat the cursor doesn't have: a chat joined or a thread followed
since the cursor was made.

`list_unread_chats` result: `chats` holds `{chatId, readId, lastId, unreadCount}`, where `readId` is
the read position (pass it to `list_messages` as `afterId`). `unreadCount` is `lastId - readId`, an
upper bound, because removed messages leave gaps in the ids.

A chat counts as unread the way the app counts it (`ChatPosition.GetUnreadCount`): a chat the caller
never joined or opened has no read position and nothing unread, or the whole history of every public
place chat would be. A peer chat has no join step, so there a missing position means nothing was
read. To start tracking a public chat without joining it, `mark_read` it once.

## Two ways to run an agent

An agent picks one of two loops.

**Follow changes** with a cursor, when it must see every message and keeps its own state. See
[The cursor](#the-cursor).

**Work off unread chats**, when "handled" can live in Voxt itself:

1. `wait_for_unread` returns the unread chats.
2. Read each one from `readId` with `list_messages`.
3. `mark_read` the last message handled, then reply.
4. Go to 1.

`wait_for_unread` returns at once while any chat is unread, so every chat the agent looks at must be
marked read, including the ones it chooses not to answer. Mark it read *before* replying: posting
moves the author's read position over their own message only when they had read everything before
it (`Chats.OnUpsertEntry`). Marking it read after replying, up to the reply's id, would also mark
whatever arrived in between, and the agent would never see it.

`archive_chat` is not a way to set a chat aside: an archived chat leaves the contact list these
tools read, and a new message doesn't bring it back, only `unarchive_chat` does
([`chat-archive.md`](../architecture/chat-archive.md)). An agent that archives a chat stops hearing
from it until it unarchives it. `list_archived_chats` lists the archive.

## The cursor

The cursor is the last message id of every chat in the set, compressed into an opaque string. A
cursor of message ids rather than a timestamp means nothing can be missed: ids grow in each chat,
while a message's time is when it was created, so a voice message or a stream that started earlier
could fall under a time cutoff.

The server keeps no state per caller. A cursor stays valid indefinitely, and calling again with an
older one returns its changes again. So persist a cursor only after handling the changes it was
returned with.

An empty cursor makes `wait_for_changes` return the current cursor at once, with no changes. To
start without missing anything:

1. Call `list_unread_chats` and keep its `cursor`.
2. Read each listed chat from `readId` with `list_messages`, and `mark_read` what you handled.
3. Loop: `wait_for_changes(cursor)`, read each change from `afterId`, keep the new cursor.

Messages posted between steps 1 and 3 show up in the first wait, because the cursor is from step 1.

## The API behind the tools

Two compute methods of `IContacts` (`Contacts.Service/Contacts.cs`) do the work, and the app or any
other client can use them the same way:

| Method | Returns |
|---|---|
| `ListChatIdRanges(session, subset)` | `ChatIdRange(ChatId, IdRange)` for each chat of the set; `LastEntryLid` is `IdRange.End - 1`. |
| `ListUnreadChats(session, subset)` | `UnreadChat(ChatId, ReadEntryLid, LastEntryLid)` for each chat with unread messages. |

`ContactSubset` picks the set: `All()` is everything the tools cover, `Chats()` the chats outside any
place, and `Place(placeId)` one place. Both results are sorted by chat id, so an unchanged result
always serializes to the same bytes. `All()` merges the per-subset results, so a message in one
place recomputes only that place's list and the merge.

Neither method sets `MinCacheDuration`: they are cheap to rebuild from the compute methods they read
(`ListIds`, `IChatThreads.ListIdsForPlace`, `IChats.GetIdRange`, `IChatPositions.GetOwn`), which
are the ones worth keeping cached.

## How the wait works

The waits capture the method's computed value with `Computed.Capture`, check it, and await
`WhenInvalidated` - the same loop as `wait_for_notifications`. A new message, a read position
moving, a chat joined, left or archived, or a thread followed invalidates the value, so the wait
wakes on that invalidation rather than polling.

`list_unread_chats` and `wait_for_unread` read the id ranges for the cursor before the unread list,
so a message that lands between the two reads is repeated by the next `wait_for_changes`, never
skipped.

A message's id is assigned when it starts. A streamed message is reported once, when it starts;
read it again later to get the final text.

## Limits

- `timeoutSeconds` is capped at 55, the same as `wait_for_notifications`, to stay under the
  common 60-second idle timeout of HTTP proxies.
- A cursor grows with the number of chats, since it holds an id per chat.
