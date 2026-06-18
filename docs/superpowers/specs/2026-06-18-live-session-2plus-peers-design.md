# Live Session = 2+ Peers, VAD-gap-tolerant — Design

Date: 2026-06-18
Status: Approved (design), pending implementation plan

## Context

Phase 2 of "Realtime Conversations" split the per-chat live state into two facets:
`LiveConversation` (the transcript / activity facet — created on the first qualifying
stream) and the `LiveSession` projection (`GetLiveSession`) that drives the call surfaces:
the right-panel **Call** tab, the member list, session **Rules**, and **MutePeer**.

Today both facets come to life on the **first** streamer: `OnStreamRegistered` creates the
`LiveConversation` state, and `GetLiveSession` returns non-null whenever that state exists.
That means a single person recording a voice message immediately presents a "call"
(Call tab, members, rules) even though no conversation is happening.

The intended meaning of a live session is **a live conversation between 2+ peers**. One peer
streaming voice/video/transcription is not a session — other peers should just see the existing
activity panel (with Join) and realtime transcription, as today. The session (call) surfaces
should appear only once a **second** peer joins with live voice/video.

A second problem surfaces from the realtime pipeline: audio/video streams are **terminated on
VAD silence** — a new stream per utterance. During a real multi-peer conversation there are
moments when **nobody** is streaming. So "is the session still running?" cannot be answered by
the instantaneous stream count; it must tolerate sub-window silence gaps.

Outcome: the call surfaces start only for genuine 2+ peer conversations and stay alive across
VAD gaps for the conversation's duration; single-talker behavior (activity panel + transcription
block) is unchanged.

## Locked decisions

1. **Session = 2+ peers streaming.** Latches when ≥2 distinct peers have streamed
   voice/video during one conversation. Listeners do not count toward the start.
2. **Persist until conversation ends.** Once latched, the session stays for the conversation's
   life even if it momentarily drops to 1 active streamer; it ends only when the underlying
   conversation closes. No flap, no grace-to-collapse-back, no host succession.
3. **Approach A — explicit latch field** on the Redis state (`SessionStartedAt`). Rejected:
   deriving from `AuthorIds.Count` (overwritten by summarization), and an explicit
   "Start a call" user action (contradicts automatic-on-2nd-peer).
4. **Reuse the existing 90s close grace** for liveness; no new timer. Also apply it to phone
   mode (fix below).
5. **"Voice chat started" notification stays as-is** — fires on the first phone-mode streamer.

## Reuse

**Existing abstractions to reuse:**
- `Streaming.Service/Backend/LiveSessionsBackend.cs` — `OnStreamRegistered` (latch site),
  `GetLiveSession` (gate site), `OnStreamsChanged` + `Get` finalize path + `SelfClose`
  (liveness), `AsyncLockSet<ChatId>` per-chat lock, `VersionGenerator`, `RedisScope`.
- Existing liveness constants on the same class: `CloseTimeout` (90s), `SelfHealDelay` (30s),
  `KeyTtl` (6 min). No new constants.
- `LiveConversation` (`Api/Live`) MemoryPack VersionTolerant record — add one field.
- `LiveSession` / `LiveSessionMember` projection + `CallList.razor` + `ShowCallTab`
  (`RightPanelContent.razor`) — all already key off `GetLiveSession`; they auto-gate, no edits.
- `LiveSessionUI.Get` → `LiveConversation` consumers (`ChatUI.Tiles.cs`, `ChatActivityUI`) —
  unchanged, stay 1+.

**Reusability of new components:** none. The only new artifact is one field on the existing
feature-specific `LiveConversation` record; it stays in `Api/Live`. No shared-project candidate.

## Data model

`Api/Live/LiveConversation.cs` — add:

```csharp
[DataMember(Order = N), MemoryPackOrder(N), Key(N)]
public Moment? SessionStartedAt { get; init; }
```

`null` = no multi-peer session yet (single-talker / activity-only). Non-null = session latched
at that moment; one-way for the conversation's life. Cleared implicitly when the state is removed
on close → "persist until conversation ends" with no extra bookkeeping.

`Api/Live/LiveSession.cs` — `StartedAt` is sourced from `SessionStartedAt` (the *call* start),
not the conversation `StartedAt`.

## Behavior

### Latch (start)
In `OnStreamRegistered`, under the per-chat lock, after `AuthorIds` is (re)built — in **both**
the fresh-create and the reactivate/append branches — before `_redisScope.Set`:

