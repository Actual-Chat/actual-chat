---
title: Chat view — reported list defects, and what testing turned up
description: Blank transcript, a sticky edge latched to the top, live-session messages that never render, a ~10Hz skeleton flicker, and the live block's expand/collapse.
---

# Chat view — reported list defects, and what testing turned up

Four issues reported against the chat transcript (`InfiniteList`), and four more
found while testing the fixes for them. Three of the reported four share one
data-side root — a transient `HasVeryLastItem == false` during a live session —
amplified by three different client behaviours; the fourth is independent. The rest
(E-H) came out of driving a real two-account live session with the list
instrumented, and are mostly about the live conversation block.

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

## E — a navigation aimed at a key the list can never reach

Found by live repro rather than by reading, and reported as "the bottom sticky
edge detached and I didn't scroll… I'm somewhat sure it happens when other items
expand". Both halves were right.

Every new non-streaming entry calls `NavigateTo(lastEntryLid)`
(`ChatView.razor.cs:414`), so a navigation per message is by design, and
`applyRenderIntent`'s fast path — `scrollToKey === getLastContentKey() &&
hasVeryLastItem` — exists precisely so that resolves to an edge re-pin instead of
a jump.

`GetData` resolved the target lid with a scan that did **not** exclude
`ShouldSkipKey` items, while every other consumer of "the last item" does:
`VirtualListData.GetLast` (`:76-83`), JS `getLastContentKey`, and the visibility
reporter, which never lists them. A target landing on a skip-key item is therefore
unsatisfiable in both directions at once:

- `scrollToKey !== getLastContentKey()` — the fast path is missed;
- `!visibleKeys.has(scrollToKey)` is permanently true — `getPendingJumpKey` keeps
  returning it.

So `applyRenderIntent` ran `setPinnedEdge(null)` plus a jump on **every render**,
with only the scroll settle re-pinning End in between. The transcript stopped
following new messages with no user input at all.

The trigger is an expanded conversation: the trailing `ConversationEnd` footer is
emitted only inside an `ExpandedConversationMessage` (`ChatUI.Tiles.cs:1468-1481`)
and both its variants are skip-key. Collapsed, there is no footer and everything
matches — which is exactly why it looked tied to expansion.

Captured on `the-actual-one`, six times in 23 seconds:

```
End -> null   via applyRenderIntent < applyRender < onRender
scrollToKey:    "4948-conversation-end"
lastContentKey: "4948"
skIsLast: false, skVisible: false, hvl: true, dEnd: 4, anim: false, scr: false
```

**Fix.** The nav scan applies the same skip-key rule as everything else, and
`getPendingJumpKey` refuses a skip-key target outright so no other producer can
reopen the loop. Measured on the same chat afterwards: 30 s of continuous sampling,
zero pin transitions, `scrollToKey == lastContentKey` in every sample, `dEnd = 0`
throughout.

This is a **second, independent cause of B's user-visible symptom** — it lives in
`applyRenderIntent`, which neither of B's fixes touches — and it is why B read as
intermittent: the settle re-pins afterwards, so it self-heals whenever one happens
to run.

## F — the live block's expansion was keyed on the wrong things

Three defects in one area, found by testing the fixes above rather than reported.

**Collapsing did nothing once you had joined.** Both halves that implement "collapsed"
were keyed on whether the viewer was in the call rather than on whether the block
renders collapsed: `hiddenLiveTailRange` (`ChatUI.Tiles.cs:400`) only hid the tail
for a viewer who was *not* joined, and the card's tail preview was gated on
`!isJoined` outright (`ConversationMessageView.razor:239`). Neither had a reason to
be — and the swallowed-count gate right beside the preview already reads
`isJoined && !isExpanded` (`:243`), so joined-and-collapsed was always meant to
exist and to hide its entries behind the card. Its "show more" button simply had
nothing to reveal.

**Nothing expanded the block on join.** The only "ensure expanded" in the codebase
is on the navigate-to-conversation path (`ChatUI.Tiles.cs:461`), and
`IsExpandedByDefault` is not about joining at all —
`LiveConversationSummaryFlow.cs:107` sets it from
`words < Settings.Summarization.MinConversationWords`. A grown block is therefore
collapsed by default and joining left it that way. Harmless while a joined viewer
saw the tail unconditionally; once collapsing worked, joining an already-summarized
conversation put the whole thing behind a card.

**And the effective state inverted under everyone.** Expansion is
`IsExpandedByDefault ^ overrides.Contains(id)`, and that default is server-derived
and *moves*: it flips when a summary lands. A flip inverts the effective state of
every conversation without an override — an auto-collapse nobody asked for. The
live block's id moves too (observed `4949 → 4991 → 4992 → 5022 → 5031 → 5060`
within one call, `LiveSessionState.cs:63-66`), so anything keyed to the id at a
moment in time is discarded at the next latch — a reader's own collapse included.

## G — collapsing a conversation moved the whole view

Measured on a real collapse, three times identically: the click removes 17 items,
the list is pinned to End, and `applyFollow` writes **+205, +94, +18** over ~380 ms
to keep the end flush — carrying everything the reader is looking at up with it, in
three lurches.

Nothing held the position, even though the mechanism and the intent were both
already there: `ConversationMessageHeader.razor` carries `data-vl-anchor` and
documents that *"the list holds this element's on-screen position across the render
the toggle triggers"*. But every toggle asked for `data-vl-hold="keep-edge"` in its
expanded form — i.e. exactly on the collapse — and `onInteractiveEvent` returns on
that **before it ever reads the anchor** when the list is pinned. The reasoning
behind that early return ("a pinned list absorbs the size change through its edge
re-pin") holds for content arriving *below* the reader; it is false for content
vanishing *around* them, where the re-pin is precisely what moves the view.

## H — a collapsed live block swallowed everything below it

The live block spans `[V, ∞)`, which is right while it is expanded and wrong once it
is collapsed: a collapsed block stands in for the rows it covers, so it must be a
single item ending where it begins. It wasn't — the entries below it were grouped
*inside* it and rendered as ordinary messages, with their own menus and list keys,
next to the card that was supposed to be standing in for them.

Its existence still has to shape the rest of the list, though: transcribed entries
in its range stay filtered out (`ChatUI.Tiles.cs:1058`, per entry and only for
`HasAudio`), while typed messages — which are never hidden, deliberately
(`:1036`) — render as ordinary messages after it.

## Shared root

Three structural defects sit under these:

1. **An "empty but complete" data result is treated as authoritative.** Zero items
   plus `HasVeryFirstItem` plus `HasVeryLastItem` is indistinguishable from a
   genuinely empty chat, and it strips the spacers, the query, the retry and both
   position guards in one step.
2. **The edge predicates are one-sided, and re-derived from geometry the list
   itself just produced.** `distance <= eps` where `|distance| <= eps` was meant,
   and a settle armed by the list's own programmatic scroll.
3. **"The last item" is defined twice and the definitions disagree.** Everything
   the list does excludes skip-key items; the navigation scan did not, which is
   enough to make a target permanently unreachable. Any other place deriving a key
   or index without that rule will produce the same signature.

## Fixes

### A — never publish an unresolved window as a loaded one

`ChatView.GetData`: a chat that has entries but produced no items now claims
neither end, and logs a warning naming the query. The list keeps its spacers, so
skeletons render, `buildDataQuery` keeps asking, and the retry stays armed. The
genuinely-empty chat still takes the `IsEmpty` → Welcome branch as before.

### B — the pin must not read a resting position as a reader's decision

Two changes, covering the two triggers; neither alone covers both.

- `isAtStart` is gated on `hasVeryLastItem` — except for a Start-default list, which
  must reach its own edge with the far end unloaded, exactly as the chat pins End
  with history above it unloaded. This kills the flap-driven latch.
- `turnOffIsScrolling` skips the pin re-derivation while `stability.isAnimating`.
  That settle is armed by *every* scroll, the list's own re-placements included
  (`turnOffIsScrollingDebounced()` runs before the `isTrusted` and
  programmatic-guard returns), and mid-animation the rendered edge is not where the
  model is taking it. A scroll of the user's own is unaffected — `onScroll` derives
  it live. This kills the zero-user-input latch.

A third rule was tried and reverted: pinning `defaultEdge` outright whenever the
chain is shorter than the viewport. It re-opened D — an End pin with the end spacer
shown is exactly the state whose follow chases the anchor into the skeletons — and
it made the pin unsheddable, so a pull-to-load-history on a short window was
carried back to the bottom when the excursion ended. The failing row it was
written for belongs to D2, which removes it at the source.

Verified against a running list, before vs. after, on the same geometry:

| case | before | after |
|---|---|---|
| short chain, `hasVeryLastItem = false` | **Start** | null |
| short chain, `hasVeryLastItem = true` | End | End |
| tall chain at top, `hasVeryLastItem = false` | **Start** | null |
| tall chain, `hasVeryLastItem = true` | End | End |

Round-trip regression, same list: opens End → scrolling up loads history 41→57
items and pins Start at the very top of a now fully-loaded chat → scrolling back
re-pins End flush.

### C — a block that renders expanded hides nothing

The invariant, as the reporter states it: a block is either **collapsed**, visibly
so, with new messages arriving inside it; or **expanded**, and you see everything.
There is no third variant. Two ways into the third one, both closed:

- `LiveBlockUI.DeriveOverlay` hid `[TailStart, ∞)` whenever the viewer had
  attended and `amInLive` read false, however little the card actually covered. A
  young session floors its fold at `V` (`MinTailEntryCount` is 10), so every frozen
  row rendered *above* the hidden range — expanded to the eye, silently truncated.
  Hiding now requires the fold to reach `TailStart`, latched one-way: the fold
  boundary only advances, so without the latch a freeze that started out showing
  its tail would re-hide rows the reader is already looking at once the boundary
  passed `TailStart`. When the tail is shown the block also stays open-ended, or
  rows arriving past `TailStart` fall outside it and split it.
- The expansion escape in `ChatUI.Tiles.cs` was gated on `overlay == null`, so an
  explicitly expanded block resumed hiding its tail as soon as a frozen overlay
  appeared. It is now keyed on `liveBlockId` against `expandedConversations` — the
  same test the fold already uses, so the two cannot disagree.

**A product decision is embedded here.** The freeze exists so a hang-up never
flashes a collapsed frame — it deliberately chose frozen-expanded over
collapse-at-leave. That cannot coexist with "no third variant". This takes the
invariant: a viewer who leaves a young session keeps seeing it stream until the
fold catches up. The alternative — make the leave *render* collapsed, so the card
stands in and `GetSwallowedCount` grows — is the other consistent shape, and wants
the reporter's call before it is built.

### D — a stand-in rebuild may not un-know the end

The end spacer is 1500px and the end anchor sits after it, so flipping
`HasVeryLastItem` moves an End-pinned list's follow target by that much: it scrolls
into the skeletons and back out on the next rebuild, at the view's recompute
cadence, sustaining itself because follow echoes are dropped before the pin is
re-derived.

`ChatView.GetData` now refuses that flip on a build whose tail coverage came from
serve-stale meta (`ChatItems.IsTailCoverageStale`, set when
`UseRangeMetaOrLastKnown` served a stand-in). The loss bound is provable:
suppression requires a stand-in, a stand-in requires a fresh fetch in flight, and
`UseIfReady` guarantees that fetch's completion invalidates the computed — so a
genuine `hasAfter` is published one cycle later, and permanent suppression is
impossible by construction.

Two weaker forms were rejected. Suppressing whenever the window merely did not move
loses messages outright: the resume-from-background path routinely accumulates more
than `HalfLoadLimit` entries, the clamp re-arms itself through `renderedData`, the
client cannot query past a loaded end it believes is final, and past ~2×
`HalfLoadLimit` the `GetIdRange` dependency stops being registered at all — a frozen
list on a growing chat. Guarding it on the window reaching `chatIdRange.End` fails
the other way: `hasAfter` is derived *after* the fold and hidden-tail exclusions
(`ChatUI.Tiles.cs:817`, `:912`), so for the non-joined viewer in the reported repro
the window's last real lid is the live card's start for the whole session and the
guard never fires.

The client-side alternative — clamping the follow so it cannot enter the spacer —
was rejected: a legitimate End pin with `HasVeryLastItem` false means newer content
is loading in, and chasing the anchor is the catch-up that makes flinging to the
bottom work while tiles stream.

**Not settled:** whether the under-reporting builds really are the stand-in ones.
The rate points that way (~10Hz vs. the 250ms visibility throttle's ~4Hz ceiling),
and the `GetData` debug log now carries `staleTail` beside `hasAfter` so one live
session settles it. If fresh builds flap too, the next suspect is
`UseConversationOrLastKnown` / `UseSnapshotOrLastKnown` feeding a stale
`EndEntryLid` into `hiddenTailToExclude` — the marker widens to those inputs under
the same one-cycle contract, rather than the suppression widening past marked
builds.

### E — the navigation target obeys the same skip-key rule as everything else

`GetData`'s nav scan gains `&& !x.ShouldSkipKey`, matching `VirtualListData.GetLast`
and `getLastContentKey`. `getPendingJumpKey` additionally refuses a skip-key target,
so a producer that hands the list an unreachable key costs nothing rather than
churning the pin on every render.

Verified on the reporting chat: six End→null cycles in 23 s before, zero pin
transitions in 30 s of continuous sampling after, with `scrollToKey ==
lastContentKey` in every sample.

### F — expansion follows the block, not the viewer's call state

`hiddenLiveTailRange` and the card's tail preview both follow the block's rendered
expansion now, so collapsing works for a viewer in the call. `IsExpandedByDefault`
is latched write-once (`_knownConversationDefaultExpanded.TryAdd`), so a summary
landing can no longer invert anyone's effective state. And the join is watched where
the data is built: on the transition into the call the current block is expanded if
collapsed — keyed on the *block id*, so a latch that mints a new collapsed block
mid-call expands it too, and recorded only once there is a block to act on, so a
join landing before the block exists is picked up by the first build that has one.
Nothing collapses it again, so a reader's own collapse sticks.

A one-shot join edge in `LiveBlockUI` was tried first and reverted: keyed to a
`ConversationId` that jumps at every latch, it was dead within a minute and consumed
its own edge even when there was no block to act on.

### G — the element you clicked keeps its place

All three conversation toggles ask for `data-vl-hold="always"`, so the anchor
applies whichever way you toggle, and the live header gains the same
`data-vl-anchor` the regular one has — needed because neither form's key survives
the toggle, so only a stable id can hold it.

### H — a collapsed live block ends where it starts

`GroupExpandedConversations` takes entries into the live block only while it is
expanded. Collapsed, the block is the header, the card and its footer, and nothing
else; everything below is placed by the ordinary rules, with transcribed entries
filtered and typed ones rendered as usual. The card's tail preview is restricted to
what is actually hidden — spoken entries — so a typed message is never previewed in
the card *and* rendered below it.
