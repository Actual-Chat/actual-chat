# Chat block coverage

`ChatUI` projects completed conversations and live blocks into `ChatBlock`, a UI view of an existing
`Conversation`. It carries the render identity, full entry-ID coverage, and a nullable `CollapsedAt`.
`IsExpanded` derives from `CollapsedAt == null`. This is client-side presentation data,
not another persisted conversation entity or a new RPC contract.

`ChatBlock` does not carry a lifecycle flag. Open/closed state belongs to `LiveBlock`; the view's
materialized identity already distinguishes retained live coverage from persisted coverage for fetching.
Expanded blocks have no cutoff. For collapsed blocks, the builder sets `CollapsedAt` just after the last
message for completed/materialized blocks, or to the start for open/dissolving blocks. These compatibility
cutoffs preserve which entries currently show; they are not recorded collapse-action timestamps.

## Coverage and visibility

A collapsed block crossing a requested window boundary must be included as a whole. The same rule
applies to live and completed blocks, even when a live block still has visible typed messages below
its card. Expanded blocks remain virtualized normally.

Coverage does not mean every entry must render. The existing live fold governor, hidden-transcript
filter, and thread/text rules still determine which loaded entries are visible. A fully collapsed
completed conversation contributes one representative card ID; its hidden interior is not fetched
as individual entry tiles by the selector.
An expanded open live block still excludes its governed fold, matching the auto-swallow behavior.
An expanded materialized block excludes nothing. Neither receives collapsed-boundary expansion.

The distinction matters for a long-running live block: its full current coverage can include many
visible messages. Normalizing to that coverage can load a large tail. This implementation favors the
complete-block coverage rule; it does not add separate pagination inside a collapsed live block.
Before the first summary, or while summarization lags, this can fetch most of the call. The query test
locks down one example: coverage `[100,250)` with fold `[100,120)` loads 27 five-ID tiles, including
the card, even for a request near 230. Typed messages interleaved with hidden transcripts still render.

## Resolving blocks

`ChatUI.GetChatBlocks` combines range metadata with the conversation tiles already loaded for the
request. Missing conversation records use the existing `IConversations.Get` compute method. Records
that are unavailable do not become fabricated cards.

`BuildChatBlocks` uses metadata coverage for completed conversations and the finite current block
coverage for a live block. The latter can extend beyond the persisted conversation's last summarized
entry. An open block ends at the current chat snapshot; a closed block is also bounded by its
`ClosedLiveBlock.EndLid`. A block with no entries can still cover its own card ID.

The live block's render ID survives materialization. Its persisted conversation can have a different
ID because it includes earlier context. The projection resolves that alias before assigning coverage.
Both tile exclusion and boundary normalization consume this projection, so stale overlapping metadata
cannot independently exclude the live card's tile.

## Overlapping blocks

Among finite blocks, the block with the later **start entry ID** wins. The earlier block ends at that
start and never resumes. For example, `[50, 500)` followed by `[100, 120)` becomes `[50, 100)` and
`[100, 120)`. This ownership rule is independent of expansion state.

An open-ended range (`End == long.MaxValue`) always owns the remaining tail. Its start and end remain
unchanged, and it is the last range in the normalized sequence. Thus `[50, 300), [100, max), [200, 250)`
becomes `[50, 100), [100, max)`. A later completed conversation cannot truncate an open live block.
An already materialized block is finite and follows the ordinary later-start rule.
When a live block starts inside an older completed conversation, the older prefix remains a conversation
card with its own collapsed/expanded state; its rows are no longer forced to render as plain messages.

`ConversationRangeExt.IsOpenEnded` is an extension property on the existing `Range<long>` type.
Callers use Core's `RangeExt.TruncateOverlaps(mustKeepOpenEnded: true)` directly. It normalizes finite
overlaps and stops at the first open-ended range, leaving its boundaries unchanged.
Duplicate starts retain the largest end. If stale input contains several open-ended candidates, the
first one in start order remains unchanged; normal backend input has only one open live range.
The live/materialized identity is resolved before UI normalization so it represents one block.

`ConversationsBackend.GetConversationRangeTile` normalizes intersecting ranges together with the nearest previous
and next candidates before deciding which ranges intersect the tile. Including the previous candidate
prevents an old block's abandoned suffix from reappearing in a later tile. Including the next candidate
truncates an earlier finite block even when that next block begins outside the current tile.

