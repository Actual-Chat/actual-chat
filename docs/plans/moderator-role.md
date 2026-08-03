# Moderator Role — Implementation Plan

## Overview

Today a chat has exactly two meaningful privilege levels: the `Owner` system
role (all-powerful) and whatever the `Anyone` system role grants every joined
member (`Write | Invite | SeeMembers | Leave`). Anything between the two —
"you may police content, but you may not dismantle the chat" — has no
representation.

This plan introduces **Moderator**: a third system role, appointed by Owners,
whose members can moderate content and calls and edit the chat's presentation,
but cannot destroy, re-type, archive, or re-staff the chat.

### Capability matrix (decided with the user)

| Capability | Anyone | Moderator | Owner |
|---|:--:|:--:|:--:|
| Read / Write / Invite / Leave | ✅ | ✅ | ✅ |
| Delete **anyone's** messages | ❌ | ✅¹ | ✅ |
| Restore deleted messages | ❌ | ✅¹ | ✅ |
| Mute a participant's mic in a call | ❌ | ✅¹ ² | ✅ |
| Mute all / manage live-session rules (voice, video, transcription) | ❌ | ✅ | ✅ |
| Reassign the call host | ❌ | ❌ ² | ✅ |
| Remove (kick) a member | ❌ | ✅¹ | ✅ |
| Edit chat title, picture, description | ❌ | ✅ | ✅ |
| Change chat type (public/private), alias, guest/anonymous policy | ❌ | ❌ | ✅ |
| Archive chat | ❌ | ❌ | ✅ |
| Delete chat / remove a thread | ❌ | ❌ | ✅ |
| Appoint Owners or Moderators, edit roles | ❌ | ❌ | ✅ |

¹ **Owners are immune.** A Moderator who is not also an Owner cannot delete an
Owner's message, mute an Owner, or kick an Owner. Moderators *can* act on each
other. Owners can demote a misbehaving Moderator at any time.

² **Calls have a third authority: the host.** The call host may mute anyone,
Owners included — the host runs *this call*, while a Moderator polices the
*chat*. Authority is the union of the actor's roles, so a Moderator who is also
the host can mute Owners. Host reassignment is therefore an Owner-or-current-host
power, never a Moderator one; otherwise Owner-immunity would be trivially
escapable. Full model in Phase 5.2.

### Place-level moderators

Place Moderators work exactly the way place Owners already do, and mostly for
free: a place's permissions live on its **root chat**, and
`ChatsBackend.GetPlaceChatRules` hands the root chat's permissions to every
**public** place chat verbatim. So a root-chat Moderator automatically moderates
every public chat in the place. Private place chats resolve from their own roles
— same as for Owners today — so a private chat still needs its own Moderator
appointment. The only new work at the place layer is the permission flag, the
`PlaceRules` accessor, and the member-menu UI.

---

## Reuse

### Existing abstractions this plan builds on

Nothing here is new machinery — Moderator rides the exact rails Owner already
uses:

- `ChatPermissions` / `ChatPermissionsExt.AddImplied` / `.Has` / `.Require`
  (`src/dotnet/Api/Chat/`) — the flag enum and implication closure.
- `SystemRole` + `Role` + `Role.Fix()` (`src/dotnet/Api/Chat/`) — system-role
  identity and permission normalization.
- `AuthorRules` (`src/dotnet/Api/Chat/AuthorRules.cs`) — the `CanXxx()` façade
  every call site already consults.
- `RolesBackend` / `IRolesBackend` / `RolesBackendExt.GetSystem` — role CRUD,
  `ListAuthorIds`, `ListSystem`; explicit author-list membership (the Owner
  model) as opposed to the automatic membership of `Anyone`/`Guest`/`User`.
- `RolesBackend_Change` + `RoleDiff.AuthorIds` (`SetDiff<AuthorId[], AuthorId>`)
  — add/remove role members; already used by `Authors.OnPromoteToOwner` and
  `AuthorsBackend.RemovePrivilegedRoles`.
- `Roles.ListOwnerIds` — including its public-place-chat redirect to the root
  chat and `AuthorsBackend.Remap`; `ListModeratorIds` is a parameterization of it.
- `Authors_PromoteToOwner` / `Places_PromoteToOwner` and
  `EditChatMemberCommands` / `EditPlaceMemberCommands` /
  `EditChatMemberMenu` — the promote-a-member command + UI pattern. These
  commands are **generalized into `Authors_ChangeRole` / `Places_ChangeRole`**
  rather than duplicated (Phase 4).
