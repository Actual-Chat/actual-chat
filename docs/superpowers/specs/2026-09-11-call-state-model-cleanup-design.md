# Call state model cleanup — design

## Motivation

`LiveSessionsBackend` models a call's lifecycle across three overlapping
places, and none of them cleanly separates *what phase the call is in* from
*what someone did* to get there:

- **`LiveSessionKind`** (`Ambient | Call | Dialing`) — `Dialing` is a
  separate *kind* of session, even though `LiveSessionState` already has
  `IsCall => Kind is Call or Dialing` and `IsDialing => Kind ==
  LiveSessionKind.Dialing`. The code already treats Dialing as a phase of a
  call, not a different kind of session — the enum just doesn't say so.
- **`CallStatus`** (`None | Dialing | Accepted | Declined | NoAnswer`), on
  the short-lived, caller-facing `CallState` record — mixes the call's own
  progress (`Dialing`, `Accepted`) with terminal reasons (`Declined`,
  `NoAnswer`), and is missing `Canceled`/`Ended` entirely (those live in a
  *different* field, `LiveSessionState.Outcome: CallOutcome`). `CallStatus`
  and `LiveSessionState.Kind` invalidate independently over RPC — this was
  the root cause of the very first bug this branch fixed (a just-accepted
  call briefly reading as unanswered on the caller's client).
- **Per-invitee status** (`CallInviteStatus`: `Ringing | Accepted | Declined
  | Missed`) conflates the invitee's *action* (accepted, declined) with
  their *state* (ringing, connected) — `Accepted` is both "the invitee
  performed Accept" and "the invitee is now in this state," and there is no
  way to tell "accepted but never actually connected" from "accepted and
  talking" without cross-referencing a completely different structure
  (`ParticipationInfo`/`_participants`).
- The caller has **no status record of their own at all** — their progress
  is inferred from `AuthorIds`/`Host`/`CallerId` on `LiveSessionState`.

This spec resolves all four by keeping each concept a **flat enum whose
current value is simply the last thing that happened** to that
participant/call — no separate "action" vs "state" fields, no
"transitional state + reason" wrapper.

**Out of scope:** the client-side counterpart of this same disease
(`IncomingCallUI`'s `_overLockRingChatId`/`_foregroundRawChatId`/etc. —
independent `MutableState<ChatId?>` fields instead of one state machine) is
a separate, later follow-up. This spec only reshapes the server-side model;
existing client call sites are updated only enough to keep compiling
against the new types.

**Rollout:** the feature is still under active development. No migration,
dual-read, or backward-compat shim for in-flight Redis state is needed —
existing live calls/rings can simply be lost on deploy.

## New model

### `LiveSessionKind`

```csharp
public enum LiveSessionKind { Ambient = 0, Call = 1 }
```

`Dialing` is removed. A call is `Kind == Call` from the moment `StartCall`
creates it — dialing is no longer a different kind, it's the state before
`SessionStartedAt` is set.

`LiveSessionState.IsDialing`/`IsCall` keep existing today's cost — no new
Redis read is needed:

```csharp
public bool IsCall => Kind == LiveSessionKind.Call;
public bool IsDialing => Kind == LiveSessionKind.Call && SessionStartedAt is null;
```

`SessionStartedAt is null` already means "hasn't connected yet" today (it's
set exactly once, at the first accept or the first stream that pushes
`AuthorIds.Count >= 2`) — `Dialing` as a `Kind` value was always redundant
with this field once `Kind` is fixed at `Call` from the start.

### `CallStatus` (aggregate, on `CallState`)

```csharp
public enum CallStatus {
    None = 0,
    Dialing = 1,
    Connecting = 2,   // someone accepted, but fewer than 2 participants are genuinely present yet
    Active = 3,       // >= 2 participants genuinely present (EnforceCallConnectGrace's target state)
    NoAnswer = 4,
    Declined = 5,
    Canceled = 6,
    Ended = 7,
}
```

Renamed `Accepted` → `Connecting` to stop reusing the same word for two
different things at two different levels: at the aggregate level it means
"the call as a whole has been accepted by someone, but isn't confirmed
live yet" (exactly the 3-second window `EnforceCallConnectGrace` already
polices); at the invitee level (below) `Accepted` keeps meaning "this one
invitee accepted." Added `Active`, `Canceled`, `Ended` so `CallStatus` alone
tells the whole story of a call in progress, rather than splitting it with
`LiveSessionState.Outcome`.

