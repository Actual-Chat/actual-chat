# Chat cleanup and retention

## Scope and behavior

Cleanup is a batch loop, separate from the import-oriented Chat History Reset/Export
and Chat Maintenance Mode plans. A durable per-chat flow, `ChatPurgeFlow`, purges
what has to go in batches while the chat stays usable. Nothing hides history up front:
an entry stays readable until the loop purges it. It's the chat's only cleanup: cleared
history, expired entries, removed accounts' entries and the whole chat on removal all go
through it. Requests reach it as messages in its inbox (`IInboxProcessingFlow`), posted
as operation events, so each arrives once the operation that made it commits.

Owners - either side, in a peer chat - can wipe the history: everything, or the last N
minutes, hours or days. `Chats_WipeHistory` names the first entry to go, and the backend
fixes the last one when the request arrives, so messages posted after it survive. A
`WipeHistory` message carries that range. A range that leaves nothing visible before it
becomes the flow's boundary, which only moves forward and is purged up to; any other one
waits in the flow's ordered list of ranges until it's purged. Retention works like the
boundary without storing anything: each run purges what has expired by then. Both a wipe
and a retention change post a `HistoryChangedEntry` naming who did it. Settings show the
retention under "History retention" (10 minutes to 100 years); before a change is saved, the
dialog estimates what it would delete right away and asks to confirm it, and it links to
the wipe dialog. The chat header menu shows the retention to those who can change it.

Retention waits until a whole conversation expires and stops before an active
conversation, and before the anchor of a thread whose replies haven't expired yet. A
conversation that loses some of its messages to any purge is re-summarized five minutes
later; one left with no messages is purged. Conversation IDs are not reassigned. A thread
goes with its anchor: once the loop purges an anchor, the thread is marked for removal.

A purge deletes entry rows, so changed-entry enumeration never returns them; indexers learn
of purged entries from `ChatEntriesPurgedEvent` (search) and the direct content-index updates
the purge makes (media, files, links). Changed-entry enumeration reports only the entries of
removed authors as removed. Ordinary soft removals remain distinct and can be restored during
the 5-minute grace period.
PurgeEntries rechecks every candidate under a chat lock.

## Chat removal

A chat goes into Removal maintenance (`MaintenanceMode.Removal`, owned by the removal
operation) the moment it is marked for removal, and `IsRemovalPending` reports it; a
thread inherits it from its parent. The backend still sees the chat while
`ChatPurgeFlow` drains it, but clients don't: `IChats.Get` returns null, the chat's
rules deny everything, and backend writes to its entries are rejected. Once its content
is gone, the flow deletes the chat and clears the maintenance. A Place keeps its chats'
maintenance on the root key: the whole Place goes under an untargeted record, a single
chat of a Place that stays is added to the record's targets. The Place itself is
removed last, after all its other chats.

Account deletion marks exclusively owned chats for removal. A chat with no owner left
would be unmanageable, so those are the only chats that go; the delete-account dialog
lists them (and the Places among them) as links, so the owner can hand any of them over
first. An import running on such a chat is ended first: nobody would be left to end it.
A chat under another operation's maintenance stays. Messages in shared chats, including
inherited thread/place identities, are purged in bounded operations while preserving
other authors. A thread the account started stays with everyone else's replies: its
anchor becomes a content-free tombstone, which is what lets clearing remove the thread
later. A summary covering other authors too is re-summarized rather than deleted; one
left with nothing is purged.

## Account tombstones

A deleted account keeps its row with `AccountStatus.Removed`, stripped of everything
that identified its owner, so its ID can never be handed out again. `AccountsBackend.Get`
reports it as missing, which makes `AuthorsBackend.Get` return null for its authors,
which in turn takes its messages out of every chat tile at once - long before the purge
reaches the rows. `Exists` on accounts and authors is the cheap form of that question,
consolidated so the avatar and KVAS dependencies behind `Get` cannot invalidate tiles.

Deleting the account posts a `RemoveAuthorEntries` message to the cleanup of every chat
its authors could have written to: a chat with its threads, or every chat of a Place. Each
chat's cleanup purges that author's entries there a batch per resume, and then removes the
author's row in that chat - the authors outlive the messages, as they are how the messages
are found.

