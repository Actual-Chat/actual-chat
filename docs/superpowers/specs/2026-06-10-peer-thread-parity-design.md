# Peer-chat thread parity — design

Date: 2026-06-10
Status: approved (brainstorming) → ready for implementation plan
Branch: bugfix/invalid-roleid-format

## Context

A production user was locked out of their chats:

```
System.FormatException: Invalid RoleId format: "p-i0fuE0-kiLGwd-148:1"
   at ActualChat.RoleId.Parse(String s)
   at ActualChat.Chat.Db.DbAuthor.ToModel()
   at ActualChat.Chat.AuthorsBackend.GetInternal(...)
   ... StatusBadge.razor → ErrorBoundary
```

`p-i0fuE0-kiLGwd-148` is a **thread** chat (thread `148`) whose parent is the
**peer** chat `p-i0fuE0-kiLGwd`. The RoleId `…-148:1` is a role scoped to that
peer thread.

Investigating produced two distinct findings:

1. **Parser gap (already fixed this session).** `ThreadChatId.Format` emits
   `parent + '-' + threadId`, so a peer thread is `p-u1-u2-N`. But
   `ChatId.TryParse` routed any `p-` id to `TryParsePeerChatId`, which never
   handled a thread suffix — group/place threads parse via
   `ParsedLocalChatId`, peer never got the same treatment. Reading the row
   threw. Fixed by teaching peer parsing to peel trailing numeric thread
   segments (see "Already done").

2. **Root-cause defect (this design).** The row should never have existed.
   Thread creation (`ChatsBackend.cs:989-998`) calls
   `CreateOwnerRole(threadChatId, parentAuthor)` for **every** thread,
   persisting a system Owner role **scoped to the thread chat** plus a
   `DbAuthorRole` linking the parent-chat author to that thread-chat role. These
   roles are **write-only dead data**: `AuthorsBackend.GetInternal` (lines
   543-546) always returns `RoleIds = []` for thread authors, so they are never
   read. For peer threads the thread-chat id additionally failed to parse → the
   crash.

Peer threads are an **intended** feature, not an accident: `ChatThreads.OnStart`
(ChatThreads.cs:172-178) explicitly supports a peer parent (gated on contact
status), and `ChatThreads.ListIdsForPlace` (ChatThreads.cs:51-53) includes
`ChatKind.Peer`. They were left half-wired — the parser and several UI/notification
sites never caught up.

### Goal & acceptance bar

Make peer threads behave with **functional parity to group/place threads,
except roles/permissions**: a thread inherits identity and permissions from its
outermost parent and carries **no roles of its own**. Peer threads are
first-class in the DM UI (creatable, listed in a threads tab, navigable).
Confirmed product direction: **DMs show a threads tab** like groups/places.

## Reuse

**Existing abstractions to reuse (no new types):**
- `ThreadChatId` + `ChatId.GetOutermostParent()` / `ParentChatId` for all
  parent delegation (already the dominant pattern; permissions delegate via
  `ChatsBackend.RequireCanRead` and `GetPeerChatRules`).
- `ParsedLocalChatId.TryParse` thread-peeling loop — already mirrored into peer
  parsing (done).
- Existing thread UI components (`ThreadList.razor`, `RightPanelContent.razor`
  threads tab, `ChatIcon.razor`) — parameterize by parent kind rather than add
  peer-specific components.
- `StandardError.Constraint` for the new validation; existing `ErrorBoundary`
  for UI degradation.

**New components:** none. The work removes a code path (thread role creation),
adds a validation guard, and widens existing kind gates to include peer.

## Already done (this session)

- `src/dotnet/Api/Identifiers/ChatId.cs` — `TryParsePeerChatId` now tries the
  plain `p-u1-u2` base first, then peels trailing numeric `-N` thread segments
  and wraps in `ThreadChatId`. Base parser extracted as
  `TryParsePeerChatIdBase`; unchanged otherwise. Base-first ordering avoids the
  all-numeric-userId ambiguity (`p-123456-admin1` stays a peer chat).