`CallState`'s shape changes to add a few facts the recompute below needs —
see "Participant presence drives `Active`/`Ended`" for the full record.

#### `CallStatus` is recomputed, never set directly by an action

Today `AcceptCall`/`DeclineCall`/`ExpireRings` each independently decide
*and* write a `CallStatus` value inline — each handler duplicates its own
slice of "what does this imply for the call as a whole." This is the same
action/state tangle as `CallInviteStatus`, one level up: an action
(Accept/Decline/Cancel/timeout) changes the *acting participant's own*
status, and the aggregate `CallStatus` must then be **recomputed** from the
current set of participant facts — never written directly by the action
that triggered the recompute.

```csharp
private async Task RecomputeCallStatus(ChatId chatId, LiveSessionState state, CancellationToken ct)
{
    if (!state.IsCall) return;

    var callState = await SafeGetCallState(chatId).ConfigureAwait(false);
    var invites = (await SafeGetInvites(chatId).ConfigureAwait(false)).Values;
    var status = Derive(callState, invites);
    await SetCallState(chatId, NewCallState(state, status, callState)).ConfigureAwait(false);
}

private static CallStatus Derive(CallState? callState, IReadOnlyCollection<CallInvite?> invites)
{
    var everActive = callState?.CallerActiveAt is not null
        || invites.Any(i => i?.ActiveAt is not null);
    var activeCount = (callState?.CallerActiveAt is not null && callState.CallerEndedAt is null ? 1 : 0)
        + invites.Count(i => i is { Status: CallInviteStatus.Active });

    if (activeCount >= 2)
        return CallStatus.Active;
    if (everActive)
        return CallStatus.Ended;   // was Active, now isn't - however it wound down, it's Ended
    if (callState?.CanceledAt is not null)   // explicit CancelCall, distinct from CallerEndedAt (presence loss)
        return CallStatus.Canceled;
    if (invites.Any(i => i is { Status: CallInviteStatus.Accepted }))
        return CallStatus.Connecting;
    if (invites.Any(i => i is { Status: CallInviteStatus.Declined }))
        return CallStatus.Declined;
    if (invites.Count > 0 && invites.All(i => i is { Status: CallInviteStatus.Missed }))
        return CallStatus.NoAnswer;
    return CallStatus.Dialing;
}
```

No `ParticipantCount(chatId)`/`_participants` read anywhere in `Derive` —
`activeCount` is a sum over the `Active`-status facts kept on `CallInvite`/
`CallState`, which the sync described below (not `SetParticipation`
directly) keeps in step with genuine presence.

Called after every fact-recording step: `AcceptCall`, `DeclineCall`,
`ExpireRings` (an invite becoming `Missed`), and the `GetState` self-heal
sync described below (whenever it changes an `Active`/`Ended` fact).

### `CallOutcome` — unchanged

```csharp
public enum CallOutcome { None = 0, NoAnswer = 1, Declined = 2, Canceled = 3, Ended = 4 }
```

Stays exactly as it is today, and keeps its existing job: the permanent,
materialized record of how a call ended (`LiveSessionState.Outcome`,
`CallEntry.Outcome`). It is **not** merged into `CallStatus` — they're
kept as two separate types on purpose, even though 4 of `CallStatus`'s 8
values share a name with `CallOutcome`'s values. `CallState` is short-lived
and caller-facing; `Outcome` is the permanent history-entry record. Nothing
keeps them in sync automatically — each is set explicitly at the point in
`LiveSessionsBackend` that already knows the outcome.

### `CallerStatus` — derived, not persisted

```csharp
public enum CallerStatus { Dialing = 0, Active = 1, Canceled = 2, NoAnswer = 3, Ended = 4 }
```