- `DbRole` boolean-column mapping and `DbRole.ToModel`'s "fix system role
  permissions" clamp.
- `LiveSessions.RequireManage` (`Streaming.Service`) — the single choke point
  for call management; `LiveSessionsBackend` already resolves the Owner role to
  group owners with the host.

No suitable existing abstraction exists for "resolve the author ids of a system
role, backend-side, with place-chat redirection" — that logic is currently
inlined in `Roles.ListOwnerIds` and duplicated in `LiveSessionsBackend`. This
plan extracts it (see below).

### New components and their placement

1. **`RolesBackendExt.ListSystemRoleAuthorIds(...)` and
   `.IsInSystemRole(authorId, systemRole, …)`** — backend-side resolution of a
   system role's members, including the public-place-chat → root-chat redirect
   and `AuthorsBackend.Remap`.
   *Placement:* **shared** — `src/dotnet/Chat.Contracts/RolesBackendExt.cs`
   (the existing extension class). Three consumers in two projects
   (`Chat.Service`: message delete/restore + `Roles.ListOwnerIds`;
   `Streaming.Service`: mute + host grouping), so it must not live in either
   service. **Recommended: shared.**

2. **`ChatDiffExt.RequiresOwner(this ChatDiff)`** — which `ChatDiff` fields are
   Owner-only.
   *Placement:* `src/dotnet/Api/Chat/ChatExt.cs` (alongside the other `Chat`
   extensions). It's contract-level knowledge that both server validation and
   the settings UI need, so it belongs in `Api`, not `Chat.Service`.
   **Recommended: shared (`Api`).**

3. **`Authors_ChangeRole` / `Places_ChangeRole` commands** — replace
   `Authors_PromoteToOwner` / `Places_PromoteToOwner` in place, in
   `src/dotnet/Api.Contracts/Chat/IAuthors.cs` / `IPlaces.cs`. Not new surface
   so much as a generalization of existing surface; no shared-project option
   applies.

---

## Phase 1 — Permission model

### 1.1 `ChatPermissions.Moderate`

**File:** `src/dotnet/Api/Chat/ChatPermissions.cs`

```csharp
Moderate       = 0x8000, // Implies EditProperties, EditMembers (-> SeeMembers)
```

`0x8000` is the one free bit between `EditMembers` (`0x4000`) and `Owner`
(`0x10_000`), which keeps the "authority" flags contiguous.

### 1.2 `ChatPermissions.Moderate` in `AddImplied`

**File:** `src/dotnet/Api/Chat/ChatPermissionsExt.cs`

- `Owner` gains `| ChatPermissions.Moderate` in its implication set. This is the
  keystone: every existing `IsOwner()` moderation check can become
  `CanModerate()` without changing behaviour for Owners.
- New clause, evaluated **before** the `EditMembers` clause so its implications
  cascade:

```csharp
if (permissions.Has(ChatPermissions.Moderate))
    permissions |=
        ChatPermissions.EditProperties
        | ChatPermissions.EditMembers;
```

`Moderate` deliberately does **not** imply `Write` — a Moderator's ability to
post comes from the `Anyone` role like everyone else's.

### 1.3 `PlacePermissions.Moderate`

**File:** `src/dotnet/Api/Chat/PlacePermissions.cs`

Add `Moderate = 0x8000` with the same value. `Places.ToPlaceRules` casts
`(PlacePermissions)(int)authorRules.Permissions`, so the bit values **must**
stay aligned or place moderators silently break.

### 1.4 Rules accessors

- `src/dotnet/Api/Chat/AuthorRules.cs`: `public bool CanModerate() => Permissions.Has(ChatPermissions.Moderate);`
- `src/dotnet/Api/Chat/PlaceRules.cs`: `public bool CanModerate() => Permissions.Has(PlacePermissions.Moderate);`

### 1.5 `SystemRole.Moderator`

**File:** `src/dotnet/Api/Chat/SystemRole.cs`

```csharp
Moderator = 91,
```

Stored as `smallint`; the value is arbitrary but must never be reused. Placed
between the automatic roles (11–23) and `Owner` (101) so ordering reads as
increasing authority.

### 1.6 Canonical Moderator permission set

**File:** `src/dotnet/Api/Chat/ChatPermissionsExt.cs`

```csharp
public static readonly ChatPermissions Moderator =
    ChatPermissions.Moderate | ChatPermissions.Write | ChatPermissions.SeeMembers | ChatPermissions.Leave;
```

