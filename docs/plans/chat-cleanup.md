# Chat cleanup and retention

## Scope and behavior

Cleanup follows the visibility-boundary design, separate from the import-oriented
Chat History Reset/Export and Chat Maintenance Mode plans. A monotonically increasing
minimum visible entry ID makes older history unreadable immediately. A durable
per-chat flow reclaims it in batches while the chat remains usable.

Owners can advance the boundary or clear a captured history range. New messages
posted after the clear confirmation opens survive. The boundary never moves
backwards, including when retention is disabled or lengthened. Settings offer off,
one day, seven days, and thirty days; chat details show the retention period.
Deleting a recent suffix remains the existing ordinary batch-removal operation.

Retention waits until a whole conversation expires and stops before an active
conversation. Explicit advancement inside a conversation hides its entire summary;
remaining messages above the boundary stay individually readable. Conversation IDs
are not reassigned. A hidden thread anchor immediately hides descendant entries.

Entries below the boundary are permanently inaccessible through backend entry
reads, including reads with includeRemoved and changed-entry enumeration. Ordinary
soft removals remain distinct and can be restored during the one-hour grace period.
The noncomputed ListEntryIdsForCleanup query returns only bounded, ascending IDs
within a requested range. PurgeEntries rechecks every candidate under a chat lock.

Account deletion marks exclusively owned chats for removal, immediately hides them,
and schedules the same cleanup flow. The internal IsRemoving tombstone prevents
writes and resurrection until the flow removes the chat after draining its contents.
Messages in shared chats, including inherited thread/place identities, are purged
in bounded operations while preserving other authors. Summaries containing those
messages are removed too. Cleanup does not own or change maintenance mode.

## Reuse

### Existing abstractions to reuse

- Chat, ChatDiff, DbChat, IChats, IChatsBackend, owner permissions, and version checks.
- Existing Chats_RemoveEntries and ChatsBackend_ChangeEntry for ordinary removal;
  ChatsBackend_RemoveAttachments, content-index update commands, MediaBackend_Change,
  and durable operation events for permanent-removal side effects.
- Flow, FlowHub.NewResumeEvent, staged resumes, and operation events. Cleanup flow
  identity contains only the chat ID; current work is persisted in chat state.
- Fusion computed dependencies, Constants.Chat.EntryIdTiles,
  Constants.Chat.ConversationIdTiles, IConversationsBackend, and ILiveSessionsBackend.
- Existing account-deletion orchestration and AuthorsBackend.Remap/Get for inherited
  identities; existing chat removal finalizes roles, authors, aliases, and metadata.
- Existing form/select/confirmation components and typed localization catalogs.
- SharedAppHostTestBase, WebClientTester, Computed.Capture, and FlowSerializationTestBase.

### Reusability of new components

The boundary and retention contracts belong in the shared chat API/backend contracts.
ChatEntriesPurgedEvent belongs in Backend because chat and search consume it.
ChatCleanupFlow and purge orchestration remain in Chat.Service: placing them in
Core.Server would introduce domain-specific storage dependencies without another
consumer. The existing shared flow and ID-allocation infrastructure needs no new
generic abstraction.

## Implementation

1. Persist MinVisibleEntryLid, RetentionPeriod, RemovedAt, IsPurged, and IsRemoving.
   New wire fields are appended; migrations add columns with compatible defaults.
2. Advance boundaries monotonically under a chat row lock, reject future boundaries,
   honor expected versions, and persist the flow wakeup in the same operation.
3. Make entry tiles/ranges, direct and changed-entry reads, attachments, conversations,
   pins, search hits, media/file/link tabs, translations, reactions, and thread access
   respect visibility before background deletion finishes.
4. Give affected computations a boundary dependency. Advancing it invalidates cached
   history and conversation projections. Deleted-chat permissions bypass the normal
   permission-consolidation delay.
5. Run a 100-entry cleanup batch per command. Hidden entries are immediately eligible;
   ordinary removed entries receive a one-hour grace period, including legacy rows
   without a removal timestamp. A completed pass sleeps for fifteen minutes; a full
   batch resumes immediately. Retention changes and removals wake the same flow.
6. Purge attachments, unreferenced media/thumbnails/audio/dubs, reactions, mentions,
   languages, translations, shared locations, content indexes, and search records.
   Preserve media referenced by other entries/chats. Emit ID-only purge events.
7. Preserve one content-free tombstone at the highest allocated entry ID so Redis
   loss cannot make the allocator reuse a deleted ID. Reclaim an older tombstone
   once a newer entry provides the durable high-water mark.
8. Serialize entry/copy/index/translation/conversation writes against cleanup and
   reject stale writes targeting permanently hidden content.
9. Mark account-owned chats, drain them with the flow, then finalize chat removal.
   Purge shared-chat messages in bounded outer operations using validated authors.
10. Add localized retention controls and clear-history confirmation; verify backend,
    account lifecycle, serialization, localization, and the browser workflow.
    Create a PR directly; no issue and no prepare-merge, as requested.

## Critical invariants

- Entry-reading APIs never expose records below the boundary, even to backend callers.
- Garbage enumeration returns IDs only and is not computed/cached.
- Retrying or overlapping cleanup cannot resurrect history or delete a newer tail.
- The purge command validates chat, IDs, eligibility, and account ownership anew.
- Hidden thread anchors stay hidden even after their parent chat is physically deleted.
- Shared/forwarded media remains while another reference exists.
- Ordinary removal and irreversible cleanup retain distinct restore semantics.

## Validation

- Fourteen cleanup integration regressions plus both existing chat account-removal
  tests pass: cache invalidation, ownership, monotonicity, concurrent new messages,
  ID-only garbage enumeration, candidate revalidation, grace period, multi-batch
  completion, media references, conversations, threads, delayed metadata writes,
  deferred owned-chat deletion, and shared-thread account removal.
- Existing content-index integration suite: seven tests passed.
- Full Users RemoveOwnAccountTest passed.
- Cleanup contract and flow serialization tests, and sixteen localization tests.
- User-started server loop rebuilt frontend and server successfully, including
  native WebAssembly linking, and applied both migrations.
- Chrome on local.voxt.ai: created a disposable private chat, sent a message, saved
  one-day retention, verified chat details, and cleared history while a second tab
  posted a new message. Old history disappeared and the new message survived.
- Standalone isolated CI-filter build hit local Emscripten exit code 9009. The
  server-loop build of the application succeeded with its configured environment.
