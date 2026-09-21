# Notifications history: read API and MCP tool

Issue: #4686. Branch: `feat/notifications-history`.

## Problem

Agents talking to Voxt through the MCP server cannot tell that they were
addressed. The only notification read surface, `INotifications.ListActive`,
returns the converged per-user set in `UserNotificationInfo.Items`, and a
notification leaves that set the moment the chat is read, the notification is
dismissed, the chat is muted, or the item expires. An agent that polls
`ListActive` misses a mention as soon as a human on the same account reads the
chat. There is no history anywhere.

The only firehose of "what was notified" is the `UserNotifiedEvent` that
`NotificationsBackend.OnNotify` enqueues before any suppression; today its sole
consumer is the web hooks `notification` event.

## Decision

Persist an append-only per-user notification log in the Notifications DB, fed by
the same `UserNotifiedEvent`, and expose it through a plain RPC method on
`INotifications` and an MCP tool `list_notifications`, both filterable by kind
and paged by a cursor.

Alternatives considered and rejected:

- **Active set only** (filter `ListActive` by kind): no new storage, but a
  mention vanishes once read, so the agent must poll constantly and still
  misses events.
- **Derive from source tables** (`DbMention`, `DbReaction`): cross-chat fan-out
  queries, no mute / own-message semantics, no reply / attention / thread kinds.
- **NATS JetStream**: NATS is only a work queue here (`Workqueue` retention);
  a queryable log would be a new operational pattern, and "last N for user,
  kinds in set, after cursor" is a query, not a subscription.
- **Valkey sorted set per user**: Valkey is treated as a cache in this codebase
  (locks, rate limiters, pub/sub); history that silently truncates on failover
  defeats the purpose, and kind filtering needs extra keys or client-side work.
  Web-hook deliveries, the same shape of data, already live in Postgres with a
  pruner.

## Storage

New table `NotificationHistory` in `NotificationDbContext`, entity
`DbNotificationHistoryItem` in `Notifications.Service/Db/`.

| Column | Type | Notes |
|---|---|---|
| `Id` | string, PK, collation C | `{NotificationId}:{SentAt ticks}`. An at-least-once redelivery of the same event maps to the same id and hits `ConflictStrategy.DoNothing` instead of duplicating. |
| `Seq` | long | `VersionGenerator.NextVersion()` at insert; the paging cursor. Monotonic per node, unique enough for keyset paging (same as `DbWebHookDelivery.Seq`). |
| `UserId` | string, collation C | Owner. |
| `Kind` | `NotificationKind` | Stored as int. |
| `ChatId` | string, collation C | From `ChatNotification.ChatId`; empty for non-chat kinds. |
| `EntryLid` | long | From `ChatEntryNotification.EntryId.LocalId` or `ChatEntryRelatedNotification.EntryLid`; 0 when the notification anchors no entry. |
| `AuthorId` | string, collation C | `ChatNotification.AuthorId`, empty when absent. |
| `Title`, `Text` | string | Snapshot of the notification at the moment it fired. |
| `SentAt` | DateTime UTC | `Notification.SentAt`. |
| `CreatedAt` | DateTime UTC | Insert time; the pruner's cutoff. |

Indexes: `(UserId, Seq)`, `(UserId, Kind, Seq)`, `(CreatedAt)`.

One migration in `Notifications.Service.Migration`.

## Write path

A new `[EventHandler] OnUserNotifiedEvent(UserNotifiedEvent, CancellationToken)`
on `INotificationsBackend`, implemented in `NotificationsBackend`. It sits next to
`IWebHooksBackend.OnUserNotifiedEvent`, which consumes the same event.

- Logged kinds: `Mention`, `Reply`, `Reaction`, `Attention`, `Thread`,
  `Invitation`, `Conversation`, `IncomingCall`. `Message` (a row per incoming
  message in every subscribed chat) and `SpeechStarted` are skipped. The set is
  a static predicate, `NotificationHistory.IsLogged(NotificationKind)`, so the
  backend, the read query default and the docs agree.
- Pre-suppression by design: the event fires before the dormant and
  active-reader checks and before the active-set mode re-check, so what is
  logged is what the web hook sends. A chat's mute setting applies earlier, at
  fan-out (`ListSubscribedUserIds` / the mention filter with `Important`
  importance), so a mention in a muted chat never becomes an event and is not
  logged either.
- Insert uses an operation DbContext, `Operation.MustStore(false)`, and relies
  on `ConflictStrategy.DoNothing` for the idempotent redelivery case.
- `NotificationsBackend.OnNotify` today enqueues the event before stamping
  `SentAt`, so a first-seen notification arrives with a default `SentAt` and the
  id above would collapse. The fix: stamp `SentAt` first (it needs
  `GetUserNotificationInfo`, which is read anyway), then enqueue, still before
  the dormant early-return. Web hooks gain a real timestamp too.

## Retention

`NotificationHistoryPruner : WorkerBase` in `Notifications.Service`, a copy of
`WebHookDeliveryPruner`: first run after 5 minutes, then hourly with 25%
jitter; deletes rows with `CreatedAt < now - Constants.Notification.HistoryRetention`
(30 days). Registered in the notifications service module the same way the
delivery pruner is in the chat module. No per-user cap in v1.

## Read API

On `INotifications`, next to `ListActive`:

