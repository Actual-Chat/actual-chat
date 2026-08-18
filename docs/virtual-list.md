# The virtual list

This describes how the virtualized lists work, in enough detail to rebuild them. It covers the
vocabulary, the invariants, `InfiniteList` as a state machine, the browser and device quirks that
shaped it, how to measure any of it, and finally `FiniteList`, which shares almost nothing.

There are two components:

- **`InfiniteList`** (`infinite-list.ts`) — the chat transcript. Unbounded in both directions, loads
  more as you approach either end, and its items change height after they render.
- **`FiniteList`** (`finite-list.ts`) — the chat list in the sidebar. Known item count, uniform item
  height, optional separators after given indexes.

They share `virtual-list.ts`: the Blazor round trip (render in, data query out, item visibility back),
the DOM handles, and the initial reveal. Everything geometric belongs to the subclass.

`InfiniteList` additionally owns a **`ScrollController`** (`src/nodejs/src/scroll-controller.ts`),
which holds the rubber-band overscroll, the return spring, and the clamping of a scroller to a band
narrower than its own scroll range. `FiniteList` has none of it.

The provenance rules of this document: anything called **measured** was measured in this repo,
anything called **derived** is arithmetic from a documented format, and anything unverified says so.
Do not promote a guess to a measurement by writing it in the same voice.

---

## 1. Glossary

Terms this document invents, or uses in a sense specific to this implementation. Each entry says
whether the term is a real identifier in the source or only shorthand used here.

### Geometry

- **chain** — *prose; `chainStart`, `chainEnd`, `chainSize` are real.* The contiguous run of currently
  loaded items, treated as one rigid body. Not a data structure — the items are an array; the chain is
  the *interval* they occupy.
- **chain start / chain end** — *`chainStart`, `chainEnd`.* The chain's top and bottom in wrapper
  coordinates. `chainSize` excludes the trailing row gap, and neither end includes the spacers or the
  end anchor.
- **wrapper** — *`wrapperRef`, `.c-wrapper`.* The fixed-height 4,000,000px box inside the scroller that
  the chain floats in. It is not "the content" — it is a reservation of scroll range, almost all of it
  empty.
- **container** — *`containerRef`, `.c-virtual-container`.* The `<ul>` holding the items, absolutely
  positioned inside the wrapper and placed by the model. The only element the list transforms.
- **spacer** — *`spacerRef` / `endSpacerRef`, `.c-spacer-start` / `.c-spacer-end`.* A block at each end
  of the container, sized from the model, standing in for the unloaded range. In `InfiniteList` its
  size is a stub that means nothing exact; in `FiniteList` it covers the unloaded range precisely. It
  also holds the skeletons and is what the load-trigger observer watches.
- **end anchor** — *`endAnchorRef`, `.c-end-anchor`.* A blank `<li>` after the last item that keeps the
  newest message clear of the message editor overlapping the list. Nothing to do with `reanchor` or
  with scroll anchoring, despite the name.

### The three position terms

- **scroll offset** — *`scrollOffset`.* The browser's scroll position expressed in wrapper coordinates,
  always in `[0, maxScrollTop]`. Not `scrollTop`: in reverse `scrollTop` is negative, and the two
  conversion functions are the only place that knows.
- **view offset** — *`viewOffset`.* The wrapper coordinate the top of the viewport is actually looking
  at: `scrollOffset + tOffset`. **This, not `scrollOffset`, is "the list's position".**
- **translation**, **tOffset** — *`tOffset`, `setTOffset`, `maxTOffset`.* The third position term: the
  container is translated by `-tOffset`. A correction the list holds *outside* the model, and the only
  term that may change while something is moving.
- **fold**, **folding** — *`foldTOffset`.* Move the translation into the model —
  `chainStart -= tOffset; tOffset = 0` — in one frame, so nothing on screen moves. A fold is a
  *renumbering*, not a motion.

### Motion, corrections and limits

- **band** — *prose; `ScrollLimits`, `getEffectiveScrollLimits`, `clampToLimits`.* The `[min, max]`
  range the controller keeps the scroller inside. Narrower than the scroller's own `[0, maxScrollTop]`
  and derived from the model, so neither end coincides with a native hard stop.
- **boundary** — *`overscrollBoundary`, `touchBoundary`.* The single edge of the band a given pull or
  spring is measured from, captured when the excursion starts and held until it ends. Not "the current
  limit" — the limit may move underneath it, and deliberately does not drag the gesture with it.
- **overscroll** — *`overscrollOffset`, `overscrollSign`, `isOverscrollActive`, `maxOverscroll`.* Being
  past a boundary. Legal and expected up to `maxOverscroll` (three screens) past *loaded* content,
  because that is how reading further back starts.
- **rubber band** — *prose; `resistancePull`, `visibleOverscroll`.* The finger-down half: the native
  scroll runs free past the boundary and a transform pulls back the part resistance eats.
- **return spring** — *`startOverscrollReturn`, `springOffset`.* The finger-up half: a critically
  damped spring that animates the visible displacement back to zero, carrying it entirely in the
  transform.
- **drift** — *`drift` in `startOverscrollReturn`; separately `checkModelDrift`.* Two unrelated senses.
  In the spring: how far the live native scroll has wandered from the boundary, which the transform
  compensates. In the debug checker: how far the DOM's item positions have diverged from the model.
- **stillness** — *prose; `isWatchingStillness`, `RecentreStillFrames`.* Three consecutive animation
  frames in which `scrollTop` has not changed, no finger is down and no overscroll is active. Stricter
  than "no scroll events", which a fling passes while it is still moving.
- **guard window** — *`lastProgrammaticScrollAt`, `ProgrammaticScrollGuardMs`; `suppressUntil` in the
  controller.* The interval after a scroll the code wrote itself, during which the resulting scroll
  event is ignored. The list's window and the controller's are separate and differently sized.
- **stability** — *`StabilityTracker`, `whenStable`, `whenNoAnimations`.* "Nothing that would
  invalidate a measurement is in flight" — no height transition, no recent scroll. Deadline-based, so
  a lost `transitionend` cannot wedge the list.

### Anchoring and pinning — four different things

- **pin**, **pinned edge**, **re-pin** — *`pinnedEdge`, `setPinnedEdge`, `updatePinnedEdge`,
  `repinEdge`, the `.sticky-end` class.* A standing constraint: "stay flush with this end", re-applied
  on every render rather than performed once. Unrelated to `position: sticky`.
- **re-anchor** — *`reanchor()`, and `ScrollToOptions.reanchor`.* Hold one on-screen item's wrapper
  coordinate fixed across a re-layout by moving `chainStart`. Compensating a change the code just made,
  not a scroll.
- **interactive anchor** — *`interactiveAnchor`, `data-vl-hold`.* An opt-in override: when the user
  clicks a control that changes an item's size, *that* item is what the next render holds still.
  Expires after 2s.
- **re-centre** — *`mustRecentre`, `watchForRecentre`, `isChainOffCentre`.* Shift the whole chain back
  to the middle of the scroll space, with the scroll following it. Invisible at a standstill, a jump
  anywhere else, so it waits for stillness.

### Kinds of movement

- **follow** — *prose; the `<= maxOverscroll` branch of `repinEdge`.* A correction no larger than
  scrolling could itself have produced. Always carried by the translation, never by a scroll write,
  because it routinely lands while the list is moving.
- **re-placement** — *prose.* A correction further than any scroll could have carried the view —
  opening a chat in its history, coming back from stranded. Legitimately a jump.
- **jump** — *`Jump`, `requestJump`, `runPendingJump`, `JumpPriority`.* A `scrollTop` write. As a
  queued object it also means "an intent whose target depends on where the content ends up", which is
  why it suppresses new animations and waits out the ones in flight.
- **stranded** — *`repinIfStranded`, `JumpPriority.stranded`.* The viewport has ended up more than
  twice the overscroll allowance — six screens — from the chain, so nothing on screen can pull it back.
  A fault, answered by a jump to the default edge.

### Content bookkeeping

- **settled height** — *`ItemHeightController.getHeight`, `c-height-unsettled`.* The height an item
  *will* have once its transition lands, and the value the model uses. An animation in flight therefore
  changes how the list looks without changing where it thinks anything is.
- **appearance** — *`applyAppearances`, `beginAppearance`, `EdgeSentinel`.* An item parked at a start
  height so it grows in. Which items qualify comes from a text-style key diff, so an item replacing
  another grows from that one's height and an item genuinely arriving grows from zero.
- **chain fitting** — *`isChainWithinViewport`, `updateChainFitting`.* "Both ends loaded, and the whole
  conversation is shorter than the viewport." A special case with its own resting place and its own
  hysteresis, not a coincidence the general rules happen to handle.
- **near-skeleton** — *`isNearSkeleton`.* A spacer is on screen or within 200px of it — i.e. the user
  is looking at a hole, which relaxes the heuristics that otherwise avoid a data query.
- **reveal** — *`reveal`, `startRevealWatch`, `c-initially-hidden`.* The wrapper stays
  `visibility: hidden` until the chain is confirmed placed, so the user never sees the frames before
  the initial scroll lands.

---

## 2. The invariants everything rests on

Everything below is derived from these. They are not observations — they are properties the code
**enforces**, and breaking one silently invalidates the design rather than producing an obvious bug.
The facts about browsers the design *depends on*, as opposed to enforces, are in §4.

1. **`overflow-anchor: none`, on the scroller and on every structural element under it.** Thirteen
   rules in `virtual-list.css`: the scroller, both data divs, the wrapper, the container, both
   spacers, every `li`, `.item`, `.group`, the end anchor, the top overscroll cue and the skeleton
   container. Per the spec `none` also excludes the element's descendants, so item *content* is
   covered by the `.item` rule and needs nothing of its own — this is the one clause here taken from
   the spec rather than measured. A single anchor-eligible element that escapes the rule
   reintroduces scroll anchoring, and the browser then fights every correction the list makes (§3.1).
