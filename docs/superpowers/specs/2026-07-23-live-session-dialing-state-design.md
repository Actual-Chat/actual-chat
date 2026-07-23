# Live-Session Dialing State — Design

**Status:** Approved (2026-07-23)
**Author:** Alexey Kochetov (with Claude)
**Area:** Live sessions / calls (`LiveSessionsBackend`, `LiveSession*` models, Call tab)

## Problem

When a peer starts a call, `LiveSessionsBackend.StartCall` immediately sets
`SessionStartedAt = now`. Throughout the codebase `SessionStartedAt != null` is
*the* "there is a live conversation" signal (~20 gates: client block rendering,
server conversation projection, split-flow, summary-flow, notifications). So the
caller's chat shows a **live conversation block the instant they dial**, before
the other peer has answered — a block for a conversation that isn't happening yet.

The session itself *should* exist while dialing: it drives the Call tab (rules,
members, mute, ring state). Only the *conversation block* is premature.

## Goal

Introduce an explicit **Dialing** phase for calls: the live session is created
(so the Call tab works and rings fire) but no live conversation block is
surfaced until the call is answered. On answer, the session latches and the
block appears — for all calls (peer and group), uniformly.

## Core model

A call has two phases, distinguished by an explicit `LiveSessionKind` value and,
in lockstep, by `SessionStartedAt`:

| Phase | `Kind` | `SessionStartedAt` | Block visible? | Call tab? |
|-------|--------|--------------------|----------------|-----------|
| Dialing | `Dialing` | `null` | No | Yes |
| Connected | `Call` | set | Yes | Yes |

**Invariant:** for a call, `Kind == Dialing ⟺ SessionStartedAt == null`. Both
fields are written together at exactly two sites — `StartCall` (enter Dialing)
and `AcceptCall` (latch to Connected) — so they cannot drift.

The latch is **monotonic**: once Connected, a session never returns to Dialing.
Promoting an already-connected ambient session to a call (an already-2-party
group already showing its block) yields `Kind = Call`, not `Dialing` — its block
stays.

**Latch trigger:** the first invitee to **accept** (`AcceptCall`). Decline,
cancel, and no-answer never latch, so those paths leave no block and no
notification.

### Why this shape

`SessionStartedAt == null` already means "session state exists in Redis, but no
live conversation yet" everywhere in the code. Keeping `SessionStartedAt` null
during dialing therefore suppresses the block/summary/split/conversation-
notification across all ~20 existing gates **by construction** — no gate needs a
`&& !dialing` clause, so there is no risk of missing one and re-leaking the block.

The explicit `Kind = Dialing` rides on top for legibility: it is inspectable in
Redis and logs, it already flows to the client via the `LiveSession` projection
(`Kind = state.Kind`) so the Call tab needs no new field, and it lets the two
ring-related guards distinguish a call from an ambient session without inferring
it from `SessionStartedAt`.

## Changes

All server changes are in
`src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` unless noted.

### 1. `LiveSessionKind` — add `Dialing`

`src/dotnet/Api/Live/LiveSessionKind.cs`:

```csharp
public enum LiveSessionKind
{
    Ambient = 0,
    Call = 1,
    Dialing = 2,
}
```

Redis-persisted enum (MemoryPack/MessagePack), **not** a DB column — a new enum
value is backward compatible and needs no migration. Old Redis states never
carry `Dialing`.

### 2. `LiveSessionState` — `IsCall` helper

`src/dotnet/Api/Live/LiveSessionState.cs`, alongside the other computed
(`[IgnoreDataMember, MemoryPackIgnore, IgnoreMember]`) members:

```csharp
public bool IsCall => Kind is LiveSessionKind.Call or LiveSessionKind.Dialing;
```

`IsCall` means "this session is a call (dialing or connected)" — the sense the
ring/close guards need. Bare `Kind == LiveSessionKind.Call` continues to mean
"connected call".

### 3. `StartCall` — enter Dialing, don't pre-latch

In the `with` that builds the call state (currently ~L477–486):