No new persisted record. `GetCallStatus` (`LiveSessions.cs:95-105`) already
projects `CallState` down to "what the caller himself should see" — it
already gates on `callState.CallerId == chat.Rules.Author?.Id`. This spec
changes only its *return type and mapping*, from returning `CallState
.Status` verbatim to a pure projection:

```csharp
CallStatus.None                      -> (no call — CallerStatus? null / not applicable)
CallStatus.Dialing                   -> CallerStatus.Dialing
CallStatus.Connecting                -> CallerStatus.Dialing   // still not confirmed live from the caller's POV
CallStatus.Active                    -> CallerStatus.Active
CallStatus.NoAnswer                  -> CallerStatus.NoAnswer
CallStatus.Declined                  -> CallerStatus.NoAnswer  // "nobody answered" reads the same to the caller
CallStatus.Canceled                  -> CallerStatus.Canceled
CallStatus.Ended                     -> CallerStatus.Ended
```

This mapping is total and lossless enough for the caller's own UI — the
caller never needs to distinguish "still dialing" from "someone answered
but isn't confirmed live yet," or "declined" from "timed out." No group
call complicates this: it's still one caller, one `CallState`, regardless
of invitee count.

### `CallInviteStatus` (extended)

```csharp
public enum CallInviteStatus {
    New = 0,        // sentinel — the invite record exists but nothing has happened to it yet
    Ringing = 1,
    Accepted = 2,
    Active = 3,     // this invitee is now a genuinely present participant
    Declined = 4,
    Missed = 5,     // never answered — timed out, or the caller canceled/the call ended while still ringing
    Ended = 6,      // was Active, then this invitee's presence deactivated — terminal, see below
}
```

`New` moves the "nothing happened yet" sentinel off of `Ringing` (today
`Ringing = 0` is both the C# default *and* an active business state, which
reads as if a freshly-defaulted/never-created value were already ringing).
In practice `StartCall` still creates the invite directly with `Status =
Ringing`, so `New` is mostly a hygiene fix for the enum's default, not a
state invitees are expected to visibly pass through.

`Active`/`Ended` are new — see "Participant presence drives `Active`/`Ended`"
below for exactly what sets them; they are not derived from `Accepted`/time,
only from a genuine presence signal. Both need timestamp fields on
`CallInvite`:

```csharp
public sealed partial record CallInvite {
    public AuthorId InviteeId { get; init; }
    public CallInviteStatus Status { get; init; }
    public Moment RingingAt { get; init; }
    public Moment? RespondedAt { get; init; }
    public Moment? ActiveAt { get; init; }        // new
    public Moment? EndedAt { get; init; }         // new
    public RingAck? Ack { get; init; }             // new, see below
    public Moment? AckAt { get; init; }            // new
}
```

`Missed` stays a single value — it does not distinguish "timed out" from
"the caller canceled while I was still ringing" (both already end up here
today via `ExpireRings`/`CancelCall`). Splitting it was considered and
rejected: nothing currently reads that distinction per-invitee.

#### Participant presence drives `Active`/`Ended` — via `GetConsolidatedParticipants`, not raw counts

`CallStatus`/`Derive` (above) must never call `ParticipantCount(chatId)` or
otherwise read `_participants`/`ParticipationInfo` directly, and — a
correction from the first draft of this section — the write to
`CallInvite.Active`/`Ended` must **not** be inlined into `SetParticipation`'s
own handler either. `SetParticipation` only knows "this one author's kind
changed"; it does not know how to detect a *silent* departure (a crash that
never calls `SetParticipation(isActive: false)` at all) — the same
liveness gap this whole session has already circled around once (Task 6's
revert).

Instead, reuse `GetConsolidatedParticipants` (`LiveSessionsBackend.cs:776`)
— the `[ComputeMethod]` **already used for Ambient sessions**
(`ListParticipants`): it filters `_participants` by `ParticipantStaleness`
freshness and self-heals via `computed.Invalidate(SelfHealDelay)` while any
participant is fresh, so a participant who goes stale drops out of its
result **with no explicit "off" signal needed** — this already is the
"someone silently disappeared" detector this session needed; it just isn't
wired to the call's own per-participant status yet.