```csharp
// Not a compute method on purpose: every insert would otherwise invalidate every
// cursor variant. Agents poll it; nothing reactive depends on it.
Task<ApiArray<NotificationHistoryItem>> ListHistory(
    Session session, NotificationHistoryQuery query, CancellationToken cancellationToken);
```

Models, in `Api/Notifications/` (`ActualChat.Notifications`), MessagePack +
DataContract like their neighbours:

```csharp
public sealed partial record NotificationHistoryQuery
{
    public ApiArray<NotificationKind> Kinds { get; init; }   // empty = all logged kinds
    public long AfterSeq { get; init; }                      // 0 = from the start
    public int Limit { get; init; } = 64;                    // clamped to 1..256
    public bool NewestFirst { get; init; }                   // default oldest first, for cursor walks
}

public sealed partial record NotificationHistoryItem(long Seq, NotificationKind Kind)
{
    public Moment SentAt { get; init; }
    public ChatId ChatId { get; init; }
    public ChatEntryId? EntryId { get; init; }
    public AuthorId? AuthorId { get; init; }
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
}
```

`NotificationsService.ListHistory` resolves the account from the session and
calls `INotificationsBackend.ListHistory(UserId, NotificationHistoryQuery,
CancellationToken)`, a plain backend method that runs the query:
`UserId == x`, `Kinds` filter when non-empty, `Seq > AfterSeq` when
`NewestFirst` is false (`Seq < AfterSeq` when true and `AfterSeq > 0`), ordered
by `Seq`, `Take(Limit)`. A guest or missing account gets an empty array.

Kinds that are not in the logged set are ignored by the filter (they can never
match); an all-unlogged filter returns an empty array.

## MCP tool

New `McpNotificationTools` in `Mcp/Tools/`, registered in `McpModule` with
`.WithTools<McpNotificationTools>(serializerOptions)`.

```
list_notifications
  kinds        string[]?  e.g. ["mention", "reaction"]; empty = all logged kinds
  afterSeq     long?      cursor; return items with seq > afterSeq (or < when newestFirst)
  limit        int        default 64, capped at 256
  newestFirst  bool       default false
```

Result `McpListNotificationsResult(McpNotification[] Items, long? NextAfterSeq)`
with `McpNotification(long Seq, string Kind, long At, string ChatId,
long? EntryId, string? AuthorId, string Title, string Text, ExternalMessage? Message)`.

- `Kind` is the enum name lower-cased, the same spelling the web hook
  `notification` payload uses; `kinds` input is parsed case-insensitively, an
  unknown name is a tool error.
- `At` is `SentAt` as Unix milliseconds, like `ExternalMessage.CreatedAt`.
- `Message` is the shared `ExternalMessage` (via `ExternalMessageExt.ToExternalMessage`)
  when the row anchors an entry that `IChats.GetEntry(Session, ...)` still
  returns; `null` otherwise. Resolving through the session means a chat the
  agent has since left yields no text.
- `NextAfterSeq` is the last item's `Seq`, or `null` when the page is empty.
  The description tells the agent to persist it and pass it back as `afterSeq`.

## Access control

The service only ever queries the session's own `UserId`. History rows carry
no ACL of their own: the snapshot (title, text) is what the user was already
shown as a banner, and the message body is re-resolved through the session.

## Testing

`tests/Notifications.IntegrationTests/NotificationHistoryTest.cs`:

- a mention lands in history and is still there after the recipient reads the
  chat (which removes it from `ListActive`);
- `Kinds` filter returns only the requested kinds; `AfterSeq` walks pages
  without overlap or gaps; `NewestFirst` reverses the order;
- a plain message notification is not logged;
- redelivering the same `UserNotifiedEvent` does not duplicate the row;
- `NotificationHistoryPruner.RunOnce` deletes rows past the cutoff and keeps
  newer ones.

`tests/Mcp.IntegrationTests/McpNotificationToolsTest.cs`:

- tool is listed with the expected schema (extend `McpToolSchemaTest`);
- a mention of the API key's account shows up with `kind == "mention"` and a
  populated `message`; `kinds: ["reaction"]` hides it; cursor round trip.

## Docs

- `docs/integrations/notifications-api.md`: the read API and the MCP tool, what
  is logged, retention, and cursor semantics; linked from `docs/index.md` next
  to the web hooks page.
- `docs/notifications.md`: one paragraph on the history log in the server-side
  table and lifecycle section, and the note that `ListActive` is not a history.
- `docs/api-index.md`: the new types.

## Out of scope

- Read / acknowledged state on rows: agents keep their own cursor.
- Any UI over the history.
- Per-user row caps, or logging `Message` traffic.
- Incoming web hooks (phase 2 of the web hooks work).

## Reuse

Existing abstractions: `UserNotifiedEvent` (Backend/Events), `VersionGenerator`
for `Seq`, `ConflictStrategy.DoNothing`, `WorkerBase` + `AsyncChain` pruner
shape, `ExternalMessage` and `ExternalMessageExt.ToExternalMessage`,
`McpSessionAccessor`, `NotificationKind`, `ApiArray`, the
`ShardedDbServiceBase` DbHub in `NotificationsBackend`.

New components and placement: `NotificationHistoryQuery` / `NotificationHistoryItem`
go to `Api/Notifications/` (shared contracts project, reachable by clients and
the MCP host). The `IsLogged` predicate lives with them. The pruner and the
entity are service-local. Nothing here is generic enough for `Core`.