```csharp
if (state.SessionStartedAt is null && state.AuthorIds.Count >= 2)
    state = state with { SessionStartedAt = now, Version = VersionGenerator.NextVersion(state.Version) };
```

`AuthorIds` at registration is the set of distinct **streamers** (listeners live in the
`_participants` hash, never in `AuthorIds`), so `Count >= 2` is exactly "2+ peers streaming
voice/video". The per-chat lock serializes registrations, so there is no race.

### Gate (display)
In `GetLiveSession`, immediately after the `state is null` guard:

```csharp
if (state.SessionStartedAt is null)
    return null;
```

A single gate. Everything that means "call/session" (Call tab via `ShowCallTab`, `CallList`
members/Rules/Manage/MutePeer) reads `GetLiveSession`, so all of them gate to 2+ with no further
change. Everything that means "someone is talking / transcription" reads `LiveSessionUI.Get` →
`LiveConversation` (tile "〰 N talking · live", activity-panel Join, in-chat transcript summary
block, bystander tail-collapse) and keeps firing on the first streamer.

### Liveness (still-running)
Reuse the existing grace-based model: when a stream tears down (VAD silence), `OnStreamsChanged`
marks `IsClosing/ClosingAt`; `Get` finalizes (`SelfClose` → returns null) only once
`now - ClosingAt > CloseTimeout` (90s). A new utterance within that window calls
`OnStreamRegistered`, which clears `IsClosing/ClosingAt` (reactivate). `Get` re-invalidates every
30s (`SelfHealDelay`) so the finalize check re-runs; the 6-min `KeyTtl` is the Redis backstop.

So "is the session still running?" = **did any peer stream within the last 90s?** The session
inherits this because `SessionStartedAt` rides the same `LiveConversation` state; the latch and
`AuthorIds` both persist across VAD gaps (stored, not recomputed per instant).

### Phone-mode close fix
Currently `OnStreamsChanged` for `!TranscriptionOn` does `_redisScope.Remove(...)` **immediately**
when no streams remain — bypassing the 90s grace. For a phone-like call without transcription,
every VAD silence between utterances would tear the session down and recreate it on the next word
→ flap.

Change: the phone-mode branch sets `IsClosing/ClosingAt` (same 90s grace as transcription) instead
of removing. The phone-vs-transcription difference moves to **finalize** time
(`SelfClose` / the `Get` finalize path): phone → remove state + "Voice chat ended" notification;
transcription → hand to `LiveConversationSummaryFlow` (materialize or vanish). Result: both modes
treat sub-90s stream-less gaps as "still running."

### Unchanged
"Voice chat started" notification still fires on the first phone-mode streamer in
`OnStreamRegistered`. Tile/activity-panel remain stream-only (1+) via `ChatActivityUI`.

## Files to change

- `Api/Live/LiveConversation.cs` — add `SessionStartedAt`.
- `Api/Live/LiveSession.cs` — `StartedAt` from `SessionStartedAt`.
- `Streaming.Service/Backend/LiveSessionsBackend.cs` — latch in `OnStreamRegistered`; gate in
  `GetLiveSession`; phone-mode close grace in `OnStreamsChanged`; phone vanish + "Voice chat
  ended" relocated to the finalize path (`SelfClose` / `Get`).
- No UI changes (Call tab already keys off `GetLiveSession`).

## Verification

- Build: in-container `dotnet build` of `Api`, `Streaming.Contracts`, `Streaming.Service`
  `--no-restore`.
- `LiveSessionsTest` (`tests/Chat.IntegrationTests`):
  - single streamer → `GetLiveSession` is null while `Get` is non-null; no Call tab.
  - second distinct streamer → `GetLiveSession` non-null, `SessionStartedAt` set.
  - VAD gap: stream end then re-register within 90s → session + latch persist
    (`GetLiveSession` stays non-null).
  - phone mode (`!TranscriptionOn`) VAD gap → state is no longer removed immediately; survives
    the grace window; finalizes (vanish + "Voice chat ended") only after 90s stream-less.
  - drop to 1 active streamer after latch → session persists until full close.
- Manual e2e (two `test-*@actual.chat` accounts): A records alone → B sees activity panel +
  Join + transcription, **no Call tab**. B joins with voice → Call tab + members appear for both.
  Natural conversation pauses (VAD gaps) do not collapse the Call tab. All stop → after ~90s the
  session ends.

## Deferred

Per-session collapse-back to single-talker on sustained drop to 1 (we persist instead);
separate shorter session grace; host succession; server-side speaking signal.