- The sync point reads `GetConsolidatedParticipants(chatId)` and diffs it
  against this call's currently-known caller/invitee `Active` set: anyone
  newly present who was `Ringing`/`Accepted` → `Active`/`ActiveAt`; anyone
  previously `Active` no longer present → `Ended`/`EndedAt` (ratchet — see
  below), then calls `RecomputeCallStatus`.
- This sync runs from the **same tick `GetState`'s existing self-heal
  already uses** (the `computed.Invalidate(SelfHealDelay)` at the end of
  `GetState`, which already fires `ExpireRings` from the same spot) —
  not a new background loop. Because `SetParticipation` already calls
  `InvalidateListParticipants(chatId)` on every write, an *explicit*
  deactivation (a real hang-up) invalidates `GetConsolidatedParticipants`
  and is picked up on the very next observation — no perceptible delay for
  the deliberate case. A *silent* crash is caught by
  `GetConsolidatedParticipants`'s own self-heal backstop instead — same
  ~90s+30s timing this file already accepts everywhere else, not faster,
  but now at least correct (today's gap: nothing would ever set `Ended`
  for a silently-crashed talker at all).
- **Reactivation is impossible** — a ratchet. If `GetConsolidatedParticipants`
  later shows a participant fresh again after they were already `Ended`
  (or `Declined`/`Missed`/`Canceled`/`NoAnswer`) for this call, it is not
  applied — logged as off-nominal (per Signal Validation below) and
  dropped. A repeat "still present" reading for an already-`Active`
  participant is an ordinary idempotent no-op, not logged either way.

`CallState` gains the caller-side equivalent of `CallInvite`'s `ActiveAt`/
`EndedAt` (the caller has no separate per-invite record, so these live
directly on `CallState`):

```csharp
public sealed partial record CallState {
    public AuthorId CallerId { get; init; }
    public CallStatus Status { get; init; }
    public Moment ChangedAt { get; init; }
    public Moment? CallerActiveAt { get; init; }   // new — presence: caller became genuinely active
    public Moment? CallerEndedAt { get; init; }    // new — presence: caller's presence deactivated (ratchet)
    public Moment? CanceledAt { get; init; }       // new — explicit CancelCall, distinct from the above
}
```

`CanceledAt` is deliberately a separate fact from `CallerEndedAt`: the
whole reason this session distinguishes deliberate departure from passive
absence (`LeaveCall`'s removal, `SetParticipation`'s kind-guard, etc.)
applies here too — a crash must never read as `Canceled`. `CanceledAt` is
set only by `CancelCall` itself; `CallerEndedAt` only by a genuine presence
deactivation. `Derive` checks `CanceledAt` only in the branch where the
call was never `Active` — once `everActive` is true, `Ended` always wins
regardless of `CanceledAt`, matching "a call that connected is Ended
whichever button ended it."

This is what makes `CallerStatus`'s `Ended` value (already in the earlier
table) and the "was this call ever Active" rule for `Derive`'s terminal
branch (open item, below) both fall out for free: "ever active" is simply
`CallerActiveAt is not null || invites.Any(i => i.ActiveAt is not null)`.

### Client-ack (`RingAck`) — new, purely informational

```csharp
public enum RingAck { Received = 0, Ringing = 1, Busy = 2 }
```

A callee's client can report, independently of the invite's own business
`Status`: it received the ring (`Received`), it started actually ringing
the user (`Ringing`), or the local device is already busy (`Busy`). This is
telemetry for diagnosing "did the ring even reach the client" — it does
**not** change server behavior: a `Busy` ack does not skip notifying that
device, cancel the invite, or otherwise short-circuit anything. It is
purely recorded (`CallInvite.Ack`/`AckAt`) for debugging and future use.

New RPC, next to `AcceptCall`/`DeclineCall`:

```csharp
// ILiveSessions
Task ConfirmRing(Session session, ChatId chatId, RingAck ack, CancellationToken cancellationToken);
```

```csharp
// ILiveSessionsBackend
Task ConfirmRing(ChatId chatId, AuthorId inviteeAuthorId, RingAck ack, CancellationToken cancellationToken);
```

