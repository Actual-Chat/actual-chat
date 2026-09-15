# Consented Chat History Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let owners import ordered historical text, attachments, and playable voice entries into group chats and Places only for members who consented.

**Architecture:** An import session owns chat/Place maintenance and a per-member consent ledger. Owners upload attributed media into session-scoped staging, then submit bounded atomic message batches using stable source IDs; the backend validates consent and monotonic timestamps, assigns Voxt local IDs, persists provenance/mappings, and creates normal entries through a dedicated import command. Ending the session revokes all remaining consent and exits maintenance.

**Tech Stack:** C# 15, Fusion services/commands, EF Core/PostgreSQL, ActualChat upload/media pipeline, notifications, Blazor, xUnit.

**Spec:** [Chat migration and external streaming](./chat-migration-and-streaming.md)

## Global Constraints

- Import supports group chats and Places; peer and anonymous chat import is rejected.
- Starting the session immediately puts the complete scope into maintenance for everyone.
- Any owner may end the session. Members may accept, decline, or revoke only their own attribution consent.
- Place consent covers every chat in that Place.
- Only an actively consenting member may be used as an imported author or imported-media owner.
- IDs are assigned by Voxt. Timestamps are nondecreasing across the visible timeline and within each batch.
- Imported audio requires audio media, transcript text, and a valid time map together.
- Imported content never generates ordinary new-message notifications.

## Reuse

### Existing abstractions to reuse

- Maintenance state and guard from [Chat maintenance mode](./chat-maintenance-mode.md).
- `ChatEntryDiff` fields `AuthorId`, `BeginsAt`, `Content`, `Audio`, `Attachments`, and
  `RepliedEntryLid`; `ChatsBackend_ChangeEntry` for final creation.
- `ChatEntryAudio`, `Transcript`, `LinearMap`, `MediaId`, `IUploads`, `IMediaSaver`, and upload processors.
- `IAuthorsBackend.GetByUserId` to resolve a consenting member's author in each target chat.
- Existing notification persistence and the maintenance view.

### Reusability of new components

- `ChatEntryImportInfo` belongs on the general entry model for audit/export and future migrations.
- `Transcript.RequireValidForAudio` belongs beside `Transcript`, shared by import and external streaming.
- The neutral source-ID mapping and import-media staging records stay in `Chat.Service`; source-specific
  parsers remain outside the server.

---

### Task 1: Define import session, consent, provenance, and batch contracts

**Files:**
- Create: `src/dotnet/Api/Chat/ChatImportSession.cs`
- Create: `src/dotnet/Api/Chat/ChatEntryImportInfo.cs`
- Create: `src/dotnet/Api/Chat/ImportedChatEntry.cs`
- Create: `src/dotnet/Api.Contracts/Chat/IChatImports.cs`
- Modify: `src/dotnet/Api/Chat/ChatEntry.cs`
- Modify: AOT source declarations under `src/dotnet/Api*/Module/`
- Test: `tests/Chat.UnitTests/ChatImportContractTest.cs`

**Interfaces:**
- Produces: session reads/lifecycle commands, consent commands, `ChatImportBatch`,
  `ImportedChatEntry`, `ChatImportBatchResult`, and immutable `ChatEntry.ImportInfo`.

- [ ] **Step 1: Write failing serialization/invariant tests**

Define the input independently of Voxt IDs:

```csharp
public sealed partial record ImportedChatEntry {
    public required string SourceId { get; init; }
    public required UserId AuthorUserId { get; init; }
    public required Moment BeginsAt { get; init; }
    public string Text { get; init; } = "";
    public string? RepliedSourceId { get; init; }
    public ApiArray<ImportedMediaId> MediaIds { get; init; }
    public ImportedAudio? Audio { get; init; }
}

public sealed partial record ChatImportBatch {
    public required string SessionId { get; init; }
    public required ChatId ChatId { get; init; }
    public required string SourceNamespace { get; init; }
    public required ChatEntryId? ExpectedLastEntryId { get; init; }
    public required ApiArray<ImportedChatEntry> Entries { get; init; }
}
```

Use a bounded source namespace/ID length and a maximum of 100 messages per batch.