## Reuse

### Existing abstractions to reuse

- Chat, ChatDiff, DbChat, IChats, IChatsBackend, owner permissions, and version checks.
- IMaintenancesBackend and its ownership rules for marking a chat or Place as removed.
- Existing Chats_RemoveEntries and ChatsBackend_ChangeEntry for ordinary removal;
  ChatsBackend_RemoveAttachments, content-index update commands, MediaBackend_Change,
  and durable operation events for permanent-removal side effects.
- Flow, FlowHub.NewResumeEvent, staged resumes, and operation events. The cleanup flow
  goes dormant when nothing is left to reclaim and no retention is set; every source of
  new work schedules its own resume event.
- Fusion computed dependencies, Constants.Chat.EntryIdTiles,
  Constants.Chat.ConversationIdTiles, IConversationsBackend, and ILiveSessionsBackend.
- Existing account-deletion orchestration and AuthorsBackend.Remap/Get for inherited
  identities; existing chat removal finalizes roles, authors, aliases, and metadata.
- Existing form/select/confirmation components and typed localization catalogs.
- SharedAppHostTestBase, WebClientTester, Computed.Capture, and FlowSerializationTestBase.

### Reusability of new components

The clear and retention contracts belong in the shared chat API/backend contracts.
ChatEntriesPurgedEvent belongs in Backend because chat and search consume it.
ChatPurgeFlow and purge orchestration remain in
Chat.Service: placing them in Core.Server would introduce domain-specific storage
dependencies without another consumer. The existing shared flow, maintenance and
ID-allocation infrastructure needs no new generic abstraction.

## Implementation

1. Persist RetentionPeriod, RemovedAt and IsPurged. New wire fields are appended; the
   migration adds columns with compatible defaults.
2. Validate a clear (owner, expected version, not past the end of the chat) and post it to
   the chat's cleanup inbox; the flow keeps the furthest boundary.
3. Mark removal with Removal maintenance; hide such chats from IChats, deny their rules,
   and reject backend writes to them.
4. Run a 100-entry cleanup batch per command. Entries up to the cleared or expired
   boundary are immediately eligible; ordinary removed entries receive a 5-minute grace
   period, including legacy rows without a removal timestamp. A full batch resumes
   immediately; a chat with retention sleeps for the cleanup interval; anything else
   goes dormant once no removed entries are left at all.
5. Purge attachments, voice and dub audio, reactions, mentions, languages, translations,
   shared locations, content indexes, and search records. Emit ID-only purge events.
   Attachment media and thumbnails stay: a forward copies their IDs into other chats,
   and nothing counts these references yet. Chat removal deletes only the chat picture
   and, for a Place, its picture and background. Seeded system media (`system-icons:*`)
   is shared by every chat that shows it and is never deleted.
6. Preserve one content-free tombstone at the highest allocated entry ID so Redis
   loss cannot make the allocator reuse a deleted ID. Reclaim an older tombstone
   once a newer entry provides the durable high-water mark.
7. Serialize entry/copy/index/translation/conversation writes against cleanup and
   reject stale writes targeting purged content. Never serve a translation whose source
   is gone or whose content hash no longer matches.
8. Mark account-owned chats, drain them with the flow, then finalize chat removal.
   Sign out every device, tombstone the account, purge its messages in bounded passes,
   and remove its authors last.
9. Add localized retention controls and clear-history confirmation; verify backend,
   account lifecycle, serialization, localization, and the browser workflow.

## Critical invariants

- A chat in Removal maintenance is gone to clients and closed to writes until it is deleted.
- Retrying or overlapping cleanup cannot resurrect history or delete a newer tail.
- The purge command validates chat, IDs, eligibility, and author ownership anew, without
  consulting the account - which by then may be a tombstone.
- Authors outlive the messages that are found through them.
- A thread goes with its anchor once the anchor is cleared.
- Attachment media is never deleted until media references are counted.
- Ordinary removal and irreversible cleanup retain distinct restore semantics.
