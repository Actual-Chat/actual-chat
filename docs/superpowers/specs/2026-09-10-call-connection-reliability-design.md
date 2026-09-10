# Call connection reliability — design

Date: 2026-09-10
Branch: `fix/update-ui-287`
Status: approved by user in chat, pending spec file review

## Problem

Two related reliability gaps in how a 1:1 (or group) call's "is anyone still
actually here" invariant is enforced, discovered while root-causing a live
bug (commit `9e0b87186c`, fixed today):

1. **No server-enforced "≥2 real participants" invariant.** The only place
   `ParticipantCount(chatId) < 2` is checked is inside `LeaveCall`, which
   only runs when a client *explicitly* calls it. If a participant's
   connection dies silently (crashed app, lost network, unexpected client
   bug) without ever sending `LeaveCall`, nothing on the server notices in
   any bounded time. The general session-liveness self-heal
   (`EvaluateLiveness` / `SelfClose` / `GetState`'s self-invalidation) checks
   "is anyone still streaming" (`IsSessionLive` — recorder-only, ignores
   headcount), not "are there ≥2 people", and it only ticks every 30s
   (`SelfHealDelay`), and only while something is actively observing the
   session.
2. **No grace-period check for "accepted but never actually connected".**
   `AcceptCall` promotes `LiveSessionState.Kind` to `Call` and registers the
   invitee as a participant (`EnsureParticipant`) synchronously, before
   their client has done anything with audio. If their audio pipeline then
   never comes up (stuck permission prompt, client bug, dead network), the
   call sits promoted to `Call` forever from the server's point of view —
   nothing ever notices the second party never really joined.

Today's fix (`IsForegroundDialingActive`/`ComputeForegroundCallChatId`
treating `CallStatus.Accepted` the same as `Dialing`) papers over one
*symptom* of the underlying fragility (two independently-invalidated
signals, `CallStatus` and `LiveSessionState.Kind`, racing on the client) but
does not give the call-establishment path an actual reliability guarantee.
This spec is the follow-up: make the server the source of truth for "is
this call still real", instead of leaving it to client-side inference.

## Goals

- The server actively enforces "a `Call`-kind session needs ≥2 genuinely
  present participants", independent of any specific client's cooperation
  (a crashed/misbehaving client must not be able to leave the other side
  hanging indefinitely).
- A participant who accepts but never truly connects is detected within a
  few seconds, not never.
- A participant who silently disappears mid-call is detected immediately
  (on the connection actually dropping), not on a 30s poll.
- The single remaining side reacts by leaving the call automatically (this
  part already exists — `IncomingCallUI.ResetActiveCall`, shipped today —
  and needs no further work *provided* the server reliably flips
  `LiveSessionState.Kind` away from `Call`).
- No behavior change for `Ambient` live-conversations (solo dictation stays
  legitimate) — this invariant is scoped to `Kind == Call` only.

## Non-goals / explicitly deferred

- The smaller, user-triggered race in `OutgoingCallBanner.OnClose`
  (`CancelCall` gated on a possibly-stale `CallStatus == Dialing` read) is
  **not** part of this redesign. Discussed and explicitly deferred: it
  requires a specific user click at a narrow moment (not automatic like the
  bug fixed today), and even when it fires the net effect — the call
  ends — is close to what the user intended anyway. Revisit separately if
  it's ever reported in practice.
- No new `CallOutcome` value. A call that fails the grace-period check
  records the same `Ended` outcome an already-established call gets on any
  other hangup (`WriteCallEntry`'s existing rule: `SessionStartedAt` set ⇒
  `Ended`, regardless of which path closed it). Decided explicitly over
  adding a distinct `Failed` outcome — not worth the extra surface for a
  case indistinguishable, from the outside, from "a very short call".

## Presence signal: connection-lifetime, not a periodic timestamp

Today, `_participants` entries carry a `RegisteredAt` timestamp, and
`IsFreshParticipant` treats anyone within `ParticipantStaleness` (90s) of
it as present. For a **recorder**, this is already a live signal in
practice: `AudioStreamingBackend.ProcessAudio` calls `OnStreamRegistered` →
`EnsureParticipant` on every registered utterance, so `RegisteredAt` keeps
rolling forward for as long as someone is actually pushing audio.

