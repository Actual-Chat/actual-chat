# Reaction notifications in the notifications panel

Status: approved design, 2026-08-30

## Problem

The navbar notifications section shows chat-derived state only. `NotificationsNavbarWidget`
renders a `ChatList` over `ChatListFilter.Unread / UnreadPeople / UnreadMentions`, so a
reaction to one of your messages never appears there.

Reaction notifications already exist end-to-end on the server: `ReactionNotification`
is raised by `NotificationsBackend.OnReactionChangedEvent`, pushed to devices, and
returned to the client by `INotifications.ListActive`. Nothing on the web/app UI reads
them. The gap is presentation, not delivery.

## Decisions

1. The Reactions tab is an **entry-level feed** — one row per reacted message — not a
   chat list. A chat-level tab would duplicate what the All-tab badge already says.
2. A chat with only a reaction **does** appear in All. Badge precedence per row:
   `@` (own mention) > unread count > reaction emoji.
3. Several people reacting to the same message **accumulate** into one notification.
4. Accumulation happens **server-side, in the notify path** (`MergeWith`, reached from
   `NotificationsBackend.cs:1742`), never on the client.
5. Accumulation **freezes at a cap** — past it the notification stops changing and stops
   re-pushing.
6. Old clients keep working unchanged: the fields they read continue to describe the
   **latest** reaction.
7. In the notifications panel, a reaction-only chat sorts by the reaction's `SentAt`.

## Reuse

Existing abstractions this builds on:

- `INotifications.ListActive` — already returns reactions to the client.
- `NotificationExt.GetChatLink` — the entry deep link for a feed row's tap target.
- `ChatEntryRelatedNotification.MergeWith` — the accumulate-authors pattern to copy:
  `AuthorIds`, `Constants.Notification.MaxTrackedAuthors`, out-of-order tolerance, and
  the reference-equality no-op that suppresses a redelivery's push.
- `EmojiIcon.razor`, `ReactionBadge.razor` (`UI.Blazor.App/Components/Reactions/`) —
  emoji rendering.
- `UnreadCount.razor`, `ChatListItem.razor`, `ChatListFilter`, `TabPanel` — the All and
  Mentions side needs no new components.
- `Emoji` (`Api/Identifiers/Emoji.cs`) — a `StringIdentifier` with a MessagePack
  formatter, so `ApiArray<Emoji>` serializes as-is.
- `MarkupConsumer.ReactionNotification` — the quoted-message text is already composed
  server-side.

New shared component and its placement:

- **`NotificationsUI`** — a scoped compute service in `UI.Blazor.App/Services/`,
  registered beside `NotificationsPanelUI` in `BlazorUIAppModule.cs:64`. It wraps
  `ListActive` in `[ComputeMethod]`s (`ListByKind`, `GetByChat`) so the feed, the
  All-tab filter, the row badge and the navbar bell share one computed instead of
  capturing `ListActive` four times over.

  Placement: `UI.Blazor.App/Services/`, not a feature folder — four existing services
  (`NotificationReconciler`, `AppIconBadgeUpdater`, `SeenNotificationDismisser`,
  `IncomingCallUI`) each capture `ListActive` today and are the obvious later callers.
  It is not `ActualChat.Core`: it depends on `AppUIHub` and Blazor scoping.

  CODING_STYLE rule 14 ("extend an existing UI service instead of adding a new one")
  was weighed. `NotificationsPanelUI` is the nearest candidate, but it owns a
  grace-period timer over the *chat* list and holds mutable per-filter state; the
  notification set is a different lifetime and a different source. A new service is
  justified here.

## Design

### Server — accumulating reactors

`ReactionNotification` gains two persisted members:

```csharp
[DataMember(Order = 9), Key(9)]
public ApiArray<AuthorId> AuthorIds { get; init; }
[DataMember(Order = 10), Key(10)]
public ApiArray<Emoji> Emojis { get; init; }
```

Keys 9 and 10 are free **within this subtype**. MessagePack union members serialize
independently, and siblings already reuse the range — `CallNotification` uses 9,
`ConversationNotification` uses 9 and 10. Because `Notification.Actions` occupies key
16, the serialized array length stays 17 either way: 9 and 10 were nil holes before.
Old clients therefore see an array of unchanged shape and skip the two members.

`MergeWith` override, modelled on `ChatEntryRelatedNotification`:

- Union the incoming `AuthorId` / emoji into the accumulated arrays.
- If neither is new, return the **existing instance** — an at-least-once redelivery
  must be a no-op so `ReferenceEquals(before, after)` at `NotificationsBackend.cs:1744`
  suppresses the duplicate push.
