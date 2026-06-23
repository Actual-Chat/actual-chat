# Conversation notifications — design

Date: 2026-06-23
Status: approved (brainstorming) → ready for implementation plan
Branch: feat/realtime-conversations

## Context

The `dev` branch landed a Notifications redesign: `Notification` is now an
abstract union (`MessageNotification`, `ReplyNotification`, `MentionNotification`,
`ReactionNotification`, `AttentionNotification`, `ThreadNotification`,
`InvitationNotification`) under `src/dotnet/Api/Notifications/`, with a
desired-state reconcile pipeline (`NotificationsBackend_Notify` →
`OnNotify`/`OnProcess`/`OnHandle`), per-chat banner grouping via
`NotificationExt.GetChatTag`, and tag-based dismissal.

The `feat/realtime-conversations` branch added live-conversation START/FINAL
notifications against the *old* flat `Notification` type. After rebasing onto
`dev` those were ported minimally to `MessageNotification` (commit
`1eb1ff38b`), which coalesces START/FINAL per chat but reuses the "message"
semantics — not a real fit.

This design introduces a first-class **conversation notification** that covers
both live and regular (split-flow) conversations, replacing per-message
notifications in summarized chats.

## Goals

- A single notification *type* for conversations, shared by live and regular.
- Notify across a conversation's observable lifecycle, coalescing into one
  updating banner per conversation.
- In summarized chats, notify at conversation granularity instead of per
  message.

## Non-goals

- No change to non-summarized chats (per-message notifications unchanged).
- No new discrete "conversation closed" signal for regular conversations
  (none exists in the data model; see below).
- No client/UI redesign beyond what the shared union type already drives
  (banner grouping, deep link, read-clear all reuse existing machinery).

## Lifecycle model

A conversation is identified by `ConversationId` (`chatId:startEntryLid`).
All phases of one conversation share **one** `NotificationId`, so the banner
updates in place rather than stacking.

| Kind | Phases emitted |
|---|---|
| **Regular** (split-flow) | `Created` — one notification when the conversation is first materialized (it is born already-titled). Re-emitted (banner update) if a re-summarize changes Title/Description. No "final". |
| **Live** | `Started` (LiveSession latched, no title yet) → `Titled` (summary names it) → `Final` (closed / materialized into a persisted `Conversation`). |

**Why regular conversations have no "begin" or "final":**
`ConversationsBackend.OnChange` → `ApplyDiff` rejects any conversation with an
empty `Title`/`Description`/`Summary` (lines ~250-257), so a regular
conversation never exists in an untitled state — `begin` and `titled` collapse
into creation. And nothing marks a regular conversation "closed": the split
flow simply stops re-summarizing a segment. So regular conversations get a
single `Created` notification, updatable on re-summarize.

Mentions, replies, reactions, and attention notifications are **not** affected
— they remain individually delivered even in summarized chats. Only the
broadcast **Message** path is suppressed (see Suppression).

## New type: `ConversationNotification`

Location: `src/dotnet/Api/Notifications/ConversationNotification.cs` (shared
contract, alongside the other union subtypes — it is consumed by the FCM send
path and the client reconciler, not feature-local).

```
public sealed partial record ConversationNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    public long StartEntryLid { get; init; }   // from ConversationId
    public long EndEntryLid { get; init; }      // read anchor

    // ChatId parsed from the similarity key (a ConversationId value)
    public override ChatId ChatId => ConversationId.Parse(SimilarityKey).ChatId;

    public static ConversationNotification New(UserId userId, ConversationId conversationId, long endEntryLid)
        => new(NotificationId.New(userId, NotificationKind.Conversation, conversationId.Value)) {
            StartEntryLid = conversationId.StartEntryLid,
            EndEntryLid = endEntryLid,
        };
}
```

Key decisions:
- **Extends `ChatNotification`, not `ChatEntryNotification`.** `IsSoftUpdate`
  hard-updates `ChatEntryNotification` ("individually seen"); a conversation
  notification must be *soft* so its phases coalesce onto one banner.
- **Similarity key = `ConversationId.Value`** → per-conversation identity. A
  new conversation in the same chat is a distinct notification; phases of the
  same conversation share the key. `ChatId` is overridden to parse from the
  key (mirrors `ChatEntryNotification.ChatId`), because the base
  `ChatNotification.ChatId => ChatId.Parse(SimilarityKey)` would fail on a
  `chatId:lid` key.

### Required wiring (existing switches over `Notification`)

- `Notification.cs` — add `[Union(8, typeof(ConversationNotification))]`.
- `NotificationKind` (`Api/Identifiers/NotificationKind.cs`) — add
  `Conversation` before `Invalid`.
- `NotificationsBackend.GetReadAnchor` → `(ChatId, EndEntryLid)` so reading to
  the end of the conversation clears it.
- `NotificationExt.GetChatTag` → `ChatId.Value` (groups under the chat
  banner). Add as a specific arm **before** the generic `ChatNotification`
  fallback (a `chatId:lid` key would otherwise mis-handle), as the existing
  `ChatEntry*` arms already do.
