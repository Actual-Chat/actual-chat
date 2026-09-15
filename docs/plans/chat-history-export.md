# Import-Compatible Chat History Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a group-chat or Place owner create a stable history snapshot and know, before clearing anything, whether the first importer can restore it without silent loss.

**Architecture:** `IChatHistoryExports.Start` captures immutable per-chat upper entry IDs and starts a resumable export flow. The flow reads only through those bounds, writes a neutral manifest plus content/media records to temporary export storage, and produces a machine-readable readiness report. Import-compatible mode fails closed when it encounters unsupported entry kinds, descendant threads, unresolved replies, unavailable media, reactions, pins, or other state the first importer cannot restore. The final snapshot token binds the scope, tails, content hashes, and expiry so import/reset can reject stale or substituted packages.

**Tech Stack:** C# 15, Fusion services/commands, ActualChat Flows, existing chat/media readers and blob storage, xUnit.

**Spec:** [Chat migration and external streaming](./chat-migration-and-streaming.md)

## Global Constraints

- Owners may export group chats and Places; peer chats are out of scope for this migration API.
- Export is read-only and does not enter maintenance. A snapshot excludes entries created after its captured tail.
- A Place snapshot captures a tail for every chat in the Place and a stable list of included chats.
- `Archive` output may preserve unsupported records for external storage. Only a package with zero readiness blockers is labeled `ImportCompatible`.
- Exporting content does not grant permission to re-import it. The later import still requires active consent from every attributed member.
- Export files are temporary, access-controlled, protected by the existing blob-storage policy, and removed after expiry.

## Reuse

### Existing abstractions to reuse

- `IChatsBackend`, `ChatEntryReader`, `ChatEntryId`, `ChatEntry`, `ChatEntryAudio`, and attachment/media readers.
- `IChatThreadsBackend`, reactions, pins, conversations, and content-item readers for the loss/readiness inventory.
- `Flow`, `FlowHub`, checkpoints, and existing blob upload/storage abstractions for resumable package creation.
- Neutral import entry and source-reference shapes from [Consented chat import](./consented-chat-import.md).

### Reusability of new components

- Snapshot bounds and content hashes belong in general chat export contracts so future account export and migration tools can reuse them.
- The readiness analyzer is a standalone service shared by the reset UI, import orchestration, and future importer versions.
- Source-specific archive formats remain outside the server; the server emits the documented neutral manifest.

---

### Task 1: Define snapshot, manifest, and readiness contracts

**Files:**
- Create: `src/dotnet/Api/Chat/ChatHistoryExport.cs`
- Create: `src/dotnet/Api/Chat/ChatHistoryExportManifest.cs`
- Create: `src/dotnet/Api.Contracts/Chat/IChatHistoryExports.cs`
- Test: `tests/Chat.UnitTests/ChatHistoryExportContractTest.cs`

**Interfaces:**
- Produces: start/status/download/revoke commands, `ChatHistoryExportMode`, immutable scope/chat tails,
  manifest version, package hash, expiry, and structured `ChatHistoryExportBlocker` values.
- Verifies: generated AOT sources include every wire type; generated files are not edited manually.

- [ ] **Step 1: Write failing serialization and invariant tests**

