---
title: Chat view — four reported list defects
description: Blank transcript, a sticky edge latched to the top, live-session messages that never render, and a ~10Hz skeleton flicker.
---

# Chat view — four reported list defects

Four issues reported against the chat transcript (`InfiniteList`). Three of them
share one data-side root — a transient `HasVeryLastItem == false` during a live
session — amplified by three different client behaviours. The fourth is
independent.

Source: team chat, 2026-08-31. Analysed twice, independently; where the two
passes disagreed the disagreement is recorded below.

## The reports

- **A — "May appear blank."** The transcript area is entirely empty — no messages
  and **no skeletons** — while the chat's own overlays (the scroll-to-end button
  and its unread badge, showing 2) still render.
- **B — the sticky edge latches to the top.** "When you're in a new chat & start
  sending texts, it seems it concludes that sticky edge is the top one in the
  beginning, and it doesn't change that for a while, even if there is >1 screen of
  messages (so you can't see the bottom ones when they appear). Maybe this is
  [only] if there is a live session."
- **C — new messages in a live session are not picked up.** "Behaves like if they
  aren't there… happens when you've already joined it (i.e. the block is
  expanded), and I saw it only at the very beginning (< 5 messages in this
  session)."
- **D — ~10 Hz flicker.** During a live conversation, a *second* account opening
  the chat sees the view alternate about ten times a second between skeletons and
  the conversation block. Scrolling up slightly settles it.

## A — a 0-item result that claims both ends are loaded

`ChatView.GetData` substitutes a welcome block only when `ChatUI.IsEmpty`
(`ChatView.razor.cs:537`). A **non-empty** chat that produced no items falls
through to `ChatView.razor.cs:644` and publishes `Items = []` with
`HasVeryFirstItem = true` and `HasVeryLastItem = true`.

Two paths produce `(0 items, hasBefore = false, hasAfter = false)` for a chat that
does have entries:

1. **The hidden live tail swallows the whole window.** For a viewer not in the
   live session `hiddenLiveTailRange` is `[start, long.MaxValue)`
   (`ChatUI.Tiles.cs:398-406`) and every `HasAudio` entry inside it is dropped in
   `GetTile` (`ChatUI.Tiles.cs:1016`). In a chat whose only entries are the
   utterances of a just-started session — and before the conversation record
   exists, since the card is emitted only from persisted conversations
   (`ChatUI.Tiles.cs:1058`) — the item list comes out empty while
   `TryGetIdTilesToLoad` reports no further meta tiles either
   (`ChatUI.Tiles.cs:911`). "3 online", unread 2, nothing rendered: the screenshot.
2. **Empty range-meta.** `chatRangeMetaList.Count == 0` returns
   `ChatItems.Empty = ([], false, false)` outright (`ChatUI.Tiles.cs:505`).

That single value disables every recovery path at once:

- `buildDataQuery` returns `null` immediately (`infinite-list.ts:421-424`) — **the
  list never asks for data again**;
- both spacers go to size 0 and `display: none` (`infinite-list.ts:955-963`), so
  there are **no skeletons** — and nothing for the `SkeletonRetryMs` "query
  produced nothing" retry to observe;
- `checkPosition` (`infinite-list.ts:2208`) and `repinIfStranded`
  (`infinite-list.ts:2175`) both return early on `items.length === 0`;
- `ShouldRender`'s "nothing visible" clause is off too, because `HasAllItems` is
  true (`VirtualList.cs:149`);
- `isContentPlaced()` (`infinite-list.ts:2293`) returns
  `hasVeryFirstItem && hasVeryLastItem` — true — so `reveal()` un-hides the
  wrapper over nothing.

The blank persists until an unrelated server-side invalidation. Scrolling cannot
help: there is nothing to scroll to, and the band is `min == max`.

## B — the start-edge predicate has no lower bound

`updatePinnedEdge` (`infinite-list.ts:1081-1101`):

```ts
const isAtEnd = rs.hasVeryLastItem
    && (this.isChainWithinViewport || (this.distanceToEndEdge() ?? Infinity) <= EdgeEpsilon);
const isAtStart = rs.hasVeryFirstItem && this.distanceToStartEdge() <= EdgeEpsilon;
this.setPinnedEdge(isAtEnd && (this.defaultEdge === End || !isAtStart) ? End : isAtStart ? Start : null);
```

`distanceToStartEdge()` (`infinite-list.ts:1386`) is `viewTop - firstTop`, tested
with `<= EdgeEpsilon` and no lower bound. A chat that fits the viewport rests with
its chain at the top (`computeScrollLimits` caps `max` at `chainStart`,
`infinite-list.ts:1460`; same cap in `measureEdgeTarget`, `:1172`), and content
shorter than the viewport hangs *below* the top — a large negative distance. Either
way `isAtStart` reads true.

It is masked only while `isAtEnd` is also true and `defaultEdge === End`
(`ChatView.razor:76`) wins the tie. Two things unmask it:

1. **`hasVeryLastItem` transiently false** — the live-session flap of D. `isAtEnd`
   is gated on it directly.
2. **The growth race.** The chain outgrows `clientHeight + ChainFittingExitPx`
   (64px), `updateChainFitting` clears `isChainWithinViewport`
   (`infinite-list.ts:967`), and the DOM-measured `distanceToEndEdge()` is now more
   than 4px because the anchor sits below the fold. Any scroll settle re-derives
   the pin at exactly that geometry — and the settle is armed by *every* scroll,
   the list's own included: `turnOffIsScrollingDebounced()` runs at
   `infinite-list.ts:1674`, **before** the `isTrusted` and programmatic-guard
   returns at `:1676-1681`.

**Why it sticks.** Once pinned Start, `applyRenderIntent` (`infinite-list.ts:1015`)
calls `repinEdge('render')` on every render and holds the first item flush with the
viewport top (`measureEdgeTarget(Start)`, `:1152`). Every arriving message lands
below the fold, and every re-derivation from that held position finds
`isAtStart` true again and **re-latches Start**. It is self-sustaining, not merely
un-refreshed.

**Verified** against a running list (`p-dPl6bk-testbot129`, real geometry
`dStart = -485`, `dEnd = 0`):

| `hasVeryLastItem` | chain fits | decision |
|---|---|---|
| true | true | End |
| true | false | End |
| **false** | true | **Start** |
| **false** | false | **Start** |

## C — the live block's frozen tail hides entries the viewer is still watching

**The fold governor is ruled out.** `LiveFoldMath.MinTailEntryCount = 10`
(`LiveFoldMath.cs:7`), and `GetTailFloorLid` returns `visibleStartLid` — no fold at
all — whenever the session has fewer than 10 real entries
(`LiveBlockUI.cs:233-255`). At "< 5 messages" the fold range is provably empty, so
folding cannot be swallowing anything.

Two candidates remain:

1. **The hidden-tail freeze while `amInLive` reads false.**
   `LiveBlockUI.DeriveOverlay` (`LiveBlockUI.cs:192`) freezes the block behind
   `HiddenTailRange = [TailStart, long.MaxValue)` whenever
   `WasAttending && !amInLive`, and `ChatUI.Tiles.cs:1016` then drops every spoken
   entry in that range. `amInLive` is derived from *local* audio state —
   `IsListening || IsRecording || IsWatching` (`LiveSessionUI.cs:147-154`) — so a
   transient false during the shaky first moments of a session (pipeline restart,
   the join transition) freezes the block while the viewer is still watching it.
   The expanded-block escape at `ChatUI.Tiles.cs:500` only covers a *manual*
   expand, not the ordinary joined view.
2. **B, seen from the other end.** At the start of a session the chat is short —
   exactly B's transition zone. A pin latched to Start renders new streaming
   entries below the fold, which looks identical to "they aren't there".

Both passes agree these are the candidates and that neither is confirmed from code
alone. Distinguishing them needs one runtime observation with
`debugUI.showVirtualListOverlay(true)`: if the entries are absent from the DOM it
is (1); if they are in the DOM but below the fold with the pin on `↑` it is (2).

## D — the end spacer, and a follow that chases the anchor into it

The skeletons live *inside* the end spacer, whose size is purely
`hasVeryLastItem ? 0 : SpacerSize`, and `SpacerSize` is **1500px**
(`infinite-list.ts:961`, `ChatView.razor:81`). The end anchor sits *after* the
spacer.

While the list is pinned End, every render runs an inline follow (`applyLayout`,
`infinite-list.ts:834` → `measureFollow`/`applyFollow`, `:1218-1260`) that measures
the **end anchor** in the DOM (`measureEdgeTarget`, `:1158`). So a render that
flips `hasVeryLastItem` to false moves the anchor 1500px down and the follow
faithfully scrolls the viewport down into the skeleton spacer; the next render
flips it back and the follow returns to the card.

The scroll limits permit it: with `hasVeryLastItem` false,
`max = chainEnd + maxOverscroll - clientHeight` (`infinite-list.ts:1455`), and
`maxOverscroll` is 3 screens — more than the 1500px spacer. (With it true, `max`
is `chainEnd + endAnchorSize - clientHeight` and the spacer is 0.)

The oscillation is invisible to the pin logic because follow echoes are dropped
before anything is derived from them (`isFollowEcho`, `infinite-list.ts:1663`), so
the End pin — and the oscillation — sustains itself. The rate is `ChatView`'s
recompute cadence: `FastUpdateDelay = 20ms` / `SlowUpdateDelay = 100ms`
(`ChatView.razor.cs:16-18`) plus build time ⇒ ~10Hz.

**Why scrolling up fixes it.** One trusted scroll re-derives the pin
(`infinite-list.ts:1684`); away from the end it becomes `null`, an unpinned reverse
list is top-anchored (`writeChainPosition`, `:944`), and the spacer toggling then
happens entirely below the fold while `reanchor` (`:704`) holds the reader's row
still.

**What flips `hasVeryLastItem`** is not settled. Two candidate engines, both with a
built-in two-phase alternation at the invalidation rate:

1. **Stand-in ↔ fresh rebuild pairs.** `UseRangeMetaOrLastKnown`
   (`ChatUI.Tiles.cs:1537`) and `UseOrLastKnown` (`:1585`) serve last-known tiles
   while a refetch is in flight, and guarantee a follow-up rebuild from fresh
   values. If the two builds disagree about `hasMoreAfter` (`:913`), you get
   A/B/A/B for as long as entries stream.
2. **The visibility-anchored re-query.** The no-query rebuild sticks to the visible
   range (`ChatView.razor.cs:700-707`), and item visibility is itself a product of
   the previous render (250ms throttle). A render showing skeletons empties the
   visible set → the next query is anchored differently → and back.

The overlay settles this in one observation: the end bracket flips square `]` ↔
round `)` on each flap.

## Shared root

Two structural defects sit under all four:

1. **An "empty but complete" data result is treated as authoritative.** Zero items
   plus `HasVeryFirstItem` plus `HasVeryLastItem` is indistinguishable from a
   genuinely empty chat, and it strips the spacers, the query, the retry and both
   position guards in one step.
2. **The edge predicates are one-sided, and re-derived from geometry the list
   itself just produced.** `distance <= eps` where `|distance| <= eps` was meant,
   and a settle armed by the list's own programmatic scroll.

## Fixes

Being settled; this section is filled in as each lands.