- Tests in `tests/Core.UnitTests/Identifiers/ChatIdTest.cs` and
  `ThreadChatIdTest.cs`: peer-thread parse, nested round-trip, numeric-userId
  guard, and the exact prod regression `RoleId.Parse("p-i0fuE0-kiLGwd-148:1")`.
  All 102 identifier tests pass.

## Changes

### 1. Stop threads creating roles — `src/dotnet/Chat.Service/ChatsBackend.cs`

In the `chatId.Kind == ChatKind.Thread` creation branch (lines 989-998):
- Keep the `AuthorsBackend.GetByUserId(threadChatId.GetOutermostParent(), ownerId, …).Require()`
  call — it validates the creator is a member of the parent chat.
- Remove the `CreateOwnerRole(chatId, author)` call. Threads carry no roles;
  the role was never read.

This applies to all thread kinds (group/place/peer) — they all produced
write-only roles. Verify no reader depends on a thread-scoped Owner role
(audit: permissions resolve via parent; none found).

### 2. Validate role/author chat match — `src/dotnet/Chat.Service/RolesBackend.cs`

Where `DbAuthorRole` rows are added (lines 213-217), reject mismatches before
insert: require `authorId.ChatId == roleId.ChatId`, and reject a role whose chat
is a thread or peer chat. Throw `StandardError.Constraint`. Prevents the bad-row
class at the write boundary regardless of caller.

### 3. Backend display bugs

- `src/dotnet/Api/IconQueryExt.cs:15` — resolve the **parent** kind for thread
  chats so a peer-thread author renders the beam (author) icon, not the default
  marble.
- `src/dotnet/Notification.Service/NotificationHelper.cs:6-10` — when a thread's
  parent is a peer chat, format the title as `"{author}"` (omit `@ {title}`),
  matching peer-chat notification style.

### 4. DM UI parity — `src/dotnet/UI.Blazor.App`

- `RightPanelContent.razor` (`ShowThreads`, ~line 116) and `ThreadList.razor`
  (early-exit, ~lines 35-36): widen the gate from `Group or Place` to also allow
  a chat whose kind is `Peer` (or a thread of one) → DM shows its threads tab
  and list.
- `ChatIcon.razor` (~lines 7-9, 28-36): for a thread chat, render a thread icon
  rather than inheriting the peer avatar, so a peer thread is visually
  distinguishable.
- Thread creation entry points (`MessageMenuContent.razor`,
  `SelectionHeader.razor`, `SelectionUI.cs`) already allow peer and the backend
  contact-gates it — keep as is.
- Member/roles UI already correctly shows no role UI for peer
  (`AuthorList.razor:104-107`) — no change.

### Out of scope

- Right-panel **search** tabs are unimplemented stubs (`RightPanelSearchTabs.razor`)
  — note only.
- ML search intentionally skips peer chats (`SearchBackend.cs:165`); peer threads
  inherit that. No change.
- **No data migration.** The parser fix makes existing thread-scoped role rows
  parse, and `GetInternal` ignores thread `RoleIds`. Leave them in place.
- Re-adding the `CreateThreadId` guard — not done; peer threads are intended.

## Verification

1. Build: `dotnet build src/dotnet/Api/Api.csproj` and the Chat.Service /
   UI.Blazor.App projects. TypeScript (if touched): `npm run build:Verify` or the
   running `/server-loop` rebuild.
2. Unit: `dotnet test tests/Core.UnitTests` `--filter Identifiers` — round-trip
   invariant green (done).
3. New regression: thread creation persists **no** `DbAuthorRole` row (assert the
   `AuthorRoles` table is unchanged after `ChatThreads_Start`); `RolesBackend`
   rejects a mismatched author/role chat.
4. Integration: start a thread inside a peer DM, confirm it appears in the DM
   threads tab, opens, navigates back to the DM, and no role row is written;
   confirm a peer-thread notification title shows just the author.
5. Manual UI: peer-thread icon is a thread icon; existing group/place threads
   unaffected.

## Risks

- Removing thread role creation could break a hidden reader of thread Owner
  roles. Mitigation: audit found none (permissions delegate to parent); the
  integration test covers thread read/permissions.
- Widening UI gates could surface threads in DMs that were previously hidden —
  this is the intended product change; verify empty-state rendering.
