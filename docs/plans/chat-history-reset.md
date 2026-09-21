# Chat History Reset Implementation Plan

> Import contract update: [consented chat import](./consented-chat-import.md) is authoritative.
> Cleanup happens before import; reset inside an active import is unavailable. Imported
> timestamps must be strictly increasing, including across batches. The export/reset
> workflows below are separate future work and must follow these constraints.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an owner remove the complete visible history of a group chat or Place safely through a resumable operation.

**Architecture:** `IChatHistoryResets.Start` enters chat/Place maintenance, or adopts an active import session's maintenance, and starts a persisted `ChatHistoryResetFlow`. The flow inventories every scoped timeline and descendant thread chat, removes content in bounded batches, cleans entry-derived state, resets positions, and changes maintenance state only after verification. Standalone reset exits maintenance; an import-owned reset returns to import-ready maintenance. Old local IDs remain tombstones; no completion system entry is written into the new timeline.

**Tech Stack:** C# 15, Fusion commands, ActualChat Flows, EF Core/PostgreSQL, Blazor, xUnit.

**Spec:** [Chat migration and external streaming](./chat-migration-and-streaming.md)

## Global Constraints

- Only a current chat/Place owner may start a reset; peer chats are out of scope.
- The complete group chat or every chat in the Place enters maintenance before destructive work begins.
- When invoked from an active import session, reset uses that session's maintenance state and
  returns to import-ready maintenance instead of making the chat writable.
- The operation is idempotent and resumable; process failure must not expose a partially cleared writable chat.
- Existing IDs are never reused and the local-ID generator is never rewound.
- No message is added after clearing, because a current timestamp would prevent importing older history.
- The UI always displays an explicit destructive-action warning.

## Reuse

### Existing abstractions to reuse

- `Chats_RemoveEntries` / `ChatsBackend_ChangeEntry` removal semantics and their invalidation hooks.
- `ChatThreads`, `IChatThreadsBackend`, and thread-chat identifiers for descendant discovery.
- `IReactions`, mention/indexing flows, `IConversationsBackend`, `ChatPinnedEntries`, and chat-position backends.
- `Flow`, `FlowHub`, flow checkpoints, and delayed resume events.
- Chat maintenance mode from [its implementation plan](./chat-maintenance-mode.md).

### Reusability of new components

The batch cleanup coordinator belongs in `Chat.Service`, where future retention and administrative
repair operations can reuse it. The public reset contract remains narrow and owner-facing. Do not
put generic deletion primitives in the UI or capture tooling.

---

### Task 1: Define reset contracts and progress state

**Files:**
- Create: `src/dotnet/Api/Chat/ChatHistoryReset.cs`
- Create: `src/dotnet/Api.Contracts/Chat/IChatHistoryResets.cs`
- Test: `tests/Chat.UnitTests/ChatHistoryResetTest.cs`

**Interfaces:**
- Produces: `IChatHistoryResets.Get`, `Start`, and a progress model consumed by maintenance UI.
- Consumes: `ChatMaintenance` and a group-chat or Place-root `ChatId` scope.
- Verifies: generated AOT sources include every wire type; generated files are not edited manually.

- [ ] **Step 1: Write failing serialization and state-transition tests**

Use an explicit phase model:

```csharp
public enum ChatHistoryResetPhase {
    Inventory = 1,
    ClearThreads = 2,
    ClearEntries = 3,
    ClearDerivedState = 4,
    ResetPositions = 5,
    Verify = 6,
    Completed = 7,
}

public sealed partial record ChatHistoryReset(
    ChatId ScopeId,
    string OperationId,
    ChatHistoryResetPhase Phase,
    long ProcessedEntryCount,
    long? TotalEntryCount,
    string Error);
```

- [ ] **Step 2: Run the focused unit test and verify it fails**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --no-restore --filter ChatHistoryResetTest`

- [ ] **Step 3: Implement the contracts and AOT registration**

`Start` carries an expected chat version plus a unique idempotency key. Do not expose local entry IDs
or backend cursors through the public contract.

- [ ] **Step 4: Run the focused test and commit**

```powershell
git add src/dotnet/Api src/dotnet/Api.Contracts tests/Chat.UnitTests/ChatHistoryResetTest.cs
git commit -m "feat(chat): define history reset contracts"
```

### Task 2: Inventory everything owned by the timeline

**Files:**
- Create: `src/dotnet/Chat.Service/ChatHistoryResetInventory.cs`
- Modify: `src/dotnet/Chat.Service/ChatThreadsBackend.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryResetInventoryTest.cs`

**Interfaces:**
- Consumes: group-chat or Place-root scope ID.
- Produces: stable scoped-chat entry ranges, descendant thread-chat IDs, counts, and cleanup work keys.

- [ ] **Step 1: Write failing inventory tests**

Create a group chat and a multi-chat Place containing text, voice/media, replies, reactions, pins,
a thread start, thread replies, conversation summaries, removed entries, and read positions. Assert
each inventory includes every scoped root/thread entry but never another Place or chat's data.

- [ ] **Step 2: Run the focused integration test and verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --no-restore --filter ChatHistoryResetInventoryTest`