- `NotificationExt.GetChatLink` → `Links.Chat(start entry)`.
- `NotificationsBackend.GetEntryId` → start `ChatEntryId`.
- FCM render path (`FirebaseMessagingClient`) — confirm it renders Title/Text
  generically; add a case only if it switches on subtype.

## Emission

A single generalized command replaces the branch's
`NotificationsBackend_NotifyLiveConversation`:

```
NotificationsBackend_NotifyConversation(
    ConversationId ConversationId,
    ConversationNotificationPhase Phase,   // Started | Titled | Created | Final
    string Title,
    string Text,
    long EndEntryLid)
```

(Lives in `Notifications.Contracts`. `Chat.Service` already references
`Notifications.Contracts` after the rebase fix, so both emitters can enqueue
it.)

- **Regular conversations** — emitted from `ConversationsBackend.OnChange`:
  - on **create**: phase `Created`.
  - on **update** where `Title`/`Description` changed: phase `Created` again
    (banner update).
  - Uses the **operation-event outbox** (`context.Operation.AddEvent(...)`)
    rather than a bare `Queues.Enqueue`, because `OnChange` is a DB operation
    and the notification must be transactionally tied to the commit (see
    project memory: operation-event outbox + `DbEventForwarder`).
- **Live conversations** — `LiveConversationSummaryFlow` emits `Started`,
  `Titled`, `Final` at the existing points (replacing today's
  `NotifyLiveConversation` enqueues).

### Recipient resolution — `NotificationsBackend.OnNotifyConversation`

- Recipients = `ListSubscribedUserIds(chatId)` minus the conversation's
  authors, honoring mute (reuse existing `IsMuted`). `Conversation.AuthorIds`
  are `AuthorId`s → resolve to `UserId`s (via `AuthorsBackend`) before
  excluding.
- For live phases, additionally skip current participants (existing
  `LiveSessionsBackend.IsParticipant`) — joined users already see it live.
- For each recipient, build `ConversationNotification.New(userId,
  conversationId, endEntryLid) with { Title, Text, SentAt = now, IconUrl }`
  and `Queues.Enqueue(new NotificationsBackend_Notify(notification))`.
  Read-state and dedup are handled downstream by the existing reconcile
  pipeline + read anchor.

## Suppression

`NotificationsBackend.OnChatEntryChangedEvent` currently suppresses the Message
path for entries at/after an active live conversation's `StartEntryLid`.
Generalize this: suppress the Message path whenever **`chat.IsSummarized`** is
true. This covers both the live-conversation tail and ordinary summarized-chat
messages, with conversation notifications replacing them.

Non-summarized chats keep firing per-message notifications unchanged.

## Wording (defaults; tune later)

- `Title` = chat title.
- `Text`:
  - `Started` → "Voice chat started"
  - `Titled` → conversation Title
  - `Created` / `Final` → conversation Title (+ short summary/description)

## Reuse (per CLAUDE.md)

**Existing abstractions reused:**
- `Notification` / `ChatNotification` union and `NotificationId.New`.
- Delivery pipeline: `NotificationsBackend_Notify`, `OnNotify`, `OnProcess`,
  `OnHandle`, per-chat banner grouping, tag dismissal — unchanged.
- `ListSubscribedUserIds`, `IsMuted`, `NotificationHelper.GetTitle` /
  `GetIconUrl`.
- `context.Operation.AddEvent` operation-event outbox (+ `DbEventForwarder`).
- `ConversationsBackend.OnChange` as the single regular-conversation choke
  point; `ConversationId`, `Conversation`, `Conversation.EndEntryLid`.
- `LiveSessionsBackend` (`Get`, `IsParticipant`) for the live path.
- **Generalize** the existing `NotificationsBackend_NotifyLiveConversation`
  command into `NotificationsBackend_NotifyConversation` rather than adding a
  parallel command.

**New components and placement:**
- `ConversationNotification` → `Api/Notifications/` (shared; reused by FCM +
  client reconciler like its siblings — not feature-local).
- `NotificationKind.Conversation` → `Api/Identifiers/NotificationKind.cs`
  (shared enum).
- `NotificationsBackend_NotifyConversation` + `ConversationNotificationPhase`
  → `Notifications.Contracts` (shared contract).

No new helper duplicates an existing one.

## Testing

- Unit: `ConversationNotification` key round-trip — `ChatId`/`StartEntryLid`
  derive correctly from `ConversationId.Value`; `GetChatTag` → chat id;
  `GetReadAnchor` → `(chatId, endEntryLid)`; `GetChatLink` → start entry.
- Backend integration:
  - Regular conversation create → one `ConversationNotification` to subscribed
    non-author; re-summarize title change → banner update (same Id).
  - Live conversation Started → Titled → Final all coalesce to one Id;
    participants excluded; non-participants notified.
  - Summarized chat: per-message Message notifications suppressed; Mention /
    Reply still delivered. Non-summarized chat: per-message unchanged.
  - Read past `EndEntryLid` clears the conversation notification.

## Open points (defaulted)

1. **Re-summarize churn.** A regular conversation whose title changes
   re-notifies (reactivates the banner). If noisy in practice, debounce or
   skip title-only churn. Defaulted to: notify on title/description change.
2. **Wording** as above — placeholder copy, refine during implementation.