- `Kind = state?.SessionStartedAt is not null ? LiveSessionKind.Call : LiveSessionKind.Dialing`
  — a fresh call is Dialing; promoting an already-connected session stays Call.
- `SessionStartedAt = state?.SessionStartedAt` — preserve an existing latch, but
  do **not** force `?? now`. A fresh call keeps `SessionStartedAt == null`.
- `VisibleStartLid = state?.VisibleStartLid ?? 0` — do not compute the chat-end
  lid here for a fresh call; it is set at the latch (`AcceptCall`). Preserve an
  existing value for the promotion case.

Everything else in `StartCall` (invites set to `Ringing`, `EnsureParticipant`
for the caller, `NotifyCall` ring) is unchanged.

### 4. `AcceptCall` — latch Dialing → Connected

In `AcceptCall`, after marking the invite `Accepted` and before/around
`EnsureParticipant`, when the session is still dialing (`SessionStartedAt is null`):

- `SessionStartedAt = now`
- `VisibleStartLid` = `ChatsBackend.GetLidRange(chatId, false, ct).End` (chat end
  at answer — the block starts from the moment the conversation connects)
- `Kind = LiveSessionKind.Call`
- add `inviteeAuthorId` to `AuthorIds` if absent (the answer makes it 2-party)
- bump `Version`
- write the updated state to Redis

If the session is already connected (`SessionStartedAt` set — a second invitee
accepting, or a promoted session), accept the invite as today without touching
the latch fields. Ring dismissal for the accepting invitee is unchanged.

### 5. Ring guards — use `IsCall`

- Ring-timeout, `GetState` (~L118): `state.Kind == LiveSessionKind.Call` →
  `state.IsCall`. A dialing call must still time out stale rings.
- `CloseAndMaterialize` (~L867): the call branch `if (state.Kind == LiveSessionKind.Call)`
  → `if (state.IsCall)`. An abandoned dial must dismiss its rings and close via
  the call branch (it never materializes: the branch already skips materialize,
  and `SessionStartedAt is null` would skip it regardless).

The ambient closing-grace guard (~L821, `Kind: not LiveSessionKind.Call`) and the
summary-flow guard (`LiveConversationSummaryFlow.cs:47`, `Kind: not Call`) already
exclude dialing calls via their `SessionStartedAt: not null` condition; update them
to the negated `IsCall` sense (`Kind is not (Call or Dialing)` /
`not { Kind: Call or Dialing }`) for clarity so a dialing call is never treated as
ambient. Behavior is unchanged.

### 6. `Get` projection — surface dialing calls

Backend `Get` (~L131): replace the early-out

```csharp
if (state.SessionStartedAt is null)
    return null;
```

with

```csharp
if (state.SessionStartedAt is null && !state.IsCall)
    return null;
```

so a dialing call (null `SessionStartedAt`, `Kind == Dialing`) still projects a
`LiveSession` for the Call tab. The projection already tolerates a null
`SessionStartedAt` (`StartedAt = state.SessionStartedAt ?? state.StartedAt`) and
builds members/invites from participants and the invite map, both populated
during dialing.

Set `Conversation = state.IsDialing ? null : state.ToConversation()` in the
projected `LiveSession` so no consumer of `LiveSession.Conversation` can render a
block while dialing. (Add `IsDialing => Kind == LiveSessionKind.Dialing` as a
computed helper on `LiveSessionState` for this and the UI.)

### 7. Ambient latch — no "started" banner for calls