`ConversationRangeTile.ApplyTo` applies finite effective ends to fetched conversation copies. For an
open-ended range it retains the record's actual end instead of copying the sentinel into message
coverage. Cached open-ended metadata remains valid as the live summary grows, so the previous
`WithLiveRange` refresh workaround is unnecessary. The UI bounds open block coverage by its current
chat snapshot or closed block end before expanding fetch boundaries; no fetch enumerates an open-ended range.
Grouping an open block separately uses an open-ended range so pending sends at or beyond
the chat end and the `long.MaxValue` transcription placeholder stay before its footer. This includes
a viewer who left while the session continues: their block remains an `OpenLiveBlock`.
Closed grouping remains bounded. Grouping coverage must not be reused for entry fetching.
Open live folding and transcript filtering are not truncated by later completed ranges. Materialized
block filtering still stops at a later block's start, including when that record is unavailable.
Navigation expansion, automatic expansion over witnessed messages, and witness filtering use the same
truncated ranges, so a stale old conversation cannot expand merely because the viewer saw newer live rows.

These are read projections only. Stored IDs, summaries, timestamps, counts, and conversation creation
or replacement commands are unchanged. In particular, the existing write path can still delete
overlapping persisted records; revisiting those building rules is separate work.

## Viewer-local live block lifecycle

`LiveSessionUI.GetBlockState` projects the seven session fields needed for block rendering into the
value-equality `LiveBlockState`. Participant and activity updates that leave those fields unchanged
do not invalidate this projection's consumers. `LiveBlockUI.GetBlock` combines it with the viewer's
attendance and fold state, returning one of these results:

| Result | Meaning | Lifetime |
| --- | --- | --- |
| `OpenLiveBlock` | A latched session still exists, whether the viewer is joined or has left. | Until the session closes. |
| `ClosedLiveBlock` with `MaterializedId` | An attended session closed with a summary. | Until dismissed, another chat is selected, or the session restarts. |
| `ClosedLiveBlock` without `MaterializedId` | An attended session closed without a persisted replacement. | The 300 ms dissolve interval. |
| `null` | No latched session or retained block for this viewer. | Until a block becomes available. |

Open/closed describe session lifetime; expanded/collapsed describe presentation. `HasAttended` includes
current attendance and remains true after leaving. Closed blocks always represent an attended session.
`ConversationId` retains the live render identity even when the materialized conversation has another ID.
`ClosedLiveBlock.EndLid` is exclusive, unlike `Conversation.EndEntryLid`.

The public `FoldEndLid` is the effective minimum of the governed boundary and any reveal boundary.
`FoldRange` derives from it and the conversation start. The governor and reveal bookkeeping remain
private; consumers cannot accidentally ignore an active reveal. A private `LiveBlockTemplate` captures
both chat and summary ends because an unsummarized dissolve retains the chat tail, while materialization
uses the summarized end.

Attendance and its retained descriptor are latched together before publishing an attended block,
including when a previously created viewer context joins and immediately leaves. Session and attendance
dependencies update `GetBlock` directly. Private changes that alter its result explicitly invalidate it
outside the state lock: reveal/reset, governor transitions, dissolve expiry, and closed-block dismissal.
The governor publishes its new fold only after completing private state changes. A changed fold's
reactive notification covers those changes; with an unchanged fold, it compares the projected result
and explicitly invalidates if needed. Reveal resets therefore still notify without a fold advance,
and fold advances do not also trigger an explicit invalidation.

Conversation and block-state projections consolidate independently. When the block state has no latch,
`GetBlock` also consults the conversation projection through its last-known helper. If no retained block
exists, an available conversation supplies the open block's identity; the private effective fold and
attendance (including current membership) still apply. Consumers use this result directly. Latched
block-state reads avoid the extra conversation dependency, preserving their resistance to transcript churn.

## Unsummarized close and dissolve

An attended session that closes without a summary has no persisted replacement conversation.
`LiveBlockUI` retains a `Conversation` descriptor in its private template before closure,
bounded to that snapshot's chat end. The dissolving `ClosedLiveBlock` exposes this descriptor until its
timer expires. The record is released from the template when the dissolve finishes.

Both `BuildChatBlocks` and the per-tile card builder use the retained descriptor, even if chat
summarization is disabled. A range alone cannot restore a missing card or header. This also keeps
the header's dissolve marker available after the server stops returning the live conversation.
During this interval its participant title remains visible and it offers no Join action.

