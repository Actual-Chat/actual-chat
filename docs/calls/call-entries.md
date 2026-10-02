# Call entries

Before this feature a call that never connected left no trace in the chat at all, and a
call with transcription off left nothing either — the session closed and took its live
block with it. This page describes what a call now leaves behind, who writes it, and what
each side sees.

**The short version.** `CallEntry` is a system entry recording how a call went. Three
outcomes mean it never connected — `NoAnswer`, `Declined`, `Canceled` — and one means it
did: `Ended`. The three failed outcomes render as their own small card. `Ended` is drawn by
the existing conversation card instead, which is what lets a call with a transcript expand
into it. Peer chats only; the model already carries the invitee list so group calls can be
enabled without a migration.

## What each side sees

One stored row, two readings. The entry carries `CallerId`, and the view compares it with
the reader's own author, so the wording and the arrow follow the reader rather than the
writer.

| Outcome | Caller sees | Invitee sees |
|---|---|---|
| `NoAnswer` | Outgoing call · *No answer* | Missed call · *Tap to call back* |
| `Declined` | Declined call | Declined call |
| `Canceled` | Canceled call | Missed call · *Tap to call back* |
| `Ended` | Call | Call |

`CallCardFormat` (`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/`) is the one
place that table lives; both render paths call it.

The card is one icon beside two rows — title with its hint on the first, timestamp on the
second. There are no avatars: a peer call has exactly two sides. There is no separate
direction mark either, because the icon carries it — `icon-call-arrow-out` for a call this
reader placed, `icon-call-arrow-in` for one they received, and `icon-call-cross` for one its
own caller dropped. That is why `Declined` resolves per side even though both sides read the
same words.

`Ended` is drawn one of two ways, depending on the session the call had:

- **A call that started its own session** gets the conversation card. It carries no caller, so
  both readers see the same neutral `icon-phone-call`. An arrow there would be a guess.
