# Chat Maintenance Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persisted chat/Place maintenance state that owners can start and finish and that blocks ordinary chat activity for every affected member.

**Architecture:** Store maintenance against a scope chat ID: a group-chat ID means one chat and a Place root-chat ID means the complete Place. `IChatMaintenances.GetEffective` resolves direct and inherited Place maintenance; a shared guard protects every mutating content path. Chat news exposes a compact maintenance summary, while the active chat view loads the full state and replaces the timeline/editor with a maintenance view.

**Tech Stack:** C# 15, Fusion compute services and commands, EF Core/PostgreSQL, Blazor, xUnit.

**Spec:** [Chat migration and external streaming](./chat-migration-and-streaming.md)

## Global Constraints

- Group chats and Places are supported; peer chats and anonymous authors are rejected.
- Place maintenance applies to all chats in the Place immediately.
- Ordinary content mutations and live publishing are blocked for everyone in scope.
- An import may be ended by any current owner; an executing reset remains active until its flow completes.
- Wire additions must remain forward compatible with older clients.
- User-visible strings must use the localized string catalog described in `docs/i18n.md`.

## Reuse

### Existing abstractions to reuse

- `ChatId`, `PlaceChatId`, and the Place root-chat convention for one stable scope identifier.
- `IChats`, `IPlaces`, `IRoles`, `ChatPermissions`, `PlacePermissions`, and `AuthorRules` for access.
- Fusion compute methods/commands and the existing Chat DB operation/invalidation pattern.
- `ChatNews` for the compact sidebar signal.
- Existing `ChatView` routing and chat-view composition rather than a separate maintenance route.

### Reusability of new components

`ChatMaintenance`, `IChatMaintenances`, and `ChatMaintenanceGuard` are shared chat-platform
components. Keep them in `Api`, `Api.Contracts`, and `Chat.Service`; do not place them in import- or
reset-specific folders. The maintenance view is app-specific and belongs in `UI.Blazor.App`.

---

### Task 1: Define maintenance state and scope resolution

**Files:**
- Create: `src/dotnet/Api/Chat/ChatMaintenance.cs`
- Create: `src/dotnet/Api.Contracts/Chat/IChatMaintenances.cs`
- Test: `tests/Chat.UnitTests/ChatMaintenanceTest.cs`

**Interfaces:**
- Produces: `ChatMaintenance`, `ChatMaintenanceKind`, `ChatMaintenanceStatus`, and
  `IChatMaintenances.GetEffective(Session, ChatId, CancellationToken)`.
- Consumes: existing identifiers and Fusion serialization attributes.
- Verifies: generated `ApiAotSource.g.cs` / `ApiContractsAotSource.g.cs` include the wire types;
  generated files are never edited by hand.

- [ ] **Step 1: Write serialization and scope tests**

Cover group scope, Place-root scope, rejection of peer IDs, immutable initiator/start metadata, and
round-trip serialization. The central contract should have this shape:

```csharp
public enum ChatMaintenanceKind { Import = 1, ClearHistory = 2 }
public enum ChatMaintenanceStatus { Active = 1, Completing = 2, Failed = 3 }

public sealed partial record ChatMaintenance(
    ChatId ScopeId,
    long Version,
    ChatMaintenanceKind Kind,
    ChatMaintenanceStatus Status,
    UserId InitiatedBy,
    Moment StartedAt,
    string OperationId);
```

- [ ] **Step 2: Run the focused unit tests and verify they fail**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --no-restore --filter ChatMaintenanceTest`

- [ ] **Step 3: Add the contracts and AOT registrations**

Define compute reads for exact scope and effective chat state plus internal start/update/finish
commands. Effective resolution maps a `PlaceChatId` to its root-chat ID before falling back to direct
chat maintenance.

- [ ] **Step 4: Run the focused unit tests**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/dotnet/Api/Chat/ChatMaintenance.cs src/dotnet/Api.Contracts/Chat/IChatMaintenances.cs tests/Chat.UnitTests/ChatMaintenanceTest.cs
git commit -m "feat(chat): define maintenance contracts"
```

### Task 2: Persist and authorize maintenance state

**Files:**
- Create: `src/dotnet/Chat.Service/Db/DbChatMaintenance.cs`
- Modify: `src/dotnet/Chat.Service/Db/ChatDbContext.cs`
- Create: `src/dotnet/Chat.Service/ChatMaintenances.cs`
- Create: `src/dotnet/Chat.Service/ChatMaintenancesBackend.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
- Create: `src/dotnet/Chat.Service.Migration/Migrations/<timestamp>_ChatMaintenance.cs`
- Test: `tests/Chat.IntegrationTests/ChatMaintenanceTest.cs`

**Interfaces:**
- Consumes: Task 1 contracts, `IChatsBackend`, `IPlaces`, and `IRolesBackend`.
- Produces: durable compute-backed state and owner-authorized lifecycle commands.

- [ ] **Step 1: Write failing lifecycle and permission tests**

Cover: owner starts group maintenance; owner starts Place maintenance; member/moderator cannot start;
peer scope is rejected; a second active operation is rejected; any current owner can finish Import;
ClearHistory cannot be user-finished while active; child Place chats resolve root maintenance.

- [ ] **Step 2: Run the focused integration tests and verify they fail**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --no-restore --filter ChatMaintenanceTest`

- [ ] **Step 3: Add the EF entity and migration**

Use one row per `ScopeId`, optimistic `Version`, enum kind/status, initiator, timestamps, and opaque
operation ID. Follow the existing DB entity conversion and migration naming patterns.

