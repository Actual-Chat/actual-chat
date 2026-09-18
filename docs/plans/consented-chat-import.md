# Consented chat import

## Decisions

Owners enable Import mode from chat edit or Place edit. A Place session covers all
its chats through inherited maintenance. The maintenance screen contains each
member's session-only consent checkbox. Owners see consenting/nonconsenting counts,
a short nonconsenting preview, a full paginated list, and End import. Consent never
changes membership or ordinary permissions; the owner must consent too.

Use a unique ChatImportSession, with consent keyed by session and UserId. Place
consent applies across that Place but does not grant membership in private chats.
Only current owners may import, only for current members with active consent. End
invalidates further imports and consent without deleting imported data. Restarting
requires fresh consent. Reject overlapping scopes and other maintenance operations.

Assume history has been prepared before import. Never clear history on start or
allow regular removal/reset during import. Regular posting, edits, removal and
other content mutations are blocked for everyone, including owners. Only consent,
import operations, reads and ending the session remain available for this workflow.

## Batches

ChatImports_ImportEntries accepts a target chat, import session and bounded entries
with explicit author UserId, BeginsAt, text and attachments. Sort each batch by
BeginsAt, keeping original indices for results. Within the write transaction read
the last available visible entry and reject every timestamp at or before its date.
Accepted entries must have strictly increasing dates; equal dates are rejected.
Rejected entries do not advance the tail. Do not rewrite source dates.

Return one ChatImportEntryResult per original input position, containing its assigned
entry ID or ExceptionInfo. ExceptionInfo is a struct with None/IsNone, so a nullable
exception is unnecessary. Missing consent, invalid author/media and old/duplicate
timestamps are per-item failures. All accepted entries and their normal operation
events commit in ONE Chat DB transaction. Unexpected persistence failures roll the
entire transaction back. Request-level authorization failures reject the command.

Keep normal entry creation/events/indexing. Suppress notification production and
delivery for every maintenance kind. Persist suppression on imported events so
processing after End does not notify. Include mention/reminder/reaction/call and
queued push paths; silent pushes are not a substitute for suppression.

## Attributed uploads

Dedicated import operations authenticate the owner and validate the attributed
member's consent. Reuse existing byte upload and media processing. Record both
uploader and attributed user, scope/session and target chat. Recheck consent when
binding attachments to an imported message. Uploads and Media DB processing are
outside the Chat batch transaction; Chat-owned attachment binding commits with
entries. Ordinary upload APIs remain self-attributed.

## Reuse

### Existing abstractions to reuse

The type indexes and current source were checked. Reuse MaintenanceMode,
IMaintenancesBackend, MaintenanceExt.ToMaintenanceKeyChain, the maintenance shell,
IAuthorsBackend.GetByUserId, IChatsBackend/IRolesBackend permission checks,
ChatEntryDiff, ChatsBackend_ChangeEntry and CreateAttachments, ChatEntryChangedEvent,
Fusion operation transactions/events and ApiCommand deduplication, ExceptionInfo,
IUploads/IUploadsBackend, IMediaSaver and existing processors. Preserve generated
serialization/AOT conventions and existing Blazor controls/localization.

The old IChatMaintenances plan is superseded by Users-backed object maintenance.
No import session/consent or batch import abstraction exists yet. Users, Chat and
Media DB commands do not share one local transaction; lifecycle must recover safely
across those boundaries. Add concurrency tests rather than assume guards drain
already running writes.

### Reusability of new components

Import models/contracts belong beside chats in Api, Api.Contracts and Chat.Contracts,
not generic Core. Persistence and batch validation belong in Chat.Service; shared
maintenance ownership/guard primitives belong in Core.Server with Users persistence,
since reset and other operations will reuse them. Import media attribution belongs
at the existing upload boundary with Chat-owned bindings. Notification suppression
belongs in existing shared event contracts and Notifications.Service. UI belongs in
UI.Blazor.App with shared checkbox/member-list controls. No new TypeScript helper
is planned; any broadly useful one belongs in src/nodejs/src.

## Implementation and validation

- [ ] Session/consent contracts, persistence/migrations, lifecycle and owner/member reads.
- [ ] Maintenance ownership, inherited scope and mutation race protection.
- [ ] Sorted transactional batches with original-order results and explicit author/date.
- [ ] Attributed upload/media commands and consent validation at binding.
- [ ] General notification suppression, including delayed imported events.
- [ ] Chat/Place edit entry points and maintenance consent/owner UI.
- [ ] Integration tests: mixed consent, Place inheritance, unauthorized writes, sorting,
      equal/old timestamps, batch rollback, concurrent batches/revoke/end, uploads,
      delayed notifications and ordinary behavior after End.
- [ ] Build affected projects/CI filter, regenerate serialization/localization as needed,
      and run appropriate unit/integration tests. Do not use the user's server loop
      or Chrome MCP sessions. Work in the isolated consented-chat-import worktree.

Playable voice with transcript maps, export/reset/restore automation, source-format
adapters and thread reconstruction remain separate. No automatic consent notices.