Cover group/Place scope, unique snapshot ID, sorted chat tails, bounded manifest fields, explicit
schema version, blocker codes with counts/sample IDs, and `IsImportCompatible == !Blockers.Any()`.

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --no-restore --filter ChatHistoryExportContractTest`

- [ ] **Step 3: Implement contracts and generator-visible registrations**

Keep the manifest independent of internal EF records and never expose storage paths or signed URLs
inside the durable status record.

- [ ] **Step 4: Run the focused test and commit**

```powershell
git add src/dotnet/Api/Chat src/dotnet/Api.Contracts/Chat tests/Chat.UnitTests/ChatHistoryExportContractTest.cs
git commit -m "feat(chat): define history export contracts"
```

### Task 2: Capture a stable scope snapshot

**Files:**
- Create: `src/dotnet/Chat.Service/ChatHistoryExports.cs`
- Create: `src/dotnet/Chat.Service/ChatHistoryExportInventory.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryExportSnapshotTest.cs`

**Interfaces:**
- Consumes: ownership/Place rules, chat enumeration, last visible entry queries.
- Produces: immutable `(ChatId, LastEntryId, LastVisibleBeginsAt)` bounds and included-chat IDs.

- [ ] Write failing authorization, peer rejection, and concurrent-post snapshot tests.
- [ ] Authorize against current ownership and capture all Place chat IDs and tails in one consistent
  database view. Later-created chats and entries are outside this snapshot.
- [ ] Persist the inventory with owner, scope, mode, created/expiry times, and flow state.
- [ ] Run focused tests and commit.

```powershell
git add src/dotnet/Chat.Service tests/Chat.IntegrationTests/ChatHistoryExportSnapshotTest.cs
git commit -m "feat(chat): capture stable export snapshots"
```

### Task 3: Analyze round-trip readiness before packaging

**Files:**
- Create: `src/dotnet/Chat.Service/ChatHistoryExportReadiness.cs`
- Modify: `src/dotnet/Chat.Service/ChatHistoryExports.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryExportReadinessTest.cs`

**Interfaces:**
- Consumes: bounded entry scan, thread descendants, reactions, pins, conversations, calls, locations,
  system entries, media availability, and reply targets.
- Produces: deterministic blocker codes, counts, and bounded samples without leaking message content.

- [ ] Write one failing test per supported shape and blocker class.
- [ ] Mark user text, supported attachments, playable audio with valid transcript maps, and replies to
  an earlier included source entry as import-compatible.
- [ ] Block import-compatible output for descendant threads, unsupported/system entry kinds, calls,
  locations, reactions, pins, unavailable media, invalid audio maps, and reply targets outside the package.
- [ ] Allow archive mode to continue while recording the same loss report; never downgrade blockers
  to warnings for import-compatible mode.
- [ ] Run focused tests and commit.

```powershell
git add src/dotnet/Chat.Service/ChatHistoryExportReadiness.cs src/dotnet/Chat.Service/ChatHistoryExports.cs tests/Chat.IntegrationTests/ChatHistoryExportReadinessTest.cs
git commit -m "feat(chat): report export round-trip readiness"
```

### Task 4: Build the neutral package in a resumable flow

**Files:**
- Create: `src/dotnet/Chat.Service/Flows/ChatHistoryExportFlow.cs`
- Create: `src/dotnet/Chat.Service/ChatHistoryExportWriter.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryExportFlowTest.cs`

**Interfaces:**
- Consumes: snapshot bounds, normal chat/media readers, neutral import records, blob storage.
- Produces: versioned manifest, ordered per-chat entry streams, media blobs/checksums, package checksum,
  progress, and a short-lived download capability.

- [ ] Write failing resume, bounded-read, ordering, reply mapping, audio/media, and checksum tests.
- [ ] Page entries by local ID through each captured tail. Assign stable source IDs derived from the
  original chat/entry identity; never reuse those IDs as target Voxt IDs.
- [ ] Export author `UserId`, historical time, text, replies, supported attachments, and complete
  audio/transcript/time-map tuples. Deduplicate media by content hash.
- [ ] Checkpoint after every bounded page and make each output chunk idempotent under replay.
- [ ] Publish the final capability only after manifest and package hashes verify.
- [ ] Run focused tests and commit.

```powershell
git add src/dotnet/Chat.Service tests/Chat.IntegrationTests/ChatHistoryExportFlowTest.cs
git commit -m "feat(chat): write resumable history exports"
```

### Task 5: Secure download, expiry, and revocation

**Files:**
- Modify: `src/dotnet/Chat.Service/ChatHistoryExports.cs`
- Create: `src/dotnet/Chat.Service/Flows/ChatHistoryExportCleanupFlow.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryExportAccessTest.cs`

**Interfaces:**
- Consumes: current owner authorization and existing protected blob-download mechanism.
- Produces: owner/scope download authorization, explicit revoke, and scheduled expiry cleanup.

- [ ] Test non-owner access, ownership loss, expired/revoked tokens, guessed IDs, repeated download,
  cleanup replay, and deletion of both package and manifest.
- [ ] Resolve a fresh short-lived download capability per request; do not persist public URLs.
- [ ] Revoke on owner request and delete all temporary blobs after expiry through an idempotent flow.
- [ ] Run focused tests and commit.

```powershell
git add src/dotnet/Chat.Service tests/Chat.IntegrationTests/ChatHistoryExportAccessTest.cs
git commit -m "feat(chat): secure temporary history exports"
```

### Task 6: Expose readiness before reset and verify the round trip

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatSettings/ResetChatHistoryModal.razor`
- Modify: `src/dotnet/Localization/Resources/Strings.*.json`
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`
- Modify: English migration/export documentation in sibling `ActualChat-docs`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/ChatHistoryExportReadinessViewTest.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryRoundTripTest.cs`

**Interfaces:**
- Consumes: export status/readiness and the import/reset snapshot gate.
- Produces: clear success/blocker UX, download action, and a snapshot token usable by import reset.

- [ ] Show snapshot progress, expiry, and every blocker category before the owner can select the
  reset-and-restore workflow. Link blocker samples to the affected chat when possible.
- [ ] Require a fresh zero-blocker snapshot for reset inside an import session; reject changed tails.
- [ ] Document archive versus import-compatible output, blocker codes, snapshot expiry, consent, and
  the complete prepend-history sequence with copy-paste API examples.
- [ ] Test a Place with text, attachments, audio, and replies through export -> import maintenance ->
  consent -> reset -> prepend older history -> restore snapshot -> end import.
- [ ] Run all `ChatHistoryExport` and round-trip tests, localization checks,
  `dotnet build ActualChat.CI.slnf --no-restore`, and `npm run build:Verify`.

```powershell
git add src/dotnet/UI.Blazor.App src/dotnet/Localization tests/Chat.UI.Blazor.IntegrationTests/ChatHistoryExportReadinessViewTest.cs tests/Chat.IntegrationTests/ChatHistoryRoundTripTest.cs
git commit -m "feat(ui): gate history reset with export readiness"
```