2. **The wrapper's height never changes for the life of the list**, so `scrollHeight` is constant, the
   browser never clamps `scrollTop`, and `maxScrollTop` is stable (§3.1, §3.12).
3. **The wrapper is small enough that no engine clamps it**, and the realized height is measured
   rather than assumed (§4.1).
4. **The chain is absolutely positioned inside the wrapper; items are in normal flow inside the
   chain.** Moving the chain never reflows the wrapper and never re-lays-out the items (§3.1).
5. **`contain: strict` on the scroller**, which bounds the layout invalidation that moving the chain
   causes — the invalidation cannot escape the scroller.
6. **The container is the only element transformed for scroll correction**, and the only writer of
   that transform is `ScrollController`, which composes every contributor into one value (§3.1).
7. **`InfiniteList` has no visible scrollbar**, which is what lets `scrollTop` sit anywhere in a 4M
   space without showing the user something meaningless. `FiniteList` keeps its scrollbar, because its
   spacers cover the unloaded ranges exactly and its scrollbar is therefore honest.
8. **Every read of the list's position goes through `viewOffset`** (§3.1). The visible position is a
   sum of three terms, and code that reads only one of them disagrees with what is on screen.
9. **`scrollTop` is only ever written as a jump** — never to move somewhere gradually. The list itself
   writes it only where a jump is what the user asked for or where nothing is moving (§3.6); the
   controller's own writes are reconciliations at a boundary, each with the momentum ended around it.
10. **The model holds settled heights, never in-flight ones.** `ItemHeightController.getHeight`
    returns the height that has been written to the item, rounded, so a running transition never moves
    the model. This is what makes "the DOM disagrees with the model" a detectable fault (§5) rather
    than a normal condition.
11. **Time is measured with `performance.now()`; `Date.now()` is banned here.** Every time value in
    this component is a duration, a deadline or a velocity, and `Date.now()` is wall-clock and
    quantised to a whole millisecond. That resolution alone makes a velocity built from two scroll
    events a frame apart wrong by a large factor — measured: entry speeds reported as 16,000 and
    33,000 px/s. And because the two clocks have unrelated epochs, a single mixed comparison is out by
    ~1.7e12 and therefore silently always true — which is how a held anchor read as expired on its
    first frame. A use that is genuinely necessary needs explicit approval and a comment saying why the
    monotonic clock will not do.

---

## 3. `InfiniteList`

### 3.1 The model, and the three terms that place it

#### Why there is a model at all

Not because the browser cannot cope with content appearing above the viewport — it can. **Scroll
anchoring** (`overflow-anchor`) exists precisely for this: the browser picks an anchor node near the
top of the viewport and silently adjusts the scroll offset to keep it visually still when content is
inserted or resized above it.

The problem is that it is a heuristic, and it is the browser's, not ours:

- the browser chooses the anchor node, and in a virtualized list it may well choose one that is about
  to be recycled out of the DOM;
- anchoring is suppressed by a list of conditions in the spec, so it is not even reliably on;
- engine support is not universal (WebKit has historically not implemented it), so the same code
  behaves differently per platform;
- and when it does fire, the adjustment lands as an implicit `scrollTop` change of an amount we did
  not compute and cannot predict.

That last point is the fatal one. This list already has its own reasons to move the scroll position —
re-anchoring, re-pinning, re-centring — and two systems independently adjusting the same number cannot
be reasoned about. Worse, the browser's correction is unbounded in size: it is whatever the anchor
moved by, which for a page of loaded history is a page.

So the list **turns scroll anchoring off** and owns the geometry itself. The design goal that follows:

> Every geometry change should be absorbed by the model, so that `scrollTop` does not need to change
> at all. Where it must change, it changes by an amount we computed — on the order of one item's
> height, never a page, and never a value the browser picked.

That is achievable only if item positions do not depend on document flow. So every item is placed from
a model the code owns:

```
chainStart        the top of the loaded window, in wrapper coordinates
offsets[i]        the distance from chainStart to item i's top
offsets[n]        one row-gap past the last item, so chainEnd = chainStart + offsets[n] - rowGap
```

`offsets` is a prefix sum of measured item heights. Rendering is then a pure function of the model:
item `i` sits at `chainStart + offsets[i]`, inside a container placed at `chainStart`.

The consequence that matters: **the rendered window can be moved anywhere in the scroll space without
anything on screen moving, and without the browser having an opinion about it.** Loading history does
not push content down and does not trigger a correction; it changes `chainStart` and the container's
position by the same amount, and `scrollTop` is left exactly where the user put it.

#### The wrapper and its fixed size

Inside the scroller sits a single wrapper element with a **fixed height of 4,000,000px**
(`InfiniteSize`, mirrored in `InfiniteList.razor.cs` — the two must agree). The chain floats near the
middle of that space. The scroller has no visible scrollbar, so the user never sees that the space is
much larger than the content.

Two separate rules govern that height, and they are easy to confuse:

- **It never changes for the life of the list.** §3.12 explains what goes wrong when it does — briefly,
  in reverse the scroll origin *is* the wrapper's bottom edge, so a resize moves every coordinate at
  once and has to be paid for with either a jump or a fling-killing scroll write.
- **It cannot be arbitrarily large, and the ceiling is not the same everywhere.** §4.1 has the numbers
  and where they came from. The short version: browsers clamp, the clamp is silent, and on Chrome the
  limit is expressed in *physical* pixels so it tightens as `devicePixelRatio` rises.

Because the clamp is silent, the code never trusts the constant. The realized height is measured
(`wrapperRef.offsetHeight`) on every layout, and every wrapper-relative calculation uses the
measurement.

#### The third term: the translation

`chainStart` is where the chain sits *in layout*. There is a third term on top of it — a transform on
the container — and the position the user sees is the sum of all three:

```
visible position  =  scrollTop        the browser's, driven by the user
                  +  container.top    the model's placement of the chain
                  +  transform        the continuous correction, ours
```

The list holds that third term as `tOffset`. The container is translated by `-tOffset`, so the
coordinate the top of the viewport is looking at — in the same frame `chainStart` and `offsets` are
measured in — is

```ts
viewOffset = scrollOffset + tOffset
```

and item `i` sits at `chainStart + offsets[i] - viewOffset` on screen. **Every position read in the
list goes through `viewOffset`**, never through `scrollOffset`; the two differ by whatever the
translation is holding, including for the loader deciding which items are visible.

The point of the term is §3.6's problem in one sentence: **a transform is composited after layout, so
writing it cannot end a fling, cannot be clamped, and cannot fight the user, which is exactly what a
`scrollTop` write does.** Every correction that happens *between* renders is therefore a `tOffset`
change: the edge re-pin, the overscroll pull, the return spring, following a growing transcript.

`ScrollController` is the only writer of the container's `transform`, because a transform is a single
property and its contributors are independent: the rubber band, the list's `tOffset` (passed in as
`setBaseOffset(-tOffset)`), and a sub-pixel repaint nudge (§4.6). Each sets its own contribution and
the controller writes the sum.

#### Folding

`foldTOffset()` turns the translation into the model's own coordinates:

```ts
chainStart -= tOffset;
tOffset = 0;
```

Both style writes land in the same frame, so **nothing on screen moves** — measured at exactly 0px in
both render directions. Note what is *not* invariant: `viewOffset` itself changes by `-tOffset`, since
the chain and the viewport frame move together. What survives is `viewOffset - chainStart`, which is
what every position depends on.

Folding costs one layout pass and **no scroll write at all**, which is what makes it safe at any time —
but it is only *free* at a render, where the layout is happening regardless. So it runs:

- **at every render and re-layout**, first thing in `applyLayout`, so everything below it works in the
  coordinates the user is looking at;
- **when the list settles**, on scroll settle and on animation settle;
- **before any `scrollTop` write**, or the jump would land `tOffset` px from where it was asked for —
  which is why `setScrollOffset` also renumbers its target by the same amount after folding.

The limits are fold-invariant, which is why a fold may land mid-bounce: the model's limits are derived
from `chainStart`, and `computeScrollLimits` converts them to `scrollTop` coordinates by subtracting
`tOffset` — a fold changes both by the same amount.