The ambient latch (`OnStreamRegistered`, ~L290) fires when
`SessionStartedAt is null && AuthorIds.Count >= 2`. This is now reachable by a
dialing call if two parties stream before a formal `AcceptCall` (a backstop). In
that case it should promote `Dialing → Call` and set `SessionStartedAt`, but must
**not** send the "voice chat started" *conversation* notification — calls
announce themselves by ringing, not by a conversation banner (today's behavior).

Gate the notification (currently unconditional at ~L298) on `Kind == Ambient`,
and in the latch's `with` set `Kind = state.IsCall ? LiveSessionKind.Call : Kind`
so a backstop-latched dialing call ends up Connected.

### 8. Call tab — "Dialing…" affordance

`src/dotnet/UI.Blazor.App/Components/RightPanel/CallList.razor`: when
`live.Kind == LiveSessionKind.Dialing`, show a "Dialing…" state (the ringing
invitees already render from `live.Invites`). `live.Kind` is already on the
projection — no new plumbing. Copy/visuals kept minimal.

## What is deliberately unchanged

- The ~20 `SessionStartedAt != null` block/summary/split/notification gates —
  they suppress during dialing for free because `SessionStartedAt` stays null.
- Keepalive: `IsSessionLive` keeps a ringing call alive with no streams;
  `IsCallAbandoned` closes a call once nothing rings and it is still <2-party.
- `CloseAndMaterialize`: a missed/declined dial has `SessionStartedAt == null`
  and an empty title → vanishes with no block and no notification.
- Peer-chat live-session notification suppression (prior commit) — orthogonal.
- The `IncomingCall` ring path (callee's modal), driven by `NotifyCall` + invite
  state, not `SessionStartedAt`.

## Edge cases

- **Peer call, answered:** dial (no block, Call tab "Dialing") → accept → latch →
  block appears for both peers, starting at the answer lid.
- **Peer call, declined/cancelled/missed:** no ringing invite + <2 participants →
  `IsCallAbandoned` → close via `IsCall` branch → rings dismissed, session
  dropped, nothing left.
- **Group call from a live ambient session:** `StartCall` sees
  `SessionStartedAt` set → `Kind = Call` (not Dialing) → the existing block stays;
  new invitees ring.
- **Stream-before-accept backstop:** two parties stream without a formal accept →
  ambient latch promotes `Dialing → Call`, sets `SessionStartedAt`, no banner.
- **Second invitee accepts an already-connected group call:** `SessionStartedAt`
  already set → accept only, latch untouched.

## Testing

Streaming-backend integration tests (drive `ILiveSessionsBackend`, pattern
`LiveSessionsTest.cs`):

- `DialingCallHasNoSessionStartedAtAndNoConversation` — after `StartCall`,
  `Kind == Dialing`, `SessionStartedAt == null`, `GetConversation`/`ToConversation`
  path yields no block; `Get` is **non-null** (Call tab works).
- `AcceptLatchesToConnectedCall` — after `AcceptCall`, `Kind == Call`,
  `SessionStartedAt` set, `VisibleStartLid` = chat end, invitee in `AuthorIds`,
  the conversation is now surfaced.
- `DeclinedOrMissedCallVanishesWithoutBlock` — decline / ring-timeout → close;
  no conversation materialized, no `NotifyConversation` enqueued.
- `StartCallOnLiveAmbientKeepsBlock` — ambient session already latched → `StartCall`
  → `Kind == Call`, `SessionStartedAt` preserved, block still surfaced.
- `RingTimeoutFiresWhileDialing` — a dialing call's stale ring still times out
  (guards use `IsCall`).

Client display test (`Chat.UI.Blazor.IntegrationTests`, pattern
`LiveConversationDisplayTest.cs`): a dialing call renders no conversation block;
after the latch the block renders.

## Reuse

- **Existing abstractions:** `SessionStartedAt` (latch/block signal),
  `LiveSessionKind` (extended), the invite state machine
  (`CallInviteStatus.Ringing/Accepted/Declined/Missed`), `IsSessionLive` /
  `IsCallAbandoned` (keepalive/close), the `LiveSession.Kind` projection
  (already carries Kind to the client), `ChatsBackend.GetLidRange` (visible-start
  lid). No new service, storage, or DB migration.
- **New components:** `LiveSessionKind.Dialing` (enum member) and the computed
  `IsCall` / `IsDialing` helpers on `LiveSessionState` — inherently shared (they
  live on the API model in `ActualChat.Api`), no feature-local placement to weigh.
```