- [ ] **Step 3: Implement snapshot inventory**

Record every scoped chat's tail local ID when maintenance starts. Because maintenance blocks new
content, these tails are the complete reset boundary. Discover every thread anchored by an entry at
or below its parent tail and record each thread's tail. Persist only resumable cursor/count data in
the flow state.

- [ ] **Step 4: Run the focused test and commit**

```powershell
git add src/dotnet/Chat.Service/ChatHistoryResetInventory.cs src/dotnet/Chat.Service/ChatThreadsBackend.cs tests/Chat.IntegrationTests/ChatHistoryResetInventoryTest.cs
git commit -m "feat(chat): inventory history reset scope"
```

### Task 3: Add an owner-authorized start command

**Files:**
- Create: `src/dotnet/Chat.Service/ChatHistoryResets.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryResetAuthorizationTest.cs`

**Interfaces:**
- Consumes: `IChatMaintenances`, `IRolesBackend`, and `FlowHub`.
- Produces: idempotent operation creation and `ChatHistoryResetFlow` scheduling.

- [ ] **Step 1: Write failing authorization tests**

Cover group and Place owner success, moderator/member rejection, peer rejection, expected-version
conflict, duplicate idempotency key, incompatible maintenance, and two simultaneous starts.

- [ ] **Step 2: Run the focused tests and verify they fail**

- [ ] **Step 3: Implement start ordering**

Within the command operation: validate current ownership and expected scope version, create
ClearHistory maintenance for a standalone reset or adopt a compatible import maintenance, create
initial reset state, then enqueue the flow resume event. A failure before commit creates neither
state; a failure afterward is resumed from persisted state. The import-owned path requires the
fresh round-trip snapshot gate added by the import plan.

- [ ] **Step 4: Run the focused tests and commit**

```powershell
git add src/dotnet/Chat.Service tests/Chat.IntegrationTests/ChatHistoryResetAuthorizationTest.cs
git commit -m "feat(chat): start history reset"
```

### Task 4: Implement resumable batched removal

**Files:**
- Create: `src/dotnet/Chat.Service/Flows/ChatHistoryResetFlow.cs`
- Create: `src/dotnet/Chat.Contracts/ChatsBackend_ClearEntryBatch.cs`
- Modify: `src/dotnet/Chat.Service/ChatsBackend.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryResetFlowTest.cs`

**Interfaces:**
- Consumes: inventory tails and typed maintenance bypass.
- Produces: idempotent removal of at most 100 entries per backend transaction and checkpointed flow progress.

- [ ] **Step 1: Write a failure/resume integration test**

Seed more than two batches, stop after the first checkpoint, resume, and assert every root/thread
entry is removed once. Include an already-removed entry and a repeated resume event.

- [ ] **Step 2: Run the focused test and verify it fails**

- [ ] **Step 3: Implement thread-first, root-second deletion**

Use `Change.Remove<ChatEntryDiff>()` so existing attachment, mention, indexing, translation, and
conversation invalidation hooks run. Remove descendants before their thread-start entries. Update
the flow cursor only after the batch operation commits.

- [ ] **Step 4: Run the focused test and commit**

```powershell
git add src/dotnet/Chat.Contracts src/dotnet/Chat.Service tests/Chat.IntegrationTests/ChatHistoryResetFlowTest.cs
git commit -m "feat(chat): clear history in resumable batches"
```

### Task 5: Clean derived state and reset positions

**Files:**
- Modify: `src/dotnet/Chat.Service/Flows/ChatHistoryResetFlow.cs`
- Modify: `src/dotnet/Chat.Service/ConversationsBackend.cs`
- Modify: `src/dotnet/Chat.Service/ChatThreadsBackend.cs`
- Modify: `src/dotnet/Users.Contracts/IChatPositionsBackend.cs`
- Modify: `src/dotnet/Users.Service/ChatPositionsBackend.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryResetDerivedStateTest.cs`
- Test: `tests/Users.IntegrationTests/ChatHistoryResetPositionsTest.cs`