For a **listener**, no equivalent refresh exists today —
`LiveAudioStreams.GetListeningStream` constructs a `ListeningStreamMuxer`
per listening session but never registers or refreshes a participant
record for it (`ParticipationKind.AudioListen` exists on the model and is
already read in `LiveSessionsBackend.Get`, but nothing writes it from the
listening path). A pure listener (e.g. mic denied, falls back to
listen-only) currently has no ongoing presence signal beyond the one-shot
`EnsureParticipant` from `AcceptCall`/`StartCall`, and goes stale after 90s
regardless of whether they're still listening.

**Decision:** stop treating "presence" as "a timestamp not too old" and
instead tie it directly to the lifetime of the actual connection:

- **Listener** — register (`EnsureParticipant`, `Kind = AudioListen`) when
  `GetListeningStream` constructs its `ListeningStreamMuxer`; remove the
  participant record when the muxer's `finally` block runs (already exists
  in `LiveAudioStreams.ToLiveAsyncEnumerable`, currently only calls
  `muxer.DisposeAsync()`) — the natural place to also react to the drop.
- **Recorder** — same idea, hooked into `AudioStreamingBackend.ProcessAudio`'s
  existing `finally` blocks (both the "ended normally" and "ended
  unexpectedly" paths already exist there).
- `ParticipantStaleness`/`IsFreshParticipant` stay as the backstop for the
  case where a `finally` never runs at all (process killed hard enough to
  skip unwind) — unchanged, still 90s, now genuinely a last resort rather
  than the primary signal.

This directly closes the "chisто слушающий" gap the user raised, and gives
both presence kinds a symmetrical, immediate on/off signal instead of a
polling window.

## Root-causing today's race: couple `GetCallState` to `GetState`

Today's already-shipped fix (`IsForegroundDialingActive`/
`ComputeForegroundCallChatId` treating `Accepted` like `Dialing`) is
defensive — it papers over the symptom. The actual root cause: `CallState`
(`_callStates`, its own Redis key, its own TTL) and `LiveSessionState`
(`_redisScope`) are two independently-invalidated `[ComputeMethod]`s —
`GetCallState` (`LiveSessionsBackend.cs:239`) reads Redis directly and
self-invalidates purely on its own TTL expiry, with **no** Fusion
dependency on `GetState`/`Kind` at all. `AcceptCall` writes both under the
same lock, but they still reach RPC clients as two unrelated invalidation
notifications with no ordering guarantee between them.

**Fix:** make `GetCallState` call `await GetState(chatId, cancellationToken)`
as part of its body (even just to read `state?.Kind`/`SessionStartedAt` for
a sanity check), so Fusion's dependency graph makes `GetCallState`'s
computed depend on `GetState`'s. Any invalidation of `GetState` (including
the `AcceptCall` promotion to `Kind = Call`) then transitively invalidates
`GetCallState` too, instead of the two drifting independently. This is a
small, low-risk, purely-internal change (no wire/schema impact) and it is
the mechanical fix for the exact race class `9e0b87186c` patched around —
it should ship as part of this work, ahead of (and independent from) the
headcount-enforcement mechanism below.

Not in scope here: collapsing `LiveSessionKind.Dialing` into a phase of
`Call` (`Dialing -> Accepting -> Talking -> Ended`) — a real modeling
simplification (removes the `Kind is Call or Dialing` idiom across ~14
call sites, client included) but orthogonal to reliability and larger in
surface. Deferred to a separate follow-up spec.

## Detection mechanism: three paths, one existing + two new

Scoped to `state.Kind == LiveSessionKind.Call` only (`Dialing` keeps its
own `ExpireRings`/`IsCallAbandoned` path unchanged; `Ambient` is exempt from
the ≥2 rule entirely — solo recording there is normal).

1. **Immediate, event-driven** (new): the same disposal hooks that make
   presence connection-lifetime-bound (above) also, right there, re-check
   `ParticipantCount(chatId) < 2` for `Kind == Call` sessions and close the
   call at once if true. No timer, no polling — the drop itself is the
   trigger. This is the fast path for "was connected, then someone left"
   (mid-call hangup, crash, or lost network), replacing reliance on
   whatever explicit `LeaveCall` the departing client may or may not have
   managed to send.
2. **One-shot grace timer** (new): scheduled once, at the exact moment
   `AcceptCall` promotes `Kind` to `Call`, for a fixed delay (**3 seconds**,
   per user's explicit choice — see "Accept-flow reorder" below for what
   makes 3s realistic). When it fires, re-check presence the same way: if
   still `< 2` genuinely-present participants, close the call as `Ended`.
   This is the fast path for "accepted but never actually connected".