- If `AuthorIds.Count` has reached `Constants.Notification.MaxReactionAuthors`, return
  the existing instance unchanged. This is decision 5: past the cap the row already
  reads "Bob, Kate and 3 others", and further reactors would only move a number.
  Trade-off accepted: the "+N" count stops growing on a message that keeps collecting
  reactions, and those reactions raise no further push.
- Otherwise return the **newest of the two** (by `SentAt`) with the accumulated arrays,
  `Version` / `CreatedAt` carried over from the existing one and
  `SentAt = Moment.Max(...)`. Taking the newest rather than always the incoming keeps an
  out-of-order older event from regressing the display fields.

New constant `Constants.Notification.MaxReactionAuthors` (proposed 5) — deliberately
separate from `MaxTrackedAuthors = 8`, which governs message coalescing and should not be
retuned by a change to reactions.

### Old-client compatibility

`Title`, `IconUrl`, `AuthorId` and `Text` continue to describe the **latest** reaction —
which is exactly today's behaviour, since the base `MergeWith` lets the newer instance
win. The override preserves it by returning the newest of the two. An old client that
knows nothing of `AuthorIds` / `Emojis` therefore shows "Kate reacted to your …",
updating as new reactions arrive, and never renders a blank or stale row.

Push banners are unaffected for the same reason: the banner body is `Notification.Text`.

### Client — `NotificationsUI`

Compute methods over `ListActive`:

- `ListByKind(NotificationKind, CancellationToken)` — sorted `SentAt` descending; feeds
  the Reactions tab.
- `GetByChat(ChatId, NotificationKind, CancellationToken)` — the per-chat projection the
  All-tab filter, the badge and the bell read. Returns a value-comparable struct so
  Fusion can consolidate it, matching `ChatUnreadState`.

### UI — the Reactions tab

`NotificationsNavbarWidget` gains a fourth tab, gated in `HasTab` like the other three.
Its content is not a `ChatList`:

- `ReactionNotificationList.razor` — the feed, `SentAt` descending.
- `ReactionNotificationItem.razor` — avatar stack from `AuthorIds`, emoji from `Emojis`
  via `EmojiIcon`, quoted text from `Notification.Text`, chat title, relative time.
  Click navigates via `NotificationExt.GetChatLink`.

Per `docs/ui/components.md` these live in a dedicated folder with one
`reaction-notification-list.css` imported from `UI.Blazor.App/styles.css`; the item is a
sub-component and gets no CSS file of its own.

### UI — the All tab

- `ChatListFilter.Unread` and `UnreadPeople` widen to
  `UnmutedUnreadCount > 0 || HasUnreadOwnMention || HasUnreadReaction`.
- `ChatInfo` gains `HasUnreadReaction`, fed from `NotificationsUI.GetByChat`.
- `UnreadCount.razor` gains a third state: the emoji, rendered only when there is no
  count and no mention. `ChatUnreadState` grows a matching field so
  `ChatUI.GetUnreadState` keeps consolidating.
- Sorting: the notifications panel passes an order that maxes a chat's last-event time
  with its newest reaction `SentAt`. Scoped to this panel — the main chat list keeps
  plain `ByLastEventTime`.
- `LeftPanelButtons.ComputeState` adds the reaction check, or the bell never appears in
  a reaction-only state.

### Localization

New keys in `Strings.<lang>.json` plus typed members in `LocalizedStringsLocalizerExt.cs`:
the tab title, and the reactor list ("{0} and {1} others"), which is counted text and so
must be localized rather than composed in the component.

## Known gaps, accepted

- **Un-reacting does not retract.** `OnReactionChangedEvent` returns early on
  `ChangeKind.Remove`, so a removed reaction leaves the accumulated reactor in place.
  Pre-existing behaviour; out of scope.
- **The feed shows unseen reactions, not history.** `SeenNotificationDismisser` clears a
  reaction once its message is on screen, and the anchor is the recipient's own message —
  so opening the chat empties the row behind you, and `ReactionLifespan` is one day. The
  tab means "reactions you have not seen". A browsable history would query reactions
  rather than notifications and is a different change.
- **Muted and important-only chats produce no reaction notifications at all**
  (`NotificationHelper.IsDeliverable`), so they cannot appear in-app either.

## Testing

- `MergeWith`: accumulation, distinct-only union, out-of-order events, redelivery
  returning the same reference, and the freeze at `MaxReactionAuthors`.
- Serialization round-trip for the new keys, plus an old-shape blob deserializing with
  empty `AuthorIds` / `Emojis` — extends `NotificationSerializationTests`.
- `NotificationsUI` projections: kind filtering, `SentAt` ordering, per-chat grouping.
- Filter and badge precedence: mention over count over emoji, and a reaction-only chat
  appearing in All.
