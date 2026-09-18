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
ChatEntry, PrepareTextEntryForSave, DbNextLocalId, DbChatEntryAttachment, ChatEntryChangedEvent,
Fusion operation transactions/events and ApiCommand request IDs, ExceptionInfo,
IUploads/IUploadsBackend, IMediaSaver and existing processors. Preserve generated
serialization/AOT conventions and existing Blazor controls/localization.

The old IChatMaintenances plan is superseded by Users-backed object maintenance.
The implementation adds import session/consent and batch records. Users, Chat and
Media DB commands do not share one local transaction; lifecycle must recover safely
across those boundaries. Add concurrency tests rather than assume guards drain
already running writes.

### Reusability of new components

Import models/contracts belong beside chats in Api, Api.Contracts and Chat.Contracts,
not generic Core. Persistence and batch validation belong in Chat.Service; shared
maintenance ownership belongs in Core.Server with Users persistence. ChatImportGuard
stays in Chat.Service because its transaction lock and queries depend on ChatDbContext;
a generic Core.Server guard would couple shared infrastructure to Chat persistence. Import media attribution belongs
at the existing upload boundary with Chat-owned bindings. Notification suppression
belongs in existing shared event contracts and Notifications.Service. UI belongs in
UI.Blazor.App with shared checkbox/member-list controls. No new TypeScript helper
is planned; any broadly useful one belongs in src/nodejs/src.

## Implementation and validation

- [x] Session/consent contracts, persistence/migrations, lifecycle and owner/member reads.
- [x] Maintenance ownership, inherited scope and mutation race protection.
- [x] Sorted transactional batches with original-order results and explicit author/date.
- [x] Attributed upload/media commands and consent validation at binding.
- [x] General notification suppression, including delayed imported events.
- [x] Chat/Place edit entry points and maintenance consent/owner UI.
- [x] Integration tests: mixed consent, Place inheritance, unauthorized writes, sorting,
      equal/old timestamps, batch rollback, concurrent batches/revoke/end, uploads,
      delayed notifications and ordinary behavior after End.
- [x] Build affected projects/CI filter, regenerate serialization/localization as needed,
      and run appropriate unit/integration tests. Do not use the user's server loop
      or Chrome MCP sessions. Work in the isolated consented-chat-import worktree.

Playable voice with transcript maps, export/reset/restore automation, source-format
adapters and thread reconstruction remain separate. No automatic consent notices.

## Implemented API contract

Owners call `ChatImports_Start` with a group chat ID or a Place root chat ID. The
returned session ID is required by every consent, upload, batch and end command.
`ChatImports_SetConsent` always uses the caller's account; owners also consent
explicitly. Any current scope owner can call `ChatImports_End`.

`ChatImports_ImportEntries` accepts 1–100 entries. Each entry carries `UserId`,
`BeginsAt`, `Content`, optional `UploadIds`, and an optional existing `RepliedEntryLid`.
Use UTC timestamps with microsecond precision, matching PostgreSQL storage. The
server sorts a stable copy, then returns results in the original request order.
Only successfully accepted entries advance the timeline bound. A batch containing
only rejected entries still records its results.

Keep the command `Uuid` stable when retrying the same batch. The Chat database
stores the request and ordered results in the same transaction as entries and
attachment bindings. Reusing that ID with another payload is rejected. Retry
receipts apply while the session remains active; ending a session closes its write
API. The batch bypasses short-lived API deduplication so durable receipts and
current owner/session checks run on every call.

For attachments, call `ChatImports_CreateUpload` with the target member, file name,
content type and byte length. Send bytes with `Uploads_Append` or `IUploads.AppendStream`,
then call `ChatImports_FinalizeUpload`. Consent is checked during upload access,
finalization and batch binding. Media and thumbnails receive immutable ownership
at creation; the staging record also retains the uploader. An upload can be bound
once, to the same member, target chat and session.

The Chat database is authoritative for session state. Start/end events durably
project that state into Users maintenance, with the session ID as maintenance
owner. Immediate projection updates the UI before returning; delayed events verify
the current session before changing maintenance. Chat/Place writes, membership,
roles, reactions and entry attachment writes share the import scope transaction
lock. Batches, consent changes and End use the same lock. Other System maintenance
blocks import commands without allowing an import to clear that maintenance.

Imported entries retain `IsImported`; ordinary message changes cannot set this flag.
Entry events retain `SuppressNotifications`. Notification creation, buffered merge,
push delivery and PTT wakes check maintenance. Summary notifications whose last
entry is imported remain silent after the session ends. Existing notifications may
receive cleanup dismissals; no new message or call alert is sent during maintenance.

## Validation evidence

Integration coverage includes consent and revocation, fresh consent after restart,
owner authorization, inherited Place consent and per-chat batches, sorted results,
strict existing-tail bounds, duplicate dates, durable retries and payload conflicts,
competing batches, batch/end and batch/revocation races, file ownership and single
binding, and database-failure rollback of both entries and receipts. Existing
maintenance and upload tests remain in the verification set. Serialization tests
exercise the JSON, MessagePack and type-decorating serializer matrix.

Run the integration suites against isolated databases. In this Windows workspace,
the test build uses `-p:WasmBuildNative=false -p:InvariantGlobalization=false` to avoid
the unavailable native WASM toolchain; it still compiles the Blazor UI and runs the
.NET integration hosts. Frontend validation uses `npm run build:Verify`. AOT sources
and the MAUI profile are regenerated with `update-aot-helpers.cmd`. Browser/runtime
visual QA remains unperformed because the shared server loop and Chrome sessions
are reserved by the user.
