# Call status: single `CallState` replacing `CallOutcome`

## Goal

Give the caller a single call-status signal — `Dialing | Accepted | Declined | NoAnswer | None` —
surfaced through one compute method, and collapse the outgoing-call banner to that single
source. This removes the two-source race (live session + `CallOutcome`) that caused the
"No answer → You are calling" flash.

## Scope

- Caller-only (host). Symmetric to the current `CallOutcome`, which the facade already filters
  by `CallerId`.
- `Accepted` is a brief confirmation (short TTL), then the status returns to `None` even though
  the connected call continues (its ongoing state is owned by `ChatActivityPanel`, not this banner).

## Model (`ActualChat.Live`)

- `enum CallStatus { None, Dialing, Accepted, Declined, NoAnswer }`. `None` = no key stored.
- `record CallState { AuthorId CallerId; CallStatus Status; Moment ChangedAt; }`. Stored `Status`
  is always one of `Dialing | Accepted | Declined | NoAnswer`.
- Remove `CallOutcome` and `CallOutcomeKind`.

## Storage

Reuse the existing caller-scoped Redis key (rename `live-session:outcome` →
`live-session:call-state`). TTL depends on phase:

- `Dialing` — 60s (covers the 20s ring + backstop; a terminal transition overwrites it sooner,
  a crashed caller lets it lapse).
- `Accepted` — 30s.
- `Declined` / `NoAnswer` — 30s.

`GetCallStatus` (backend) self-invalidates at `ChangedAt + ttl(Status)` — the same trick the old
`GetCallOutcome` used, so the observed value expires with the Redis key.

## Timeouts (testing values)

- `RingTimeout` = **20s** (temporary for testing; production is 40s). Mark it clearly so it is
  reverted.
- `RingTtl` stays 60s (invariant `RingTtl > RingTimeout` still holds).

## Server transitions (all inside the existing `_changeLocks` critical sections)

| Point | Action |
|---|---|
| `StartCall` | `SetCallState(Dialing, host)` (replaces the old outcome-clear) |
| `AcceptCall` — first answer latches the session | `SetCallState(Accepted, host)` |
| `DeclineCall` — abandoned | `SetCallState(Declined, host)`, before `InvalidateState` |
| `ExpireRings` — abandoned dialing | `SetCallState(NoAnswer, host)` |
| `CancelCall` | delete the key (hanging up needs no status) |
| `DismissCallStatus` | delete the key (the banner's X) |

Every path that closes a dialing session (`Decline`/`ExpireRings`/`Cancel`) already goes through
one of these, so `CallState` stays consistent with the session. `CloseCall`/`Close` do **not**
touch `CallState` — a terminal status is written before the close and must outlive the dropped
session. An anomalous lingering `Dialing` is reaped by its 60s TTL.

Close-grace logic and `LiveSessionState` are left untouched.

## Client

- Contracts: `ILiveSessionsBackend`, `ILiveSessions`, `LiveSessionUI` —
  `GetCallOutcome` → `GetCallStatus` (returns `CallStatus`; facade filters by `CallerId`),
  `DismissCallOutcome` → `DismissCallStatus`.
- Banner (`OutgoingCallBanner`) reads only `GetCallStatus`:
  - `IsVisible = status != None`
  - `Text` / `Icon` by `switch(status)`: Dialing → "You are calling…", Accepted → "Call accepted",
    Declined → "Call declined", NoAnswer → "No answer".
  - X button: `Dialing` → `CancelCall`; otherwise → `DismissCallStatus`.
  - Drops `Get`, `Authors.GetOwn`, the `Ringing` check, and the two-compute combination.
- `WatchOutgoingCall` / `JoinAnsweredCall` stay as-is (they auto-join the caller on answer and do
  not depend on `CallState`).

## Reuse

Reuses the whole `CallOutcome` mechanism: the `RedisScope`, `SetCallOutcome` → `SetCallState`,
`NewCallOutcome` → `NewCallState`, the self-invalidation, and the facade host-filter. New:
`CallStatus` enum (shared, `ActualChat.Live`), the `Dialing`/`Accepted` write points, and
per-phase TTL. The banner loses code.

## Out of scope (YAGNI)

- Close-grace / `LiveSessionState` changes.
- Wiring Accept/Decline in `IncomingCallModal`. Consequence: `Accepted` only fires once acceptance
  is wired to the UI (today only `VoiceCallTestPage` can accept); the other statuses work
  immediately.

## Tests

- Rewrite the `CallOutcome` tests onto `GetCallStatus`.
- Backend: `StartCall` → `Dialing`; `AcceptCall` → `Accepted`; `DeclineCall` (abandoned) →
  `Declined`; `CancelCall` → `None`; status goes to the caller only; a fresh dialing call is not
  closed by self-heal; dismiss clears with the session gone.
- Client-UI layer: `GetCallStatus` reacts through `LiveSessionUI` (dialing → declined) without a
  fresh capture.
