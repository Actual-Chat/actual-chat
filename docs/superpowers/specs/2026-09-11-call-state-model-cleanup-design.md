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

`CallState` itself is unchanged in shape:

```csharp
public sealed partial record CallState {
    public AuthorId CallerId { get; init; }
    public CallStatus Status { get; init; }
    public Moment ChangedAt { get; init; }
}
```

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

    var invites = await SafeGetInvites(chatId).ConfigureAwait(false);
    var activeCount = await ParticipantCount(chatId).ConfigureAwait(false);
    var callerCanceled = /* see open item below */;
    var status = Derive(state, invites.Values, activeCount, callerCanceled);
    await SetCallState(chatId, NewCallState(state, status)).ConfigureAwait(false);
}

private static CallStatus Derive(
    LiveSessionState state, IReadOnlyCollection<CallInvite?> invites, int activeCount, bool callerCanceled)
{
    if (activeCount >= 2)
        return CallStatus.Active;
    if (callerCanceled)
        return CallStatus.Canceled;
    if (invites.Any(i => i is { Status: CallInviteStatus.Accepted or CallInviteStatus.Active }))
        return CallStatus.Connecting;
    if (invites.Any(i => i is { Status: CallInviteStatus.Declined }))
        return CallStatus.Declined;
    if (invites.Count > 0 && invites.All(i => i is { Status: CallInviteStatus.Missed }))
        return CallStatus.NoAnswer;
    return CallStatus.Dialing;
}
```

Once the call has been genuinely `Active` at least once, its eventual
terminal status is always `Ended`, regardless of how it winds down — this
mirrors the already-existing rule "a call that connected is Ended whichever
button ended it." `RecomputeCallStatus` needs to know "was this call ever
Active" to apply that rule once `activeCount` drops back below 2 — see the
open item below.

Called after every fact-recording step: `AcceptCall`, `DeclineCall`,
`ExpireRings` (an invite becoming `Missed`), and after any presence-count
change that could cross the `>= 2` threshold in either direction (the same
places `EnforceCallConnectGrace` and `SetParticipation`'s `shouldCloseAsCall`
path already touch).

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
    Active = 3,     // this invitee is now a genuinely present participant (>= 1 fresh stream/heartbeat)
    Declined = 4,
    Missed = 5,     // never answered — timed out, or the caller canceled/the call ended while still ringing
}
```

`New` moves the "nothing happened yet" sentinel off of `Ringing` (today
`Ringing = 0` is both the C# default *and* an active business state, which
reads as if a freshly-defaulted/never-created value were already ringing).
In practice `StartCall` still creates the invite directly with `Status =
Ringing`, so `New` is mostly a hygiene fix for the enum's default, not a
state invitees are expected to visibly pass through.

`Active` is new. It requires a new timestamp field on `CallInvite`, set the
first time this invitee is confirmed as a genuine participant (mirrors
`ParticipationInfo.RegisteredAt`'s role, scoped to this invite):

```csharp
public sealed partial record CallInvite {
    public AuthorId InviteeId { get; init; }
    public CallInviteStatus Status { get; init; }
    public Moment RingingAt { get; init; }
    public Moment? RespondedAt { get; init; }
    public Moment? ActiveAt { get; init; }        // new
    public RingAck? Ack { get; init; }             // new, see below
    public Moment? AckAt { get; init; }            // new
}
```

`Missed` stays a single value — it does not distinguish "timed out" from
"the caller canceled while I was still ringing" (both already end up here
today via `ExpireRings`/`CancelCall`). Splitting it was considered and
rejected: nothing currently reads that distinction per-invitee.

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

An invalid signal still no-ops (same observable behavior as today) —
this only adds the log line, it doesn't change what happens on a
legitimate race (e.g. a client retries `AcceptCall` after a dropped
response — the retry arrives to find `Status` already `Accepted`, logs a
warning, and no-ops, exactly as it silently does today). Worth
factoring into one small shared helper (something like
`EnsureValidTransition(chatId, authorId, expected, actual, signalName)`)
rather than four separate log call sites — left for the plan.

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
| `SetParticipation`'s `shouldCloseAsCall` path | when the drop is *not* a close (count stays >= 2), no explicit `CallStatus` write needed — `RecomputeCallStatus` after any presence-count change naturally keeps reporting `Active` |
| `ExpireRings` (~1072-1077) | writes `CallInviteStatus.Missed` per expired invite (already does), then `RecomputeCallStatus` instead of the direct `SetCallState(..., CallStatus.NoAnswer)` |
| `CancelCall` | records the caller's explicit cancel as a fact (see open item below), then `RecomputeCallStatus` — replaces today's direct `SetCallState(chatId, null)` |
| `NewCallState`'s TTL helper (~980) | `status == CallStatus.Dialing ? DialingStateTtl : ResolvedStateTtl` — extend to treat `Connecting`/`Active` as non-resolved (short TTL only makes sense pre-connect; a resolved status should get `ResolvedStateTtl` even from `Active`→terminal) |
| `LiveSessions.GetCallStatus` (`Services/LiveSessions.cs:95-105`) | return type becomes `CallerStatus`, body applies the projection table above |

This list is a map for the implementation plan, not exhaustive line edits —
`writing-plans` should re-verify each site against the code at plan time.

## Open items for the plan (not decided here)

1. **Where does "the caller explicitly canceled" live as a fact?**
   `RecomputeCallStatus` needs a `callerCanceled` input distinct from "the
   caller's presence just disappeared" (a crash must never read as
   `Canceled` — the whole point of this session's earlier fixes was
   telling deliberate departure apart from passive absence). Candidates:
   a `CanceledAt: Moment?` field on `CallState` itself (set by `CancelCall`
   right before calling recompute, read back by recompute the same tick);
   or a transient in-memory flag threaded through the one call. Also
   replaces today's `SetCallState(chatId, null)` — decide whether
   `CallState` should instead be *kept* with `Status = Canceled` and
   `ResolvedStateTtl`, so the caller's own client reads back a real
   `Canceled` rather than an instant `None`/absence. Needs a look at
   `OutgoingCallBanner`/`IncomingCallOverLockView`'s current handling of
   `CallStatus.None` before deciding.
2. **`ConfirmRing`'s call site on the client** — needs a client-side hook
   that fires the instant a ring push/RPC lands (before any user
   interaction), and a way for the client to self-detect `Busy` (likely:
   "is `_inCallChatId`/its future replacement already set for another
   chat"). This is client work, sequenced after the server model lands.
3. **`OnStreamRegistered`'s edge case** (a still-`Dialing` call latching
   because a stream registered before a formal accept, ~line 323) — likely
   resolved for free by also calling `RecomputeCallStatus` there (it's a
   presence-count change like any other), but `AuthorIds.Count` (what this
   code path checks) and `ParticipantCount`/genuine-presence staleness
   (what `RecomputeCallStatus` would check) are two different countings
   today — confirm they agree here before relying on it.
4. **`Derive`'s "was this call ever Active" rule** — reuse
   `state.SessionStartedAt is not null`, or add a dedicated flag. Needed so
   a call that reached `Active` and then loses its last participant is
   recomputed as `Ended`, not `NoAnswer`/`Declined`.