3. **Existing 30s self-heal** (`GetState`'s `computed.Invalidate(SelfHealDelay)`
   cycle): unchanged, kept purely as a backstop for whatever the two fast
   paths above might miss (e.g. the delayed grace-timer job itself getting
   lost across a process restart) — not the primary mechanism for either
   case anymore.

Both new paths reuse the existing `ParticipantCount`/`IsFreshParticipant`
machinery and the existing `_changeLocks`-guarded close path
(`CloseCall`/`CloseAndMaterialize`) — no new closing logic, only new
triggers into it.

## Accept-flow reorder (client)

Today, both `IncomingCallUI.Accept()` and `LiveSessionUI.JoinAnsweredCall()`
gate `SetListeningState`/`GetListeningStream` behind
`MicrophonePermission.CheckOrRequest()` completing — listening only starts
as a fallback *after* the mic prompt resolves (granted or denied). If that
prompt sits unanswered, neither recording nor listening starts, and the new
3s grace timer would fire a false positive on a call that's actually just
waiting on the user to click through an OS permission dialog.

**Decision:** reorder both call sites so listening starts immediately and
unconditionally right after the call is confirmed accepted, with the mic
permission request running independently/in parallel — granted, it upgrades
to recording on top of the already-open listening stream; denied or still
pending, presence is already established via listening alone, so the grace
timer doesn't need to know or care. Same pattern in both places:

- `IncomingCallUI.Accept()` (the accepting side)
- `LiveSessionUI.JoinAnsweredCall()` (the caller, once their outgoing call
  is answered)

## What does *not* change

- `IncomingCallUI`'s client-side `ResetActiveCall`/`IsStillInCall`
  (shipped today, commit `92a35e9954`) — it already does the right thing
  once the server flips `Kind` away from `Call`; this design is what makes
  that flip actually happen reliably instead of never.
- `ExpireRings`/`IsCallAbandoned` — unchanged, they own the `Dialing`
  (ringing) phase exclusively; the new invariant only starts applying once
  `Kind` is `Call`.
- `EvaluateLiveness`/`SelfClose`/`ParticipantStaleness` — unchanged
  mechanically, just demoted from "the only backstop" to "the last-resort
  backstop" for the `Call` case specifically.
- `OutgoingCallBanner.OnClose` — explicitly deferred, see above.

## Rollout / compatibility

- Server-only + client-only changes ship together on this branch; no wire
  contract changes (no new RPC methods, no new `CallOutcome`/`CallStatus`
  values) — an in-flight call across a deploy sees no schema mismatch.
- The grace-timer job is scheduled in-process, fire-and-forget, from
  `AcceptCall`. If the server process restarts before it fires, the 30s
  self-heal backstop still catches a genuinely-abandoned session, just on
  its slower cadence — acceptable, since a mid-deploy restart racing the
  first 3 seconds of a brand-new call is already a rare double coincidence.

## Testing

- Unit/integration: extend `tests/Chat.IntegrationTests/CallEntryTest.cs`
  (existing suite for this area) with cases for (a) accept, then never
  start any stream — call must close as `Ended` around the grace window;
  (b) accept, connect, then the listener's muxer disposes without an
  explicit `LeaveCall` — call must close immediately, not after 30s.
- Live verification: two-browser check via `chrome1`/`chrome2` +
  `/server-loop`, same rig used to root-cause and verify today's fix —
  confirm a killed tab (not a clean hangup click) still ends the call
  promptly for the remaining side.

## Open implementation questions for the plan

These are plan-level, not design-level, but flagged here so they aren't
lost: exact scheduling primitive for the one-shot grace timer (raw
`Task.Delay` fire-and-forget vs. the existing `FlowHub`/queue-based delayed
scheduling used for `WakeSummaryFlow`); exact call sites to add the
`EnsureParticipant`(`AudioListen`)/removal hooks in `LiveAudioStreams` and
`AudioStreamingBackend.ProcessAudio`; whether group calls (if `Kind.Call`
ever has >2 legitimate participants) need anything beyond the existing
"< 2 total" rule, or whether that already generalizes correctly (current
reading: it does — the invariant is "a conversation needs ≥2", not
peer-chat-specific).