Clamp it in both places where `SystemRole.Owner` is already clamped, so the
permission set is defined in code and needs no data migration when it changes:

- `Role.Fix()` (`src/dotnet/Api/Chat/Role.cs`) — mirror the `SystemRole.Owner`
  branch.
- `DbRole.ToModel()` (`src/dotnet/Chat.Service/Db/DbRole.cs`) — mirror the
  `if (SystemRole is SystemRole.Owner) permissions = ChatPermissions.Owner;`
  clamp.

---

## Phase 2 — Persistence

### 2.1 `DbRole.CanModerate`

**File:** `src/dotnet/Chat.Service/Db/DbRole.cs`

`DbRole` stores permissions as individual `bool` columns, so a new flag needs a
new column. Add `public bool CanModerate { get; set; }`, plus the matching lines
in `ToModel()` and `UpdateFrom()`.

Strictly speaking the column is redundant for `SystemRole.Moderator` (clamped in
1.6), but it is required for any *custom* role that wants the flag, and omitting
it would make `Role → DbRole → Role` lossy.

### 2.2 EF migration

```
./ef-migrations.cmd Chat.Service add Add_Role_CanModerate
```

(Build first — the script passes `--no-build`.) One nullable-free `boolean NOT
NULL DEFAULT false` column on `"Roles"`; no data backfill.

---

## Phase 3 — Role plumbing

**File:** `src/dotnet/Chat.Service/RolesBackend.cs`

1. `List(...)` — the author-membership query filters
   `r.SystemRole == SystemRole.None || r.SystemRole == SystemRole.Owner`.
   Add `|| r.SystemRole == SystemRole.Moderator`. **Without this, appointing a
   moderator has no effect on their permissions.**
2. `Change(...)`, remove branch (`role.SystemRole is SystemRole.Owner or
   SystemRole.Anyone`) — add `or SystemRole.Moderator`; the role is emptied by
   removing its members, never deleted.
3. `Change(...)`, `update.AuthorIds` branch (`role.SystemRole is not
   SystemRole.None and not SystemRole.Owner` → "automatic membership rules") —
   add `and not SystemRole.Moderator`, so explicit membership is allowed.
   **Without this, appointing a moderator throws.**

**File:** `src/dotnet/Chat.Contracts/RolesBackendExt.cs`

4. Add `GetOrCreateSystem(rolesBackend, commander, chatId, systemRole,
   permissions, ct)` — returns the existing role or issues a
   `RolesBackend_Change` create. This is how the Moderator role comes into
   existence: **lazily, on first appointment**. Chosen over eager creation at
   chat-creation time because eager creation would also require an upgrade sweep
   over every existing chat (`ChatsUpgradeBackend.OnUpgradeChat`) to be
   consistent, and would leave an empty role on every chat that never uses one.
   `RolesBackend.Change` already guards duplicate system roles under
   `ForUpdate()`, so a concurrent double-create fails cleanly.
5. Add `ListSystemRoleAuthorIds(rolesBackend, chatsBackend, chatId, systemRole,
   ct)` — the backend-side generalization of `Roles.ListOwnerIds`'s core:
   redirect to `PlaceId.RootChatId` for public place chats, resolve the system
   role, list author ids, `AuthorsBackend.Remap` them back. Plus
   `IsInSystemRole(...)` on top of it.
6. Refactor `Roles.ListOwnerIds` (`src/dotnet/Chat.Service/Roles.cs`) to call
   (5), keeping its session/`CanSeeMembers` gate and anonymous-owner masking.
   Add `Roles.ListModeratorIds` alongside it with the same shape.
   `IRoles` (`src/dotnet/Api.Contracts/Chat/IRoles.cs`) gains `ListModeratorIds`.

**Note on masking:** `ListOwnerIds` masks anonymous owners for non-owner callers.
Owner-immunity checks must therefore use the **unmasked** backend helper (5), not
`IRoles.ListOwnerIds`, or an anonymous Owner would lose their immunity.

**File:** `src/dotnet/Chat.Service/AuthorsBackend.cs`

7. `RemovePrivilegedRoles` — generalize from Owner-only to a loop over
   `[SystemRole.Owner, SystemRole.Moderator]`, so leaving/being excluded drops
   Moderator too.

**File:** `src/dotnet/Chat.Service/Chats.cs` (template-chat clone, ~line 696)

8. The clone filter is `SystemRole is not Anyone and not None and not Owner`;
   `Moderator` falls through and is re-created on the clone with its members —
   which is the desired behaviour. Verify with the template-clone test rather
   than changing code.

---