- **A call that rang into an ongoing session** leaves that session's block an ordinary one (see
  [Incoming call flow](./incoming-call-flow.md#a-call-into-an-ongoing-session)). There is no call
  card around it, so the entry gets the small card itself, with the arrow and the talk time.

Duration appears only on `Ended`. A ring lasts at most `Constants.Call.RingTimeout` (20s), so a
duration on a failed call would be noise rather than information. The entry carries its own: an
answered call's entry spans the talk time, `BeginsAt` at the answer and `EndsAt` at the end, so
`ChatEntry.Duration` is the call's length. The conversation card uses its own `StartsAt`/`EndsAt`
instead, which give the same span.

## The model

```csharp
public enum CallOutcome { None = 0, NoAnswer = 1, Declined = 2, Canceled = 3, Ended = 4 }

[DataContract, MessagePackObject]
public sealed partial record CallEntry : SystemEntry
{
    [DataMember, Key(20)] public AuthorId CallerId { get; init; } = null!;
    [DataMember, Key(21)] public string CallerName { get; init; } = "";
    [DataMember, Key(22)] public CallOutcome Outcome { get; init; }
    [DataMember, Key(23)] public ApiArray<AuthorId> InviteeIds { get; init; }
    [DataMember, Key(24)] public bool HasVideo { get; init; }
}
```

**One type with an enum, not a record per outcome.** `MembersChangedEntry` set the
precedent — joined and left in one type — and the cost difference is real: a future outcome
costs an enum value, where a new record would cost a union tag, a row in the version table
and a compatibility event.

`CallerName` is a fallback, not the display name. `MentionResolver.Enrich` resolves the
author and uses the stored name only when it cannot, which is what keeps a renamed author's
old entries from showing a stale name.

Union tags: `101` on `ChatEntry`, `3` on `SystemEntry`. 101 sits in the system range
100..199 deliberately — see [Forward-compatible unions](../architecture/union-tolerance.md).

### On disk

The row is stored in the pre-existing `LegacySystemEntry` envelope rather than a new
column, so existing rows deserialize without a migration. `LegacyCallOption` is the arm that
carries a call. That envelope is a database format only — it never reaches the wire.

## Write path

The outcome is recorded where it is known and written where the session ends, and those are
deliberately different places.

**Recording** happens in `LiveSessionsBackend` at each terminal response — `DeclineCall`,
`CancelCall`, and `ExpireRings` when a ring times out — through `SetOutcome`, under
`_changeLocks`. The outcome lives on the chat's `LiveCall`, and it is **first-writer-wins**:
the earliest terminal response is the call's story, and nothing later may overwrite it, so a
decline outranks a sibling invitee's ring expiring into `NoAnswer` afterwards. An answer
counts as a writer too: once `AnsweredAt` is set, no outcome is recorded, and once an outcome
is set, `AcceptCall` refuses.

**Writing** happens once, in `EndCall`, the single funnel every end of a call reaches — a
hang-up, a decline, a ring expiry, a party check, the self-heal that notices a call nobody is
in any more, or the backstop close of the session the call started. Several of those can
decide the same call is over at the same instant, so the funnel opens with an atomic claim:
dropping the call's key from Redis returns whether this caller is the one that removed it,
and only the winner writes.

The claim is taken **under `_changeLocks`**, the same lock every write of that key takes,
and the call it writes the entry from is re-read inside that lock. Both matter: taken
outside it, a write whose read-modify-write straddled the claim would put the key back, and
the resurrected call would be ended — and recorded — a second time; and the snapshot the
caller read before the lock can miss an outcome a racing hang-up just recorded, which
dropped the entry entirely.

Whether a call counts as finished is decided by whether it was answered, not by the recorded
outcome. `CancelCall` is also how a caller hangs up a call that *was* answered — there it is a
party leaving — so an answered call writes `Ended` whichever button ended it.

The teardown then drops the call's invites and invalidates everything derived from them. The
session - whichever started it - keeps its participants: it outlives the call and closes once
nobody records, taking its participant map along then. In a peer chat that is at once, as the
call's end stops both clients' media (see
[Incoming call flow](./incoming-call-flow.md#when-the-call-ends-and-its-session)).

### The tail the close can't see

A transcript's entry is created on its **first non-empty result**, and a call closes the instant it
empties, with none of the grace an ambient session gets. So the last utterance routinely gets its id
after the range was already fixed, and lands outside the card as a loose message.

`CallTailFlow`, scheduled from the close and keyed by the conversation, re-reads the tail a few
seconds later and pulls in every entry whose speech started before the call ended. That timestamp is
the test, not `HasAudio`: a message written after the hang-up begins after it, while a finalized
transcript drops its `Audio` when the media never saved. An entry that fails the test but sits before
one that passes is swallowed — a range is contiguous, so the alternative isn't a cleaner range, it's
leaving the transcript outside.

The same pass re-sizes the conversation, which is the other half of what it's for. Finalization waits
out the offline refine pass, so at the close the entries' content is usually still empty:
`OnMaterialize` counts zero words, `ScheduleCallRefresh` drops the call below its threshold, and the
call never gets the summary that refresh is its only source of. The flow re-materializes once nothing
in the range is streaming any more — or, failing that, once `StreamingEntryFixupFlow` has had its
chance — so the counting happens over text that is actually there.

Hence the `CallEntry` is no longer the last row of a grown conversation. The invariant the render
path needs is that the range **covers** it, not that it ends on it.

## Render path

`ChatEntryMessageView` routes every `CallEntry` it is given to its own card. `ChatUI.Tiles`
skips building a visible message for an `Ended` entry that a call conversation's lid range
covers, but keeps it in the pipeline, because for a transcription-off call it is the only entry
inside that range — the thing the card hangs on. An `Ended` entry no call conversation covers
is one that rang into an ongoing session, and it is shown.

The conversation carries `CallerId`, set once when the answer starts the call's session — not at
close, where `Host` may already have been handed to another participant by `ReassignHost`. It is
the only stored mark of a call: `Conversation.IsCall` derives from it, and the card reads both,
the flag to swap in the call icon and label, the caller to say which way the call went. A
session the call rang into never gets one, so its block stays an ordinary conversation.

While it lasts, a call is summarized like any live session: `LiveConversationSummaryFlow` runs
on it with the same gates. Only its finish differs - the flow's `Finalize` pass is for an
ambient session alone, so a call's last stretch is summarized after the close instead, by the
`ConversationRefreshFlow` that `ScheduleCallRefresh` sets up at materialization (#5052 is about
unifying the two). Two consequences follow:

- **It is sized at materialization.** `ConversationsBackend.OnMaterialize` counts
  the entries and words in the range and applies `SummarizationSettings.IsExpandedByDefault`
  — the same rule, from the same thresholds, that the summary flow applies to an ordinary
  conversation. A short call materializes expanded, a long one collapsed. The count at the
  close is provisional, though; `CallTailFlow` redoes it once the transcripts are final.
- **A call with no transcript is collapsed whatever the rule says**, and its card drops both
  the expand toggle and the details link. There is nothing behind either: expanding reveals
  an empty block, and there is nothing to summarize. The footer still says "0 messages" — that
  count is the reason the toggle is gone, so hiding it would only make the card look broken.

Conversation validation is skipped for a call — it has neither the summarizer's three texts
nor any message of its own to count.

## Localization

Twelve keys, `SystemEntry_Call*` for the entry's own text and `Call_Entry_*` for the card.
Every card title is a noun phrase — *Missed call*, *Canceled call*, and for a finished one
*Incoming call* / *Outgoing call*, which reuses the key the unanswered outgoing card already
had. Bare **Call** is left for the outcome no arm matches: a row from a newer server.

## Compatibility

Covered by [Forward-compatible unions](../architecture/union-tolerance.md). In short: a peer
at 2.20+ degrades an unknown entry to a placeholder; a peer at or below `2.19.9999` is
served by filtering twins and never receives one; a server rolled back past the release
reads the row as the same placeholder rather than throwing.

One additive migration: `caller_id` on `conversations`, `defaultValue: ""`. A conversation
written by an older build reads back with no caller and so is not a call — which is right,
since only this build writes one.

## Known gaps

- Client-side expansion is an override relative to a default, and a participant's client
  latches that default as `false` when the live block appears. A long call should
  materialize collapsed, but a participant's latched override may keep it expanded.
- The window in `EndCall` between the claim and the teardown spans two database round trips.
  A call placed inside it keeps its invites.
- An ambient session's close has a race of its own, and nothing catches it there.
  `LiveConversationSummaryFlow.Finalize` takes `entries[^1].LocalId` at the moment it runs, so a
  transcript that lands after it is outside the conversation for good. It bites far less often —
  that close goes through a long grace, where a call's waits at most `CallLeaveGrace` (2 s) —
  which is why `CallTailFlow` is keyed
  to calls rather than to every materialized live session.