**Interfaces:**
- Produces: empty visible history with no live thread, pin, reaction, conversation, mention/search,
  attachment-index, or stale position reference.

- [ ] **Step 1: Write failing derived-state tests from Task 2's fixture**

Assert no visible entries or thread chats, empty pins and conversations, no reaction/mention/search
hits, media no longer presented as chat attachments, and positions reset for every member.

- [ ] **Step 2: Run both focused test classes and verify they fail**

- [ ] **Step 3: Add explicit cleanup commands where entry removal is insufficient**

Set read/heard positions to the removed tail boundary and reset view positions to empty. This avoids
rewinding forward-only public position writes while ensuring the next higher imported ID is unread.
Delete conversation rows and thread metadata only after their entries are removed. Normalize pins.

- [ ] **Step 4: Verify index cleanup reaches eventual consistency**

In tests, resume the relevant indexing flows and assert deleted content cannot be searched. Do not
sleep for arbitrary delays.

- [ ] **Step 5: Run focused tests and commit**

```powershell
git add src/dotnet/Chat.Service src/dotnet/Users.Contracts src/dotnet/Users.Service tests/Chat.IntegrationTests/ChatHistoryResetDerivedStateTest.cs tests/Users.IntegrationTests/ChatHistoryResetPositionsTest.cs
git commit -m "feat(chat): clean reset-derived state"
```

### Task 6: Verify completion before leaving maintenance

**Files:**
- Modify: `src/dotnet/Chat.Service/Flows/ChatHistoryResetFlow.cs`
- Test: `tests/Chat.IntegrationTests/ChatHistoryResetCompletionTest.cs`

**Interfaces:**
- Consumes: all cleanup phases.
- Produces: verified Completed state and the correct standalone/import maintenance transition.

- [ ] **Step 1: Write failure and completion tests**

If any visible entry or descendant remains, keep maintenance active, store a concise error, and
schedule retry. When verification passes, mark Completed and either finish standalone maintenance
or return the owning import session to `ReadyToImport`. Confirm that no system entry is added to any
cleared chat.

- [ ] **Step 2: Implement verification and run the focused test**

- [ ] **Step 3: Commit**

```powershell
git add src/dotnet/Chat.Service/Flows/ChatHistoryResetFlow.cs tests/Chat.IntegrationTests/ChatHistoryResetCompletionTest.cs
git commit -m "feat(chat): verify history reset completion"
```

### Task 7: Add destructive-action UI and progress

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/ChatSettings/ResetChatHistoryButton.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatSettings/ResetChatHistoryModal.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatSettings/ChatSettingsStartModalPage.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatMaintenanceView.razor`
- Modify: `src/dotnet/Localization/Resources/Strings.*.json`
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/ResetChatHistoryViewTest.cs`

**Interfaces:**
- Consumes: `IChatHistoryResets.Start/Get` and maintenance shell.
- Produces: explicit warning, confirmation, progress, failure, and completion UX.

- [ ] **Step 1: Write failing owner/member UI tests**

The warning must say that messages, voice, files, reactions, threads, pins, and summaries disappear
for everyone and that IDs/links will not be reused. Require deliberate confirmation; never make the
action a one-click menu item.

- [ ] **Step 2: Implement the modal and progress view**

The maintenance view links back to the initiating owner and displays processed/total counts when
known. It does not offer Cancel after the flow starts.

- [ ] **Step 3: Run UI tests and `npm run build:Verify`**

- [ ] **Step 4: Commit**

```powershell
git add src/dotnet/UI.Blazor.App tests/Chat.UI.Blazor.IntegrationTests/ResetChatHistoryViewTest.cs
git commit -m "feat(ui): add reset chat history flow"
```

### Task 8: Final verification

- [ ] Document owner authorization, complete removal semantics, threads/derived-state cleanup,
  progress/recovery, and export-before-reset guidance in sibling `ActualChat-docs`.
- [ ] Run all `ChatHistoryReset` tests.
- [ ] Run `dotnet build ActualChat.CI.slnf --no-restore` when the filter exists.
- [ ] Run `npm run build:Verify`.
- [ ] Manually interrupt a multi-batch reset, restart the service, and confirm it resumes while the chat remains read-only.
- [ ] Start an import afterward and confirm its first historical timestamp is accepted despite old tombstone IDs.
