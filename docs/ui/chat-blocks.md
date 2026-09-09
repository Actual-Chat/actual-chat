# Chat block coverage

`ChatUI` projects completed conversations and live blocks into `ChatBlock`, a UI view of an existing
`Conversation`. It carries the render identity, full entry-ID coverage, effective expansion state,
live/completed kind, and a compatibility `CollapsedAt` value. This is client-side presentation data,
not another persisted conversation entity or a new RPC contract.

## Coverage and visibility

A collapsed block crossing a requested window boundary must be included as a whole. The same rule
applies to live and completed blocks, even when a live block still has visible typed messages below
its card. Expanded blocks remain virtualized normally.

Coverage does not mean every entry must render. The existing live fold governor, hidden-transcript
filter, and thread/text rules still determine which loaded entries are visible. A fully collapsed
completed conversation contributes one representative card ID; its hidden interior is not fetched
as individual entry tiles by the selector.
An expanded active live block still excludes its governed fold, matching the auto-swallow behavior.
An expanded materialized block excludes nothing. Neither receives collapsed-boundary expansion.

The distinction matters for a long-running live block: its full current coverage can include many
visible messages. Normalizing to that coverage can load a large tail. This implementation favors the
complete-block coverage rule; it does not add separate pagination inside a collapsed live block.

## Resolving blocks

`ChatUI.GetChatBlocks` combines range metadata with the conversation tiles already loaded for the
request. Missing conversation records use the existing `IConversations.Get` compute method. Records
that are unavailable do not become fabricated cards.

`BuildChatBlocks` uses metadata coverage for completed conversations and the finite current block
coverage for a live block. The latter can extend beyond the persisted conversation's last summarized
entry. An active block ends at the current chat snapshot; a frozen block is also bounded by its
overlay end. A block with no entries can still cover its own card ID.

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
becomes `[50, 100), [100, max)`. A later completed conversation cannot truncate an active live block.
An already materialized block is finite and follows the ordinary later-start rule.

`ConversationRangeExt.IsOpenEnded` is an extension property on the existing `Range<long>` type.
`MergeConversationRanges` selects the earliest open-ended start, drops candidates starting after it,
and delegates the remaining finite overlap normalization to Core's `RangeExt.TruncateOverlaps`.
Duplicate starts retain the largest end. If stale input contains several open-ended candidates, the
first one in start order remains unchanged; normal backend input has only one active live range.
The live/materialized identity is resolved before UI normalization so it represents one block.

`ConversationsBackend.GetConversationRangeTile` normalizes intersecting ranges together with the nearest previous
and next candidates before deciding which ranges intersect the tile. Including the previous candidate
prevents an old block's abandoned suffix from reappearing in a later tile. Including the next candidate
truncates an earlier finite block even when that next block begins outside the current tile.

`ConversationRangeTile.ApplyTo` applies finite effective ends to fetched conversation copies. For an
open-ended range it retains the record's actual end instead of copying the sentinel into message
coverage. Cached open-ended metadata remains valid as the live summary grows, so the previous
`WithLiveRange` refresh workaround is unnecessary. The UI bounds active block coverage by its current
chat/overlay snapshot before expanding fetch boundaries; no fetch enumerates an open-ended range.
Active live folding and transcript filtering are not truncated by later completed ranges. Materialized
block filtering still stops at a later block's start, including when that record is unavailable.
Navigation expansion, automatic expansion over witnessed messages, and witness filtering use the same
truncated ranges, so a stale old conversation cannot expand merely because the viewer saw newer live rows.

These are read projections only. Stored IDs, summaries, timestamps, counts, and conversation creation
or replacement commands are unchanged. In particular, the existing write path can still delete
overlapping persisted records; revisiting those building rules is separate work.

## Wire compatibility

`ConversationRangeTile` replaces `ConversationRangeMeta`. Its range properties are `ConversationRanges`,
`PreviousConversationRange`, and `NextConversationRange`. The four MessagePack positions stay unchanged,
and ranges still use the existing `[start, end]` formatter. Named serializers use the new property names;
this backend metadata type has no legacy naming aliases. The client-facing `ChatRangeMeta` names are unchanged.

`IConversationsBackend` has no legacy methods or aliases: backend services upgrade together.
Internal consumers call `GetConversationRangeTile` and use its open-ended metadata directly.
Conversation API parameters use `start` and `range` where the tile kind is clear. Mixed entry and
conversation code uses `cidTile`, `cidTileStart`, or `cidTileRange` for conversation tiles, whose layer
differs from the small entry-tile layer.

`IChats.GetChatRangeMeta` keeps its existing finite client contract. Its backend page-map builder
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

`CollapsedAt` is introduced as metadata only. No visibility predicate reads it, collapse clicks do not
update it, and it is not persisted or synchronized between viewers.

- Live block: `Conversation.StartsAt`, at or before the first message represented by the block.
- Completed conversation: `Conversation.EndsAt` plus one tick, saturating at `Moment.MaxValue`.

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