Because folding happens at every render, `tOffset` never accumulates. Measured over 20s of the stress
page (messages arriving, the newest item's height churning 4×/s), it is non-zero on 4% of frames in
natural and 12% in reverse, peaks at 41px and 206px respectively, and its longest unbroken excursion
past 20px is **150–190ms**. That is why sticky counter-translation is not implemented yet — §7.

There is a second cap, on the *standing* offset rather than on the correction: past `maxTOffset` —
half a screen, floored at 200px for a list that is briefly tiny — the translation is folded on the
spot rather than left in place, because both engines rasterize tiles around where the *scroller*
thinks it is, and content carried far from there is content the compositor has not painted. That fold
is a layout write, so it is still not a scroll write.

### 3.2 The render direction is fixed at construction

`isReverse` is settled in the constructor and never changes for the life of the list. `Reverse` is
what the chat view uses; the test page picks one explicitly. There is no `Auto`: switching at runtime
is a coordinate change of the whole scroll space — the origin moves from one end of the 4M wrapper to
the other — and paying for that needs a `scrollTop` write, which is the one thing this design exists
to avoid.

- **Natural** — `flex-direction: column` on the scroller, container anchored by
  `top: chainStart - startSpacerSize`. `scrollTop` runs from 0 to `maxScrollTop`.
- **Reverse** — `flex-direction: column-reverse`, container anchored by
  `bottom: wrapperSize - chainEnd - endSpacerSize - endAnchorSize`. `scrollTop` runs from
  `-maxScrollTop` to 0.

The code works in one coordinate system, the **scroll offset**, always measured from the wrapper's top:

```ts
scrollOffset = isReverse ? scrollTop + maxScrollTop : scrollTop
toScrollTop(o) = isReverse ? o - maxScrollTop : o
```

`maxScrollTop` is **measured** (`scrollHeight - clientHeight`), never derived from the constant.

#### Why `scrollTop` goes negative in reverse

`scrollTop = 0` is not "the top of the content"; it is the **scroll origin**, which sits where the
scroller's own flow *starts* — the flex direction is set on the scroller, whose single flex child is
the wrapper. `column-reverse` reverses the main axis, so the flow starts at the bottom,
the origin sits at the bottom edge, and content overflowing in the start direction gets negative
coordinates. Same rule that makes `scrollLeft` negative under `direction: rtl`. Measured on a
standalone scroller: `column` gives `0 … 9800`, `column-reverse` gives `−9800 … 0`.

There is no way to move the scroll origin independently of the flow direction, so the sign is the
browser's to decide, not ours. It is confined to the two conversions above; nothing else in the list
ever sees a negative number.

#### What the two directions actually differ in

In two of the three places you would expect a difference — and not in the first one, which is the one
the folklore is about.

| | natural | reverse |
|---|---|---|
| a render that changes the model | identical — `reanchor` holds the item at the viewport top regardless of direction, and both container anchors then place the chain to match. Measured: content moved by exactly 0 in both. | identical |
| a height transition in flight, i.e. the DOM taller or shorter than the model | the container's *top* is pinned, so the growing item extends below the fold until the next re-layout or re-pin catches it. Measured: −213…+22px | the container's *bottom* is pinned by the model's settled `chainEnd`, so the growth eats upward and the newest content never leaves the fold. Measured: ±0.12px over 1593 frames |
| the viewport itself resizing — editor growing, keyboard opening | the browser keeps the top anchored. Measured: shrinking the scroller by 60px moved content 0px | keeps the bottom anchored. Measured: −60px |

The second row is the case the chat view lives in, and it is why the chat view is pinned to reverse.
It works precisely *because* of invariant 10: the model already holds the item's final height, so the
container is placed for the settled geometry and the animation plays out inside it.

The structural consequence of reverse: **the scroll origin is the wrapper's bottom edge**, so the
mapping to wrapper coordinates depends on the *measured* `maxScrollTop`. Natural's mapping is the
identity and depends on nothing. That asymmetry is why the Chrome height clamp (§4.1) broke reverse
and left natural untouched.

### 3.3 The states

Two nearly independent axes, plus a set of latches. Presenting it as one linear state diagram would be
tidier and wrong.

**Who owns the position right now** — mutually exclusive, listed in the order the code tests them:

| state | how the code knows | who moves the view |
|---|---|---|
| **Placing** | a jump is in flight: `pendingJump`, `isAwaitingJump`, or the guard window is open | the list, with one `scrollTop` write and up to `RepinMaxPasses` (3) convergence passes |
| **Returning** | `isReturning` | the spring, through the transform; `scrollTop` is left alone |
| **Pulling** | `isTouching` and the position is past a limit; `touchBoundary` latched | the browser moves `scrollTop` freely; the controller writes the resisted remainder to the transform |
| **Free-scrolling** | `stability.isScrolling` — a `holdScroll(200ms)` re-armed by every scroll event | the browser; the list only reads |
| **Resting** | none of the above, and `tOffset` is 0 | nobody |

Note "Resting" says nothing about item height animations, which can and do run while the position is
still. That is a third condition, tracked separately by `StabilityTracker`, and it is why
`repinWhenStable` exists at all.

**What the list owes the content** — a standing mode, orthogonal to the above:

| mode | how the code knows | meaning |
|---|---|---|
| **Pinned** | `pinnedEdge != null`, mirrored to the DOM as `.sticky-end` | "stay flush with this end", re-asserted on every render, relayout and viewport resize |
| **Free** | `pinnedEdge == null` | the user's position is the truth; nothing corrects it |

**Latches** — deferred work, at most one of each outstanding:

| latch | armed by | what it is waiting for |
|---|---|---|
| `isAwaitingStability` | `repinWhenStable` | animations and scrolling to stop, then re-pin, fold, clamp, and re-run the drift check |
| `isAwaitingOverscrollEnd` | `repinWhenOverscrollEnds` | the bounce to end, by rAF poll — the stability tracker cannot answer this (§3.6) |
| `isAwaitingJump` | `requestJump` while animating | animations to finish, with new ones suspended meanwhile |
| `isWatchingStillness` | `watchForRecentre` | three still frames, then set `mustRecentre` and re-lay out |

And one fault the code detects rather than stores: **stranded**, §3.6.

### 3.4 The transitions

For each: what fires it, what the code does, and which of the three terms moves.

| trigger | transition | what happens | term |
|---|---|---|---|
| any `scroll` event | Resting → Free-scrolling | hold the scroll for 200ms and arm the settle timer — this much happens for every scroll event, including the ones the list caused | `scrollTop` |
| the same event, trusted and outside the guard window | — | additionally: drop any interactive anchor, re-derive the pinned edge, queue a data query | — |
| 200ms with no scroll event, or `scrollend` | Free-scrolling → Resting | fold and write the chain position, release the scroll hold, re-derive the pin, report visibility, query, arm the stranded check | `container.top` |
| `touchstart` | any → finger down | a spring in flight is *caught*, not ended; the touch rAF loop starts | — |
| the touch loop sees the position past a limit | Free-scrolling → Pulling | latch the boundary, then each frame write `sign × resistancePull(over)` to the transform | `transform` |
| the finger returns inside the latched boundary | Pulling → Free-scrolling | drop the latch, clear the transform | `transform` |
| `touchend` while still past the boundary | Pulling → Returning | spring from `visibleOverscroll(over)` at `touchSpeed`; `scrollTop` written once to the boundary; momentum ended for one frame | `scrollTop` once, then `transform` |
| `touchend` with the position legal but a pull still on the transform (the edge moved out from under the finger) | Pulling → Returning | spring from the current position *as its own boundary*, so nothing scrolls and the leftover displacement is animated away rather than dropped | `transform` |
| a fling crosses a limit with no finger down | Free-scrolling → Returning | entry offset from `flingEntryOffset(over)`, entry speed from the smoothed scroll speed, which also picks the stiffness | `scrollTop` once, then `transform` |
| `touchstart` during a spring | Returning → Pulling | invert `visibleOverscroll` to find the raw overscroll reproducing what is on screen, write it, apply the matching resistance in the same frame | `scrollTop` + `transform`, same frame |
| spring within `ReturnSettlePx` (0.3px) and 8px/s | Returning → Resting | one reconciling write to the boundary, transform dropped, overflow unlocked | `scrollTop` |
| spring drift past `MaxDriftCompensationPx` (440px) | Returning → Returning | write the boundary back and end the momentum again, because the list reads `scrollTop` to decide what to load | `scrollTop` |
| `scrollTo` to a target other than the boundary during a spring | Returning → Placing | the spring is cancelled — **unless** the write is flagged `reanchor`, when the boundary moves by the same delta and the spring carries on (§3.9) | `scrollTop` |
| a render arrives | any → the render loop | §3.5 | mostly `container.top` |
| render, relayout or viewport resize while pinned, delta within `maxOverscroll` | Pinned → follow | `repinEdge` measures the edge in the DOM and adds the delta to `tOffset`; past `maxTOffset` it folds on the spot | `transform` |
| the same, delta past `maxOverscroll` | Pinned → Placing | a re-placement: up to three `setScrollOffset` passes, re-measuring between them because near an edge `container.top` moves with the scroll | `scrollTop` |
| `repinEdge` called while overscrolled | Pinned → awaiting overscroll end | deferred; past a boundary the measured position is not the visible one and the write would snap the bounce | — |
| `scrollToKey` that is not the newest item | any → Placing | suppress new height animations, wait out the ones in flight, then place the item at `center` or `end` | `container.top` then `scrollTop` |
| `scrollToKey` that *is* the newest item, end loaded | any → Pinned End | pin and re-pin, which in reverse writes nothing at all — so a message you just posted still animates in | `transform` |
| the user scrolls away from the edge | Pinned → Free | `updatePinnedEdge` finds neither edge within `EdgeEpsilon` (4px); on desktop `onWheel` also clears the pin even inside the guard window | — |
| a `data-vl-hold` control is clicked | Pinned → Free, interactive anchor set | the clicked item — or the first content item below it, for `data-anchor="below"` — is what the next render holds; expires after 2s | — |
| the chain eats into the reserve | any → watching for stillness | armed by `applyLayout`, and the watcher cancels itself the moment the chain is no longer off centre | — |
| three still frames while armed | watching → re-centre | chain moved to the middle, scroll shifted by the same amount, flagged `reanchor` and unclamped | `container.top` + `scrollTop`, cancelling |
| a settle finds the viewport more than `2 × maxOverscroll` from the chain | any → Placing | a priority-1 jump to the default edge, which outranks a queued navigation jump | `scrollTop` |
| viewport resize | any | the list re-pins now and again when stable; the controller separately suppresses its spring, finishes one in flight, and snaps to the new limits rather than animating a ~400ms return | `transform`, sometimes `scrollTop` |
| Blazor calls `reset()` | any → initial | items, offsets and caches cleared, `tOffset` zeroed, pin dropped, chain re-centred, the reveal watch restarted | `container.top` |

### 3.5 The loops

**Render.** `MutationObserver` → `onRenderBatch` → `applyRender`. The render index attribute and the
render-state JSON are written in different Blazor batches, so a render only counts once the JSON's own
index matches the attribute.

1. snapshot the old keys, offsets, `chainStart` and heights;
2. `rebuildItems` — observers attached and detached, height tracking updated, dropped keys purged;
3. if nothing survived, it is a full replacement: apply all pending heights instantly and release every
   animation, since nothing on screen can be interrupted and nothing needs holding;
4. `measureItems` (settled heights, with the mean as a fallback for an item not yet laid out), then
   `computeOffsets`;
5. `reanchor` against the old geometry (§3.9);
6. `applyAppearances` — the key diff decides which additions animate in (§3.10); anything parked makes
   the chain shorter than the model says, so offsets and the anchor are recomputed against the settled
   geometry;
7. `applyLayout`: update chain fitting, re-read the wrapper size, **fold**, perform or arm a re-centre,
   size both spacers, write the chain position, and clamp — unless something is animating, or the list
   is pinned and about to correct itself anyway;
8. `applyRenderIntent`: a `scrollToKey` becomes a pin or a jump; a fresh interactive anchor returns
   immediately, because re-anchoring has already done the work; otherwise a pinned list re-pins now and
   again when stable, and an unplaced list makes its initial placement;
9. back in `onRender`: report visibility, queue a data query, run the drift check.

**Scroll → data.** A trusted scroll event re-derives the pinned edge and queues `requestData` on a
64ms throttle. `buildDataQuery` works entirely in `viewOffset` coordinates: it takes the viewport,
expands it by `expandMultiplier` screens, unions in the range that must stay loaded — whatever is on
screen, or the nearest few items when nothing is — and asks for the difference, but only if the gap is
worth a render (half a viewport) or a skeleton is already visible. At a known edge the zone is clamped
one way only: there is nothing further out to ask for, but the zone moving inwards must still be able
to drop what it left behind, or a long read through history ends up holding thousands of items. A query identical to the last one is dropped, except while
a skeleton is on screen, where it is retried once a second. A request that never produces a render is
released after 2.5s and retried after 1s; `renderSkipped` from Blazor does the same, because a render
that did not happen cannot re-evaluate the query and a list sitting on a skeleton would wait there
forever.

**Overscroll.** boundary crossed → pull (transform) → release → spring (transform) → settle
(one reconciling write) → the next settle folds. Details in §3.7.

**Re-centre.** Loading walks the chain towards one end of the fixed space. When it eats into the
reserve — `RecentreReservePercent` (20%) of the distance between the midpoint and either end, i.e.
400,000px in a 4M space — `applyLayout` arms the stillness watcher. The watcher polls by rAF and fires
only after three consecutive frames with the position unchanged, no finger down and no overscroll
active; then `applyLayout` shifts the chain to the middle and moves the scroll by the same amount,
flagged as a re-anchor so a spring in flight survives it. Nothing about this is time-based, and there
is no hurry: the reserve is hundreds of thousands of pixels deep.

**Height.** content `ResizeObserver` → settle delay (100ms from the *first* change, so a live
transcript is followed rather than waited out) → write `style.height` → 150ms linear transition →
`transitionend` or its backstop timer → settle → re-schedule with whatever the content did meanwhile.
Each write calls back into the list, which updates the model and queues a throttled `relayout` —
offsets, re-anchor, layout, re-pin. The item is clipped (`c-height-unsettled`) for exactly as long as
the written height is behind the content, and not a moment longer, because a settled item is exactly
its content's height and a permanent clip would cut off the hover menu.

### 3.6 Which term may move when

This is the rule the whole state machine serves. **A `scrollTop` write during momentum ends the fling
on WebKit** (§4.3), which the user experiences as the list stopping dead for no reason.

| term | when it may change | cost |
|---|---|---|
| `scrollTop` | **jumps only, at a standstill** — never to move somewhere gradually | ends a fling |
| `container.top` / `bottom` | **at a render**, which is paying for layout anyway | one layout pass |
| `transform` (`tOffset`) | **everything continuous, at any time** | composite only |

So the permitted `scrollTop` writers are exactly the cases where a jump is what the user asked for, or
where nothing is moving and a jump is invisible:

1. **Opening or switching chat**, and any explicit `scrollToKey`.
2. **A re-placement re-pin** — see below for what separates one from a follow.
3. **Re-centring the chain** in the scroll space, at a standstill.
4. **Stranded recovery**.
5. **Clamping back into the band**, inside `ScrollController` only — and not while the list is pinned,
   because a pinned list is about to correct itself by re-pinning and the clamp would get there first
   with the scroll write the re-pin exists to avoid.

Everything else — the edge re-pin as a message arrives, the overscroll pull, the return spring,
following a growing transcript — is a `tOffset` change, folded into the model at the next render.

**What separates a follow from a re-placement is not its size.** A follow is anything scrolling could
itself have produced, up to `maxOverscroll` (three screens); a re-placement moves the view further than
any scroll could have carried it. Size is the wrong test because on a short viewport a single tall
message can exceed the translation cap, and jumping there would end a fling for an ordinary new
message. A follow past the cap is still a follow: it is made real immediately (a layout write) instead
of jumping (a scroll write).

Measured over 15s of the stress page while pinned to the newest message — messages arriving, the
newest item's height churning 4×/s:

| | `scrollTop` frames | chain moves | translated frames | end held flush to |
|---|---|---|---|---|
| Chrome, natural, before | 7 | 8 | 0 | −70 … +308px |
| Chrome, natural, after | **0** | 20 | 64 | −268 … +22px |
| Chrome, reverse, before | 16 | 16 | 0 | ±0.06px |
| Chrome, reverse, after | **0** | 30 | 191 | ±0.07px |
| Android, reverse, after | **0 of 901** | 35 | 154 | ±0.07px |

Reverse holds the newest message exactly where it was and stops writing the scroll position to do it.
Natural rests in the right place but lags a growing item by up to a re-layout's worth — §7.

**Re-anchoring is not on either list.** Compensating a change the code just made to the model is a
coordinate translation, not a scroll: `reanchor` moves `chainStart`, and the container's position
follows it at the same render.

#### Edge pinning

When the list is pinned to an edge (normally the End), a render that moves that edge triggers a
re-pin: measure where the edge actually is in the DOM and move so it is flush. It is measured from the
DOM rather than derived from the model because the pin has to land flush even when the model runs a
pixel or two long — and a rect already carries the translation, so the gap it reports is the visible
one and the target comes out in `viewOffset` coordinates directly. A target within `RepinEpsilon`
(1px) of where the view already is is dropped: when the list is already flush, the re-derived target
sits about one device pixel off on a fractional-DPI screen, and writing it would flip the position by
a pixel on every render.

Two guards:

- **Never while overscrolled.** Past a boundary the position is not what the user sees — a transform
  holds the rest of it — so a re-pin measured there aims at the wrong place, and the write ends the
  bounce with a snap. It waits for the overscroll to finish. The stability tracker cannot be used for
  this: it watches animations and scroll events, and a pinned spring produces neither. "Overscrolled"
  is answered by testing the position against the limits, not by reading the flag the touch loop
  latches: that flag is set a frame late, and a re-pin in the gap would translate by the first pixels
  of pull and, since the limits move with the translation, swallow them.
- **Never when there is a fresh interactive anchor**, because re-anchoring has already preserved the
  clicked row and a re-pin would drag it towards the edge.

#### The programmatic-scroll guard

Every write the list makes stamps `lastProgrammaticScrollAt`. For `ProgrammaticScrollGuardMs`
afterwards — **250ms on mobile, 100ms elsewhere** — `onScroll` ignores what it sees: the scroll a
re-pin just wrote is not the user moving, and reading it as one would drop the very pin that produced
it. Scroll events the page dispatched itself (`isTrusted === false`) are dropped outright.

`ScrollController` keeps its own, separate window: `scrollTo` sets `suppressUntil = now + 300ms`
(`ProgrammaticScrollSuppressMs`), during which the controller neither updates its speed estimate nor
treats a boundary crossing as a gesture.

The guard also suppresses `updatePinnedEdge`, so a user swipe that begins inside a guard window does
not clear the pin. That used to trap the view at the bottom during live transcription, because there
was a scroll write per settle and therefore a guard window open almost continuously. Now the follow is
a translation and produces no scroll event at all, so the guard is only open after a genuine jump —
opening a chat, a `scrollToKey`, a re-centre — where suppressing the handler is what we want.
`onWheel` remains the escape hatch on desktop.

#### Stranded recovery

If a settled viewport ends up more than `StrandedGapFactor` (2) times the overscroll allowance — six
screens — from the chain, nothing on screen can pull it back, so the list jumps to its default edge.
The threshold is deliberately a multiple of the legal overscroll rather than an independent number:
overscrolling is normal and already bounded, and this is the case where the view and its chain have
come apart entirely, whatever the cause.

#### Clamping

`clampToLimits` always **snaps**. It early-returns while a finger is down or a spring is running.

Handing an out-of-band position to the return spring instead looked like the gentler option and was
tried, and it produced the Android "stops at random places while you spin it" bug: `applyLayout` clamps
on every render, and back then starting a return locked overflow for the whole return on every
non-WebKit engine, so any transient out-of-band position during a spin was a dead stop. That mechanism
is gone (§3.7), so springing on clamp is now merely unnecessary rather than harmful — a snap is still
the right answer, because the position the clamp corrects is one the model never intended to be at.

### 3.7 Overscroll: the band, the friction, the spring

The scroller's real scroll range (0 … 4M) is far larger than the band the content occupies. The
limits come from the model:

```ts
min = hasVeryFirstItem ? chainStart : chainStart - maxOverscroll
max = hasVeryLastItem  ? chainEnd + endAnchorSize - clientHeight
                       : chainEnd + maxOverscroll - clientHeight
```

where `maxOverscroll = clientHeight * 3`. So you may overscroll up to three screens past *loaded*
content — that is legal, it must not prevent more from loading, and it exists so a fast spin into
unloaded territory does not slam into a wall. Beyond that a query built from the position would ask
around a window the data can never reach, so nothing would come to meet the view; that is where the
throw is stopped.

Two adjustments before the band is handed to the controller, both easy to lose and both load-bearing:

- an inverted band (`min > max`) is collapsed to a single point, towards `max` when the default edge
  is End;
- the result is shifted by `-tOffset` and clamped into `[0, maxScrollTop]`, because the controller's
  band is in `scrollTop` and these limits are in what the user sees. Unshifted, the rubber band would
  engage `tOffset` px early at one edge and that much late at the other. Unclamped, short content in
  reverse leaves `min` permanently out of reach and the scroller reads every resting frame as an
  overscroll to bounce back from.

Crucially **both limits are enforced the same way by the same code**, and neither coincides with the
scroller's own end — see §3.12 for why that matters.

#### Friction while the finger is down

The native scroll is *not* blocked past a limit. It is allowed to run, and a transform pulls the
content back by the part resistance "eats":

```ts
resistancePull(over) =
    over <= ResistanceRampPx ? MaxResistance * over² / (2 * ResistanceRampPx)
                             : MaxResistance * over - MaxResistance * ResistanceRampPx / 2
visibleOverscroll(over) = over - resistancePull(over)
```

with `MaxResistance = 0.667` and `ResistanceRampPx = 667`. Resistance ramps quadratically from zero,
so the first pixel past the edge is free and it stiffens the further you go — the rubber band feel.
The transform is applied to the container; `scrollTop` is left alone.

**The boundary is latched.** It is captured when the pull starts and held until the finger comes back
inside it. If the limits were re-read every frame, a page of history arriving mid-pull would move the
boundary and the resistance would jump with it — the content steps under a finger that never moved. A
pull is a gesture and is measured from where the gesture began.

If the edge moves out from under a finger that never came back, the release springs the leftover
transform away (boundary = the current position, so nothing scrolls) rather than dropping it.

#### The return spring

On release, a **critically damped** spring returns the offset to zero — one excursion, no second
bounce. Integrated per frame:

```ts
c = 2 * sqrt(k)                                  // critical damping
springSpeed += (-k * springOffset - c * springSpeed) * dt
springOffset += springSpeed * dt
```

clamped to `springCap = max(|entryOffset|, MaxFlingOverscrollPx)` — 220px — with the outward velocity
zeroed at the cap. Without that zeroing the spring presses against the cap until damping bleeds the
speed off, which measures as ~115ms of dead time at the start of every release — the bounce looks
frozen before it moves.

**There are two stiffnesses**, chosen by the speed the spring is entered with:

| entry | stiffness | why |
|---|---|---|
| released from a held pull (`< 0.5 px/ms`) | `ReturnStiffness = 120` | the content is at rest and the whole motion *is* the return; drawn out, it reads as sluggish |
| braking an arriving fling (`>= 0.5 px/ms`) | `FlingReturnStiffness = 70` | the damping force is proportional to the incoming speed, and at 120 it takes ~2200px/s out of a fast spin in one frame — that is a stop, not a brake |

A fling that reaches a boundary enters the same spring with its incoming speed (clamped to
`MaxReturnSpeedPxS = 3000`) and an entry offset from `flingEntryOffset`, which saturates at
`FlingEntryAsymptotePx = 100` so arrival speed does not translate into unbounded excursion.

#### Catching a bounce

A finger landing on a live bounce **takes it over where it is**; it does not send it home first.

The two phases express the same displacement differently — the spring holds the scroller at the
boundary and carries the offset in the transform, while a drag lets the scroller run past and keeps
only the resisted remainder — so the handover is a change of representation, not of position. The
code inverts `visibleOverscroll` to find the raw overscroll that reproduces the offset currently on
screen, writes that position, and applies the matching resistance transform in the same frame.

Ending the return instead (what the code used to do) drops up to `MaxFlingOverscrollPx` of offset in
the frame the finger lands: measured at 78–89px of instant teleport, versus 3–5px now.

#### What the spring actually drives

The spring animates a *visible* displacement. It does not steer the scroller frame by frame:

1. **Once, on entry**, the momentum that carried the view past the edge is ended — one frame of
   `overflow: hidden`, on every engine. That is what a native rubber band does, and one frame is short
   enough that the element is scrollable again before the user can ask it to move.
2. **After that the scroller is left alone.** Each frame the transform is set to `drift - springOffset`,
   where `drift = scrollTop - boundary`, so whatever the native scroll has done since the boundary is
   compensated rather than fought.
3. **Only a drift too large to hide that way is written back** — past `MaxDriftCompensationPx` (440px)
   the position is written to the boundary and the momentum behind it ended again, because the list
   reads `scrollTop` to decide what to load and should not see a position nobody is at.
4. **On settle the two are reconciled** with a single write, in the frame the transform is dropped.

This used to be WebKit-only; every other engine held `overflow: hidden` for the whole return and wrote
`scrollTop` back on every frame. Measured, before and after, with the spring's visible behaviour
unchanged in both places:

| | lock held for | overshoot | return |
|---|---|---|---|
| Chrome desktop, before | **118 frames — the whole ~931ms return** | 119px | 931ms |
| Chrome desktop, after | 0 observed | 120px | 940ms |
| Android (Galaxy S25 Ultra), before | **62 of 97 frames sampled** | 70px | 851ms |
| Android, after | 0 of 97 | 73px | 851ms |

So on Android the scroller used to be dead for about half a second on every bounce. §4.5 has the
consequences that followed from that.

A **frame** here — and everywhere else in this document — means one `requestAnimationFrame` sample
taken by the test harness, not a compositor frame. The desktop rows come from a headless Chrome run
well above 60fps and the Android rows from a 60Hz phone, so **frame counts do not compare across
rows**; the milliseconds do. Only the Android rows have a meaningful denominator: 97 samples is the
whole 1600ms recording window.

### 3.8 Spacers, the end anchor, and the conversation that fits on screen

**The spacers** do three jobs at once, and it is worth seeing all three:

1. *Reserved scroll space.* Their height is set from the model by `setSpacerSize` — the start spacer
   is `clamp(chainStart, 0, spacerSize)`, the end spacer is `spacerSize` — so the scrollbar-less
   scroller has somewhere to go while more is loading.
2. *The loading placeholder.* Each spacer contains a `VirtualListSkeletonView`; the skeletons live
   inside the spacer, not beside it. The CSS gives the spacer `overflow-y: hidden`, so that content is
   clipped to the model-derived height rather than contributing to it — the placeholder can never
   affect the geometry, which keeps the spacer's size a pure function of the model. The skeleton count
   is capped by `BeforeCount` / `AfterCount` when known, so you never see more skeletons than there are
   items left to load.
3. *The load trigger.* The spacers are what the `IntersectionObserver` observes, with a
   `SkeletonDetectionBoundaryPx` (200px) `rootMargin`. "A skeleton is on screen or nearly on screen"
   is precisely "a spacer intersects", and that sets `isNearSkeleton`, which relaxes the data-request
   heuristics — the user is already looking at the hole, so filling it wins over avoiding a query.
   The flag is tracked per spacer rather than recomputed from each callback, or a callback carrying
   only the spacer that just left would report "no skeleton" while the other is still on screen.

`setSpacerSize` is the single owner of both the height and the visibility — size zero *is* hidden, so
there is nothing to keep in step. The markup deliberately renders no `style` on the spacers: Blazor
writes the `style` attribute whole, so a second owner there would periodically wipe the JS-set height.
`applyLayout` runs on every render, so the size is always current, and both spacers reach zero on
their own because `startSpacerSize` is `0` when `hasVeryFirstItem` and `endSpacerSize` is `0` when
`hasVeryLastItem`.

Also inside the container, and also outside the geometry: the **top overscroll cue**, an image parked a
viewport above the chain that fades in after three seconds to suggest you can pull past the first
message. It is rendered only when the list opts in *and* the very first item is loaded, and it is
`position: absolute`, so it contributes nothing to any measurement.

**The end anchor** is a blank `<li class="c-end-anchor">` after the last item, whose job is to keep the
newest message clear of the message editor that overlaps the bottom of the list. Its height is CSS —
4px normally, 48px on a narrow screen, 80px when a listening-activity or audio-panel header is up —
and JS *measures* it through a `ResizeObserver` rather than being told, so a layout change that alters
it needs no code change. It sits *after* the items, so `chainEnd` does not include it and the bottom
limit adds it explicitly:

```ts
max = chainEnd + endAnchorSize - clientHeight
```

— "scroll far enough that the bottom of content-plus-anchor meets the bottom of the viewport".

#### Why a conversation that fits on screen is an exception

Take a fully loaded chat whose content is 200px, in a 578px viewport, with a 48px anchor:

- `min = chainStart` — there is nothing above the first message to scroll to
- `max = chainStart + 200 + 48 − 578 = chainStart − 330`

`max` is 330px *below* `min`: an inverted band. And read literally, that position puts the chain's top
330px above the viewport top — the only messages in the chat pushed off the top of the screen, with
`min` forbidding any scroll back to them.

It is not hypothetical, because the list is pinned to End and `repinEdge` measures exactly this: the
end anchor's bottom against the viewport's bottom, scrolling to make them flush. With short content,
"flush" *is* that position.

So both places that could take you there cap at `chainStart` — the chain's top at the viewport top:

```ts
if (this.isChainWithinViewport)            // computeScrollLimits
    max = Math.min(max, this.chainStart);

return this.isChainWithinViewport          // repinEdge.measureTarget
    ? Math.min(target, this.chainStart) : target;
```

A conversation that fits is therefore shown from its first message, and the anchor is not honoured at
all — there is nothing to keep clear of the editor when the content never reaches it.

Two conditions on it. It requires **both** ends loaded (`hasVeryFirstItem && hasVeryLastItem`) — "fits
on screen" means nothing about a partially loaded window that happens to be short. And it has
hysteresis: entered at `chainSize <= clientHeight`, left only above `clientHeight + ChainFittingExitPx`
(64px), because crossing that boundary adds or removes a whole `endAnchorSize` of scroll range — a live
transcript sitting at exactly one viewport and growing and shrinking by a line would otherwise toggle
it on every measure and jump 48px each time.

The same case leaks into two other places, and both are deliberate: a chain that fits counts as being
at the End edge for pinning, however far the anchor says it is, and it counts as "the newest message is
visible" for read tracking. Without those an End-pinned list would settle on Start and stop following
new messages until the conversation outgrew the viewport.

### 3.9 Re-anchoring

When a render changes the model — items loaded, dropped, or re-measured — the list picks an anchor item
that is on screen and shifts `chainStart` so that item's position does not change.

The default anchor is **the item at the viewport top**: everything the user is reading sits below it,
so holding it still means a change further down grows away from them rather than under them. When that
item is gone, the pair of surviving items that bracketed it is what the view is held between; when
nothing above the viewport survived, the first surviving item goes to the top.

The awkward case is a *collapse*. Collapsing the conversation you are reading takes every key at the
viewport with it, and the gap that held them shrinks to a single row — so keeping the raw offset into
that gap drops the view clean past the row, onto whatever happens to sit that far below. So the offset
is only kept while it still fits inside the surviving gap; past that, the view lands at the top of the
gap, which is where the user was already looking.

**Interactive anchors** are the opt-in override. Only controls marked `data-vl-hold` arm one — plain
taps, links and text selection must not affect anchoring. `always` holds the item and drops the pin, a
deliberate "read history" action; `keep-edge` holds only when the list is not pinned, since a pinned
list absorbs the size change through its edge re-pin instead. `data-anchor="below"` means the control
reveals rows *above* itself, so the first content item below it is what must keep its position. The
same clamp as above applies: when the viewport is deeper into the block than the block is tall, the
item itself is what the user gets back.

**Screen anchors** are the second override, and the stronger one: an element marked
`data-vl-anchor="<id>"` is one the caller promises to render again under that id, and whose position on
screen is to survive the next render. It beats an interactive anchor when both apply.

It exists because an item key is not always enough to name the thing that must hold still, and because
an item's *modelled* position is not always where it is:

- **The thing may have no key.** A control can sit inside a `<li class="group">`, which carries no
  `data-key` and is not an item, so there is nothing for the list to hold.
- **The key may not survive its own render.** Expanding a collapsed block replaces the item the control
  was in with a different one. Measured on conversation expand/collapse: the clicked key was absent
  from the next render every single time, in both directions.
- **A stuck element is not where the model puts it.** `position: sticky` clamps an element to the
  viewport edge, and that is where the user is looking at it. Hold its *flow* position instead and
  collapsing it drops it to a place that is off screen.

So the position is recorded as **rendered**, at the moment of the interaction — which is what projects a
stuck element's position into the list's coordinates. Restoring it measures the element's **flow**
position, with sticky suspended for the measurement, and moves the chain by the difference. Aiming at
the rendered position instead does not work: a stuck element does not follow the chain, so the
correction moves the chain without moving the element, and repeats.

**The anchor is spent on the render the interaction caused, and only that one.** Left standing for its
lifetime it re-applies on every render that follows — including the ones growing revealed items in from
zero — each time forcing a target measured before any of them existed. Measured on a single expand:
four applications, 124px of drift. Afterwards, ordinary re-anchoring is what keeps up.

Measured on a conversation header in view, before → after: collapse 16px → 0px, expand 124px → 0px.

A scroll or a 2s timeout clears either anchor.

#### Re-anchor writes are flagged

`scrollTo` normally cancels a return spring in flight when the target differs from the boundary — an
authoritative scroll to a new position supersedes a settling bounce, and without that the spring keeps
yanking `scrollTop` back to a now-stale edge.

A re-anchor is not that. It is the same view, renumbered. So it passes `{ reanchor: true }`, and the
spring's boundary moves by the same delta instead of the spring being cancelled.

This matters constantly, because **reaching an edge is exactly what makes the list request data**, so
the answer lands during the bounce. Measured: a plain write mid-bounce cut a 592ms / 117-frame spring
to 162ms / 16 frames with a 90–163px snap. That is the "it just jumps to the final position as soon as
you release the finger" symptom.

### 3.10 Item heights, appearances, and stability

`ItemHeightController` owns `style.height` of every item in a list that animates heights (the chat
view; the test page can turn it off). What the geometry model reads is the **settled** height, so an
animation in flight changes how the list looks without changing where it thinks anything is —
invariant 10.

Three things make this harder than it sounds, and each has a defence:

- **An item's own box says nothing once we drive it.** The intrinsic height is read from the item's
  single content child, plus everything the item is responsible for reserving around it: its own
  padding and border, and the content's margins. An item that renders two elements is a bug the
  controller logs, because the second one would be sized as if it were not there — clipped, and
  unreachable by a scroll-to.
- **Blazor rewrites the whole `class` attribute** whenever an item's own classes change, which the
  edge classes do as the loaded window moves. That silently drops `c-height-controlled` and
  `c-height-unsettled`, leaving the item with a written height and none of the rules that make it mean
  anything. A `MutationObserver` re-asserts them, ignoring the controller's own writes so it cannot
  loop with itself.
- **A `transitionend` can simply not arrive** — the element detached mid-flight, or nothing ever
  rendered it, so no transition started. Every "is animating" claim is therefore backed by a deadline
  as well as by an event, in both the controller and `StabilityTracker`.

**Appearances.** `applyAppearances` classifies everything a render added the way a text diff would: a
key standing where a removed one stood is an *edit* and grows from the height of what it replaced;
anything else is an *insertion* and grows from zero. An addition that merges *outside* the outermost
surviving keys is neither — that is the loaded window being extended, and growing a page of arriving
history out of nothing heaves the list — so only additions landing inside the old range animate. The
pinned edge joins the diff as a sentinel symbol, which is what makes a message appended while the list
is parked at that edge count as inside the range rather than as an extension. And nothing animates for
the first `AppearanceQuietMs` (300ms) after the list is revealed, or opening a chat would play the
whole first screen in.

**Stability** is the tracker both use: a height write in the settle delay, a running transition, and a
recent scroll all count as "in flight", and everything that wants to reposition the list asks here
first. A jump additionally *suspends* new animations while it waits, since starting more would only
keep pushing the moment away; transitions already running are left to finish, because they are exactly
what the jump is waiting out.

### 3.11 The initial reveal

The wrapper is `visibility: hidden` from the markup (`c-initially-hidden`) and revealed with an inline
`visibility: visible` — inline beats the class, so later renders that keep the class stay visible.
`visibility` rather than `display`, so items still lay out and measure while hidden.

`InfiniteList` polls by rAF until the content is *placed*: the `scrollToKey` item is on screen, or the
preferred edge is within `RevealEpsilon` (8px), or — for a chain that fits — the first item is not
clipped off the top. A `RevealTimeoutMs` (1500ms) backstop reveals it regardless, for cases that never
"place", such as an empty chat. Revealing deliberately does **not** re-derive the pinned edge: on the
timeout path the content has not finished settling, and re-deriving there would drop the pin the
initial placement just established, leaving a freshly opened chat at the bottom but not following it.

### 3.12 Rules that came from painful debugging

#### `scrollHeight` does not follow `style.height`

Freezing the wrapper's `style.height` does **not** freeze the scroller's `scrollHeight`. A
bottom-anchored container overflows a frozen wrapper and the scroller silently grows to cover it. Any
scheme that relies on "the wrapper is not changing size, so the origin is stable" has to verify the
realized `scrollHeight`, not the style.

#### At these magnitudes, style round-trips lie

The browser serializes a 4,000,000px length in exponential form (`4e+06px`). Parsing `style.height`
back therefore returns a rounded value and every render looks like a change. The code tracks the value
it wrote in a field (`lastWrapperSize`) and only rewrites the style when that differs; the *realized*
height is read from `offsetHeight`, which is honest.

#### A boundary at the scroller's own end can never rubber-band

The wrapper used to be trimmed to the newest item when parked at the end, which gave the bottom a free
native hard stop. That was wrong twice over:

- it made the bottom boundary the scroller's *own* end, where the JS rubber band can never engage, so
  the two edges resisted differently;
- in reverse the scroll origin **is** that edge, so every trim moved `maxScrollTop`, and the code
  re-derives the container's `bottom` from `wrapperSize` on every layout in order to hold `chainEnd` at
  a fixed *top-down* coordinate. Those two move in opposite directions, so the resize had to be paid
  for with either a silent jump or a compensating `scrollTop` write — and that write ends the fling.
  There is no third option while the wrapper tracks the content.

  Worth being precise about the mechanism, because the obvious version of this claim is wrong: a
  wrapper resize is *not* inherently visible in reverse. Measured on a standalone scroller, shrinking
  the wrapper by 1000px with the `bottom` anchor left alone moved the content by **zero** in both
  directions — the anchor and the scroll origin are measured from the same edge, so they move together.
  It becomes visible only because we insist on holding the chain at a fixed top-down coordinate, which
  is what re-deriving `bottom` from `wrapperSize` does.

Hence the fixed wrapper size. Both boundaries now sit inside the scroll range and are enforced by the
same code.

#### A touch keeps the target it started on

`touchend` and `touchcancel` are listened for on the **document**, in the capture phase. A touch keeps
the target it started on, and a virtualized list unloads items out from under the finger — so a gesture
that began on a row that has since been recycled delivers `touchend` to a detached node. The element
would never hear it, `isTouching` would latch on, and with it every clamp and the return spring stay
disarmed: the list ends up parked off its own content with no way back.

There is also a `TouchStaleMs` (3s) backstop: if nothing has moved the scroller for that long, whatever
we are waiting on is not a gesture. *Known limitation:* a genuinely resting finger — a long press while
reading — trips it.

Because the listener is on the document, `onTouchEnd` also returns early when this controller never saw
the matching `touchstart`.

---

## 4. Browser, OS and device quirks

Everything in this section is a behaviour of a specific engine or device, not of this code. Each one
forced a design decision.

### 4.1 How large the wrapper may actually be

Everything the list does rests on a wrapper that is enormously taller than the content. There are
limits on that, they differ per engine, and **they are enforced silently** — you ask for a height, you
get a smaller one, and nothing tells you.

#### The hard ceiling: Chrome clamps at 2²⁵ physical pixels

Blink stores layout coordinates as `LayoutUnit`, 1/64 of a pixel in a signed 32-bit int, giving a
maximum of `2³¹ / 64 = 33,554,432`. That budget is spent in **physical** pixels, so the ceiling in CSS
pixels is `33,554,432 / devicePixelRatio` and tightens as screens get denser:

| devicePixelRatio | ceiling in CSS px | 10M wrapper? | 4M wrapper? |
|---|---|---|---|
| 1 (desktop) | 33,554,432 | fits | fits |
| 2 | 16,777,216 | fits | fits |
| 3 (iPhone) | 11,184,810 | fits, barely | fits |
| **3.75 (this Android phone)** | **8,947,848** | **clamped to 8,947,847** | fits |
| 4 | 8,388,608 | clamped | fits |
| 8.4 | 3,994,575 | clamped | clamped |

This is what actually bit: a 10M request returned **8,947,847** on a DPR 3.75 phone and nowhere else.
Natural rendering did not care — it anchors by `top: chainStart`, which never touches the wrapper
height — but reverse anchors by `bottom: wrapperSize - chainEnd - …`, so the chain was drawn about
1,052,000px from where the scroll position said it was: "scrolling to the end jumps to a random spot",
Android only.

#### The soft ceiling: coordinates stop being exact before they stop being accepted

Compositor scroll offsets and transforms are single-precision floats, which represent integers exactly
only up to **2²⁴ = 16,777,216**. Again in physical pixels, so in CSS pixels that is
`16,777,216 / devicePixelRatio`. Past it, positions round to 2 physical pixels, then 4, and so on.

This is quieter than the clamp — you get drift and sub-pixel jitter rather than a gross misplacement —
and it bites at *half* the height the clamp does. At 4M and DPR 3.75 the working coordinates are 15M
physical, still inside the exact range; 4M only crosses it above DPR 4.19.

Worth noting what this means for the old constant: 10M on the iPhone is 30M physical, which is under
the hard ceiling (so nothing was clamped and reverse rendering was correct) but well past the exact
range. Those coordinates were being rounded to the nearest 2 physical pixels the whole time. It never
produced a reported symptom, but it is the kind of margin worth not living on.

This one is derived from the format, not measured here. Treat it as the reason not to sail close to the
hard ceiling rather than as a number to design against.

#### What we have actually verified, and what we have not

| claim | provenance |
|---|---|
| DPR 3.75 Chrome clamps 10M → 8,947,847 | **measured** on the device; matches `2²⁵ / 3.75` to the pixel |
| DPR 3.75 Chrome accepts 4M unclamped | **measured** (`asked 4e+06px`, `realized 4000000`) |
| Desktop Chrome (DPR 1) accepts 10M | **measured indirectly** — all the edge-symmetry work ran at 10M with correct geometry |
| WebKit at DPR 3 accepts 10M | **measured indirectly** — reverse rendering was correct on the iPhone at 10M, which the clamp would have broken |
| Firefox ceiling ≈ 17.9M element height | **inherited** from the original code comment, never re-verified |
| Blink's limit is `LayoutUnit` (1/64 px, int32) | **inferred** from the exact match with 2²⁵; not read from Chromium source |
| float32 exactness limit of 2²⁴ | **derived** from the format; no symptom observed |

So: one engine's ceiling is known precisely, two are known only as lower bounds, and one is hearsay.

#### What the code does about it

1. `InfiniteSize` is **4,000,000** — comfortably under every ceiling above at any plausible
   `devicePixelRatio`, while still giving ~40k items of scroll either way at ~50px an item.
2. The realized height is measured every layout (`wrapperRef.offsetHeight`) and every wrapper-relative
   calculation uses that, never the constant. Re-measured each time rather than cached at construction,
   because `devicePixelRatio` changes when a window moves to another display or the page is zoomed —
   which would silently change the ceiling underneath a cached value.

The second defence is what makes the first one safe to be wrong about. If some future device clamps 4M
too, the geometry stays correct; the list simply has less scroll space than it asked for, and
re-centring (§3.5) keeps the chain inside it.

Two related measurement traps at this magnitude: `offsetHeight` is honest, but **computed style is
not** — it comes back as `8.94785e+06px`, six significant digits, which is 10px of rounding. And
`scrollHeight` follows the realized layout, not your `style.height` (§3.12).

### 4.2 Scroll anchoring is on by default, and differs per engine

Chrome and Firefox implement scroll anchoring and apply it by default; WebKit has historically not.
Left on, it means the browser adjusts `scrollTop` by amounts of its own choosing whenever content above
the viewport changes — which in a list that is constantly loading, dropping and re-measuring items is
most renders, and the adjustment competes with every correction this code makes.

`virtual-list.css` therefore sets `overflow-anchor: none` throughout. Anything new added to that subtree
inherits the opt-out from its nearest ancestor that has the rule; a *new structural element added
outside* the container would need its own.

### 4.3 WebKit ends a fling on any `scrollTop` write

This is the single most consequential quirk, and most of §3.6 exists because of it. On iOS Safari,
writing `element.scrollTop` while momentum is running stops the momentum instantly — the write reaches
UIKit as `[scrollView setContentOffset:]`, and that is what UIKit does. There is no way to write "just
a little"; the fling is gone. The plan document has the WebKit source path and the matching Firefox
bug.

Everything that would naturally be expressed as "correct the position now" therefore has to be
expressed as either a coordinate translation the user cannot see, or a deferral to a standstill.

### 4.4 WebKit refuses to scroll an element that was `overflow: hidden` when the finger landed

The return spring wants `overflow: hidden` so the native scroller cannot fight it. On WebKit, holding
that lock for the whole spring would make the bounce uninterruptible — Safari will not scroll an
element that was locked at `touchstart`. So WebKit gets the lock for **one frame only**, and the native
scroll runs free for the rest of the spring.

That in turn means the spring cannot pin `scrollTop` on WebKit: writing it every frame fights the live
scroller, and at frame rate a single missed frame is visible as jitter. Instead the native scroll is
left alone and the **transform compensates** for whatever the scroll has drifted since the boundary, up
to `MaxDriftCompensationPx` (440px), beyond which the position is written back. When the spring
finishes, the two are reconciled with a single write.

Every engine now does the same thing, for this reason — see §3.7.

### 4.5 Locking overflow mid-fling ends the fling everywhere

Which is the property the one-frame lock *uses*: ending the momentum at the edge is what a rubber band
is supposed to do. It is also why the lock must not be held any longer than that. While non-WebKit
engines held it for the whole return, any code path that started a return while the list was coasting
stopped it dead — that was the Android "stops at random places" bug (§3.6), reached through
`clampToLimits` on a render that happened during a spin.

### 4.6 WebKit can leave a composited scroller unrastered after a `scrollTop` write

On iOS the chat view lands on the new position showing nothing, and only the next touch brings it back.
Forcing layout (`offsetHeight`) does not help — layout is not paint. Invalidating the layer does, so
after a non-smooth programmatic scroll the controller applies a **sub-pixel transform nudge** (0.01px)
for one frame. It is composed into the same transform as everything else, and it stays out of the
rubber band's way in both directions: it never starts while an overscroll is active, and it only clears
a value it still owns.

### 4.7 Programmatic scrolling is visibly jittery on Android

Driving the scroll position from JavaScript at frame rate is visibly jittery on Android even when every
frame lands on time — one missed frame in a per-frame write stream is visible. Nothing in the list
writes `scrollTop` per frame any more: the rubber band compensates the native scroll with a
**transform** (§3.7), and so does every other continuous correction (§3.1).

### 4.8 iOS moves the *document* when the keyboard opens

Focusing the message editor scrolls the page itself, not the list. On iOS the list responds to a
viewport resize by pinning `documentElement` and `body` to `position: fixed` while
`visualViewport.offsetTop` is non-zero, and releasing them when it returns to zero. Without it the
editor ends up behind the keyboard.

### 4.9 Chrome's touch slop and touchmove coalescing

The browser consumes the first several pixels of finger movement before it will start scrolling (touch
slop), and it coalesces `touchmove` events under load. Consequences for testing: two gestures with
identical dispatched coordinates can deliver measurably different raw scroll, so comparisons must be
made **at matched input**, not at matched finger travel. An apparent 10px asymmetry between the two
edges turned out to be exactly one dropped dispatch step.

### 4.10 The test page cannot be touched on a narrow phone layout

Worth knowing before spending an hour on it: at phone width the chat-list panel is painted over
`/test/virtual-list`'s list — hit-tested, *every* point of the list resolves to the panel, so no
injected gesture reaches it. Programmatic scrolling (writing `scrollTop` from the page) works fine, so
the overscroll and pinning measurements can still be taken there; real-gesture tests have to run
against a chat.

For those, `adb shell input swipe` is the tool — CDP's `Input.dispatchTouchEvent` travels over adb and
arrives too unevenly for Chrome's velocity tracker to read as a throw, so it scrolls but never flings.
Page coordinates map to screen coordinates as `chrome + cssY * devicePixelRatio`, where
`chrome = screenHeight - innerHeight * devicePixelRatio` — about 532px on a 3120px-tall device, so
ignoring it puts every gesture in the URL bar.

### 4.11 `Input.synthesizeScrollGesture` does not work on this page

For anyone automating this: CDP's `synthesizeScrollGesture` delivers `touchstart: 1, touchmove: 0,
touchend: 1` here — no movement at all. Drive real `Input.dispatchTouchEvent` sequences instead; a fast
drag followed by an immediate `touchEnd` produces a genuine compositor fling. Also bring the tab to the
front (`Page.bringToFront`) — input synthesis does not reach a backgrounded tab — and check that the
point you are aiming at actually hit-tests into the list.

### 4.12 Blazor render modes

The page runs Server, WebAssembly, or Auto (which upgrades to WebAssembly). After a rebuild, a plain
reload in WASM mode keeps serving the cached hashed bundle from the service worker — a hard reload with
caches cleared is required, or you will be testing the old code and drawing conclusions from it.

---

## 5. Measuring this stuff

`scrollTop` is not a valid measure of what the user sees. The wrapper, the container offset and the
overscroll transform all move content without moving it.

The only honest measure is **the on-screen position of a real item**, sampled every frame. When
something does move, decompose it into the three things that can cause it: the container's position in
wrapper coordinates, the item's offset within the chain, and the scroll position.

Two things are built in and worth reaching for first:

- **The consistency checker.** `debugUI.virtualListDebug(true)` turns on `checkModelDrift`, which after
  every render and every settle compares each item's real `getBoundingClientRect().top` against what
  the model says and warns past `DriftWarnThresholdPx` (8px). Sticky items are excluded from both the
  baseline and the comparison, since they report where they are stuck rather than where they sit in the
  flow. The same pass runs `checkContentOverflow`, which catches an item whose box is smaller than the
  content it must reserve for — the usual cause of one message painting over the next.
- **The overlay**, which draws the live render direction, pinned edge, spacer visibility, known ends,
  request and render activity, window sizes and mean item height, refreshed every 200ms and reading
  only inline styles so it costs no layout.

Measurement traps worth knowing, each of which produced a confidently wrong answer first:

- **`scrollTop - transform` is not the visible position.** It is `viewOffset`, and a fold moves
  `viewOffset` and the chain by the same amount (§3.1) — so every fold reads as a screenful of motion
  that never happened. What the user sees move is `viewOffset - chainStart`; in a harness that is
  `scrollTop - transform - container.top` (or `+ container.bottom` in reverse).
- **A hidden tab gets no rAF.** A Chrome window behind another window reports
  `document.visibilityState === 'hidden'` and stops firing animation frames, so every rAF-driven probe
  hangs and every recording comes back empty. `Page.bringToFront` does not fix it. Run measurements in a
  dedicated `--headless=new` instance instead — cookies can be copied across from the visible browser
  with `Storage.getCookies` / `Network.setCookies`, which is enough to reach an admin-only test page.
- **A teleported scroll position is read as a huge velocity.** The controller estimates entry speed from
  consecutive `scrollTop` values, so a harness that reaches an edge by writing `scrollTop += 3000` in a
  loop hands the return spring thousands of px/s and gets a bounce twice the real size — or, if the last
  write happens to land in the same millisecond, no velocity at all. There is no longer a reset hook
  (`resetMotionTracking` went with the direction switch), so either walk out of the boundary over
  several frames the way a real fling does, or drive the scroll through `scrollController.scrollTo`,
  which zeroes the estimate for its 300ms suppression window.
- **rAF sampling lags a composited scroll.** During a fling the main thread can read 0 for one frame and
  double for the next. That is sampling lag, not a jump, and it cancels over three frames — so
  discontinuity tests must run on a smoothed series. A single-frame delta test reports jumps that are
  not there.
- **Test data that changes under the measurement.** The virtual-list test page re-seeds its item range
  every 3 seconds and an item's content every 10 (its word count every 0.25), so the list's real ends
  and its item heights move while you measure. Pin `RangeSeed` and `ContentSeed` before drawing any
  conclusion — except when the churn *is* the subject, which is what the stress-page numbers above use.

---

## 6. `FiniteList`

Last, and short, because it shares almost nothing with `InfiniteList`.

**What it shares**, all of it in `virtual-list.ts`: the Blazor round trip (a `MutationObserver` on the
render index and the container, the render-state JSON parsed only when its index matches the attribute,
`RequestData` out, `UpdateItemVisibility` back, the request guard with its 2.5s timeout and 1s retry),
the DOM handles (wrapper, container, both spacers), and the initial reveal.

**What it deliberately does not have:**

| `InfiniteList` | `FiniteList` |
|---|---|
| a `ScrollController`: overscroll physics, a return spring, a band narrower than the scroller | none — the browser's own scrolling, unmodified |
| a render direction | always natural |
| a 4M wrapper with an absolutely positioned chain floating in it | the container stays in flow (`position: relative`), so the wrapper's height is content-driven |
| spacers as a rough reservation | spacers that cover the unloaded ranges **exactly** |
| no scrollbar, because the range is mostly empty | a real scrollbar, which is therefore honest |
| re-anchoring, edge pinning, re-centring, stranded recovery, a translation term | none of it |
| heights measured per item and animated | one measured item height plus one measured separator height |
| `scrollTop` written for jumps, re-placements, re-centres and stranded recovery | one writer only: an explicit `scrollToKey` |

All of that follows from one property: **position is a pure function of index.** So a window move alone
cannot shift what is on screen, and there is nothing to correct. Only two things can move content — the
measured item size changing, or an irregular item entering above the viewport — and both are handled by
recomputing the spacers rather than by touching the scroll position. A correction here would be
indistinguishable from the user's own scroll, and would end their fling for nothing.

The model is arithmetic. The data source supplies the total count, **the list of indexes after which a
separator appears**, and the items for the requested window:

```ts
topOf(index) = index * itemSize + separatorsBefore(index) * separatorSize
separatorsBefore(index)   // binary search in the separator index list
indexAt(offset)           // inverse of topOf, by fixed-point iteration (max 4 passes)
```

`indexAt` converges because removing the separators above a guess can never uncover another one below
it — one pass per separator in the way, i.e. one for the chat list.

Both measurements are taken rather than assumed, and both have a wrinkle:

- **The item size** comes from an item the data source marked `HasRegularSize` (`data-vl-size-source`),
  plus one row gap. Items carrying a separator are irregular and would poison the estimate. Sticking to
  a marked key while it stays rendered also keeps the value from flapping between two rows that differ
  by a sub-pixel; changes under `ItemSizeEpsilon` (0.5px) are ignored outright, because every spacer
  rewrite is a chance for the browser to clamp `scrollTop`.
- **The separator size** is measured from an invisible, absolutely positioned copy rendered once
  (`.c-separator-measure`), so the model has its exact outer height even when every item carrying one is
  outside the loaded window. Its margins are part of what it costs and are not in its own box, so they
  are read from the computed style and added.

Spacer heights come straight out of `topOf`, minus one row gap each: sitting in the same flex column as
the items, a spacer contributes a gap that the position it stands for does not. Both are written only
when they change, for the same clamp reason as above.

Visibility bookkeeping keeps its state across renders rather than re-observing from scratch: clearing
the set on every render would report an empty viewport each time, which reads downstream as "the user
can see no chats at all".

---

## 7. Known open issues

- **Sticky items are dragged by the translation.** `position: sticky` is resolved during layout and a
  transform is applied after it, so a pinned conversation header is displaced by the full `tOffset` —
  measured at exactly 200px for a 200px translation. It is not counter-translated yet, on the grounds
  that the offset is transient: measured non-zero on 4% (natural) / 12% (reverse) of frames, with the
  longest excursion past 20px lasting 150–190ms. The fix, when a sustained non-overscroll translation is
  added, is to counter-translate the pinned ones only — which the model can decide without a DOM read,
  since a sticky item is pinned exactly when its flow position `chainStart + offsets[i] - viewOffset` is
  above the sticky threshold. During an *overscroll* no counter-translation is wanted: dragging stuck
  headers along is what a native rubber band does.

- **`reanchor` is not direction-aware.** It holds the item at the viewport top in both directions; in
  reverse it should hold the bottom, keeping `chainEnd` fixed. Today reverse gets the equivalent effect
  only for the in-flight-animation case (§3.2), and only because the model carries settled heights.

- **A fling read through history is still unmeasured against this design.** The Android numbers above
  cover the two mechanisms that were broken — the held overflow lock, and the per-render `scrollTop`
  write — but not the remaining claim, that folding (a `container.top` write) during a live fling leaves
  it running. That rests on the churn measurement in the plan document and on §4.3, not on a device.
  Testing it needs a chat with enough history that flinging through it triggers loads, on an account the
  phone is signed into; §4.10 explains why the test page cannot stand in.

- **Nothing here is measured on iOS at all.** See the plan document for what is expected to differ:
  momentum runs in a different process there, so a per-frame correction is computed from a stale offset,
  and the predicted failure is a velocity-proportional sag rather than a symmetric shake.

- **`TouchStaleMs` fires on a genuine long hold** (§3.12).

---

The provenance behind these decisions — engine source paths, bug links, the survey of other
implementations, and the phase plan this was built to — is in
[docs/plans/virtual-list-translation-scrolling.md](plans/virtual-list-translation-scrolling.md). It is
a separate document on purpose: this one describes what the code does, that one describes why it was
believed it would work.