## Phase 4 — Appointing and revoking: `Authors_PromoteToOwner` → `Authors_ChangeRole`

`Authors_PromoteToOwner` is a one-directional, single-role command. Adding a
second appointable role would mean a second near-identical command (and a third
later). **Replace it with a general `Authors_ChangeRole`** that sets an author's
membership in any explicit-membership system role, in either direction.

### 4.1 Commands

**File:** `src/dotnet/Api.Contracts/Chat/IAuthors.cs`

```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Authors_ChangeRole(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] SystemRole SystemRole,
    [property: DataMember, MemoryPackOrder(3), Key(3)] bool IsInRole
) : ISessionCommand<Unit>, IApiCommand;
```

`SystemRole` rather than `RoleId`, because this is specifically the convenience
command for the two system roles with explicit membership — it also owns the
lazy creation of the Moderator role (Phase 3.4). Arbitrary/custom roles keep
going through `Roles_Change`, which is already Owner-gated and takes a `RoleId`.

**File:** `src/dotnet/Api.Contracts/Chat/IPlaces.cs` — `Places_ChangeRole` with
the same shape, delegating to `Authors_ChangeRole` on the root chat exactly as
`Places_PromoteToOwner` delegates today.

Register both in `ApiContractsAotSource.g.cs` the way the `*_PromoteToOwner`
commands are (generated file — regenerate, don't hand-edit).

### 4.2 Handler

**File:** `src/dotnet/Chat.Service/Authors.cs` — `OnChangeRole`, replacing
`OnPromoteToOwner`:

- `chatId.EnsureNonThread()`; `chat.Rules.Require(ChatPermissions.Owner)` —
  **only Owners change roles**, for both Owner and Moderator;
- `ValidatePlaceMembershipRules(chat)`;
- reject any `SystemRole` other than `Owner` / `Moderator`:
  `Anyone`/`Guest`/`User`/`AnonymousUser` use automatic membership (the same
  constraint `RolesBackend.Change` enforces one layer down), and `None` is not a
  role;
- target author must exist and not have left; self-targeting is a no-op (as
  today);
- resolve the role via `GetOrCreateSystem` (Phase 3.4) — `Owner` always exists,
  `Moderator` is created on first appointment with `ChatPermissionsExt.Moderator`;
- issue `RolesBackend_Change` with `AuthorIds.AddedItems` or `RemovedItems`
  depending on `IsInRole`;
- on `(Owner, true)`, also remove the author from the Moderator role — Owner
  already implies `Moderate`, and keeping one role per author keeps
  `ListModeratorIds` and the member badges unambiguous.

**Owner demotion.** `(Owner, false)` is a genuinely new capability that the
current command cannot express. Today's UI copy is explicit — "This action
cannot be undone" — and `RolesBackend.Change` already guards the last-owner case
("There must be at least one user in Owners role"). **Recommendation: reject
`(Owner, false)` in `OnChangeRole` for now** with a clear constraint error, so
this refactor stays behaviour-preserving; the command shape then makes enabling
demotion later a one-line change plus UI, rather than another new command.
Flag if you'd rather enable Owner demotion as part of this work.

**File:** `src/dotnet/Chat.Service/Places.cs` — `OnChangeRole`, mirroring the
existing `OnPromoteToOwner` (`ThrowIfNonPlaceRootChatAuthor` + delegate).

### 4.3 Retiring the old commands — rewrite, don't duplicate

`Authors_PromoteToOwner` / `Places_PromoteToOwner` are `IApiCommand` — RPC-exposed
— so deleting them outright breaks clients that haven't updated. Keep both
records and their `[CommandHandler]` entries for one release, marked with the
convention already used in `IChats.cs` / `ILiveVideoStreams.cs`:

```csharp
[Obsolete("2026.08: Use Authors_ChangeRole. Old clients only.")]
```

**Make the legacy handler rewrite into the new command** —
`Commander.Call(new Authors_ChangeRole(session, authorId, SystemRole.Owner,
true), true, cancellationToken)` — rather than keeping a copy of the current
body. This isn't a stylistic preference; it's what the command's own type says
it must do. `IApiCommand : IDelegatingCommand`
(`src/dotnet/Core/Commands/IApiCommand.cs`), and `IDelegatingCommand` is
documented as:

> A tagging interface that marks a command delegating its work to other
> commands. Such commands aren't supposed to make any changes directly — all
> they can do is to either read the data or invoke other commands to make the
> changes. […] Always execute nested commands as the outermost ones.

So the concern about the new command needing to go through the commander
pipeline resolves itself:

- **It does go through the full pipeline.** `CommandContext.New` promotes any
  command nested inside an `IDelegatingCommand` to outermost
  (`CommandContext.cs:48`), so `Authors_ChangeRole` gets its own context and
  operation scope exactly as if a client had sent it — including its own
  permission checks, which is what we want, since the legacy shim carries no
  auth of its own. The explicit `isOutermost: true` argument is belt-and-braces,
  matching the existing style.
- **No double invalidation or double operation scope.** Operation-Framework
  handlers are filtered out of delegating-command pipelines, and
  `InvalidatingCommandCompletionHandler` skips `IDelegatingCommand`s entirely
  (`InvalidatingCommandCompletionHandler.cs:109`). Note also that
  `OperationReprocessor` does not reprocess delegating commands.
- **There is already a live precedent in this exact feature.**
  `Places.OnPromoteToOwner` today does nothing but rewrite itself into
  `Authors_PromoteToOwner` via `Commander.Call(promoteCommand, true, ct)`. The
  legacy shim is the same shape, one link further down the chain.

The alternative — leaving the old handler's body in place — would fork the
appointment logic, and `OnChangeRole` is not a pure superset of today's body:
it also drops the target's Moderator membership on `(Owner, true)` and rejects
`(Owner, false)`. A duplicated legacy path would silently diverge on both.

Keep the existing `if (Invalidation.IsActive) return;` guard in the shim for
consistency with its neighbours, even though the remarks above note delegating
handlers don't strictly need one — removing those guards codebase-wide is a
separate cleanup.

Call sites to update now: `EditChatMemberCommands.OnPromoteToOwnerConfirmed`,
`EditPlaceMemberCommands.OnPromoteToOwnerConfirmed`, and `Places.OnChangeRole`'s
internal delegation (the new one — `Places.OnPromoteToOwner` keeps delegating to
`Authors_PromoteToOwner`, so the two legacy shims retire together).

---

## Phase 5 — Enforcement

Every site below currently reads `Rules.IsOwner()`. Because `Owner` now implies
`Moderate` (1.2), swapping to `CanModerate()` preserves Owner behaviour exactly.

### 5.1 Message deletion / restoration

**File:** `src/dotnet/Chat.Service/Chats.cs` — `RemoveTextEntry`, `RestoreTextEntry`

```csharp
if (!(textEntry.AuthorId == author.Id || chat.Rules.IsOwner() || chat.Id.Kind == ChatKind.Peer))
```
becomes `chat.Rules.CanModerate()`, plus the owner-immunity guard: when the
acting author is not an Owner and the entry's author **is** an Owner, deny.
Uses `RolesBackendExt.IsInSystemRole` (Phase 3.5) — `Chats` needs an
`IRolesBackend` dependency (it already holds `IChatsBackend`, `IAuthorsBackend`,
`IRoles`).

### 5.2 Live sessions

#### Authority model

Three independent sources of call authority — the actor holds the **union** of
whatever applies to them:

| Actor | May mute | Notes |
|---|---|---|
| Chat **Owner** | anyone — including other Owners and the host | |
| Call **host** | anyone — including Owners | Not in a peer chat (see below) |
| **Moderator** | anyone **except Owners** — the host included, unless the host is an Owner | |
| Anyone else | only themselves | Existing self-mute path |

This is not a linear ladder: the host outranks a Moderator specifically on
Owner-immunity, because the host runs *this call* while a Moderator polices the
*chat*. It follows that anyone who is both Moderator and host can mute Owners —
which is why host reassignment must not be a Moderator power (5.2.3).

**Peer chats:** unchanged. `RequireNotPeerChat` already blocks muting in 1:1
chats ("neither side may silence the other"), and a 1:1 call has no meaningful
host. *Assumption:* "private chat" in this rule means a **peer (1:1) chat**, not
a non-public group chat — a private group call still has a host and moderation
works normally there. Flag if you meant `IsPublic == false` group chats too.

#### 5.2.1 `LiveSessions` (`src/dotnet/Streaming.Service/Services/LiveSessions.cs`)

Introduce one private helper, `GetCallAuthority(session, chatId, ct)`, returning
`(AuthorId? ownAuthorId, bool isOwner, bool isModerator, bool isHost)` — resolved
once from `chat.Rules` plus `RolesBackendExt.IsInSystemRole` (Phase 3.5, the
unmasked backend path) plus `Backend.GetState().Host`. All four call sites use it.

- `RequireManage` — accept Owner, Moderator, or host. Message becomes "Only the
  call host, a chat Owner or Moderator can manage the live session."
- `MutePeer` — after `RequireManage`, apply the table: deny when the actor is
  neither Owner nor host and the target **is** an Owner ("You can't turn off an
  Owner's microphone.").
- `MuteAll` — build the exception set:
  `{ ownAuthorId } ∪ (isOwner || isHost ? ∅ : ownerIds)`.
- `SetRules` — `RequireManage` only; session rules are chat-wide, so no
  per-person immunity applies.

#### 5.2.2 `MuteAll` signature widening

`ILiveSessionsBackend.MuteAll(ChatId, AuthorId exceptAuthorId, bool, ct)` becomes
`MuteAll(ChatId, ApiArray<AuthorId> exceptAuthorIds, bool, ct)`.
`LiveSessionsBackend.MuteAll` already loops participants and skips
`exceptAuthorId`, so the body is a one-line change to a set-membership test.
Backend interface — internal to the cluster, no client compat concern.

#### 5.2.3 Host promotion (new)

`LiveSessionState.Host` is already a single `AuthorId`, assigned on session start
(`OnStreamRegistered`) and on `StartCall`, with a `state.Host ?? state.AuthorIds[0]`
fallback in `LiveSessionsBackend.Get`. So "exactly one host" already holds; what's
missing is the ability to move it.

- `ILiveSessionsBackend.SetHost(ChatId, AuthorId, ct)` — under the existing
  `_changeLocks` lock: require the target to be a current participant, write
  `state with { Host = authorId, Version = NextVersion() }`, `InvalidateGet`.
- `ILiveSessions.SetHost(Session, ChatId, AuthorId, ct)` — **who may reassign:
  a chat Owner (to any participant), or the current host (handing off).**
  Deliberately **not** Moderators: a Moderator who could make themselves host
  would inherit the host's power to mute Owners, making Owner-immunity vacuous.
  `RequireNotPeerChat` applies. *Flag if you'd rather let Moderators reassign
  the host.*
- **Host departure.** With an explicit host the stale-host case now matters: if
  the host leaves the call, `Host` keeps pointing at a departed author and — in
  a call with no Owner present — nobody can take over. On `LeaveCall` (and on
  the participation drop that retires a member), if the leaver is the host,
  reassign to the first remaining Owner participant, else the first remaining
  participant. Recommended as part of this phase; without it "one host" becomes
  "one absent host".

#### 5.2.4 `LiveSessionsBackend` member grouping

The `MemberGroup.Host` grouping ("Owners are grouped with the host") resolves the
Owner role inline. Replace with two `ListSystemRoleAuthorIds` calls (Owner +
Moderator) so Moderators are grouped with the host too. Update the comment.

### 5.3 Kicking members

**File:** `src/dotnet/Chat.Service/Authors.cs` — `OnExclude`

Already requires `ChatPermissions.EditMembers` (which `Moderate` now implies)
and already refuses to exclude Owners — owner-immunity is satisfied for free.
Add a test rather than code. `OnRestore` has the same gate and needs no change.

### 5.4 Chat properties

**File:** `src/dotnet/Chat.Service/Chats.cs` — `OnChange`

`Moderate` implies `EditProperties`, so title/picture/description edits work
immediately. The **new restriction** is the important half: in the non-thread
update branch, after `Require(ChatPermissions.EditProperties)`, add

```csharp
if (!chat.Rules.IsOwner() && chatDiff2.RequiresOwner())
    throw ChatPermissionsExt.NotEnoughPermissions(ChatPermissions.Owner);
```

`ChatDiffExt.RequiresOwner()` (new, `src/dotnet/Api/Chat/ChatExt.cs`) returns
true when the diff touches anything other than `Title`, `Description`,
`MediaId` — i.e. `Kind`, `IsPublic`, `IsTemplate`, `TemplateId`,
`TemplatedForUserId`, `PlaceId`, `SystemTag`, `IsArchived`, `AliasId`,
`AllowGuestAuthors`, `AllowAnonymousAuthors`, `IsSummarized`.

`change.Remove` already requires `ChatPermissions.Owner`; thread removal already
requires `Owner` on the parent — both unchanged.

**File:** `src/dotnet/Chat.Service/Places.cs` — `OnChange`

Same treatment against `PlaceDiff` (a place Moderator may edit title/picture/
description; type and deletion stay Owner-only).

### 5.5 Other Owner-only checks — explicitly **not** changed

`Conversations.OnReSummarize`, `Chats` copy/move-to-place (`Chats.cs` 871–987),
`ChatsBackend.GetConsolidatedRules` archived-chat handling, `Roles.OnChange` /
`RequireOwner`, `Authors.OnPromoteToOwner`, `ChatCopyBanner`, `PlaceMenu`
settings entry. These stay `IsOwner()`.

**Known consequence:** on an **archived** chat,
`ChatsBackend.GetConsolidatedRules` returns `AuthorRules.None` for anyone who
is not an Owner — so Moderators lose all access to archived chats. That matches
"Moderators can't archive" and is left as-is.

---

## Phase 6 — UI

### 6.1 Member menu and badges

- `EditChatMemberModel` / `EditPlaceMemberModel` — add `IsModerator`,
  `CanSetModerator`.
- `EditChatMemberCommands` / `EditPlaceMemberCommands` — `ComputeState` fetches
  `Roles.ListModeratorIds`; `CanSetModerator = ownIsOwner && !isOwner`. The
  `OnPromoteToOwnerConfirmed` helpers switch to `Authors_ChangeRole` /
  `Places_ChangeRole` (Phase 4.3), and gain a `OnSetModeratorConfirmed`
  counterpart differing only in the `SystemRole` / `IsInRole` arguments.
- `EditChatMemberMenu.razor` / `EditPlaceMemberMenu.razor` — `statusText` becomes
  `IsOwner ? "Owner" : IsModerator ? "Moderator" : null`; add a "Make
  Moderator" / "Remove Moderator" entry (icon `icon-shield` or similar; unlike
  the Owner entry, no "cannot be undone" warning — say what a Moderator can do).
  Gate `CanRemoveFromGroup` so a non-Owner Moderator can't kick an Owner —
  matching the server rule so the UI doesn't offer an action that will fail.
- `AuthorList.razor` — add a "Moderators" group between "Owners" and the rest;
  `ListModeratorIds` alongside `ListOwnerIds`, and exclude authors already listed
  as Owners.

### 6.2 Message menu / selection

- `MessageMenu.razor` — rename `MessageModel.IsOwner` to `CanModerate`
  (`rules.CanModerate()`), and add `IsEntryByOwner` so the delete entry hides on
  an Owner's message for a non-Owner Moderator.
- `MessageMenuContent.razor` — `canDelete` uses the new fields.
- `SelectionHeader.razor` — bulk-delete gating: `chat.Rules.IsOwner()` →
  `CanModerate()`, with the same per-entry Owner-author exclusion in the loop.

### 6.3 Chat / place settings

- `ChatSettingsStartModalPage.razor` already gates on `CanEditProperties()`,
  which Moderators now have — title, picture and description become editable
  with no change. **But:** hide the "Chat type" tile and the archive/delete
  actions unless `Rules.IsOwner()`, and make sure the form does not submit
  Owner-only `ChatDiff` fields for a Moderator (5.4 would reject the whole
  update). Audit the form's diff construction.
- `EditChatTypeModalPage.razor` / `PlaceSettingsEditTypeModalPage.razor` — switch
  their `CanEditProperties()` gate to `IsOwner()`.
- `ChatSettingsStartModalPage`'s `CanRemoveThread` and the delete/leave paths in
  `ChatUI.DeleteOrLeaveChatInternal` stay Owner-gated.

### 6.4 Call UI

- `CallList.razor` — `isController` becomes
  `rules.CanModerate() || live.Host == ownAuthor.Id`; the toast text becomes
  "Only the call host, a chat Owner or Moderator can turn off someone's
  microphone." The model also needs the call-owner ids so the per-member mic-off
  affordance is suppressed on an Owner when the actor is a Moderator who is
  neither Owner nor host (mirror of 5.2.1 — the UI must not offer an action the
  server will reject).
- `CallList.razor` — "Make host" entry on a member, shown when the viewer is a
  chat Owner or the current host (5.2.3). The existing `MemberGroup.Host`
  section already visually separates hosts/owners/moderators, so the promoted
  member simply regroups on the next state push.

---

## Phase 7 — Tests

**`tests/Chat.UnitTests/ChatPermissionsExtTest.cs`**
- `Owner` implies `Moderate`.
- `Moderate` implies `EditProperties`, `EditMembers`, `SeeMembers` — and **not**
  `Write`, `Owner`, `EditRoles`.
- `PlacePermissions.Moderate == (PlacePermissions)(int)ChatPermissions.Moderate`
  (guards the `ToPlaceRules` cast).

**`tests/Chat.UnitTests`** — `Role.Fix()` clamps `SystemRole.Moderator` to the
canonical set; `DbRole` round-trips `CanModerate`.

**`tests/Chat.IntegrationTests`** (new `ModeratorRoleTest`)
- Owner appoints a Moderator; the target's `AuthorRules` gain `Moderate`.
- Moderator deletes and restores another member's message; a plain member cannot.
- Moderator **cannot** delete an Owner's message.
- Moderator edits title + description + picture; **cannot** flip `IsPublic`,
  archive, set `AliasId`, or delete the chat.
- Moderator kicks a plain member; cannot kick an Owner.
- Moderator cannot appoint another Moderator or an Owner (`Roles_Change`,
  `Authors_ChangeRole` both rejected).
- Owner demotes the Moderator; permissions revert.
- `Authors_ChangeRole` rejects the automatic system roles (`Anyone`, `Guest`,
  `User`, `AnonymousUser`, `None`) and rejects `(Owner, IsInRole: false)`.
- `(Owner, true)` on an existing Moderator drops their Moderator membership.
- The `[Obsolete]` `Authors_PromoteToOwner` / `Places_PromoteToOwner` shims still
  promote to Owner, **and inherit the new behaviour** — promoting an existing
  Moderator through the legacy command drops their Moderator membership too.
  That last assertion is the regression test for "rewrite, don't duplicate":
  it fails if the legacy handler ever grows its own body again.
- Moderator leaves / is excluded → dropped from the role
  (`RemovePrivilegedRoles`).
- Place root-chat Moderator is a Moderator in every **public** place chat, and
  not in private ones.
- Template-chat clone carries the Moderator role and its members.

**`tests/Streaming.IntegrationTests`**

Mute authority — one case per cell of the 5.2 table:
- Owner mutes a plain member, another Owner, and the host.
- Host (neither Owner nor Moderator) mutes a plain member **and an Owner**.
- Moderator mutes a plain member and the host; **cannot** mute an Owner.
- Plain member can mute only themselves.
- `MuteAll` by an Owner or host mutes everyone but the actor; `MuteAll` by a
  non-Owner, non-host Moderator leaves every Owner unmuted.
- Moderator can `SetRules`; a plain member cannot.

Host promotion:
- Owner sets any participant as host; the previous host is no longer host
  (exactly one host).
- Current host hands off to another participant.
- A Moderator who is neither Owner nor host **cannot** reassign the host —
  the anti-escalation guard.
- Setting a non-participant as host is rejected.
- Host leaves the call → host reassigns to a remaining Owner participant, or
  to the first remaining participant when no Owner is present.
- A Moderator promoted to host **can** then mute an Owner (documents the union
  rule deliberately, so a future change doesn't break it silently).

Peer chats:
- `RequireNotPeerChat` still fires for `MutePeer`, `MuteAll` and `SetHost`.

---

## Phase 8 — Docs

- `docs/api-index.md` — `Role` entry mentions the Moderator system role;
  `docs/api-index-full.md` regenerated for the new members.
- `docs/plans/index.md` — link this plan under **Active**, remove on ship.
- If a user-facing guide describes chat roles, add Moderator there.

---

## Risks and sequencing notes

- **The three `RolesBackend` filters (Phase 3.1–3.3) are load-bearing.** Missing
  3.1 makes appointment silently ineffective; missing 3.3 makes it throw. Land
  Phase 3 before Phase 4.
- **Bit-value alignment** between `ChatPermissions` and `PlacePermissions` is an
  unchecked invariant today — the test in Phase 7 makes it checked.
- **Host reassignment is a privilege-escalation surface.** The union rule means
  host ⇒ can mute Owners. Whoever may call `SetHost` may therefore hand out
  Owner-muting power. Keeping `SetHost` to Owners and the current host is what
  makes Owner-immunity hold; the guard has a dedicated test (Phase 7).
- **Client/server version skew.** `Moderate` is a new flag on an existing enum
  in an existing `AuthorRules` payload, so old clients deserialize it fine and
  simply ignore it: a Moderator on an old client sees no moderation affordances
  but the server still honours their commands. No contract break. The new
  `Authors_ChangeRole` command is only issued by new clients, and
  `Authors_PromoteToOwner` survives as an `[Obsolete]` shim (Phase 4.3) so old
  clients keep working.
- **Order of work:** Phases 1–2 (model + DB), then 3 (plumbing), then 4
  (appointment), then 5 (enforcement), then 6 (UI). Phases 1–5 are independently
  testable via integration tests before any UI exists.