- [ ] **Step 2: Run the focused unit test and verify it fails**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --no-restore --filter ChatImportContractTest`

- [ ] **Step 3: Implement contracts, provenance, and AOT registration**

`ChatEntryImportInfo` contains session, source namespace/ID, importing owner, and import time.
`IsImported` is computed from the presence of this immutable record; do not add a caller-settable
boolean to ordinary `Chats_UpsertEntry`.

- [ ] **Step 4: Run the focused test and commit**

```powershell
git add src/dotnet/Api src/dotnet/Api.Contracts tests/Chat.UnitTests/ChatImportContractTest.cs
git commit -m "feat(chat): define consented import contracts"
```

### Task 2: Persist sessions and per-member consent

**Files:**
- Create: `src/dotnet/Chat.Service/Db/DbChatImportSession.cs`
- Create: `src/dotnet/Chat.Service/Db/DbChatImportConsent.cs`
- Modify: `src/dotnet/Chat.Service/Db/ChatDbContext.cs`
- Create: `src/dotnet/Chat.Service/ChatImports.cs`
- Create: `src/dotnet/Chat.Service/ChatImportsBackend.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
- Create: `src/dotnet/Chat.Service.Migration/Migrations/<timestamp>_ChatImports.cs`
- Test: `tests/Chat.IntegrationTests/ChatImportSessionTest.cs`

**Interfaces:**
- Consumes: `IChatMaintenances`, owner/member resolution, current Place membership.
- Produces: active session and consent ledger with `Pending`, `Accepted`, `Declined`, and `Revoked` states.

- [ ] **Step 1: Write failing lifecycle tests**

Cover group/Place start, peer rejection, initiator auto-consent, member accept/decline/revoke,
non-member rejection, duplicate commands, new member appearing as Pending, any-owner end, and all
consents becoming inactive on end.

- [ ] **Step 2: Run the focused integration test and verify it fails**

- [ ] **Step 3: Implement session start**

Create Import maintenance and the import session atomically. Snapshot current members for display,
but authorize against current membership and consent at every write. For Place scope, use the root
chat as session ID and resolve all child chats through that Place.

- [ ] **Step 4: Implement consent and end commands**

Consent is keyed by session and `UserId`, not `AuthorId`, because a Place member can have a different
author identity in each child chat. Ending removes maintenance after marking the session complete.

- [ ] **Step 5: Run the focused tests and commit**

```powershell
git add src/dotnet/Chat.Service src/dotnet/Chat.Service.Migration tests/Chat.IntegrationTests/ChatImportSessionTest.cs
git commit -m "feat(chat): persist import sessions and consent"
```

### Task 3: Add import invitation notifications