- [ ] **Step 4: Implement exact and effective compute reads**

Resolve maintenance in this order: direct group-chat state; for a Place child chat, Place-root state;
otherwise none. Invalidate both the scope read and affected chat news when state changes.

- [ ] **Step 5: Implement internal lifecycle commands**

Start must be an expected-absent create. Status updates and finish must carry expected versions.
Public import/reset services call these internal commands after their own operation-specific checks.

- [ ] **Step 6: Run the focused integration tests**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/dotnet/Chat.Service src/dotnet/Chat.Service.Migration tests/Chat.IntegrationTests/ChatMaintenanceTest.cs
git commit -m "feat(chat): persist maintenance state"
```

### Task 3: Enforce maintenance across chat mutations

**Files:**
- Create: `src/dotnet/Chat.Service/ChatMaintenanceGuard.cs`
- Modify: `src/dotnet/Chat.Service/Chats.cs`
- Modify: `src/dotnet/Chat.Service/Reactions.cs`
- Modify: `src/dotnet/Chat.Service/ChatThreads.cs`
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`
- Modify: `src/dotnet/Streaming.Service/Services/LiveVideoStreams.cs`
- Test: `tests/Chat.IntegrationTests/ChatMaintenanceGuardTest.cs`
- Test: `tests/Streaming.IntegrationTests/MaintenanceModeTest.cs`

**Interfaces:**
- Consumes: `IChatMaintenances.GetEffective`.
- Produces: `ChatMaintenanceGuard.RequireAvailable` and an explicit internal bypass token usable only
  by reset/import backend commands.

- [ ] **Step 1: Write a mutation matrix test**

Assert that maintenance blocks create/edit/remove/restore, reaction changes, thread creation, pin
changes, audio stream start, video stream start, and calls or other system entries that mutate the
timeline. Reads, role/member management, consent, import, reset progress, and allowed cancellation
remain available.

- [ ] **Step 2: Run the two focused test classes and verify they fail**

Run the Chat and Streaming test projects with `--filter MaintenanceModeTest|ChatMaintenanceGuardTest`.

- [ ] **Step 3: Implement one shared guard and add it at service boundaries**

Do not scatter enum checks. The guard resolves effective state and throws one typed constraint error
that clients can translate into maintenance UI. Backend-only import/reset commands use a typed bypass
created inside `Chat.Service`; no public boolean `skipMaintenance` flag is allowed.

- [ ] **Step 4: Run the focused tests**

Expected: every blocked operation returns the maintenance error and produces no durable or live state.

- [ ] **Step 5: Run affected project builds**

Run:

```powershell
dotnet build src/dotnet/Chat.Service/Chat.Service.csproj --no-restore
dotnet build src/dotnet/Streaming.Service/Streaming.Service.csproj --no-restore
```

- [ ] **Step 6: Commit**

```powershell
git add src/dotnet/Chat.Service src/dotnet/Streaming.Service tests/Chat.IntegrationTests/ChatMaintenanceGuardTest.cs tests/Streaming.IntegrationTests/MaintenanceModeTest.cs
git commit -m "feat(chat): enforce maintenance mode"
```

### Task 4: Surface maintenance in news and the chat UI

**Files:**
- Modify: `src/dotnet/Api/Chat/ChatNews.cs`
- Modify: `src/dotnet/Chat.Service/Chats.cs`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatMaintenanceView.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/chat-maintenance-view.css`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor`
- Modify: `src/dotnet/Localization/Resources/Strings.*.json`
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/ChatMaintenanceViewTest.cs`

**Interfaces:**
- Consumes: effective maintenance state and operation-specific detail supplied by later plans.
- Produces: compact `ChatNews.MaintenanceKind` and the reusable maintenance shell.

- [ ] **Step 1: Write failing UI state tests**

Cover ordinary chat, direct maintenance, inherited Place maintenance, initiator link, no timeline or
editor, owner action area, member action area, progress/failure text, and old-client-safe news data.

- [ ] **Step 2: Run the focused UI tests and verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --no-restore --filter ChatMaintenanceViewTest`

- [ ] **Step 3: Add the compact news field and populate it**

Expose kind only in `ChatNews`; the active view obtains current full state from
`IChatMaintenances.GetEffective`. Keep the slim-news payload small.

- [ ] **Step 4: Implement the maintenance shell**

Replace the chat timeline/footer while active. Show kind, initiator, start time, current status, and
an operation-specific child content area. Do not hide navigation to other unaffected chats.

- [ ] **Step 5: Add localized strings and run TypeScript/UI validation**

Run: `npm run build:Verify`

- [ ] **Step 6: Run the focused UI tests and Chat service tests**

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/dotnet/Api/Chat/ChatNews.cs src/dotnet/Chat.Service/Chats.cs src/dotnet/UI.Blazor.App tests/Chat.UI.Blazor.IntegrationTests/ChatMaintenanceViewTest.cs
git commit -m "feat(ui): show chat maintenance mode"
```

### Task 5: Final verification

- [ ] Document maintenance states, blocked operations, owner recovery, and Place inheritance in the
  English owner/API pages in sibling `ActualChat-docs`.
- [ ] Run `dotnet build ActualChat.CI.slnf --no-restore` if the CI solution filter exists; otherwise build the affected projects.
- [ ] Run all `ChatMaintenance` tests in Chat, Streaming, Notifications, and UI test projects.
- [ ] Run `npm run build:Verify`.
- [ ] Manually verify one group and one Place child chat with two owners and one member.
- [ ] Confirm an old client receives a normal chat/news payload and every server mutation is still rejected while maintenance is active.
