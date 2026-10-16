---
title: Place and chat invites, stage 1
description: Implementation plan for the membership journal, join requests, landing page, share dialog and queue UI.
---

# Place and chat invites: stage 1 implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Anyone can be brought into a private place or chat by a link: a public target joins at once,
a private one queues a join request that an owner or moderator approves, an owner's invite link joins
without approval, and every membership change is recorded.

**Architecture:** Every join and leave already goes through `AuthorsBackend.OnUpsert`; it gains a
`MembershipChange` (method, actor, invite, request) and appends a row to a new `membership_events`
table in the same transaction. Join requests are a second table behind a new `IJoinRequests` API and
`IJoinRequestsBackend`; approval upserts the author through the same backend command, so the journal
records it like any other join. Non-members never get `Read` on a private target: the landing page
reads a narrow `JoinTarget` card (title, picture, state) from `IJoinRequests`, not from `Chats.Get`.

**Tech Stack:** C# 15, Fusion compute methods and commands, EF Core/PostgreSQL, MessagePack, Blazor,
xUnit + FluentAssertions, bUnit.

**Spec:** [Place and chat invites](./place-invites.md). Issue
[#5371](https://github.com/Actual-Chat/actual-chat/issues/5371).

[[toc]]

## Global constraints

- No membership change bypasses the journal, except chat removal (`AuthorsBackend_Remove`, which
  deletes the authors together with the chat) and the copy-chat insert, which writes its rows itself.
- Nothing private goes into `Author` / `AuthorFull` / `Chat` / `Place` beyond the `JoinRequestAccess`
  value. Journal rows and requests reach clients only through `EditMembers`-gated methods.
- A non-member of a private target never gets `ChatPermissions.Read`; `ChatsBackend.GetRulesRaw` is
  not changed for join requests.
- Approving requires `EditMembers` on the target. A chat moderator can never let anyone into the place.
- Opening a link never joins. Joining happens only on the button.
- Migrations come from `./ef-migrations.cmd` only; `has-pending-model-changes` is clean before each
  commit that touches the model; `DbMigrationTest` runs after the migration.
- `JoinRequestAccess` defaults: `Off` for existing rows (column default), `SignedIn` for private
  targets created after the release. The default is to be confirmed with Alexey on the demo, so keep it
  in one place (see [Task 4](#task-4-joinrequestaccess-on-chats-and-places)).
- The landing card (title, picture) reaches a non-member only when `JoinRequestAccess` lets that
  viewer see it; for everyone else the target is indistinguishable from a missing one, including in
  link previews.
- Wire types get MessagePack keys after the last used one; `ApiContractsAotSource.g.cs` and the UI
  AOT keep lists are regenerated with `App.AotHelper -g`, never edited.

## Reuse

### Existing abstractions to reuse

- **Membership writes:** `AuthorsBackend_Upsert` / `AuthorsBackend.OnUpsert` stay the single join
  and leave path; the new field is an init property at `Key(7)`, like `ImportId`, so the positional
  call sites keep compiling.
- **Public commands:** `Authors.OnJoin`, `OnInvite`, `OnExclude`, `OnRestore`, `OnLeave` and the
  `Places_*` wrappers in `Places.cs` supply the method and actor. `RestoreAuthorMembership` gets the
  method as a parameter, since it serves both restore and re-invite.
- **Invites:** `DbInvite`, `InvitesBackend.OnUse`, `ChatInviteSettings`, `Invite.Grants` /
  `CanUse`, `InvitesBackend.GetInviteChatLinkPreview` (the invite landing card), and `InviteList`
  (owner's list of links, reused in the share dialog).
- **Permissions:** `ChatPermissions.EditMembers` / `EditProperties`, `SystemRole.Moderator`,
  `ChatExt.RequiresOwner` (not extended; `JoinRequestAccess` is `EditProperties`, per the spec).
- **Recipients of moderator pushes:** `RolesBackendExt.ListSystemRoleAuthorIds` for Owner + Moderator,
  as `LiveSessionsBackend` does.
- **Notifications:** `Notification` union, `ChatNotification`, `MergeWith` coalescing,
  `NotificationsBackend_Notify`, and the dismiss-for-everyone pattern of `OnCancelCall`
  (deterministic per-user `NotificationId`s). The unused `InvitationNotification` /
  `NotificationKind.Invitation` carries "you've been let in".
- **Expiry:** `PeriodicFlow` + `IMasterFlow` (as `TranslationCleanupFlow`) calling a backend command,
  so expiry invalidates and raises events; `Computed.GetCurrent().Invalidate(expiresAt - now)` in
  reads, as `SharedLocationsBackend` does.
- **Persistence:** `DbWebHookDelivery` (append-only, status, composite indexes as attributes) and
  `DbWebHook` (versioned, UTC `DateTime` properties, `RequireSameOrEmptyId`) as entity templates;
  `SharedLocationId` as the id template; `DbSetExt.Lock` for per-key serialization;
  `Invalidation.Defer` for invalidation (no `Invalidation.IsActive` blocks).
- **Landing page:** the dormant `InaccessiblePlace` and `SendJoinPlaceRequestWidget` components, the
  `ChatPage.canSendJoinPlaceRequest` hook (hard-coded to `false` today), the existing
  `JoinRequest_NotMember` / `JoinRequest_Text` / `JoinRequest_Send` strings, `ChatNotFound` for the
  "off" case, and the signed-out path of `ChatInvitePage` (`RequestSignInFromHomePage`, `AttentionUI`
  holds).
- **Queue UI:** `AuthorList`'s `Collapsed` groups ("Owners — N") for "New requests — N";
  `PlaceInfoMembersTab`; `ChatListUI.GetSeparatorIndexes` / `IsLastItemInBlock` and `Divider` for the
  People tab block; `UnreadCount` for the unread mark.
- **Settings:** `EditChatTypeModalPage` toggles block (`TileItem` + `ToggleEdit`) and
  `PlaceSettingsEditTypeModalPage`, which gets the same block.

Nothing found for: a generic audit log (none exists in the solution), a request/approval queue
(none), a notification addressed to a role rather than to users (none; fan-out by user id is the
pattern).

### Reusability of new components

| Component | Placement | Why |
|---|---|---|
| `MembershipEvent`, `MembershipMethod`, `MembershipChange` | `Chat.Contracts` (backend), moved to `Api` in stage 2 when the filtered view ships | Only Chat.Service and its tests read it in stage 1. A shared `Core.Server` audit log was considered: no second consumer exists, and the rows reference chat-specific ids. Local. |
| `DbMembershipEvent`, `DbJoinRequest` | `Chat.Service/Db` | Chat database tables. |
| `JoinRequest`, `JoinRequestId`, `JoinTarget`, `IJoinRequests` | `Api` / `Api.Contracts` | Client-facing. |
| `JoinRequestAccess` + `Allows` / `Min` | `Api/Chat` | Part of `Chat` and `Place`; the UI, the backend and the Open Graph provider all evaluate it. |
| `JoinRequestsBackend`, `JoinRequestExpiryFlow` | `Chat.Service` | Uses `AuthorsBackend` and the chat DB directly. |
| `JoinRequestNotification` | `Api/Notifications` | Sits in the existing union. |
| `JoinLanding` (card + one button) | `UI.Blazor.App/Components/JoinLanding/` | Used by three pages (`/join`, `/chat`, `/place`); app-specific. |
| `JoinRequestList` (rows with ✕/✓) | `UI.Blazor.App/Components/JoinRequests/` | Used by `AuthorList`, `PlaceInfoMembersTab` and the chat-list People block. |

Nothing here is a candidate for `ActualChat.Core` / `Core.Server` or `src/nodejs`: no new TypeScript
is planned.

---

### Task 1: Journal model and table

**Files:**
- Create: `src/dotnet/Chat.Contracts/MembershipEvent.cs` (`MembershipEvent`, `MembershipEventKind`,
  `MembershipMethod`, `MembershipChange`)
- Create: `src/dotnet/Chat.Service/Db/DbMembershipEvent.cs`
- Modify: `src/dotnet/Chat.Service/Db/ChatDbContext.cs` (DbSet, collation "C" on id columns)
- Generate: `src/dotnet/Chat.Service.Migration/Migrations/<ts>_Add_MembershipEvents.cs` (+ Designer,
  snapshot)
- Test: `tests/Chat.UnitTests/MembershipChangeSerializationTest.cs`

**Interfaces:**
- `MembershipEventKind`: `Joined`, `Left`, `RequestSubmitted`, `RequestApproved`, `RequestRejected`,
  `RequestWithdrawn`, `RequestExpired`.
- `MembershipMethod`: `None` (an author row that isn't a membership change — the per-chat row a
  public place chat materializes on first write; not journaled), `System` (0, the default for an
  unannotated upsert), `Creator`, `Self`, `InviteLink`, `Added`, `Restored`, `JoinRequest`,
  `Excluded`, `PlaceLeft` (the cascade into place chats), `Template`, `CopyChat`, `Bot`. Peer chats
  never flip `HasLeft`, so they write nothing.
- `MembershipChange(MembershipMethod Method, UserId ActorId, string? InviteId, JoinRequestId? RequestId)`,
  `[DataContract, MessagePackObject]`.
- Row: `Id` (random, `SharedLocationId` style), `ChatId`, `UserId`, `AuthorId`, `Kind`, `Method`,
  `ActorId`, `InviteId`, `RequestId`, `CreatedAt`. Indexes `(ChatId, CreatedAt)`, `(UserId)`,
  `(InviteId)` for the stage-2 revoke dialog. No `Version`: rows are never updated.

- [ ] **Step 1:** Write the serialization round-trip test for `MembershipChange` (MessagePack, as
  `ChatCommandSerializationTest` does).
- [ ] **Step 2:** Add the types and the entity; register the DbSet.
- [ ] **Step 3:** `dotnet build src/dotnet/Chat.Service.Migration`, then
  `./ef-migrations.cmd Chat.Service add Add_MembershipEvents --context ChatDbContext`.
- [ ] **Step 4:** `has-pending-model-changes` reports none; unit test passes.

### Task 2: Write the journal from `OnUpsert`

**Files:**
- Modify: `src/dotnet/Chat.Contracts/IAuthorsBackend.cs` (`AuthorsBackend_Upsert.Change`, `Key(7)`;
  `[ComputeMethod] ListMembershipEvents(ChatId, CancellationToken)`)
- Modify: `src/dotnet/Chat.Service/AuthorsBackend.cs` (`OnUpsert`, `OnCopyChat`,
  `ExcludeUserFromPlaceChats`, the webhook-independent paths)
- Modify: `src/dotnet/Chat.Service/Authors.cs` (`OnJoin`, `OnLeave`, `OnInvite`, `OnExclude`,
  `OnRestore`, `RestoreAuthorMembership(…, MembershipMethod)`)
- Modify: `src/dotnet/Chat.Service/ChatsBackend.cs` (creator at :917, `EnsurePlaceChatAuthorExists`
  at :2031, peer chats via `EnsureJoined`)
- Modify: `src/dotnet/Chat.Contracts/AuthorsBackendExt.cs` (`EnsureJoined` takes a method)
- Modify: `src/dotnet/Chat.Service/Chats.cs` (template clone, :994 and :1062)
- Modify: `src/dotnet/Chat.Service/WebHooks/WebHooksBackend.cs` (`EnsureBot`, `RetireBot` → `Bot`)
- Test: `tests/Chat.IntegrationTests/MembershipJournalTest.cs`

**Rules inside `OnUpsert`:** the kind comes from the flip (`existing == null || existing.HasLeft` →
not left is `Joined`; not left → left is `Left`; no flip writes nothing). Method and actor come from
`command.Change ?? new(System, default, null, null)`. The row is added to the same `dbContext` before
`SaveChangesAsync`, and `ListMembershipEvents(chatId)` is invalidated through `Invalidation.Defer`.

**Call sites:** `OnJoin` → `Self`, or `InviteLink` with the invite id when a `ChatInviteSettings`
grant exists — the grant read moves **before** the upsert (it is still deleted after). Implicit joins
on post, stream, upload and forward reach `OnJoin` too and are recorded as `Self`; the spec doesn't
ask to tell them apart. `OnInvite` → `Added` (re-invite of a left member → `Added`, not `Restored`).
`OnRestore` → `Restored`. `OnExclude` → `Excluded`. `OnLeave` → `Self`. The place cascade →
`PlaceLeft`, actor empty: the root row holds who excluded them. Creator, template, peer and bot sites
get their methods; seed and upgrade code stays `System`. `OnCopyChat` adds one `Joined`/`CopyChat` row
per copied active author next to `dbContext.Authors.Add` at :444.

- [ ] **Step 1:** Write failing integration tests, one per method: self-join a public chat; join by
  invite link (row carries the invite id); add by owner; exclude and restore by a moderator (actor is
  the moderator); leave; leaving a place writes `PlaceLeft` rows for its chats; creator; copy chat to
  place; a `DoNotNotify` upsert still writes a row.
- [ ] **Step 2:** Add the field and the write in `OnUpsert`; thread the method through the call sites.
- [ ] **Step 3:** Run `MembershipJournalTest`, `AuthorsTest`, `PlaceOperationsTest`,
  `ModeratorRoleTest`, `ChatOperationsTest`, `WebHooksIncomingBackendTest`.
- [ ] **Step 4:** Commit: journal model, migration and writes.

### Task 3: Invite link name

**Files:**
- Modify: `src/dotnet/Api/Invite/Invite.cs` (`string Name`, next free key), `DbInvite.cs` (column,
  `ToModel` / `UpdateFrom`), `Invites.cs` (`Invites_Generate` carries it; no rename command in stage 1)
- Modify: `src/dotnet/UI.Blazor/Components/InviteList/InviteList.razor` (show the name when set)
- Generate: migration `Add_InviteName` in `Invite.Service.Migration`
- Test: extend `tests/Chat.UnitTests/InviteDetailsSerializationTest.cs`

- [ ] **Step 1:** Test, model, column, migration, `has-pending-model-changes`.
- [ ] **Step 2:** Commit.

### Task 4: `JoinRequestAccess` on chats and places

**Files:**
- Create: `src/dotnet/Api/Chat/JoinRequestAccess.cs` (`Off = 0`, `PlaceMembers`, `SignedIn`,
  `Anyone`, ordered from the strictest; `JoinRequestAccessExt.Allows(access, isSignedIn,
  isPlaceMember)` and `Min` for the chat-in-place rule)
- Modify: `src/dotnet/Api/Chat/Chat.cs` (`Chat.JoinRequestAccess` `Key(22)`, `ChatDiff`)
- Modify: `src/dotnet/Api/Chat/Place.cs` (`Place.JoinRequestAccess` `Key(12)`, `PlaceDiff`)
- Modify: `src/dotnet/Chat.Service/Db/DbChat.cs`, `Db/DbPlace.cs` (int column)
- Modify: `src/dotnet/Chat.Service/PlacesBackend.cs` (`ToChatDiff` copies the value to the root chat)
- Modify: `src/dotnet/Chat.Service/ChatsBackend.cs` (`OnChange` create: a private group or place chat
  with no value gets `JoinRequestDefaults.ForNewTarget`; `ApplyDiff`: the value is meaningless on a
  public chat and is reset to `Off` when one becomes public; `PlaceMembers` is refused on anything but
  a non-root place chat)
- Modify: `src/dotnet/Chat.Service/ChatDiffExt.cs` (`ValidateForPlaceChat` allows the value)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatSettings/EditChatTypeModalPage.razor(.cs)` (a
  choice of the values under the Private option; `PlaceMembers` only for a chat in a place),
  `PlaceSettings/PlaceSettingsEditTypeModalPage.razor` (the same choice without `PlaceMembers`)
- Modify: `src/dotnet/Localization/Resources/*` (label, one caption per value, all languages)
- Generate: migration `Add_JoinRequestAccess` (both columns, default `0`)
- Test: `tests/Chat.IntegrationTests/JoinRequestAccessTest.cs`, `tests/Chat.UnitTests/JoinRequestAccessTest.cs`

The settings pages close for non-owners today; the server accepts the value from anyone with
`EditProperties`, as the spec says, and a moderator-facing entry point waits for the queue UI
(Task 11), where the choice is also shown.

- [ ] **Step 1:** Unit tests for `Allows` and `Min` over every value and viewer (guest, signed in,
  place member). Integration tests: a new private chat gets `SignedIn`, a migrated one `Off`; a place
  change reaches its root chat; `PlaceMembers` on a place or a standalone chat is refused; a moderator
  can change it, a plain member can't.
- [ ] **Step 2:** Model, migration, backend, settings toggles, strings.
- [ ] **Step 3:** Commit.

### Task 5: Join request model, table and backend

**Files:**
- Create: `src/dotnet/Api/Identifiers/JoinRequestId.cs`
- Create: `src/dotnet/Api/Chat/JoinRequest.cs` (`JoinRequest`, `JoinRequestStatus`, `JoinTarget`,
  `JoinTargetState`)
- Create: `src/dotnet/Api.Contracts/Chat/IJoinRequests.cs` (+ commands)
- Create: `src/dotnet/Chat.Contracts/IJoinRequestsBackend.cs` (+ backend commands, events)
- Create: `src/dotnet/Chat.Service/Db/DbJoinRequest.cs`, `JoinRequests.cs`, `JoinRequestsBackend.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` (`AddApi` / `AddBackend`),
  `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` (`AddClient<IJoinRequests>`), AOT sources
- Generate: migration `Add_JoinRequests`
- Test: `tests/Chat.IntegrationTests/JoinRequestsTest.cs`

**Model:** `JoinRequest(Id, Version)` with `ChatId` (a place request targets the root chat), `UserId`,
`Status` (`Pending`, `Approved`, `Rejected`, `Withdrawn`, `Expired`), `ParentId` (the place request of
a linked chat request), `InviteId` (pre-approves the chat request), `WasExcluded`, `CreatedAt`,
`ExpiresAt`, `DecidedAt`, `DecidedBy`. Indexes: unique `(ChatId, UserId)` filtered on
`status = Pending`; `(ChatId, Status)`; `(ParentId)`; `(Status, ExpiresAt)`.

**`IJoinRequests` (client):**
- `GetTarget(Session, ChatId, CancellationToken)` → `JoinTarget?`: title, picture, place title,
  `State` (`Member`, `CanJoin`, `CanRequest`, `SignInToRequest`, `RequestPending`, `RejectedUntil`,
  `Unavailable`), `AlsoRequestsPlace`. `null` when the target doesn't exist or its
  `JoinRequestAccess` doesn't let this viewer see it (`Off`; `PlaceMembers` for a non-member of the
  place; `SignedIn` for a guest; for a chat in a place, the stricter of the chat's and the place's
  value) — the caller then shows the ordinary not-found page. `SignInToRequest` is a guest under
  `Anyone`.
- `ListPending(Session, ChatId, CancellationToken)` → `ApiArray<JoinRequest>`, `EditMembers` only;
  chat requests whose parent is still pending are left out.
- Commands: `JoinRequests_Submit { ChatId }`, `JoinRequests_Withdraw { ChatId }`,
  `JoinRequests_Approve { JoinRequestId }`, `JoinRequests_Reject { JoinRequestId }`.

**Rules in the backend:**
- Submit: refuses a guest, a member, a public target (join instead), a target whose
  `JoinRequestAccess` hides it from this user (the same check as `GetTarget`), and a rejection younger
  than 7 days; returns the
  existing pending request instead of a second one. A private chat in a place the person isn't in
  creates two rows: the place request and the chat request with `ParentId`. A public place is joined
  at once through `Places_Join` instead of a place request.
- `WasExcluded` = an author row with `HasLeft` whose last journal row is `Excluded`.
- Approve: `EditMembers` on the request's chat, then `AuthorsBackend_Upsert` with
  `Change = (JoinRequest, approver, request.InviteId, request.Id)`, then the status. Approving a place
  request activates its chat request: pre-approved (has `InviteId`) → joined at once as `InviteLink`;
  otherwise it becomes visible in the chat's queue and notifies the chat's moderators.
- Reject: status and pause; rejecting a place request closes its chat request as `Rejected` too.
- Every transition appends a `Request*` journal row.
- Reads invalidate on the deferred path: `ListPending(chatId)`, `GetTarget(chatId)` for the user;
  `ListPending` also invalidates itself at the nearest `ExpiresAt`.

- [ ] **Step 1:** Tests: submit for a private chat; second submit returns the same request; withdraw;
  approve by a moderator joins and journals with the actor; a plain member can't approve; reject then
  resubmit before 7 days fails and after it works (shift the clock through the test host's
  `MomentClock`); linked requests in a private place — the chat request is hidden until the place is
  approved and closes on rejection; a public place joins the place at once; `GetTarget` and
  Submit across the values — `Off` hides from everyone, `PlaceMembers` shows a place member and hides
  from an outsider, `SignedIn` hides from a guest, `Anyone` gives a guest `SignInToRequest`, a chat in
  a place takes the stricter value; `WasExcluded` after an exclusion.
- [ ] **Step 2:** Implement contracts, entity, migration, services, registration; regenerate AOT.
- [ ] **Step 3:** Commit.

### Task 6: Expiry

**Files:**
- Create: `src/dotnet/Chat.Service/Flows/JoinRequestExpiryFlow.cs`
- Modify: `IJoinRequestsBackend` (`ListExpired(limit)`, `JoinRequestsBackend_Expire { Ids }`),
  `ChatServiceModule.cs` (`AddFlows().Add<JoinRequestExpiryFlow>()`)
- Test: extend `JoinRequestsTest`

A `PeriodicFlow, IMasterFlow` in the style of `TranslationCleanupFlow`: lists a batch of pending rows
with `ExpiresAt <= now`, expires them through the backend command (journal row, invalidation, the
moderators' push cleared), runs again at once while batches are full, otherwise hourly. Expiry is
silent for the requester.

- [ ] **Step 1:** Test: a request 30 days old is `Expired`, `GetTarget` offers `CanRequest` again.
- [ ] **Step 2:** Implement, register, commit.

### Task 7: Notifications

**Files:**
- Modify: `src/dotnet/Api/Identifiers/NotificationKind.cs` (`JoinRequest` before `Invalid`)
- Create: `src/dotnet/Api/Notifications/JoinRequestNotification.cs` (a `ChatNotification` keyed by
  the target chat, `PendingCount`, `MergeWith` keeps the latest count, `DismissMode.Explicit`)
- Modify: `src/dotnet/Api/Notifications/Notification.cs` (union tag 11), `NotificationExt.cs` (push
  tag = chat tag)
- Modify: `src/dotnet/Notifications.Contracts/INotificationsBackend.cs`,
  `Notifications.Service/NotificationsBackend.cs` (`OnJoinRequestChangedEvent`)
- Modify: `src/dotnet/Chat.Service/JoinRequestsBackend.cs` (raises `JoinRequestChangedEvent`)
- Modify: the Android/iOS notification renderers if they switch on the kind
  (`App.Maui/Platforms/Android/Notifications/NotificationData.cs`), localized strings
- Test: `tests/Notifications.IntegrationTests/JoinRequestPushTest.cs`

**Behaviour:** on submit and on activation of a linked chat request, every owner and moderator of the
target (`ListSystemRoleAuthorIds`) gets `JoinRequestNotification` with the current pending count ("3
new requests in *P*"). When a decision leaves no pending request, the notification is dismissed for
all of them by id, the `OnCancelCall` way; when some remain, it is re-sent with the new count. Approval
sends the requester an `InvitationNotification` that opens the chat. Rejection, withdrawal and expiry
send nothing to the requester.

Risk: `NotificationKind` travels as a number; an older app parses `11` as invalid and drops the push.
Acceptable — the in-app queue still shows the request — but check the Android renderer doesn't crash
on it before shipping.

- [ ] **Step 1:** Tests: two moderators get one coalesced push; approval by one dismisses it for the
  other; the requester gets the approval push and no rejection push.
- [ ] **Step 2:** Implement, commit.

### Task 8: Invite link joins in one step; private place chat via invite

**Files:**
- Modify: `src/dotnet/Api.Contracts/Invite/IInvites.cs` (`Invites_Use.Join`, `AvatarId?`, next keys)
- Modify: `src/dotnet/Invite.Service/Invites.cs` (after the grant, joins through `Authors_Join` /
  `Places_Join` when `Join` is set)
- Modify: `src/dotnet/Invite.Service/InvitesBackend.cs:160-176` (no more "Only place members can use
  this code": for a private place the user gets a place request plus a pre-approved chat request; if
  the invite's creator owns the place, the root chat gets a grant too and the user joins both)
- Test: extend `JoinRequestsTest` and `ChatOperationsTest`

- [ ] **Step 1:** Tests: `Invites_Use { Join = true }` leaves the user a member with an `InviteLink`
  journal row; a chat invite into a private place queues the place request and joins the chat on
  approval; the same invite created by the place owner joins both at once.
- [ ] **Step 2:** Implement, commit.

### Task 9: Landing page and routing

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/JoinLanding/JoinLanding.razor` (+ `.css`), replacing
  `InaccessiblePlace` and `SendJoinPlaceRequestWidget` (delete both, and the `EnableIncompleteUI`
  branch that gated them)
- Modify: `src/dotnet/UI.Blazor.App/Pages/ChatPage.razor` (`canSendJoinPlaceRequest` → `JoinTarget`
  from `IJoinRequests.GetTarget`; `JoinLanding` instead of `ChatNotFound` when it isn't `null`)
- Modify: `src/dotnet/UI.Blazor.App/Components/PlaceInfo/PlaceInfoPage.razor` (the same for
  `/place/{id}`; a public place shows `PlaceInfo` with a Join button)
- Modify: `src/dotnet/UI.Blazor.App/Pages/ChatInvitePage.razor` (stops using the invite on open;
  shows `JoinLanding` built from `GetInviteChatLinkPreview`; the button sends
  `Invites_Use { Join = true }`; the signed-out branch is unchanged)
- Modify: `src/dotnet/Chat.Service/OpenGraphTagsProvider.cs` (when `Chats.Get` is `null` for the
  crawler's guest session, a private target under `Anyone` gets its title and picture from
  `GetTarget`; every other value keeps returning `null`, as today)
- Modify: localized strings (reuse `JoinRequest_*`, add "request sent", "Withdraw", "rejected",
  "asks for the place too", "sign in to request access")
- Test: `tests/Chat.UI.Blazor.UnitTests/JoinLandingTest.cs` (bUnit, one render per `JoinTargetState`),
  `tests/Chat.IntegrationTests/OpenGraphTagsTest.cs` (preview only under `Anyone`)

The button per state: `CanJoin` → Join (public: `Authors_Join` / `Places_Join`; invite: as above),
`CanRequest` → Request access, `SignInToRequest` → Sign in (`RequestSignInFromHomePage` with the link
as the redirect, as `ChatInvitePage` does), `RequestPending` → "Request sent" + Withdraw,
`RejectedUntil` → text only, `Member` → navigate to the chat. A guest who gets `null` under
`SignedIn` sees `ChatNotFound` with its usual sign-in footer, and the same redirect brings them back
to the link.

- [ ] **Step 1:** bUnit tests per state.
- [ ] **Step 2:** Implement; remove the dormant components and their AOT keep entries.
- [ ] **Step 3:** Manual check with `/debug-ui` on the local server, two accounts and a guest window:
  a plain link to a private place under each of the four values (`PlaceMembers` on a chat in the
  place), signed in and as a guest, including the sign-in round trip under `SignedIn`; a private chat
  in a private place; an invite link signed out and signed in; a public place by its own address.
- [ ] **Step 4:** Commit.

### Task 10: Share dialog

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/ShareUIExt.cs` (`GetModel(ChatId)` / `GetModel(PlaceId)`: the
  plain address for every member; a private target only when `JoinRequestAccess` isn't `Off`; no
  `GetOrGenerate*Invite`; a public place by `Links.PlaceInfo`)
- Modify: `src/dotnet/UI.Blazor/Components/Share/ShareModalModel.cs`, `ShareKind.cs` (caption kinds:
  "needs approval", "joins without approval"), `ShareWithPlaceMembersOnly.GetFor` (when the effective
  value is `PlaceMembers`: the link only reaches place members, so the selector stays limited to them)
- Modify: `src/dotnet/UI.Blazor.App/Components/Share/ShareModal.razor` (owners: an "Invite link"
  section — `InviteList` plus Create, with the warning caption)
- Modify: `src/dotnet/UI.Blazor.App/Components/AddMember/AddMemberModalPage.razor` (the placeholder
  is the plain link for both places and chats; drop the TODO)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatWelcomeBlock.razor:39-69` (same rules as Share)
- Test: `tests/Chat.UI.Blazor.UnitTests/ShareModelTest.cs`

- [ ] **Step 1:** Tests: a plain member of a private place under `SignedIn` gets the place address;
  under `Off` gets no model; a chat under `PlaceMembers` is shared to place members only; an owner gets
  the invite section; no invite is generated by opening Share. *Not written: `ShareUIExt` needs an
  `AppUIHub`, which the unit-test hosts don't build.*
- [x] **Step 2:** Implement, commit. *The invite section is a shared `InviteLinksSection`, which also
  replaced the copies in both settings pages. A public place is still shared by its welcome chat link
  (`NOTE(DF)` in `ShareUIExt`: direct place navigation works poorly). Manual check pending: no browser
  in this session.*

### Task 11: Queue UI

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/JoinRequests/JoinRequestList.razor` (+ `.css`): rows
  with avatar and name (`IAccounts.Get`), "previously excluded" mark, ✕ / ✓
- Modify: `src/dotnet/UI.Blazor.App/Components/RightPanel/AuthorList.razor` (a `Collapsed` "New
  requests — N" group first, when `CanEditMembers` and N > 0)
- Modify: `src/dotnet/UI.Blazor.App/Components/PlaceInfo/PlaceInfoMembersTab.razor` (the same block)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatListUI.cs`, `Components/ChatList/ChatList.razor`
  (People tab: pending requests of the chats and places the user moderates, as a block above the list,
  marked unread, followed by a `Divider` like the pinned block)
- Modify: the `JoinRequestAccess` choice reachable for moderators from the requests block
- Test: `tests/Chat.UI.Blazor.UnitTests/JoinRequestListTest.cs`

Before coding, open the Figma frame (node 27697-223756) through the Figma MCP and match spacing, the
✕/✓ icons and the divider.

- [x] **Step 1:** bUnit tests: the block is hidden without `EditMembers`, shows the count, approve and
  reject send the commands. *In `tests/Chat.UI.Blazor.IntegrationTests/JoinRequestQueueTest.cs`: only
  that host builds an `AppUIHub`. Also covers a Place's requests in `ChatListUI.ListJoinRequestGroups`
  and a moderator changing `JoinRequestAccess`.*
- [x] **Step 2:** Implement. *The member-list group is a shared `JoinRequestQueue` (used by `AuthorList`
  and `PlaceInfoMembersTab`). The People-tab block (`JoinRequestBlock`) sits above the virtual list, not
  inside it, so it doesn't scroll with the chats; its "unread" mark is an `UnreadCount` with the request
  count per chat. `JoinRequestAccess` is reachable from the block through `JoinRequestAccessModal` for
  anyone with `EditProperties` (moderators included). The groups are found client-side: the listed group
  and Place chats plus the user's Places, filtered by `CanEditMembers` — no server-side "all my requests"
  method, which a sharded backend couldn't invalidate. Manual check against the mockup pending: no
  browser in this session; the narrow layout still has no frame.*
- [x] **Step 3:** Commit.

### Task 12: Final verification

- [ ] `dotnet build ActualChat.CI.slnf`.
- [ ] Chat, Notifications, UI unit and integration tests touched above; `DbMigrationTest` (Slow).
- [ ] `has-pending-model-changes` for `Chat.Service` and `Invite.Service`.
- [ ] End-to-end on the local server with two accounts: Alexey's scenario — a new user, not in anyone's
  contacts, follows a plain link to a private place, the owner approves from the People tab, the user
  gets the push and lands in the place; then the same with an invite link and no approval.
- [ ] Update the spec's [Stages](./place-invites.md#stages) with what shipped, and the
  `JoinRequestAccess` default after the demo.

## Open points

- `JoinRequestAccess` default for new targets (`SignedIn` proposed) — demo to Alexey (spec
  decision 10).
- The chat-list People block (Task 11) needs Grigory's frame for the narrow layout; the screenshot
  covers desktop only.