**Files:**
- Create: `src/dotnet/Api/Notifications/ChatImportNotification.cs`
- Modify: `src/dotnet/Api/Notifications/Notification.cs`
- Modify: `src/dotnet/Notifications.Service/NotificationsService.cs`
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs`
- Modify: AOT notification serialization declarations
- Test: `tests/Notifications.IntegrationTests/ChatImportNotificationTest.cs`

**Interfaces:**
- Consumes: import session lifecycle events.
- Produces: a producer-resolved notification with one Open action targeting any affected chat.

- [ ] **Step 1: Write failing notification lifecycle tests**

Assert one notification per pending member, navigation to maintenance UI, explicit dismissal rejected,
Dismiss All skipping it, and server removal after accept, decline, revoke, or session end.

- [ ] **Step 2: Run the focused notification test and verify it fails**

- [ ] **Step 3: Add a producer-resolved dismissal policy**

Extend notification policy explicitly rather than special-casing a notification ID in dismissal
handlers. The client action opens the affected scope; consent itself is submitted on the chat page.

- [ ] **Step 4: Wire lifecycle production/removal and run tests**

- [ ] **Step 5: Commit**

```powershell
git add src/dotnet/Api/Notifications src/dotnet/Notifications.Service tests/Notifications.IntegrationTests/ChatImportNotificationTest.cs
git commit -m "feat(notifications): add chat import consent request"
```

### Task 4: Validate and persist atomic sequential batches

**Files:**
- Create: `src/dotnet/Chat.Contracts/ChatsBackend_ImportEntries.cs`
- Create: `src/dotnet/Chat.Service/ChatImportValidator.cs`
- Modify: `src/dotnet/Chat.Service/ChatImports.cs`
- Modify: `src/dotnet/Chat.Service/ChatsBackend.cs`
- Modify: `src/dotnet/Chat.Service/Db/DbChatEntry.cs`
- Create: `src/dotnet/Chat.Service/Db/DbChatImportEntry.cs`
- Modify: `src/dotnet/Chat.Service/Db/ChatDbContext.cs`
- Test: `tests/Chat.IntegrationTests/ChatImportBatchTest.cs`

**Interfaces:**
- Consumes: active maintenance/session, consent ledger, source mappings, typed maintenance bypass.
- Produces: server-assigned entries and persisted `(session, namespace, source ID) -> ChatEntryId` mappings.

- [ ] **Step 1: Write the batch validation matrix**

Cover inactive/wrong scope, non-owner caller, nonconsenting author, author outside target chat, stale
expected tail, descending time, timestamp earlier than visible tail, duplicate source ID, retry of an
identical completed batch, conflicting retry, missing/forward reply, peer chat, batch size, and max
text length. Assert any invalid item leaves the complete batch unwritten.

- [ ] **Step 2: Run the focused test and verify it fails**

- [ ] **Step 3: Implement pure validation before opening the write transaction**

Resolve source replies from prior mappings or earlier items in the same batch. Require every reply
target to be in the same target chat. Compare the first timestamp with the last nonremoved visible
entry, not with tombstones left by reset.

- [ ] **Step 4: Implement one backend transaction per bounded batch**

Allocate local IDs in request order, create normal text entries with explicit imported author/time,
write provenance and source mappings, and suppress ordinary new-message notifications. Emit normal
compute invalidations and indexing/summary resumes after commit.

- [ ] **Step 5: Run the focused tests and commit**

```powershell
git add src/dotnet/Chat.Contracts src/dotnet/Chat.Service tests/Chat.IntegrationTests/ChatImportBatchTest.cs
git commit -m "feat(chat): import ordered message batches"
```

### Task 5: Add attributed imported-media staging

**Files:**
- Create: `src/dotnet/Api/Media/ImportedMedia.cs`
- Create: `src/dotnet/Api.Contracts/Media/IImportedMedia.cs`
- Create: `src/dotnet/Media.Service/Db/DbImportedMedia.cs`
- Modify: `src/dotnet/Media.Service/Db/MediaDbContext.cs`
- Create: `src/dotnet/Media.Service/ImportedMediaService.cs`
- Create: `src/dotnet/Media.Service.Migration/Migrations/<timestamp>_ImportedMedia.cs`
- Modify: `src/dotnet/Chat.Service/ChatImportValidator.cs`
- Test: `tests/Media.IntegrationTests/ImportedMediaTest.cs`
- Test: `tests/Chat.IntegrationTests/ChatImportMediaTest.cs`

**Interfaces:**
- Consumes: `IUploads`, normal media processing, import session/consent reads.
- Produces: session-scoped `ImportedMediaId` records with target chat, attributed user, uploader, media reference, and consumed state.

- [ ] **Step 1: Write failing staging and authorization tests**

Cover consenting attribution, owner uploader audit, wrong chat/scope, revoked consent, invalid media,
single-use binding, safe identical retry, session end, and abandoned staging cleanup.

- [ ] **Step 2: Run focused tests and verify they fail**

- [ ] **Step 3: Implement create/upload/finalize using `IUploads`**

Do not duplicate chunk storage or media conversion. Store import attribution around the finalized
`MediaRef`; revalidate active consent both when staging begins and when a message binds the media.

- [ ] **Step 4: Bind attachments in the batch transaction**

Require all imported media to target the same chat and attributed author as the entry. Mark staging
records consumed only when entry creation commits.

- [ ] **Step 5: Add cleanup for unconsumed expired staging and run tests**

- [ ] **Step 6: Commit**

```powershell
git add src/dotnet/Api/Media src/dotnet/Api.Contracts/Media src/dotnet/Media.Service src/dotnet/Media.Service.Migration src/dotnet/Chat.Service/ChatImportValidator.cs tests/Media.IntegrationTests/ImportedMediaTest.cs tests/Chat.IntegrationTests/ChatImportMediaTest.cs
git commit -m "feat(media): stage attributed import media"
```

### Task 6: Import playable audio with transcript maps

**Files:**
- Modify: `src/dotnet/Api/Transcription/Transcript.cs`
- Create: `src/dotnet/Api/Transcription/ImportedAudioValidator.cs`
- Modify: `src/dotnet/Chat.Service/ChatImportValidator.cs`
- Modify: `src/dotnet/Chat.Service/ChatsBackend.cs`
- Test: `tests/Chat.UnitTests/ImportedAudioValidatorTest.cs`
- Test: `tests/Chat.IntegrationTests/ChatImportAudioTest.cs`

**Interfaces:**
- Consumes: finalized imported audio media, transcript text, `LinearMap`, and media duration.
- Produces: finalized `ChatEntryAudio` indistinguishable in playback from native completed voice entries, plus import provenance.

- [ ] **Step 1: Write time-map validation tests**

Reject missing transcript/map, nonmonotonic coordinates, text positions outside `[0, Text.Length]`,
audio positions outside `[0, Duration]`, nonfinite values, invalid endpoints, and excessive point count.
Accept the same tolerances native `Transcript.RequireValid` uses.

- [ ] **Step 2: Run the focused unit tests and verify they fail**

- [ ] **Step 3: Implement shared audio/transcript validation**

Keep the validator independent from import so external streaming can call it at finalization.

- [ ] **Step 4: Create the entry audio from imported media**

Set finalized `MediaId`, `BeginsAt`, `EndsAt`, `ContentEndsAt`, and `TimeMap`; never set a live
`StreamId`. Store transcript text as entry content. Audio without a valid map fails the batch rather
than being silently discarded.

- [ ] **Step 5: Run unit, import, and replay tests and commit**

```powershell
git add src/dotnet/Api/Transcription src/dotnet/Chat.Service tests/Chat.UnitTests/ImportedAudioValidatorTest.cs tests/Chat.IntegrationTests/ChatImportAudioTest.cs
git commit -m "feat(chat): import playable voice entries"
```

### Task 7: Build the import maintenance UI

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatImportMaintenanceView.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/chat-import-maintenance-view.css`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatSettings/StartChatImportModal.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatMaintenanceView.razor`
- Modify: chat/Place settings components and localized string resources
- Test: `tests/Chat.UI.Blazor.IntegrationTests/ChatImportMaintenanceViewTest.cs`

**Interfaces:**
- Consumes: import session and consent commands.
- Produces: member consent UX, owner status/member list, documentation link, and owner End Import action.

- [ ] **Step 1: Write role/state UI tests**

Members see initiator, scope, Accept/Decline or Revoke, and no editor/timeline. Owners see every
member's current status, profile links for direct contact, API documentation link, and End Import.
In a Place, every child chat renders the same Place-level decision and status.

- [ ] **Step 2: Implement start and maintenance views with localized text**

Phrase consent as permission to import messages under the member's identity for this session. Make
clear that declining protects that member's authorship but does not stop the Place/chat import.

- [ ] **Step 3: Run UI tests and `npm run build:Verify`**

- [ ] **Step 4: Commit**

```powershell
git add src/dotnet/UI.Blazor.App tests/Chat.UI.Blazor.IntegrationTests/ChatImportMaintenanceViewTest.cs
git commit -m "feat(ui): add consented import workflow"
```

### Task 8: Gate destructive prepend workflows with an export snapshot

**Files:**
- Modify: `src/dotnet/Chat.Service/ChatImports.cs`
- Modify: `src/dotnet/Chat.Service/ChatHistoryResets.cs`
- Test: `tests/Chat.IntegrationTests/ChatImportPrependWorkflowTest.cs`

**Interfaces:**
- Consumes: the import-compatible snapshot token from [Chat history export](./chat-history-export.md).
- Produces: an atomic readiness check before the first destructive reset batch.

- [ ] Require the owner to supply the snapshot ID and expected per-chat tail when reset is requested
  as part of an import session.
- [ ] Reject reset when the snapshot is not round-trip ready, belongs to another scope, has expired,
  or any target chat's visible tail changed after the snapshot.
- [ ] Keep the import session and its consents active through reset; after verification, return the
  maintenance view to `ReadyToImport` rather than ending maintenance.
- [ ] Test export -> consent -> reset -> older import -> snapshot re-import -> ordinary posting.

### Task 9: Document the neutral API and verify end to end

**Files:**
- Modify: the English API/import pages in sibling `ActualChat-docs` after their information architecture is established
- Test: `tests/Chat.IntegrationTests/ChatImportEndToEndTest.cs`

- [ ] Add copy-paste examples for starting a session, reading consent, staging media, importing sequential batches, resolving source IDs, and ending the session.
- [ ] Document snapshot-import-reset ordering, round-trip blockers, and stale-tail recovery.
- [ ] Run an end-to-end test: start Place import, gather mixed consent, import two chats in several batches, revoke one member, reject their next message, end, and resume ordinary posting.
- [ ] Run all `ChatImport`, `ImportedMedia`, notification, maintenance, replay, and serialization tests.
- [ ] Run `dotnet build ActualChat.CI.slnf --no-restore` when available and `npm run build:Verify`.