The backend method just writes `Ack`/`AckAt` onto the matching `CallInvite`
(no-op if the invite isn't `Ringing`/doesn't exist) and invalidates state —
no lock-then-branch dance is needed since nothing downstream reacts to it.

## Signal validation

Every RPC that represents an explicit signal from a participant
(`AcceptCall`, `DeclineCall`, `CancelCall`, `ConfirmRing`) must check that
the signal is a valid transition **from the participant's current status**
before applying it. Today this exists only as a silent guard (e.g.
`AcceptCall`/`DeclineCall` already do `if (invite is not { Status: Ringing
}) return;`) — invalid/out-of-order signals (a double accept, a decline
after already accepted, a client that missed a state change and cancels
twice) currently vanish with no trace. This spec makes the check explicit
everywhere and logs a warning — attempted action + the status it was
attempted against — instead of a bare no-op, for the same "can we tell
what actually happened" reason `RingAck` exists.

Valid-from table:

| Signal | Valid only when current status is |
|---|---|
| `AcceptCall` (invitee) | `CallInviteStatus.Ringing` |
| `DeclineCall` (invitee) | `CallInviteStatus.Ringing` |
| `ConfirmRing` (invitee) | `CallInviteStatus.Ringing` |
| `CancelCall` (caller) | `CallStatus.Dialing` or `CallStatus.Connecting` (not yet `Active` — hanging up an `Active` call goes through the ordinary presence path, not `CancelCall`) |
| presence *activation* (invitee) | `Ringing`, `Accepted`, or already `Active` (heartbeat — idempotent, not logged either way) |
| presence *activation* (caller) | any status before a terminal one — i.e. `CanceledAt is null` and not already `Ended`/`NoAnswer`/`Declined` overall; already-`Active` is an idempotent heartbeat, same as invitee |
| presence *deactivation* (invitee/caller, via the `GetConsolidatedParticipants` sync) | only meaningful from `Active` — a participant dropping out of `GetConsolidatedParticipants` while not currently `Active` for this call has nothing to do, no log needed |

An invalid signal still no-ops (same observable behavior as today) —
this only adds the log line, it doesn't change what happens on a
legitimate race (e.g. a client retries `AcceptCall` after a dropped
response — the retry arrives to find `Status` already `Accepted`, logs a
warning, and no-ops, exactly as it silently does today). The presence-
activation row is the important new case: a participant already `Ended`
(or `Declined`/`Missed`/`Canceled`/`NoAnswer`) whose stream reactivates is
never resurrected — logged as off-nominal and dropped, per "Participant
presence drives `Active`/`Ended`" above.

Worth factoring into one small shared helper (something like
`EnsureValidTransition(chatId, authorId, expected, actual, signalName)`)
rather than repeating the same log call at every site — left for the plan.

## Call sites affected (backend)

All in `LiveSessionsBackend.cs`, from the current `LiveSessionKind.Dialing`/
`CallStatus.*`/`IsDialing`/`IsCall` usages:

| Line(s) (current) | Change |
|---|---|
| `GetState` (~119-122) | `state.IsCall`/`state.IsDialing` keep working (redefined property, same call site) |
| `Get` (~145, ~226) | same — `IsCall`/`IsDialing` unchanged call sites |
| `OnStreamRegistered` (~330) | drop the `Kind == Dialing ? Call : Kind` branch — `Kind` is already `Call` |
| `StartCall` (~597, ~603) | `Kind` set to `Call` unconditionally (no more `SessionStartedAt is not null ? Call : Dialing`); `SetCallState(..., CallStatus.Dialing)` unconditional (no `state.IsDialing ?` guard needed — a promoted ambient session never re-enters Dialing) |
| `AcceptCall` (~659) | writes `CallInviteStatus.Accepted` on the invite (already does, via `_invites.Set`), then calls `RecomputeCallStatus` instead of `SetCallState(..., CallStatus.Accepted)` directly |
| `DeclineCall` (~691) | writes `CallInviteStatus.Declined`, then `RecomputeCallStatus` instead of `SetCallState(..., CallStatus.Declined)` directly |
| `EnforceCallConnectGrace` | on success or failure, calls `RecomputeCallStatus` instead of only closing on failure — success needs to *advance* the status (to `Active`) too, which recompute does for free once `activeCount >= 2` |
| `GetState`'s self-heal (~127, alongside its existing `ExpireRings` trigger) | new: for a live call, reads `GetConsolidatedParticipants(chatId)`, diffs against the caller's/invitees' known `Active` set, writes `Active`/`Ended`/`ActiveAt`/`EndedAt` for whoever changed (ratchet-checked), then `RecomputeCallStatus` — see "Participant presence drives `Active`/`Ended`" |
| `ExpireRings` (~1072-1077) | writes `CallInviteStatus.Missed` per expired invite (already does), then `RecomputeCallStatus` instead of the direct `SetCallState(..., CallStatus.NoAnswer)` |
| `CancelCall` | records the caller's explicit cancel as a fact (see open item below), then `RecomputeCallStatus` — replaces today's direct `SetCallState(chatId, null)` |
| `NewCallState`'s TTL helper (~980) | `status == CallStatus.Dialing ? DialingStateTtl : ResolvedStateTtl` — extend to treat `Connecting`/`Active` as non-resolved (short TTL only makes sense pre-connect; a resolved status should get `ResolvedStateTtl` even from `Active`→terminal) |
| `LiveSessions.GetCallStatus` (`Services/LiveSessions.cs:95-105`) | return type becomes `CallerStatus`, body applies the projection table above |

This list is a map for the implementation plan, not exhaustive line edits —
`writing-plans` should re-verify each site against the code at plan time.

## Open items for the plan (not decided here)

1. **`CancelCall`'s `SetCallState(chatId, null)` replacement** — the fact
   (`CanceledAt`) and the recompute logic are now settled (`Derive` reads
   `CanceledAt` directly). Still open: should `CallState` be *kept* after
   `CancelCall` (with `Status = Canceled`, `ResolvedStateTtl`) so the
   caller's own client reads back a real `Canceled` rather than an instant
   `None`/absence, instead of clearing it outright as today? Needs a look
   at `OutgoingCallBanner`/`IncomingCallOverLockView`'s current handling of
   `CallStatus.None` before deciding.
2. **`ConfirmRing`'s call site on the client** — needs a client-side hook
   that fires the instant a ring push/RPC lands (before any user
   interaction), and a way for the client to self-detect `Busy` (likely:
   "is `_inCallChatId`/its future replacement already set for another
   chat"). This is client work, sequenced after the server model lands.
3. ~~`OnStreamRegistered`'s edge case~~ — **resolved** by the
   `GetConsolidatedParticipants`-based sync above: it reads `_participants`
   directly, regardless of whether `EnsureParticipant` (this edge case) or
   `SetParticipation` wrote the entry, and `EnsureParticipant` already
   calls `InvalidateListParticipants` too. No special-casing needed.
4. **Does `EnforceCallConnectGrace`/`SetParticipation`'s `shouldCloseAsCall`
   close-decision itself also switch off `ParticipantCount(chatId)`, or
   only `CallStatus`'s computation does?** This spec bans `ParticipantCount`
   from `Derive` specifically; it does not by itself require rewiring the
   already-shipped, tested close-decision logic (Task 3/4 earlier this
   session) to read `activeCount` from the new per-participant facts
   instead. Worth noting while deciding: `ParticipantCount`
   (`LiveSessionsBackend.cs:1130-1135`) and `GetConsolidatedParticipants`
   are *already* the same filter (`SafeGetHashMap` + `IsFreshParticipant` +
   `ParticipantStaleness` cutoff) duplicated twice — one as a plain private
   helper, one as a self-healing `[ComputeMethod]`. Recommend collapsing
   `ParticipantCount` into `(await GetConsolidatedParticipants(chatId, ct))
   .Count` outright (both are already called from inside `Computed
   .BeginIsolation()` blocks, so this is a safe, idiomatic substitution,
   not a new pattern) — this removes the duplication *and* makes "how many
   are active" a true single source of truth end to end. Confirm before
   the plan commits to touching `shouldCloseAsCall`/`EnforceCallConnectGrace`/
   `IsCallAbandoned` (all three currently call `ParticipantCount`).