`ConversationViewState` carries the descriptor in its tile cache key so entering and leaving dissolve
rebuild the relevant tiles. `NarrowTo` removes it from unrelated tiles. Its reference remains stable
through the dissolve interval. The tile scope includes the actual preceding message, which may be
farther back than the adjacent tile when entries were deleted. Author groups split at the descriptor's
finite end, keeping subsequent messages outside the block even when they come from the same author.

## Wire compatibility

`ConversationRangeTile` exposes `ConversationRanges`,
`PreviousConversationRange`, and `NextConversationRange`. The four MessagePack positions stay unchanged,
and ranges still use the existing `[start, end]` formatter. Named serializers use the new property names;
this backend type has no legacy naming aliases. `ChatRangeTile` and `ChatEntryRangeTile` likewise retain
their MessagePack positions. `ChatRangeTile.ConversationRanges` retains the previous JSON member name
through serializer attributes. The client method retains its old RPC name through `LegacyName`.

`IConversationsBackend` has no legacy methods or aliases: backend services upgrade together.
Internal consumers call `GetConversationRangeTile` and use its open-ended metadata directly.
Conversation API parameters use `start` and `range` where the tile kind is clear. Mixed entry and
conversation code uses `cidTile`, `cidTileStart`, or `cidTileRange` for conversation tiles, whose layer
differs from the small entry-tile layer.

`IChats.GetChatRangeTile` keeps its existing finite client contract. Its backend page-map builder
uses `ConversationRangeTile.ToFinite` with the builder's isolated chat-end snapshot. This retains at least
the card ID and reclassifies current/previous/next ranges after bounding them. All contributing tiles
use the same snapshot; historical metadata does not subscribe to every new message. No separate
legacy conversation-tile service or additional chat-end lookup is needed.

## Normalizing a request

`ChatDataQuery` uses **inclusive** ID endpoints. Conversation metadata and block coverage use
**half-open** ranges. A block `[100, 200)` expands an inclusive request `[130, 160]` to `[100, 199]`.
A request ending at 99 or starting at 200 does not intersect that block.

`ChatUI.TryGetIdTilesToLoad` performs these steps:

1. Build the sequence of loadable IDs, replacing fully collapsed conversation interiors with card IDs.
2. Apply the requested item offsets and retain the currently visible range.
3. Extend intersecting collapsed block coverage to whole block boundaries, until no boundary changes.
4. Require additional metadata if the expanded boundaries cross the loaded metadata span and there
   are predecessor/successor tiles. The existing metadata walk repeats projection and selection.
5. Select entry tiles and compute whether more content remains before or after the result.

Normalizing after offsets allows a scroll request to move completely past a block and unload its
card. Normalizing after visible-range retention prevents a retained hidden ID from losing its card.
The live block's coverage is never replaced by the unbounded hidden-tail sentinel.

## CollapsedAt compatibility defaults

`CollapsedAt == null` means expanded; `ChatBlock.IsExpanded` is derived rather than stored separately.
A non-null cutoff means collapsed, but its timestamp remains a compatibility default: entry filtering
does not yet compare message times against it. It is not persisted or synchronized between viewers.

- Expanded block: `null`.
- Collapsed open or dissolving block: `Conversation.StartsAt`, at or before its first message.
- Collapsed completed or materialized block: `Conversation.EndsAt` plus one tick, saturating at `Moment.MaxValue`.

These defaults express today's intended text behavior without enabling a collapse-time feature.
Implementing real per-viewer collapse cutoffs later requires explicit state and lifecycle semantics;
the current derived defaults must not be mistaken for a record of the user's last collapse action.

## Recovery and verification

Boundary normalization handles known collapsed coverage. It cannot manufacture an unavailable
conversation or repair a failed metadata fetch. The virtual list's existing unresolved-window
recovery remains as a fallback.

`ChatBlockQueryTest` exercises the production selector: interior/edge requests, adjacent windows,
expanded virtualization, offsets, visible coverage, metadata continuation, card-only loading, and
live tails beyond persisted coverage. `ChatBlockProjectionTest` covers authoritative ranges, live
ownership, stale overlap, materialized identity, and compatibility defaults. `ConversationRangeTileTest`
covers tile neighbors, finite projection, and legacy bytes. `ConversationRangeExtTest`
covers open-ended precedence, input ordering, duplicates, invalid ranges, and finite overlap controls.
Integration cases in `LiveConversationDisplayTest` cover both overlap orders, stable open-ended metadata,
finite client responses, and the distinction between descriptor boundaries and actual summary ends.
