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

`Ended` is the exception: its card is built from the conversation, which carries no caller,
so both readers see the same neutral `icon-phone-call`. An arrow there would be a guess.

Duration appears only on `Ended`, where the conversation supplies it. A ring lasts at most
`Constants.Call.RingTimeout` (20s), so a duration on a failed call would be noise rather
than information.

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
100..199 deliberately — see [Forward-compatible unions](./architecture/union-tolerance.md).

### On disk

The row is stored in the pre-existing `LegacySystemEntry` envelope rather than a new
column, so existing rows deserialize without a migration. `LegacyCallOption` is the arm that
carries a call. That envelope is a database format only — it never reaches the wire.

## Write path

The outcome is recorded where it is known and written where the session ends, and those are
deliberately different places.

**Recording** happens in `LiveSessionsBackend` at each terminal response — `DeclineCall`,
`CancelCall`, and `ExpireRings` when a ring times out — through `SetOutcome`, under
`_changeLocks`. It is **first-writer-wins**: the earliest terminal response is the call's
story, and nothing later may overwrite it, so a decline outranks a sibling invitee's ring
expiring into `NoAnswer` afterwards.

**Writing** happens once, in `CloseAndMaterialize`, the single funnel every close path
reaches — a hang-up, a ring expiry, the session finalizer behind the summary flow, or the
backstop self-close. Several of those can decide the same call is over at the same instant,
so the funnel opens with an atomic claim: dropping the session key from Redis returns
whether this caller is the one that removed it, and only the winner writes.

The claim is taken **under `_changeLocks`**, the same lock every write of that key takes,
and the state it writes the entry from is re-read inside that lock. Both matter: taken
outside it, a write whose read-modify-write straddled the claim would put the key back, and
the resurrected session would be closed — and recorded — a second time; and the snapshot the
caller read before the lock can miss an outcome a racing hang-up just recorded, which
dropped the entry entirely.

Whether a call counts as finished is decided by `SessionStartedAt`, not by the recorded
outcome. `CancelCall` is also how a caller hangs up a call that *did* connect, so a session
that ever latched writes `Ended` whichever button ended it.

`Close` then tears the session down. It drops the participant map and invalidates
everything derived from it — a caller is registered as a recorder the moment they dial, and
a stale `HasRecorder` reads as someone talking in the chat list long after the call is over.

## Render path

`ChatEntryMessageView` routes a `CallEntry` to its own card **unless** the outcome is
`Ended`; `ChatUI.Tiles` skips building a visible message for an `Ended` entry but keeps it
in the pipeline, because for a transcription-off call it is the only entry inside the
conversation's lid range — the thing the card hangs on.

The conversation carries `CallerId`, set once when the call is dialled — not at close, where
`Host` may already have been handed to another participant by `ReassignHost`. It is the only
stored mark of a call: `Conversation.IsCall` derives from it, and the card reads both, the
flag to swap in the call icon and label, the caller to say which way the call went.

Two consequences follow from a call never reaching the summary flow:

- **It is sized at materialization instead.** `ConversationsBackend.OnMaterialize` counts
  the entries and words in the range and applies `SummarizationSettings.IsExpandedByDefault`
  — the same rule, from the same thresholds, that the summary flow applies to an ordinary
  conversation. A short call materializes expanded, a long one collapsed.
- **A call with no transcript is collapsed whatever the rule says**, and its card drops both
  the expand toggle and the details link. There is nothing behind either: expanding reveals
  an empty block, and a call is never summarized. The footer still says "0 messages" — that
  count is the reason the toggle is gone, so hiding it would only make the card look broken.

Conversation validation is skipped for a call — it has neither the summarizer's three texts
nor any message of its own to count.

## Localization

Twelve keys, `SystemEntry_Call*` for the entry's own text and `Call_Entry_*` for the card.
Every card title is a noun phrase — *Missed call*, *Canceled call*, and for a finished one
*Incoming call* / *Outgoing call*, which reuses the key the unanswered outgoing card already
had. Bare **Call** is left for the outcome no arm matches: a row from a newer server.

## Compatibility

Covered by [Forward-compatible unions](./architecture/union-tolerance.md). In short: a peer
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
- The window in `CloseAndMaterialize` between the claim and the teardown now spans two
  database round trips. A `StartCall` landing inside it creates a session the closer then
  deletes.
- A caller who hangs up an *answered* call does not end it for the other party — `CloseNow`
  declines to close while the invitee is still a fresh recorder.
