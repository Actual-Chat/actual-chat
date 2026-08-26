# The virtual list

This describes how the virtualized lists work, in enough detail to rebuild them: the vocabulary, the
invariants, `InfiniteList` as a state machine, the overscroll model, the browser and device quirks that
shaped all of it, how to measure any of it, and finally `FiniteList`, which shares almost nothing.

Every section opens with a short summary in italics. If you only read those, you have the shape of the
thing; the rest of each section is the detail and the evidence.

There are two components:

- **`InfiniteList`** (`infinite-list.ts`) — the chat transcript. Unbounded in both directions, loads
  more as you approach either end, and its items change height after they render.
- **`FiniteList`** (`finite-list.ts`) — the chat list in the sidebar. Known item count, uniform item
  height, optional separators after given indexes.

They share `virtual-list.ts`: the Blazor round trip (render in, data query out, item visibility back),
the DOM handles, and the initial reveal. Everything geometric belongs to the subclass.

`InfiniteList` additionally owns a **`ScrollController`** (`src/nodejs/src/scroll-controller.ts`),
which keeps the scroller inside a band narrower than its own scroll range and draws an iOS-style rubber
band past its edges. `FiniteList` has none of it.

Provenance rules: anything called **measured** was measured in this repo (on a phone, on desktop
Chrome, or on the rig in `tools/virtual-list-rig/`), anything called **derived** is arithmetic from a
documented format, and anything unverified says so. Do not promote a guess to a measurement by writing
it in the same voice.

---

## 1. Glossary

*Terms this document uses in a sense specific to this implementation. Each entry says whether the term
is a real identifier in the source or only shorthand used here. Skim it once and come back when a word
in a later section looks loaded — most of them are.*

### Geometry

- **chain** — *`chainStart`, `chainEnd`, `chainSize`.* The contiguous run of currently loaded items,
  treated as one rigid body. Not a data structure — the items are an array; the chain is the *interval*
  they occupy in wrapper coordinates. `chainSize` excludes the trailing row gap, and neither end
  includes the spacers or the end anchor.
- **wrapper** — *`wrapperRef`, `.c-wrapper`.* The fixed-height 4,000,000px box inside the scroller that
  the chain floats in. It is not "the content" — it is a reservation of scroll range, almost all of it
  empty.
- **container** — *`containerRef`, `.c-virtual-container`.* The `<ul>` holding the items, absolutely
  positioned inside the wrapper and placed from the model. The element the rubber band translates, and
  the only element with a transform at all.
- **spacer** — *`spacerRef` / `endSpacerRef`, `.c-spacer-start` / `.c-spacer-end`.* A block at each end
  of the container, sized from the model, standing in for the unloaded range. In `InfiniteList` its
  size is a stub that means nothing exact; in `FiniteList` it covers the unloaded range precisely. It
  also holds the skeletons and is what the load-trigger observer watches.
- **end anchor** — *`endAnchorRef`, `.c-end-anchor`.* A blank `<li>` after the last item that keeps the
  newest message clear of the message editor overlapping the list. Nothing to do with `reanchor` or
  with scroll anchoring, despite the name.

### The two position terms

- **scroll offset** — *`scrollOffset`.* The browser's scroll position expressed in wrapper coordinates,
  always in `[0, maxScrollTop]`, and **"the list's position"**. Not `scrollTop`: in reverse `scrollTop`
  is negative, and the two conversion functions are the only place that knows. It deliberately does not
  include the rubber band's own transform (§3.7).
- **chain position** — *`chainStart`, `writeChainPosition`.* Where the loaded window sits in the scroll
  space, written to the container as `top` or `bottom`. Moving it is a *renumbering*, not a motion:
  every correction the list makes between renders is either this or the scroll position.

### The band and the rubber band

- **band** — *`ScrollLimits`, `getEffectiveScrollLimits`, `computeScrollLimits`, `clampToLimits`.* The
  `[min, max]` range of `scrollTop` the controller keeps the scroller inside. Narrower than the
  scroller's own `[0, maxScrollTop]` and derived from the model, so neither end coincides with a native
  hard stop.
- **excursion** — shorthand. The whole of one trip outside the band: from the scroll event that crosses
  a limit to the moment the content is back at the edge with nothing left in the transform.
- **boundary** — *`boundary` in the controller.* The edge of the band an excursion is measured from,
  captured when the excursion starts and held until it ends. Not "the current limit" — the limit may
  move underneath it, and deliberately does not drag the gesture with it.
- **phase** — *`phase`: `'in-band'`, `'following'`, `'engaged'`.* Where the controller is: inside the
  band; past an edge with a finger down; past an edge with nobody holding it.
- **momentum phase** — *`momentumPhase`: `'none'`, `'arming'`, `'transform'`.* The iOS/WebKit-only
  release handoff: not active; waiting for the first frame after release; or native scrolling frozen
  while the return runs entirely in the content transform.
- **`over`** — *`over`.* The raw pull the display currently corresponds to, in `scrollTop` px past the
  boundary. Fed by the scroll's deltas and reduced by the return through the resistance curve's
  inverse. Not the raw scroll position, and nothing needs that.
- **rubber band**, **resistance** — *`resistancePull`, `visibleOverscroll`, `signedOverscroll`,
  `rawOverscroll`, `visibleSlope`.* The curve that maps a raw pull to what is on screen. The part it
  eats is exactly what the transform carries.
- **carry** — *`carried`.* The display's own outward speed after a release that was still heading out:
  the browser's fling is ended and this is what the bounce is made of, decaying under the spring until
  it turns.
- **floor** — shorthand for the inward half of the return. Each frame the content is moved home at
  *least* as fast as a spring released just outside would move it; a browser fling already faster than
  that is left alone.
- **catch** — shorthand. A finger landing on a return; the one place `scrollTop` is written with a
  finger down.
- **settle** — *`settle()`.* The ordinary path's one finger-up write, once nothing is on screen and the
  scroll is still. The iOS transform takeover finishes without this write because its native scroll is
  already parked at the boundary.
- **sticky shift** — *`updateStickyItems`, `writeStickyInsets`.* Minus whatever the band's transform
  is carrying, added to every declared sticky element's own inset while an excursion is open. It moves
  the browser's sticky clamp out of layout space and into screen space (§3.7).
- **overscroll allowance** — *`maxOverscroll`* in `InfiniteList`. Three screens. How far past *loaded*
  content the band lets you go, because that is how reading further back starts.

### Waiting, guarding, settling

- **stability** — *`StabilityTracker`, `whenStable`, `whenNoAnimations`.* "Nothing that would
  invalidate a measurement is in flight" — no height transition, no recent scroll. Deadline-based, so a
  lost `transitionend` cannot wedge the list.
- **quiet moment** — *`watchQuietMoment`, `isWatchingStillness`, `QuietStillFrames`.* Three
  consecutive animation frames in which `scrollTop` has not changed, no finger is down and no excursion
  is open. Stricter than "no scroll events", which a fling passes while it is still moving. The one
  standing intent that changes the list's coordinates — a re-centre — waits for one.
- **guard window** — *`lastProgrammaticScrollAt`, `ProgrammaticScrollGuardMs`; `suppressUntil` in the
  controller.* The interval after a scroll the code wrote itself, during which the resulting scroll
  event is ignored. The list's window and the controller's are separate and differently sized.
- **position guard** — *`checkPosition`, `PositionGuardIntervalMs`.* The one correction that does not
  run off an event: a 1s check that the view is inside the band and has content on it, for the state
  where a blank viewport leaves the user nothing to scroll with (§3.6). Nothing to do with a *guard
  window*, which suppresses a handler rather than running one.

### Anchoring and pinning — five different things

- **pin**, **pinned edge**, **re-pin** — *`pinnedEdge`, `setPinnedEdge`, `updatePinnedEdge`,
  `repinEdge`, the `.sticky-end` class.* A standing constraint: "stay flush with this end", re-applied
  on every render rather than performed once. Unrelated to `position: sticky`.
- **re-anchor** — *`reanchor()`, and `ScrollToOptions.reanchor`.* Hold one on-screen item's wrapper
  coordinate fixed across a re-layout by moving `chainStart`. Compensating a change the code just made,
  not a scroll.
- **interactive anchor** — *`interactiveAnchor`, `data-vl-hold`.* An opt-in override: when the user
  clicks a control that changes an item's size, *that* item is what the next render holds still.
  Expires after 2s.
- **screen anchor** — *`screenAnchor`, `data-vl-anchor`, `data-anchor="below"`, `watchScreenAnchor`.*
  The stronger override: an element held at its *rendered* screen position through the render and the
  height animations that follow — addressed by a `data-vl-anchor` id the caller promises to render
  again, or by the key of the item below a `data-anchor="below"` control (§3.9). Waits up to 10s for
  its render; expires 2s after being placed.
- **re-centre** — *`mustRecentre`, `isChainOffCentre`.* Shift the whole chain back to the middle of the
  scroll space, with the scroll following it. Invisible at a standstill, a jump anywhere else, so it
  waits for a quiet moment.

### Kinds of movement

- **follow** — *the `<= maxOverscroll` branch of `repinEdge`, then `scheduleFollow`.* A correction no
  larger than scrolling could itself have produced. Written as a scroll, on the frame after the render
  that noticed it and re-measured there, because it moves the position the user is at and everything
  else — the compositor, `position: sticky` — reads that same term (§3.6).
- **re-placement** — A correction further than any scroll could have carried the view — opening a chat
  in its history, coming back from stranded. Legitimately a jump.
- **jump** — *`Jump`, `requestJump`, `runPendingJump`, `JumpPriority`.* A `scrollTop` write. As a
  queued object it also means "an intent whose target depends on where the content ends up", which is
  why it suppresses new animations and waits out the ones in flight.
- **stranded** — *`repinIfStranded`, `JumpPriority.stranded`.* The viewport has ended up more than
  twice the overscroll allowance — six screens — from the chain, so nothing on screen can pull it back.
  A fault, answered by a jump to the default edge. Ranks below a navigation jump, which places the view
  itself; if that jump's target never renders it re-arms this check rather than leaving the view stranded.

### Content bookkeeping

- **settled height** — *`ItemHeightController.getHeight`, `c-height-unsettled`.* The height an item
  *will* have once its transition lands, and the value the model uses. An animation in flight therefore
  changes how the list looks without changing where it thinks anything is.
- **appearance** — *`applyAppearances`, `beginAppearance`, `EdgeSentinel`.* An item parked at a start
  height so it grows in. Which items qualify comes from a text-style key diff, so an item replacing
  another grows from that one's height and an item genuinely arriving grows from zero. A key the list
  had on screen moments ago never qualifies — see *reappearance*.
- **reappearance** — *`recentlyRemoved`, `ReappearanceMs`.* A key that leaves a render and comes back
  inside 1.5s. It looks exactly like an insertion to the diff, because the render it is diffed against
  does not contain it, but the user was reading it a moment ago — so it does not animate.
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

*Eleven properties the code enforces, as opposed to observes. Everything in §3 is derived from them,
and breaking one silently invalidates the design rather than producing an obvious bug. The facts about
browsers the design depends on, as opposed to enforces, are in §4.*

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
6. **The container is the one element whose transform moves the content**, and the only writer of
   that transform is `ScrollController`, which composes every contributor into one value (§3.1). The
   list writes a transform on nothing else at all. What it writes on a declared sticky item is that
   element's own inset, which moves the browser's sticky clamp rather than the element (§3.7).
7. **`InfiniteList` has no visible scrollbar**, which is what lets `scrollTop` sit anywhere in a 4M
   space without showing the user something meaningless. `FiniteList` keeps its scrollbar, because its
   spacers cover the unloaded ranges exactly and its scrollbar is therefore honest.
8. **The list writes no transform of its own** (§3.1). Everything it corrects is either the scroll
   position or the chain's, both of which the browser resolves `position: sticky` and its own
   rasterization against. The rig checks it: the composed transform minus the band's own share has to
   be zero on every frame.
9. **`scrollTop` is never *animated*.** Nothing interpolates it towards a target over several frames.
   The list writes it where a jump is what the user asked for, where the list is still, and — at most
   once per frame — to follow a pinned edge the content has moved, each write being the whole remaining
   gap as measured in that frame (§3.6, §4.7). The controller writes it in a handful of named places,
   each with the list still or the native fling deliberately frozen, and every one is read back. The iOS handoff is the important latter case: lock overflow, snap to the boundary, and put
   the measured visual delta into the transform in the same frame (§3.7, §4.3–§4.5).
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

*The chat transcript. §3.1–3.2 are the geometry: a model the list owns, three position terms, and a
render direction. §3.3–3.6 are the state machine: states, transitions, loops, and which term may move
when. §3.7 is the overscroll model. §3.8–3.12 are the parts that earned their own section.*

### 3.1 The model, and the two terms that place it

*The list turns the browser's scroll anchoring off and places every item itself, from a model:
`chainStart` plus a prefix sum of measured heights. What the user sees is the sum of two terms —
`scrollTop` and the container's position — and every correction moves one of them. The list writes no
transform at all; the only transform on the container is the rubber band's, and it is the controller's.*

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

#### There is no third term

`chainStart` is where the chain sits *in layout*, and the position the user sees is the sum of two
things only:

```
visible position  =  scrollTop        the browser's, driven by the user
                  +  container.top    the model's placement of the chain
```

so item `i` sits at `chainStart + offsets[i] - scrollOffset` on screen, and **every position read in
the list goes through `scrollOffset`**.

There used to be a third: a transform on the container (`tOffset`), which the list wrote for every
correction that happened between renders — the pinned edge following a growing transcript, a screen
anchor held through an expand. The argument for it was that a transform composites after layout, so
writing it cannot end a fling, cannot be clamped and cannot fight the user, which a `scrollTop` write
can. What it cost was everything that resolves against the browser's own idea of the position:
`position: sticky` clamped where the scroller was rather than where the user was looking (§3.7), the
compositor rasterized around the same stale place, and the model needed a fold, a fold delay, a
standing-excursion cap and a diagnostic baseline to keep it honest.

Both of those corrections turned out to be cases where the user is *not* scrolling — a message arriving
while parked at the edge, a tap on a conversation header — so the reason to avoid the scroll position
did not apply to either. They write it now (§3.6), the term is gone, and with it the fold.

The container still carries a transform, but it is the rubber band's and it belongs to
`ScrollController` (§3.7), which is its only writer: the band's own displacement and a sub-pixel
repaint nudge (§4.6), composed into one value.

#### Corrections between renders

Two things correct the position without a render having asked them to, and both write the scroll
position, once per frame at most:

- **The pinned edge's follow** — `repinEdge` → `scheduleFollow`. A render, re-layout or viewport
  resize moved the edge in the DOM by no more than `maxOverscroll`, so the view follows it (§3.6).
  When a render is what moved it, the follow is applied inside that render instead, in the same task as
  the chain write — see §3.6; the scheduled one remains for everything else.
- **A screen-anchor hold** — `correctScreenAnchor`, once per frame for the duration of a
  `data-vl-anchor` interaction. Expanding a conversation summary moves the chain by the whole of what
  is still growing — measured at 349px — and this is what holds the tapped header still through it. Its
  `maxOverscroll` bound is a runaway guard against the loop feeding itself, not a size limit.

Both are cases where the user is not scrolling: the first only runs while the list is pinned, which the
user's first scroll event ends, and the second only starts from a tap on a control inside a conversation
header. That is what makes a scroll write the right instrument for them, and both defer any frame with
a finger down, an open band, or a scroll of the user's still settling.

### 3.2 The render direction

*The direction is configured at construction — `Reverse` for the chat view — and there is no `Auto`.
The one runtime switch is a borrow: a reverse list renders natural for the duration of a
screen-anchored interaction, and goes back at the next quiet moment. Reverse puts the scroll origin at
the bottom, so `scrollTop` is negative there; the code confines that to two conversion functions.*

`isReverse` is set from the configured `RenderDirection` in the constructor. There is no `Auto`:
switching at runtime is a coordinate change of the whole scroll space — the origin moves from one end
of the 4M wrapper to the other — and paying for that needs a `scrollTop` write, which is the one thing
this design exists to avoid.

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
the wrapper. `column-reverse` reverses the main axis, so the flow starts at the bottom, the origin
sits at the bottom edge, and content overflowing in the start direction gets negative coordinates.
Same rule that makes `scrollLeft` negative under `direction: rtl`. Measured on a standalone scroller:
`column` gives `0 … 9800`, `column-reverse` gives `−9800 … 0`.

There is no way to move the scroll origin independently of the flow direction, so the sign is the
browser's to decide, not ours. It is confined to the two conversions above; nothing else in the list
ever sees a negative number.

#### What the two directions actually differ in

In two of the three places you would expect a difference — and not in the first one, which is the one
the folklore is about.

| | natural | reverse |
|---|---|---|
| a render that changes the model | identical — `reanchor` holds the item at the viewport top regardless of direction, and both container anchors then place the chain to match. Measured: content moved by exactly 0 in both. | identical |
| a height transition in flight, i.e. the DOM taller or shorter than the model | the container's *top* is pinned, so the growing item extends below the fold until the next re-layout or re-pin catches it. Measured: −213…+22px | the container's *bottom* is pinned by the model's settled `chainEnd` **while the list follows an edge**, so the growth eats upward and the newest content never leaves the fold (measured ±0.12px over 1593 frames); reading above the end the container is anchored by its *top* instead, so the reader is what stays put (§3.10) |
| the viewport itself resizing — editor growing, keyboard opening | the browser keeps the top anchored. Measured: shrinking the scroller by 60px moved content 0px | keeps the bottom anchored. Measured: −60px |

The second row is the case the chat view lives in, and it is why the chat view is pinned to reverse.
It works precisely *because* of invariant 10: the model already holds the item's final height, so the
container is placed for the settled geometry and the animation plays out inside it.

The structural consequence of reverse: **the scroll origin is the wrapper's bottom edge**, so the
mapping to wrapper coordinates depends on the *measured* `maxScrollTop`. Natural's mapping is the
identity and depends on nothing. That asymmetry is why the Chrome height clamp (§4.1) broke reverse
and left natural untouched.

#### A change in the middle of the list

The second row of that table cuts the other way for a change *in the middle* of the list. Anchored by
its bottom, the container's rendered top is that edge minus the rendered height, so while a block in
the middle grows, everything above it moves — the row before it, the control that was clicked, the
sticky badges beside them. Only a top-anchored chain leaves what is above a growing block alone.

A screen-anchored interaction (§3.9) gets that top anchor from the placement rather than from the
direction: it unpins, and an unpinned chain is placed by its `top` whichever way the list renders
(§3.10). **The direction is fixed for the list's lifetime.**

It was not always. A reverse list used to flip to natural at such an interaction and flip back at the
next quiet moment, which bought the same top anchor at the price of two coordinate changes per tap:
flipping `flex-direction` moves the scroll origin from one end of the wrapper to the other, the
browser clamps `scrollTop` into a range it has not been given yet, and the position has to be rebuilt
from a measured drift. That reconstruction is exact only where scroll offsets are fractional. On iOS
they are integer-quantized, so each round trip lost part of a pixel and every expand/collapse moved the
view exactly 1px toward the newest message — measured on an iPhone at +1 per toggle, indefinitely.

### 3.3 The states

*Who owns the position — the list placing it, the controller past an edge, the browser scrolling, or
nobody — plus a standing pin mode and a handful of latches for deferred work. Two nearly independent
axes; one linear diagram would be tidier and wrong.*

**Who owns the position right now** — mutually exclusive, listed in the order the code tests them:

| state | how the code knows | who moves the view |
|---|---|---|
| **Placing** | a jump is in flight: `pendingJump`, `isAwaitingJump`, or the guard window is open | the list, with one `scrollTop` write and up to `RepinMaxPasses` (3) convergence passes |
| **Following** | the controller's phase is `following` | the browser moves `scrollTop` freely past an edge; the controller draws the resisted share into the transform (§3.7) |
| **Engaged** | the controller's phase is `engaged` | ordinary path: the browser, if it still is, plus the carry and floor; iOS takeover: the native scroll is locked at the boundary and an exact spring owns the band transform (§3.7) |
| **Free-scrolling** | `stability.isScrolling` — a `holdScroll(200ms)` re-armed by every scroll event | the browser; the list only reads |
| **Resting** | none of the above | nobody |

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
| `isAwaitingStability` | `repinWhenStable` | animations and scrolling to stop, then re-pin, clamp, and re-run the drift check |
| `isAwaitingOverscrollEnd` | `repinWhenOverscrollEnds` | the excursion to end, by rAF poll — the stability tracker cannot answer this (§3.6) |
| `isAwaitingJump` | `requestJump` while animating | animations to finish, with new ones suspended meanwhile |
| `isWatchingStillness` | `watchQuietMoment` | a quiet moment, then re-centre (`mustRecentre`) |
| `isWatchingScreenAnchor` | `applyScreenAnchor` | the anchored element to stop moving for `ScreenAnchorStillFrames` (12) frames while animations finish, holding it with a per-frame scroll write meanwhile |

And one fault the code detects rather than stores: **stranded**, §3.6.

### 3.4 The transitions

*One row per trigger: what fires it, what the code does, and which of the three terms moves. The
overscroll rows are the summary of §3.7.*

| trigger | transition | what happens | term |
|---|---|---|---|
| any `scroll` event | Resting → Free-scrolling | hold the scroll for 200ms and arm the settle timer — this much happens for every scroll event except the list's own follow, which is recognised by where it landed and dropped whole | `scrollTop` |
| the same event, trusted and outside the guard window | — | additionally: drop any interactive or screen anchor, re-derive the pinned edge, queue a data query | — |
| 200ms with no scroll event, or `scrollend` | Free-scrolling → Resting | release the scroll hold, re-derive the pin, report visibility, query, arm the stranded check | — |
| a scroll event past a limit, finger down | in-band → following | latch the boundary; seed `over` at the true excursion and the transform at its resisted share; each frame after: `over += Δscroll`, nudge by the resisted share | `transform` |
| a scroll event past a limit, no finger, ordinary path | in-band → engaged | the same seed, the carry seeded from the fling's speed, then the bounce and the floor per frame | `transform` |
| a scroll event past a limit, no finger, iOS/WebKit | in-band → engaged + momentum `arming` | seed the same band, estimate the recent native velocity, then perform the next-frame lock/FLIP handoff | `transform`, then `scrollTop` + `transform` |
| the finger comes back inside | following → in-band | nothing: `over` and the transform are already zero | — |
| `touchend` past the boundary, ordinary path | following → engaged | an outward release carries its speed into a bounce; each frame shows the browser's motion resisted, the carry tops it up until it turns, the floor adds only its shortfall after that; the browser's outward fling is ended by `killMomentum` | `transform` |
| `touchend` past the boundary, iOS/WebKit | following → engaged + momentum `arming` | estimate the release speed from the recent native samples; on the next frame force the overflow lock, snap `scrollTop` to the boundary and FLIP the measured visual delta into the content transform; momentum `transform` then runs the critically damped return | `scrollTop` + `transform`, same frame; then `transform` |
| `touchstart` during a return | engaged → following | release any takeover lock, integrate what the scroll did since the last frame, then write `scrollTop := boundary + over`, read back, and take the same delta out of the transform | `scrollTop` + `transform`, same frame |
| nothing on screen and the scroll still, ordinary path | engaged → in-band | `settle`: the one finger-up write, to the boundary, read back; only what landed comes out of the transform, and it retries next frame if nothing did | `scrollTop` then `transform` |
| iOS transform reaches zero | momentum `transform` → none, engaged → in-band | the native scroll is already at the boundary; clear the band transform and release the forced overflow lock | `transform` |
| `scrollTo` during an excursion | — | flagged `reanchor`: the boundary moves by the same delta and the excursion carries on. Not flagged, and to a target other than the boundary: `boundary` and `over` move together so the screen does not, and the return runs from the new edge (§3.9) | `scrollTop` |
| a wheel notch, or `cancelOverscroll` | any phase → in-band | the excursion ends at the boundary: write, read back, transform dropped | `scrollTop` + `transform` |
| a precise-device wheel gesture starting within `WheelOwnScreens` of a limit | — | the gesture is driven: every event cancelled, the scroll performed by `followBy` and clamped, so no excursion happens at all | `scrollTop` |
| a render arrives | any → the render loop | §3.5 | mostly `container.top` |
| render, relayout or viewport resize while pinned, delta within `maxOverscroll` | Pinned → follow | a follow is booked for the frame: it re-measures the edge in the frame's read phase and writes the delta to `scrollTop` in its write phase, at most once per frame, skipping the frame entirely under a finger or an open band | `scrollTop` |
| the same, delta past `maxOverscroll` | Pinned → Placing | a re-placement: up to three `setScrollOffset` passes, re-measuring between them because near an edge `container.top` moves with the scroll | `scrollTop` |
| `repinEdge` called during an excursion | Pinned → awaiting overscroll end | deferred; past a boundary the measured position is not the visible one and the write would snap the bounce | — |
| `scrollToKey` that is not the newest item | any → Placing | suppress new height animations, wait out the ones in flight, then place the item at `center` or `end` | `container.top` then `scrollTop` |
| `scrollToKey` that *is* the newest item, end loaded | any → Pinned End | pin and re-pin, which in reverse is a follow of a few pixels or nothing at all — so a message you just posted still animates in | `scrollTop` |
| the user scrolls away from the edge | Pinned → Free | `updatePinnedEdge` finds neither edge within `EdgeEpsilon` (4px); on desktop `onWheel` also clears the pin even inside the guard window | — |
| a `data-vl-hold` control is clicked or tapped | Pinned → Free, interactive anchor set | the clicked item is what the next render holds; `keep-edge` controls leave a pinned list alone; expires after 2s | — |
| a `data-vl-hold` control marked `data-anchor="below"` | Pinned → Free, key-addressed screen anchor set | the first content item below the control keeps its rendered position; placed only by the render that inserts content directly above that item, however many unrelated renders land first | — |
| a `data-vl-hold` control inside a `data-vl-anchor` element | Pinned → Free, screen anchor set | the element's rendered screen position is recorded; unpinning is what gives the chain its top anchor (§3.2) | — |
| the render that follows a screen anchor | — | the chain moves so the element's *flow* position lands where its rendered one was, then a per-frame scroll write holds it there until it has been still 12 frames | `container.top`, then `scrollTop` |
| the chain eats into the reserve | any → watching for a quiet moment | armed by `applyLayout`, and the watcher cancels itself the moment the chain is no longer off centre | — |
| a quiet moment while armed | watching → re-centre | chain moved to the middle, scroll shifted by the same amount, flagged `reanchor` and unclamped | `container.top` + `scrollTop`, cancelling |
| a settle finds the viewport more than `2 × maxOverscroll` from the chain | any → Placing | a jump to the default edge; a queued navigation jump supersedes it, and re-arms this check if its target never arrives | `scrollTop` |
| viewport resize | any | the list re-pins now and again when stable; the controller separately opens its guard window, ends any excursion at the boundary, and clamps to the new limits — a snap, not a return | `scrollTop` |

### 3.5 The loops

*Five loops: render (`MutationObserver` → `applyRender`, nine steps), scroll-to-data (a 64ms throttle
building a query in wrapper coordinates), overscroll (§3.7), re-centre (a rAF watcher waiting
for a quiet moment), and height (`ResizeObserver` → settle delay → transition → settle).*

**Render.** `MutationObserver` → `onRenderBatch` → `applyRender`. The render index attribute and the
render-state JSON are written in different Blazor batches, so a render only counts once the JSON's own
index matches the attribute.

1. snapshot the old keys, offsets, `chainStart` and heights;
2. `rebuildItems` — observers attached and detached, height tracking updated, dropped keys purged; a
   render that keeps no old key is a *full replacement*: every pending height is applied instantly
   and every animation released, since nothing on screen can be interrupted and nothing needs holding;
3. `findStickyItems` — the declared sticky elements re-collected, and any that arrived mid-excursion
   given the shift the others are already carrying (§3.7);
4. `measureItems` (settled heights, with the mean as a fallback for an item not yet laid out), then
   `computeOffsets`;
5. `reanchor` against the old geometry (§3.9) — a screen anchor first, then an interactive anchor,
   then the item at the viewport top;
6. `applyAppearances` — the key diff decides which additions animate in (§3.10); anything parked makes
   the chain shorter than the model says, so offsets and the anchor are recomputed against the settled
   geometry;
7. `applyLayout`: update chain fitting, re-read the wrapper size, perform or arm a re-centre,
   size both spacers, write the chain position, correct a watched screen anchor, and clamp — unless
   something is animating, or the list is pinned and about to correct itself anyway;
8. `applyRenderIntent`: a `scrollToKey` becomes a pin or a jump; a fresh interactive or screen anchor
   returns immediately, because re-anchoring has already done the work; otherwise a pinned list re-pins
   now and again when stable, and an unplaced list makes its initial placement;
9. back in `onRender`: report visibility, queue a data query, run the drift check.

**Scroll → data.** A trusted scroll event re-derives the pinned edge and queues `requestData` on a
64ms throttle. `buildDataQuery` works entirely in wrapper coordinates: it takes the viewport,
expands it by `expandMultiplier` screens, unions in the range that must stay loaded — whatever is on
screen, or the `retainedItemCount` (5) items nearest the viewport centre when nothing is — and asks for
the difference, but only if the gap is worth a render (half a viewport) or a skeleton is already
visible. At a known edge the zone is clamped one way only: there is nothing further out to ask for,
but the zone moving inwards must still be able to drop what it left behind, or a long read through
history ends up holding thousands of items. Move counts are rounded to 5 so a drifting viewport does
not produce a new query every frame. A query identical to the last one is dropped, except while a
skeleton is on screen, where it is retried once a second. A request that never produces a render is
released after 2.5s and retried after 1s; `renderSkipped` from Blazor does the same, because a render
that did not happen cannot re-evaluate the query and a list sitting on a skeleton would wait there
forever.

**Overscroll.** limit crossed → following (transform) → release. The ordinary path becomes engaged:
bounce, then floor (transform) → settle (one reconciling write). iOS/WebKit instead freezes native
momentum, transfers the exact rendered position into `translate3d`, and runs the whole return there.
The iOS spring never becomes a data-position
delta. Details in §3.7.

**Re-centre.** Loading walks the chain towards one end of the fixed space. When it eats into the
reserve — `RecentreReservePercent` (20%) of the distance between the midpoint and either end, i.e.
400,000px in a 4M space — `applyLayout` arms `watchQuietMoment`. The watcher polls by rAF and fires
only after `QuietStillFrames` (3) consecutive frames with `scrollTop` unchanged, no finger down, no
excursion open and no height animation running; then `applyLayout` shifts the chain to the middle and
moves the scroll by the same amount, flagged as a re-anchor so an excursion in flight survives it.
Nothing about this is time-based, and there is no hurry: the reserve is hundreds of thousands of
pixels deep. The same watcher restores a borrowed direction (§3.2).

**Height.** content `ResizeObserver` → settle delay (`DefaultSettleDelayMs`, 100ms from the *first*
change, so a live transcript is followed rather than waited out; overridable per item with
`data-vl-h-delay`) → write `style.height` → 150ms linear transition (the duration is read from the
computed style and cached; 150 is the fallback) → `transitionend` or its backstop timer → settle →
re-schedule with whatever the content did meanwhile. Each write calls back into the list, which
updates the model and queues a throttled (64ms) `relayout` — offsets, re-anchor, layout, re-pin. The
item is clipped (`c-height-unsettled`) for exactly as long as the written height is behind the
content, and not a moment longer, because a settled item is exactly its content's height and a
permanent clip would cut off the hover menu.

### 3.6 Which term may move when

*`scrollTop` is written only as a jump, at a standstill, or inside the compensated iOS handoff after
native motion has been frozen; the container's position at a render, and on the scroll that changes
which end of the chain is anchored; the transform whenever something continuous has to move. No path
animates `scrollTop` frame by frame.*

Historically, a `scrollTop` write also ended a slowed WebKit fling, while a fast fling ignored several
writes; Safari/iOS 27 removes even that stop semantic (§4.3). Either way, a JavaScript scroll-position
animation racing the compositor is not a usable continuous-motion primitive.

| term | when it may change | cost |
|---|---|---|
| `scrollTop` | **jumps, follows and compensated handoffs** — never interpolated towards a target | may end or race native inertia unless overflow is already locked |
| `container.top` / `bottom` | **at a render**, which is paying for layout anyway, and when the pin changes which end is anchored (§3.10) | one layout pass |
| `transform` | **the rubber band only**, and it is `ScrollController`'s (§3.7) | composite only |

So the permitted `scrollTop` writers in the list are exactly the cases where a jump is what the user
asked for, or where nothing is moving and a jump is invisible:

1. **Opening or switching chat**, and any explicit `scrollToKey`.
2. **A re-placement re-pin** — see below for what separates one from a follow.
3. **Re-centring the chain** in the scroll space, at a quiet moment.
4. **Stranded recovery**, and the position guard's return to the near end of the chain.
5. **A direction switch** (§3.2), at an interaction or a quiet moment: the measured drift, unclamped.
6. **Clamping back into the band**, inside `ScrollController` only — and not while the list is pinned,
   because a pinned list is about to correct itself by re-pinning and the clamp would get there first
   with the scroll write the re-pin exists to avoid.
7. **The iOS release handoff** — after overflow is forcibly locked, snap the native position to the
   boundary and transfer the measured screen delta into the transform in the same frame (§3.7).
8. **A follow** — the pinned edge moving with content that grew under it (`scheduleFollow`,
   `ScrollController.followBy`). One write per frame at most, in the frame's write phase, from a
   measurement taken in its read phase; clamped into the limits and read back like every other write.
   **A render that re-places the chain applies its own follow inline**, at the end of `applyLayout`,
   for the reason the screen-anchor hold is applied there: while the list follows an edge, the chain
   write moves it by everything that render added, and the scroll that compensates is what keeps the
   view still. Waiting for the frame the scheduled follow runs on lets that displacement paint, once,
   in full — recorded on a live conversation at 200–300px per render, on Chrome and on Android alike,
   and reproduced at exactly the size of the change. The scheduled follow stays as the fallback for the
   frames a render may not write on (a finger down, an open band, a scroll still settling), where it
   defers as before.
9. **A screen-anchor hold** — the same mechanism holding a tapped conversation header still while the
   block under it expands (`correctScreenAnchor`), once per frame for the length of the animation.

The controller's own writes are listed in §3.7; each is at a standstill or with the fling deliberately
frozen, and each is read back.

The last two are the writes that land between renders, and they are scroll writes because of what they
*are*: they move the position the user is at, and everything that resolves against that position — the
compositor's rasterization, `position: sticky` — reads `scrollTop`. Carried in a transform instead,
all of that kept pointing at a place the user had left. Neither of them fights a scroll of the user's,
because neither happens during one: the follow only runs while the list is pinned, which the user's
first scroll event ends, and a hold only starts from a tap on a control. Both defer any frame with a
finger down, an open band, or a scroll still settling — deferred, not dropped, so the correction
survives a finger resting on the list. The follow's retry waits on a 10Hz bucket
(`FollowRetryHz`) rather than the next frame: a rest can last as long as the user likes, and a pending
animation frame makes the browser run its whole rendering lifecycle, so polling at frame rate would
cost a style recalc per frame to re-read three booleans.

**What separates a follow from a re-placement is not its size.** A follow is anything scrolling could
itself have produced, up to `maxOverscroll` (three screens); a re-placement moves the view further than
any scroll could have carried it. Size is the wrong test because on a short viewport a single tall
message can exceed half a screen, and jumping there would end a fling for an ordinary new message.

Measured over 15s of the stress page while pinned to the newest message — messages arriving, the
newest item's height churning 4×/s — from when the follow was a translation. The right-hand column is
the one that still means anything: how flush the end was actually held. The middle columns record what
the correction was made of at the time, and the follow has since moved from the transform to the scroll
position, one write per frame instead of one per render.

| | `scrollTop` frames | chain moves | translated frames | end held flush to |
|---|---|---|---|---|
| Chrome, natural, before | 7 | 8 | 0 | −70 … +308px |
| Chrome, natural, after | **0** | 20 | 64 | −268 … +22px |
| Chrome, reverse, before | 16 | 16 | 0 | ±0.06px |
| Chrome, reverse, after | **0** | 30 | 191 | ±0.07px |
| Android, reverse, after | **0 of 901** | 35 | 154 | ±0.07px |

Reverse holds the newest message exactly where it was. Natural rests in the right place but lags a
growing item by up to a re-layout's worth — §7.

**Re-anchoring is not on either list.** Compensating a change the code just made to the model is a
renumbering, not a scroll: `reanchor` moves `chainStart`, and the container's position follows it at
the same render.

#### Edge pinning

When the list is pinned to an edge (normally the End), a render that moves that edge triggers a
re-pin: measure where the edge actually is in the DOM and move so it is flush. It is measured from the
DOM rather than derived from the model because the pin has to land flush even when the model runs a
pixel or two long. A target within `RepinEpsilon`
(1px) of where the view already is is dropped: when the list is already flush, the re-derived target
sits about one device pixel off on a fractional-DPI screen, and writing it would flip the position by
a pixel on every render.

Two guards:

- **Never during an excursion.** Past a boundary the position is not what the user sees — a transform
  holds the rest of it — so a re-pin measured there aims at the wrong place, and the write ends the
  bounce with a snap. It waits for the excursion to finish (`repinWhenOverscrollEnds`, a rAF poll).
  The stability tracker cannot be used for this: it watches animations and scroll events, and a
  return produces neither once the scroll is still. "During an excursion" is the controller's phase
  (`isOverscrollActive`), which is set on the very scroll event that crosses the limit, so there is no
  frame in which the position is out and the flag is not.
- **Never when there is a fresh interactive or screen anchor**, because re-anchoring has already
  preserved the clicked row and a re-pin would drag it towards the edge.

#### The programmatic-scroll guard

Every write the list makes stamps `lastProgrammaticScrollAt`. For `ProgrammaticScrollGuardMs`
afterwards — **250ms on mobile, 100ms elsewhere** — `onScroll` ignores what it sees: the scroll a
re-pin just wrote is not the user moving, and reading it as one would drop the very pin that produced
it. Scroll events the page dispatched itself (`isTrusted === false`) are dropped outright.

`ScrollController` keeps its own, separate window: `scrollTo` and a viewport resize set
`suppressUntil = now + 300ms` (`ProgrammaticScrollSuppressMs`), during which the controller neither
updates its speed estimate nor treats a boundary crossing as a gesture.

The guard also suppresses `updatePinnedEdge`, so a user swipe that begins inside a guard window does
not clear the pin. That used to trap the view at the bottom during live transcription, because there
was a scroll write per settle and therefore a guard window open almost continuously.

The follow writes `scrollTop` again — and deliberately does **not** open that window, because it would
never close. It is recognised the other way instead: `followBy` reports where the write landed, and
the next scroll event standing on that exact value is dropped whole, while the first event anywhere
else is the user's. So the guard proper is only open after a genuine jump — opening a chat, a
`scrollToKey`, a re-centre — where suppressing the handler is what we want. `onWheel` remains the
escape hatch on desktop.

#### Stranded recovery

If a settled viewport ends up more than `StrandedGapFactor` (2) times the overscroll allowance — six
screens — from the chain, nothing on screen can pull it back, so the list jumps to its default edge.
The threshold is deliberately a multiple of the legal overscroll rather than an independent number:
overscrolling is normal and already bounded, and this is the case where the view and its chain have
come apart entirely, whatever the cause.

#### The position guard

Every correction above runs off an *event* — a render, a settle, a scroll — and there is one state in
which none of them arrives: the position is illegal, and the viewport is blank, so the user has nothing
on screen to scroll and cannot produce the event that would fix it. `checkPosition` is the standing
check that runs on its own clock instead, every `PositionGuardIntervalMs` (1s).

It defers to anything that legitimately owns the position: a finger down, a scroll still settling, an
open excursion, or one that ended within `PositionGuardOverscrollQuietMs` (500ms) —
`ScrollController.isOverscrollRecent`, which a caller polling on its own clock needs and one reacting
to an event does not. It also defers to a correction already booked (a pending jump, a scheduled
follow) and to a fresh interactive or screen anchor, which is a click of the user's deliberately
holding the view somewhere. Then two tiers:

- **Out of band.** Exactly what the first scroll event out there would have found, so it is answered
  the same way: `clampToLimits`, the snap that `ScrollController.onScroll` performs itself for any
  crossing that is not a finger. It acts on the second consecutive check rather than the first, because
  a correction booked for a frame that has not run yet can be seen out there once.
- **In band, but with no content on screen.** Up to three screens past what is loaded is legal — that is
  how reading further back starts — so the first answer is the data query that space exists for, and
  only after `OffContentChecks` (3) checks with none in flight is the blank treated as a fault. Then the
  view moves to the *near* end of the chain rather than to the default edge: this is the position the
  user scrolled to, and the nearest place it puts content on screen is where scrolling itself would have
  stopped.

Deliberately **not** gated on the height animations that `applyLayout`'s own clamp waits for: the limits
are built from the model, which carries settled heights (§3.10), so the guard reads the same numbers the
settled pass would. Persistence across checks is what separates a fault from a frame in transit.

It is a backstop, not the mechanism: with the collapse handled where it happens (§3.9) and the settled
clamp below, the repro above recovers in ~500ms without the guard ever firing. What it covers is the
cases we have not found.

#### Clamping

`clampToLimits` always **snaps**. It early-returns while a finger is down or an excursion is open, and
`applyLayout` skips it while the list is pinned.

**Mid-animation it is deferred, not skipped.** The clamp needs the real sizes and the DOM does not have
them yet, so `applyLayout` books the settled pass instead — which an unpinned list has to book for
itself, `repinWhenStable` otherwise being reached only from the pinned path. Left to that path alone, a
block collapsing under a view that is not at an edge got no clamp at all: the render skipped it for the
animation, and nothing re-ran it afterwards.

Handing an out-of-band position to the return instead looked like the gentler option and was
tried, and it produced the Android "stops at random places while you spin it" bug: `applyLayout` clamps
on every render, and back then starting a return locked overflow for the whole return on every
non-WebKit engine, so any transient out-of-band position during a spin was a dead stop. That mechanism
is gone (§3.7, §4.5), so springing on clamp is now merely unnecessary rather than harmful — a snap is
still the right answer, because the position the clamp corrects is one the model never intended to be
at.

### 3.7 Overscroll: the band, the resistance, the bounce and the floor

*The controller keeps `scrollTop` inside a band narrower than the scroller and draws a rubber band in
the content's `translate3d`. Under a finger, every engine uses the same delta resistance. After release,
the ordinary path continues to observe the browser and supplies a carry and return floor; iOS/WebKit
instead freezes native inertia, FLIP-transfers the exact rendered position into the transform, and runs
an exact critically damped spring. The band is the only thing on the container's transform, in both
paths. Verified mechanically by the rig and qualitatively on an iPhone.*

#### The band

The scroller's real scroll range (0 … 4M) is far larger than the band the content occupies. The limits
come from the model (`computeScrollLimits`):

```ts
min = hasVeryFirstItem ? chainStart : chainStart - maxOverscroll
max = hasVeryLastItem  ? chainEnd + endAnchorSize - clientHeight
                       : chainEnd + maxOverscroll - clientHeight
```

where `maxOverscroll = clientHeight * MaxOverscrollScreens` (3). So you may overscroll up to three
screens past *loaded* content — that is legal, it must not prevent more from loading, and it exists so
a fast spin into unloaded territory does not slam into a wall. Beyond that a query built from the
position would ask around a window the data can never reach, so nothing would come to meet the view;
that is where the throw is stopped.

Three adjustments before the band is handed to the controller, all easy to lose and all load-bearing:

- a chain that fits on screen caps `max` at `chainStart` (§3.8);
- an inverted band (`min > max`) is collapsed to a single point, towards `max` when the default edge
  is End;
- the result is clamped into `[0, maxScrollTop]`: unclamped, short content in reverse leaves `min`
  permanently out of reach and the scroller reads every resting frame as an overscroll to bounce back
  from.

Crucially **both limits are enforced the same way by the same code**, and neither coincides with the
scroller's own end — see §3.12 for why that matters. A `null` limit from the model (no items yet)
means no limit that way.

#### The rules

The position the user sees is the pair `(scrollTop, transform)`. Everything below follows from four
rules:

1. **The transform has one owner.** `ScrollController` is the only writer of the CSS property, and
   composes the band's `overscrollOffset` with the repaint nudge (§4.6). The ordinary band changes its
   part only by `nudge(delta)`. During the iOS takeover the first FLIP is also a nudge, after which the
   spring owns and assigns `overscrollOffset`.
2. **Every `scrollTop` write is named and read back.** The ordinary path writes for a catch, settle,
   wheel/cancel/watchdog end, tiny or non-touch crossing, and the owner's `scrollTo` / clamp. The iOS
   path additionally snaps to the boundary after forcing the overflow lock, and repeats that snap if
   native movement leaks through. Only the measured visual effect of a write is transferred into the
   transform. Anything else that wants to correct a moving view moves the scroll position or the
   chain's, never this.
3. **The two release paths are intentionally different.** Non-iOS uses the browser's continuing scroll
   and locks overflow for only two frames when it must kill an outward fling. iOS/WebKit cannot rely on
   a `scrollTop` write to stop inertia, so its takeover force-locks overflow for the whole, now-short
   transform return (§4.3–§4.5).
4. **Loading coordinates never become a synthetic inertia engine.** The iOS spring moves only the band
   contribution. It does not advance `chainStart`, the data-query viewport, or the pinned edge. An
   earlier experiment drove inertial deltas through the list's own coordinates; the loader then believed
   a top-edge pull was travelling toward the opposite end and rendered skeletons there.

The ordinary state is small: `boundary` (the edge, in `scrollTop` px, latched at the crossing), `over`
(the raw pull the display corresponds to), and `carried` with its `bounceCap` while a bounce is under
way. The invariant is that what is on screen past the edge is exactly `signedOverscroll(over)` at every
frame boundary. The iOS takeover adds the recent native samples and an exact spring position/velocity,
but keeps the same boundary and transform composition.

| phase | who is scrolling | what this does per frame | how it ends |
|---|---|---|---|
| `in-band` | the browser | nothing | a crossing |
| `following` | the browser | `over += Δscroll`; nudge by the resisted share | back inside under the finger, or release |
| `engaged`, ordinary | the browser, if it still is | the resisted share of any motion; outward, topped up to the carry; inward, topped up to the floor | settled and still |
| `engaged`, iOS momentum `arming` | native inertia for this last frame | integrate the last native delta, then lock and FLIP to the boundary | momentum `transform` |
| `engaged`, iOS momentum `transform` | the exact transform spring | keep native scroll pinned; compensate any leak; advance the spring | transform reaches zero |

**Following** is one delta rule: the scroll moved by `Δ` since the band last looked; the fraction of it
the curve eats goes into the transform, the rest reaches the screen. The crossing itself is not treated
as a delta — a limit that moved under the scroll can be noticed thousands of pixels out, and integrating
that as one frame of finger travel steps the content by its resisted share (measured on Android: 1480px
into the transform in one step). So the latch *seeds* `over` and the transform at the true excursion,
and only integrates from there. Nothing here draws from the scroll event: the loop draws once per
frame from one read (see *One frame, one read, one write*).

#### The iOS/WebKit release takeover

iOS is taken over only after a touch release beyond a boundary, or when native momentum reaches a
boundary after the finger is already up. Other engines stay on the ordinary path; `?vltakeover=1`
exists only so the Chrome rig can exercise the same state machine.

The handoff is one measured FLIP:

1. While touch motion is native, keep up to 12 `scrollTop` samples from the last 96ms. A direction
   reversal discards the older side of the curve. Least-squares slope gives the release velocity and
   falls back to the latest follow velocity when there are too few samples.
2. Arm the takeover and wait for the next animation frame. That lets the final compositor delta land;
   the frame integrates it through the same resistance as every other delta.
3. Measure the content's screen top, force `overflow-y: hidden`, flush layout, write the latched
   boundary, flush again, and measure the content top again. Add the exact before/after difference to
   the band transform. The native coordinate changes, but the rendered content does not.
4. Convert the sampled native velocity through the resistance curve's slope and run the exact
   critically damped solution in `overscrollOffset`. `writeTransform` emits
   `translate3d(0, y, 0)`.
5. Keep `scrollTop` at the boundary. If WebKit leaks a native step despite the lock, repeat the
   measured snap and put the landed delta into the transform before continuing the same spring.
6. At zero, clear the band contribution and release overflow. There is no data-position update, because
   the spring never entered the list's model.

The first versions sampled for several frames after release, delayed the freeze to a predicted minimum
absolute speed, or emulated the remaining inertial scroll in a transform of the list's own. Moving the
freeze merely moved the visible out-of-sync frames earlier or later, and the synthetic one corrupted
loading. The FLIP
handoff removes both races: one native position is traded for one transform position, once.

**Engaged, outward — the ordinary-path bounce.** A release that is still heading out keeps its
momentum. `carried` is seeded from the release speed through the curve's slope (an inward release seeds
nothing — that is a throw into the list, and the browser performs it), and each frame it is at least
the outward speed the browser is observed to move the display at, decaying under the same critically
damped spring (`stepCriticalSpring`, the exact closed-form step). The display moves out by the larger
of the browser's resisted step and the
carry's, so the browser and the carry are one motion, not two. Meanwhile the browser's own fling is
being ended (`killMomentum`), so once it is dead the carry is what the bounce is made of; where
nothing ends a fling (`?vllock=0`), the browser may lead the carry only up to `bounceCap` —
`MaxBouncePx` (150) beyond where the release happened — and past that its motion is absorbed like any
other leak. The carry ends when it turns, when the browser is observed coming home (it knows better),
or at the cap. `MaxCarrySpeedPxS` bounds the carried speed to what reaches `MaxBouncePx` from the edge
(`v/(ω·e)` is a spring's peak from initial speed `v`). The cap stays 150px as stiffness changes because
the speed bound scales with `√k`.

**Engaged, inward — the ordinary-path floor.** The browser's motion is shown resisted, exactly as a
throw is; then the content has some real speed toward the edge, and a spring released from rest just
outside here would be moving it at some speed of its own. If the real one is already that fast, nothing
is added and the frame is a throw. If it is slower — stalled, outward, or just slow — exactly the shortfall is
added, and never through the edge. So a fling out of the band and back into the list looks like a
fling, a slow push looks like a spring, and there is no handover between them because there is one
rule. The rig compares swing-back and up-down coast against a control fling started inside the band;
the acceptance rule is that a throw from overscroll remains a throw.

The ordinary floor speed at displacement `x` is what a critically damped spring of stiffness `k`,
released from rest 20% further out, has reached by the time it passes `x`. From rest the speed at `x`
itself is zero, so this is the nearest well-posed thing to "the speed a release from here would have":
`x(t) = x₀(1 + ωt)e^{−ωt}` with `ω = √k`; from `x₀ = 1.2x` it reaches `x` at `ωt = 0.731`, moving at
`ReturnFloorFactor · ω · x` with `ReturnFloorFactor = 0.4223`, capped at `MaxReturnSpeedPxS` (6000).
`ReturnStiffness` is 1600 and `ReturnSettlePx` is 0.3. The iOS takeover uses the exact same critically
damped equation rather than the floor approximation. Its time scale is `1/√k` (25ms), so 1600 returns
in half the time of the preceding 400 setting; the exact solution and the zero clamp prevent crossing
the boundary or oscillating.

**A catch** — a finger landing on a return — first releases an iOS takeover lock if one exists, moves
the phase, integrates whatever the scroll did since the last frame (a fling still running is a delta
like any other), and writes the scroll position to
`boundary + over`, read back, with the same delta taken out of the transform. Nothing on screen moves.
This is the one legal finger-down write, and it exists to pay down whatever the scroll leaked past the
edge during the return; left standing, the leak compounds across catches (measured on the rig as the
transform growing 82 → 1194px over five, and the settle write at the end owing all of it at once). The
resumed pull continues from `over` as it stands, at the stiffness that displacement deserves — the
resistance is keyed to what is on screen, never to where the raw scroll is parked. A catch drops any
carry.

**Settle**, on the ordinary path, is the one finger-up write. When nothing is on screen and the scroll
has stopped, the position is written to the boundary, read back, and the transform gives up exactly
what landed. If the engine takes none of it — WebKit with takeover disabled, when the fling it thinks
is still running was at speed a frame ago — nothing changes and it runs again next frame; within a
device pixel counts as landed. Dropping the
transform before the write, which an earlier version did, was a step of the whole leak whenever the
write was refused.

**Ending inside the band under a finger** writes nothing and drops nothing: by the time the scroll is
back inside, `over` has been integrated to zero and the transform with it, so letting go moves nothing.
A release from inside the band is a release, not a bounce: `engage` checks the position, and a finger
that crossed back in and lifted — still `following`, because ending that phase waits on a scroll
event a release does not produce — ends the excursion instead of springing at the edge it just passed.

**What this replaced.** The previous model kept `visible` as its own state and re-derived it from the
raw scroll at every phase change, and every one of those re-derivations was a step. The worst were
measured on phones: a catch re-deriving the band from a raw scroll the spring had never given back
(251px in one frame, ratcheting across touches, 467 → 1743px of raw offset); a catch with 4.7px on
screen and the raw scroll 184px out computing the next frame's resistance as though 131px were on
screen, so that 16px of finger crossed the band to the wrong side; and a settle that dropped the
transform and then wrote a position WebKit refused. None of those can be expressed in the delta model,
because there is nothing to re-derive from.

#### Resistance, and why it is gentle

```ts
resistancePull(over) =
    over <= ResistanceRampPx ? MaxResistance * over² / (2 * ResistanceRampPx)
                             : MaxResistance * over - MaxResistance * ResistanceRampPx / 2
visibleOverscroll(over) = over - resistancePull(over)
```

with `MaxResistance = 0.667` and `ResistanceRampPx = 444`, both overridable from the URL as
`?vlfriction=<max>x<ramp>`. Resistance ramps quadratically from zero, so the first pixel past the edge
is free and it stiffens the further you go; past the ramp only a third of a pull reaches the screen.
`rawOverscroll` is the exact inverse — the carry and the floor move what is on screen and `over` has
to follow — and `visibleSlope` is the curve's slope, which is what a release speed is carried through.

**The ramp is the strength dial, not the cap.** Within it the pull is `R * over² / (2 * ramp)`, so
shortening the ramp scales the pull and its slope by the same factor — 667 → 444 is a 1.5× braking,
measured as 50px → 75px of transform at a 315px pull. Leaving the cap alone keeps the far field as it
was and keeps the band from ever freezing the content outright, which `MaxResistance = 1` would.

**`resistancePull` is exactly what the transform carries**, and that is why this curve is as gentle as
it is. While `following`, what is on screen is `-scrollTop + transform`, where the scroll is composited
off the main thread and the transform is written from a main-thread read of it. Any disagreement
between those two comes out scaled by `1 - slope`: with no resistance the transform is zero and there
is nothing to disagree about, and the stiffer the curve, the more of the scroll the transform is
carrying and the more of that disagreement reaches the screen.

Two curves were tried and both were stiff enough to shake. An exponential
(`200 * (1 - exp(-pull/200))`) and `UIScrollView`'s own
(`(1 - 1/(pull*0.55/D + 1)) * D`) both put **48px** into the transform at 100px of pull, where this one
puts **5px**. Measured across the same return drag, the transform carried 197 → 177 → 158px under the
stiff curve and 19 → 6 → 0px under this one. On a phone that was the difference between a pull that
follows the finger and one that jitters while you move it.

So the feel of the *return* is the floor's business, not the resistance's. The resistance is what the
finger is holding, and it stays out of the way.

#### The boundary is latched

The edge a pull is measured from is captured when the excursion starts and held. Re-read every frame, a
page of history arriving mid-pull would move it and the resistance would jump with it — the content
steps under a finger that never moved. A pull is a gesture, measured from where the gesture began.

Which side of it the content is on is just the sign of `over`, and on a band collapsed to a single point
(a conversation shorter than its viewport: `min == max`) `over` crosses zero and continues on the other
side while the gesture goes on. There is no in-band position to return through, so nothing ends the
excursion at the crossing — the excursion ends when `over` is back at zero and the finger is up, like
any other. Measured on the rig on exactly such a chat: every scenario passes, and no band inverts.

An excursion cut short by a `scrollTo` to a new destination moves `boundary` and `over` by the same
delta, so the screen does not change, and the return runs from there — the displacement is carried off
from the new edge rather than dropped. A `scrollTo` flagged `reanchor` moves the boundary alone (§3.9).
An overscrolled edge is exactly what makes the list load the data that then re-pins it, so this fires
constantly.

#### Crossings too small to be excursions, and crossings by anything but a finger

Below `MinExcursionPx` (2px) a crossing is snapped away rather than taken over. Taking the scroller
over for a rounding error is what makes an edge feel sticky: a chat list is parked *against* its bottom
limit, crosses it by fractions of a pixel constantly, and every takeover would kill the gesture in
flight.

Only touch gets a band. A wheel notch, middle-button autoscroll (scroll events without wheel events,
so it is caught at the crossing, not in `onWheel`), the keyboard and a programmatic scroll all stop at
the edge, the way they do everywhere else on a desktop; a wheel notch during an excursion ends it at
the boundary. `isTouchMotion` carries a finger's flag through its fling and clears after `MotionGapMs`
(200ms) without scroll events, so a mouse does not inherit the band from a finger.

#### Precise pointing devices: the gesture is driven, not corrected

A trackpad — an Apple Magic Trackpad or any Windows precision touchpad — is not a wheel with smaller
notches. It reports pixel deltas at frame rate for as long as the finger is down, and then the *driver*
keeps reporting them for up to a second and a half of inertia. Stopping that at an edge by writing
`scrollTop` back after each one cannot work: the write lands a frame after the position it answers, at
full amplitude, so every frame shows a step out and a step home. Measured on a Magic Trackpad against
Windows Chrome, and reported first from a MacBook — the same jitter, and not a platform bug.

So a gesture that starts near a limit is **driven** rather than corrected: every event is cancelled and
the scroll performed by `followBy`, clamped into the limits. The browser never scrolls, so there is
nothing to disagree with, through the whole inertial tail.

Two properties of the engine shape this, both measured on the rig:

- **Cancelling is all-or-nothing per gesture.** A wheel sequence stays cancelable only while the handler
  cancels every event of it; the first one left uncancelled hands the rest to the compositor for good.
  Cancelling *every* event held for 100% of a 2.5s, 157-event gesture; cancelling only on reaching the
  edge got 1 cancelable event out of 61-107, because the approach to an edge always begins in-band.
  This is also why the listener is not passive and is never detached: a gesture is only ever offered as
  cancelable when a blocking listener is already registered for it.
- **A gesture's first event does not say where it is going.** It is routinely `deltaY: 0`, or a single
  pixel *the other way* before the real direction arrives. So `canOwnWheel` reads proximity only, never
  direction — a direction test there loses whole gestures, which is unrecoverable.

The claim is therefore made blind and given back later: once a decisive delta shows the gesture leaving
the edge behind — past `WheelOwnScreens` (2) screens from either limit — one event goes uncancelled and
the compositor scrolls the rest of it, which it does better than the main thread can.

Notched wheels are deliberately never driven. They don't shake at an edge, and a click at a time is an
animation the browser owns; `isPreciseWheel` separates them by `wheelDeltaY` being a whole multiple of
`WheelClickDelta` (120). A finger's own momentum reaches some engines as wheel events too, so a gesture
is never claimed while `isTouching` or `isTouchMotion` is set — that band belongs to the touch path.

**Known gap.** Middle-button autoscroll produces scroll events and no wheel events at all, so there is
nothing to cancel and it still jitters at an edge — much less than a trackpad did, and scoped out
deliberately. Closing it means drawing the excursion rather than snapping it, i.e. giving non-touch
continuous input the band, not extending anything here.

#### The scroller is never left switched off

A watchdog, on a timer rather than on the frame loop, hands the element back if the phase machine has
not advanced a frame in `LockWatchdogMs` (1.5s). It is re-armed by the loop itself, so what it really
asks is whether the loop is still running.

This is a backstop and not a mechanism — every phase ends on its own — but the failure it covers is not
a glitch: it is an app whose list cannot be scrolled again at all, reported from a phone as *"it doesn't
recover, ever"*. Nothing else can catch that, because everything else that would hand the element back
runs on the loop, which is the thing that failed.

#### One frame, one read, one write

**The loop is the only thing that draws the band.** Events decide which phase to be in and start the
loop; what that phase looks like is worked out and written once per frame, from a single read of the
scroll position. The first frame after an event seeds the loop's clock (`dt = 0`) rather than
integrating the wait for it — that wait is latency, not motion — which is why the carry observes over
the real time since the last read instead.

Drawing from the events as well — a `scroll` handler and an animation frame both calling the same
draw — means two writes per frame from two different reads of a position that an off-main-thread
scroller is moving underneath both of them. Whichever landed last before compositing decides the frame,
and which one that is varies. That is jitter, and it shows up on the finger-down pull, because that is
where the transform is carrying the most.

#### Fingers are not tracked; the scroll is

The controller reads no touch coordinates. `touchstart` on the element says a gesture is ours;
`touchend`/`touchcancel` on the **document**, in the capture phase, say it is over — and only when
`touches.length === 0`, so the last finger leaving is the release and a second finger arriving or
leaving changes nothing. Everything about *where* the finger is comes from `scrollTop`, which the
browser moves as it sees fit: the browser's own multi-touch handling, slop and coalescing are used as
they are, and there is nothing to disagree with.

The document listener is load-bearing rather than defensive: a touch keeps the target it started on,
and a virtualized list unloads rows out from under the finger, so a gesture that began on a row that
is gone by the time it ends delivers `touchend` to a detached node the element never hears. Missing
that latches `isTouching` on, and with it every clamp and the return stay disarmed — the list parked
off its own content with no way back. Because the listener is on the document, `onTouchEnd` also
returns early when this controller never saw the matching `touchstart`. And a finger that has not moved
the scroller for `TouchStaleMs` (3s) while `following` is treated as a `touchend` that never arrived
(§3.12).

#### What this replaced, and what it measured

The two models before this one are worth keeping on record, because their numbers are the bar. The
compensating model ended momentum once on entry (one frame of `overflow: hidden`) and then left the
scroller live, compensating drift in the transform and writing `scrollTop` back only past a 440px
guard. Before that, every non-WebKit engine held the lock for the whole return and wrote `scrollTop`
every frame:

| | lock held for | overshoot | return |
|---|---|---|---|
| Chrome desktop, holding the lock | **118 frames — the whole ~931ms return** | 119px | 931ms |
| Chrome desktop, compensating | 0 observed | 120px | 940ms |
| Android (Galaxy S25 Ultra), holding the lock | **62 of 97 frames sampled** | 70px | 851ms |
| Android, compensating | 0 of 97 | 73px | 851ms |

The current ordinary path holds the lock for two frames after a release and never through its return.
The iOS takeover is the deliberate exception: it holds a forced lock until its much faster transform
spring reaches zero (§4.4, §4.5). The delta model's own numbers are above and in §5.

A **frame** here — and everywhere else in this document — means one `requestAnimationFrame` sample
taken by the test harness, not a compositor frame. The desktop rows come from a headless Chrome run
well above 60fps and the Android rows from a 60Hz phone, so **frame counts do not compare across
rows**; the milliseconds do. Only the Android rows have a meaningful denominator: 97 samples is the
whole 1600ms recording window.

#### Sticky items under a transform

A `position: sticky` element is clamped during layout, against the *real* scroll position, and the
content's transform is applied after that. So the element rides the transform like everything else —
right while it is not stuck, wrong the moment it is. Measured here, a 120px container transform moved
every stuck element by the whole 120px; measured on a 300px bottom overscroll, a pinned conversation
header travelled **140px** relative to its neighbours, and on Android the author avatars did the same.

Note what is *not* the problem: a stuck header carried along by the rubber band is correct, and is what
a native one does. The problem is it moving relative to everything it is stuck to.

**The clamp moves, not the element.** Each declared sticky element's own insets are rewritten with the
shift in them — `top: base + shift`, `bottom: base - shift`, where the shift is minus whatever the
band's transform is carrying. The threshold then sits exactly where the transform is about to take the
element, so what the browser paints is what the same amount of real scrolling would have painted. Measured against a real scroll of the same size, worst case over every
sticky element on screen:

| the content moved | transform alone | with the shift |
|---|---|---|
| 120px up | 27px off | **0.00px** |
| 120px down | 109px off | **0.00px** |

And under a live band rather than a synthetic transform. The position is the pair
(`scrollTop`, transform), so rewriting the same frame as pure scroll — all of the transform moved into
`scrollTop` — must move nothing on screen, sticky elements included. With a finger held 66.3px past the
bottom edge:

| | content | sticky, worst on screen |
|---|---|---|
| with the shift | 0.25px | **0.25px** |
| without it | 0.25px | 27–66px |

The 0.25px is the scroll write's own rounding, and it moves everything equally. The range in the last
cell is content-dependent: 66.25px where the stuck elements are conversation headers with the whole
transform to give up, 27.25px where they are avatar badges that hit the bottom of their own message
group first.

Four things follow from doing it this way:

- **Nothing has to decide which elements are pinned.** That was the hard part, and three tests for it
  were all wrong — against the parent, which for the header moves with it; against `offsetTop`, which
  the engine reports already shifted by the stick; against the element's own inset, which mis-fires as
  soon as a correction has moved it. The browser does the clamping here, so even the awkward case is
  exact rather than approximated: an element stuck now that would not be after the move un-sticks by
  the right amount, and one riding the bottom of its containing block keeps riding it.
- **It covers the *hidden scroll* for free** — the distance the scroller has run outside its own band.
  That part is already in the layout position the browser clamped, and the shift is exactly what the
  transform then does to it, so one number answers for both.
- **Nothing is written in band.** The insets are read from the element and written back with the shift
  in them the first time it has to carry one, and removed when it stops, so the value read is always
  the element's own stylesheet rather than a stale copy of it — a media query or a class may have
  changed it in between. An inset the stylesheet never set reads back as `auto` and is left alone: an
  unset inset is a clamp the element does not have. (Verified on Blink; §7 lists the WebKit check.)

Elements are **declared, not discovered**: the consumer marks them with `vl-sticky` (`StickyItemClass`),
and the chat view does that through the presence-class system — on the item for a conversation header,
on the avatar itself for an author badge, since there only the picture is sticky. Discovery would mean
a computed style for every descendant on every render. One consequence worth knowing:

- **`classList.toggle(name, force)` is absolute.** Two presence rules writing one class means the
  second one switches off what the first set whenever its own match is absent. One rule with a
  multi-selector match, never two rules with the same class name.

**Travel relative to the content is the wrong measure of this**, and it is worth saying because the
previous mechanism was built around it. Holding every sticky element at a fixed offset within the
content makes that number zero — by un-sticking them all for the duration. But a stuck element is
*supposed* to move relative to the content: that is what being stuck means, and it is what a real
scroll of the same size does. So the number to drive to zero is the difference from that, which is what
the tables above measure. On the same 300px gesture the worst relative travel is 48px with the shift
and 87px without it, and the 48px is correct: it is an avatar badge sliding within its own message
group until the group's bottom stops it, exactly as a real scroll would slide it.

In band nothing is written at all: the shift is zero, the elements carry no inline insets, and the
header pins at exactly its `-17px` inset. Verified mid-band on a 66.3px excursion — every element
carrying an inset of `base - 66.26px` — and after it, with every inline inset removed again.

### 3.8 Spacers, the end anchor, and the conversation that fits on screen

*The spacers reserve scroll space, hold the skeletons, and trigger loading. The end anchor is blank
space under the newest message that the bottom limit adds explicitly. A conversation shorter than the
viewport is a special case: its band inverts, so it rests with its first message at the top and the
anchor is not honoured.*

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

The same case leaks into three other places, and all are deliberate: a chain that fits counts as being
at the End edge for pinning, however far the anchor says it is; it counts as "the newest message is
visible" for read tracking; and the initial reveal asks only that its first item is not clipped off the
top. Without the first two an End-pinned list would settle on Start and stop following new messages
until the conversation outgrew the viewport.

### 3.9 Re-anchoring

*A render that changes the model holds one on-screen item still by moving `chainStart`. By default
that is the item at the viewport top; a `data-vl-hold` control overrides it with the clicked item, and a
`data-vl-anchor` element overrides both with a rendered screen position held through the animations
that follow. A `scrollTo` flagged `reanchor` is the same idea for the controller: the view renumbered,
not moved.*

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

**With nothing surviving after the viewport, the gap is the rest of the chain.** That is the same
collapse at the *end* of the list — a live conversation folding into its summary, which is where a live
conversation always is, and a long newest message shrinking under a view parked at its tail. The gap
used to be `Infinity` there, on the reasoning that no following item constrains anything, so the raw
offset was always kept and the view fell as far past the chain as it had been deep inside the block.
The chain's own end is the constraint, and it is what bounds the gap. Reproduced by growing the newest
message to 4,000px, reading 2,000px into it, and taking the 4,000px away: the view stayed 2,800px past
the band with `phase: 'in-band'`, and the chat was blank until something produced a scroll event.

**Interactive anchors** are the opt-in override. Only controls marked `data-vl-hold` arm one — plain
taps, links and text selection must not affect anchoring — and both a click and a `touchend` on the
container count. `always` holds the item and drops the pin, a deliberate "read history" action;
`keep-edge` holds only when the list is not pinned, since a pinned list absorbs the size change through
its edge re-pin instead. The clamp from above applies: when the viewport is deeper into the block than
the block is tall, the item itself is what the user gets back.

**Screen anchors** are the second override, and the stronger one: an element whose position on screen
is to survive the render the interaction causes. It beats an interactive anchor when both apply, and
it is addressed one of two ways. An element marked `data-vl-anchor="<id>"` is one the caller promises
to render again under that id. A control marked `data-anchor="below"` instead anchors the first
content **item below itself, by key**: the control reveals rows above that item (the live block's
Show-more pill), and the item below the insertion is what must keep its rendered position — the rows
grow upward and everything from the anchor down stays put.

A key-addressed anchor differs from an id-addressed one in when it is spent. It records the content
key directly above its item at the click, and is **placed only by the render that changes that** — the
one that actually inserts the revealed rows. A live chat keeps streaming renders in while that one can
be seconds away on WASM, and placing on whichever render lands first would start (and retire) the
per-frame watch before there is anything to hold; those renders pass through and re-anchor by the
viewport top as usual. Its item leaving the item set releases it — an id may be rendered again, but an
item that is gone has nothing to come back as, and the live block folding away must free
`checkPosition` (§3.6) rather than sit behind a stale hold. A full replacement releases it the same
way, since `reanchor` — the path that would notice the vanished key — does not run there.

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
four applications, 124px of drift. What holds the element through that growth is `watchScreenAnchor`:
per frame, by a scroll write, until the element has been still for `ScreenAnchorStillFrames` (12)
frames and no animation runs — the model reaches the settled heights before the DOM does, and
re-laying out to them while the DOM is still transitioning moves the whole chain by what is left to
grow (measured at 349px, arriving one frame after the tracker went quiet). A correction that runs past
`maxOverscroll` is a loop feeding itself, and releases the anchor. The interaction also unpins, which
is what gives the chain its top anchor (§3.2) - a bottom-anchored one moves everything above a growing
block.

Measured on a conversation header in view, before → after: collapse 16px → 0px, expand 124px → 0px.

A trusted scroll clears either anchor at once. The timeout depends on what the anchor is waiting for:
a screen anchor still waiting on its render gets `ScreenAnchorRenderTtlMs` (10s) — on WASM that render
can be seconds away — and everything else, an interactive anchor and a placed screen anchor alike,
expires `InteractiveAnchorTtlMs` (2s) after the click or the placement.

#### Re-anchor writes are flagged

A `scrollTo` during an excursion to a target other than the boundary re-aims the excursion: `boundary`
and `over` move together, the screen does not change, and the return runs from the new edge (§3.7).
That is right for an authoritative scroll to a new position.

A re-anchor is not that. It is the same view, renumbered — the re-centre is one. So it passes
`{ reanchor: true }`, and the boundary alone moves by the delta while `over`, the carry and the
transform carry on untouched.

This matters constantly, because **reaching an edge is exactly what makes the list request data**, so
the answer lands during the bounce. Measured, before either treatment existed: a plain write mid-bounce
cut a 592ms / 117-frame return to 162ms / 16 frames with a 90–163px snap. That is the "it just jumps to
the final position as soon as you release the finger" symptom.

### 3.10 Item heights, appearances, and stability

*`ItemHeightController` owns every item's `style.height` and animates changes over 150ms after a
100ms settle delay; the model always reads the settled height. A key diff decides which arriving items
grow in and from what; at most three animate at once. `StabilityTracker` is what everything that
repositions the list asks before doing so. Animations can be blocked for the duration of a promise,
and three things do it: a jump, a list's first 500ms, and a conversation collapse.*

`ItemHeightController` owns `style.height` of every item in a list that animates heights (the chat
view; the test page can turn it off). What the geometry model reads is the **settled** height, so an
animation in flight changes how the list looks without changing where it thinks anything is —
invariant 10.

Three things make this harder than it sounds, and each has a defence:

- **A render can replace the element under a key.** The item is the same; its element is not, and the
  height state is keyed to the element. Rebuilding that state from scratch loses the one thing the next
  transition needs - what the item was showing - and a rebuilt state is neither appearing nor
  controlled, which is exactly what `scheduleNow` refuses to animate. So the replacement adopts where
  its predecessor stood, instantly and without a transition, and the change that follows is a change
  from that height rather than an arrival at a new one. Mid-transition that is the height the old
  element rendered, not the one it was written to: adopting the target would land the swap at the end
  of a movement the reader is still watching. A conversation summary re-renders its
  element on every update; without this it stepped between heights - measured on a live call at 455px
  and 85px alternating about once a second, every write unanimated while the card was on screen.
- **An item's own box says nothing once we drive it.** The intrinsic height is read from the item's
  single content child, plus everything the item is responsible for reserving around it: its own
  padding and border, and the content's margins. An item that renders two elements is a bug the
  controller logs, because the second one would be sized as if it were not there — clipped, and
  unreachable by a scroll-to. A render that keeps the item but swaps what it renders inside is caught
  by a `MutationObserver` on the container, or the height observer would measure a detached element
  forever.
- **Blazor rewrites the whole `class` attribute** whenever an item's own classes change, which the
  edge classes do as the loaded window moves. That silently drops `c-height-controlled` and
  `c-height-unsettled`, leaving the item with a written height and none of the rules that make it mean
  anything. The same observer re-asserts them, ignoring the controller's own writes so it cannot loop
  with itself, and re-measures — a class can change the item's padding without the content resizing.
- **A `transitionend` can simply not arrive** — the element detached mid-flight, or nothing ever
  rendered it, so no transition started. Every "is animating" claim is therefore backed by a deadline
  as well as by an event, in both the controller and `StabilityTracker`.

**Appearances.** `applyAppearances` classifies everything a render added the way a text diff would: a
key standing where a removed one stood is an *edit* and grows from the height of what it replaced;
anything else is an *insertion* and grows from zero. An addition that merges *outside* the outermost
surviving keys is neither — that is the loaded window being extended, and growing a page of arriving
history out of nothing heaves the list — so only additions landing inside the old range animate. The
pinned edge joins the diff as a sentinel symbol, which is what makes a message appended while the list
is parked at that edge count as inside the range rather than as an extension. Nothing animates for
the first `AppearanceQuietMs` (300ms) after the list is revealed, or opening a chat would play the
whole first screen in; nothing animates on a full replacement, or when a jump is pending — a jump
has to measure its target against where the content will be, not against items parked at zero.

**And nothing animates that was on screen a moment ago.** The diff can only compare this render with
the last one, so an item the source *dropped and put back* — which is what a conversation block
materializing around messages that were already there does — reads as an insertion and would grow from
nothing under the reader's eyes. So the list keeps the keys that left a recent render, with the height
they had, and an addition whose key is in that memory is not an appearance at all
(`recentlyRemoved`, `ReappearanceMs` = 1.5s). The memory is pruned by age only: the render that brings
a key back rebuilds the item list before appearances are decided, so dropping it there for being
present again would drop it exactly when it is about to be needed.

The alternative — matching on an *intrinsic* identity the app supplies, so the same content is
recognised under a different key — is not implemented, because nothing was found that renames a
message. `ChatMessageKey` is `<lid><suffix-for-kind>`, and a chat entry keeps `Kind = None` whether or
not it sits inside a conversation, so its key is the bare lid either way. Measured on a live page:
three collapse/expand cycles and a Summarize off/on cycle produced no renamed key at all.

**Which end of the chain is anchored is which end has to stay put.** The model is placed from *settled*
heights, so while a transition runs the rendered chain is shorter than the model says it is. Anchored by
its bottom — which is what keeps the newest content flush while the list follows it — the container's
top edge hangs lower by exactly that difference, so everything above an animating item goes down with it
and comes back as it lands. Measured: a 40px growth below the reader moved them 40px, then 33, 24, 16,
7, 0. On a narrow viewport it fires far more often, because a streaming message gains a whole line.

So reading above the end (`pinnedEdge == null`), the container is placed by its `top` instead — the same
line the forward direction uses. A chain that grows downward from a fixed top leaves everything above
the growth alone, held by the browser every frame with nothing here to compute. The two placements are
one piece of arithmetic seen from opposite ends: a bottom-anchored container plus the render's shortfall
*is* `chainStart − startSpacerSize`, since the container's rendered height is the spacers plus what the
chain has actually rendered. They therefore agree wherever nothing is animating, and the regimes meet
with no step at the pin.

Measured after: **0.00px on every frame** of a 40px growth and of an 80px growth below the reader, and
0.00px across two items growing 60px 30ms apart.

**The pin changing is itself a re-placement.** The two anchors agree only where nothing is animating, so
a pin that changes mid-transition has to move the chain to the other end there and then —  nothing else
writes the container's position between renders. Left alone, the old anchor stays and drags the reader
over the rest of the animation: measured at 66.6px against a 120px growth, when scrolling away from the
end while it was still growing. The two directions are not symmetric (`repinChainAnchor`). *Letting go*
of an edge holds what is on screen: the chain's rendered top is what the new anchor is given, because
the reader is looking at it and a step under their finger is the whole complaint. *Taking* an edge hands
the placement back to the model, because the model is what puts the newest content flush with the fold,
and the re-pin the change schedules lands it inside a frame. That last case is why subtracting the shortfall from
the bottom is not equivalent in practice: the `chainEnd` it subtracts from is rebuilt by a 64ms throttle
while the shortfall moves every frame, so a second change landing inside that window is counted against
a model that has not caught up with the first — measured at **66.75px** of reader displacement, against
0.00px for the anchored top.

**At most `MaxAnimatedItems` (3) items animate at once**, in either direction, and everything past that
is written to its real height on the spot. Expanding a conversation turns one item into a whole thread;
animating all of them buys nothing over animating the first few — the direction of the change is
already obvious — while costing a full-height transition per item. Two details matter:

- **The slots go top to bottom, in both render directions.** Appearances get theirs in call order, and
  `applyAppearances` walks the diff in chain order; anything else is sorted into document order when
  the batch ends. Document order is top to bottom either way — the container is a plain flex column,
  and rendering in reverse anchors the chain differently without reordering it.
- **A slot is counted, not tracked.** An item holds one while it is parked at an appearance height,
  waiting out its settle delay, or transitioning. A counter would have to be given back along a good
  half-dozen paths — settling, being applied instantly, suspension, the item going away — and one
  missed release would silently stop the list animating anything ever again.
- **An appearance outranks a height change already in flight**, and takes a slot from one when none is
  free (`takeSlotFromChange`). First-come was the wrong order for the case that matters most: a live
  conversation is never still — its card and its newest transcript are both mid-transition whenever
  expand is pressed — so the messages the expansion reveals lost every slot to a line of transcript
  finishing its growth. Measured with two items already transitioning: **1 of 3 revealed messages grew
  in before, 3 of 3 after**, with the peak number animating at once unchanged at 3. The change that
  gives up its slot is the one nearest its end, so what it forfeits is the tail of a transition rather
  than the whole of one; an item still waiting out its settle delay is taken only when nothing is
  transitioning, since it has shown none of its change yet.

Measured on expanding a conversation, items with a height in flight on the same frame: **5 with the cap
lifted, 3 with it in place.** Collapsing the same conversation animates none — it removes items rather
than shrinking them — so the shrink side of the budget rests on the gate sitting in the one place every
height change passes through, not on a measurement.

Per item, `data-vl-h-transition` names the only animation the item wants — `"appearance"` for one whose
height the app writes once it is on screen, `"change"` for one that is always present and grows into
place, `"none"` for neither. Absent, both run. `data-vl-h-delay` sets the item's own settle delay.

`"change"` exists for items that are never really arriving. A conversation card is one item in both
forms of its block — collapsing swaps the expanded header's element for the card's under the same key —
so treating that as an arrival grows the block at the moment it was asked to shrink.

**Stability** is the tracker both use: a height write in the settle delay, a running transition, and a
recent scroll all count as "in flight", and everything that wants to reposition the list asks here
first.

#### Blocking animations for the duration of a promise

`suspendUntil(promise)` blocks new height animations until the promise settles; changes that arrive
meanwhile land instantly. Transitions already running are left to finish — for a jump they are exactly
what it is waiting out. Blocks **stack**: the count drops only as each outstanding promise settles, so
two overlapping callers cannot cut each other short, and a *rejected* promise still releases, so a
block can never wedge.

Three callers today:

| Caller | Window | Why |
|--------|--------|-----|
| A pending jump | until `whenNoAnimations()` | starting more would keep pushing the moment it waits for further away |
| A newly created list | 500ms | every item is "appearing" at once, and would grow in from nothing on the first frame. The list is keyed by chat id, so this is also a chat view's first moments |
| A conversation collapse | 200ms | see below |

#### Why a collapse has to arm, and cannot just be observed

Collapsing a block removes its items; what replaces them lands on keys the list last saw somewhere
else, and the diff has no way to tell that from an arrival — so it grows them in on the one gesture
that only ever makes things smaller.

The block cannot be armed from a `MutationObserver`, because `beginAppearance` both decides *and
commits* the park — it writes the start height and forces a reflow — synchronously inside
`applyRender`, before any observer sees the markup. Arriving after that can only downgrade the
transition to an instant write once the settle delay expires, which trades a smooth grow for the item
sitting collapsed for ~100ms and then snapping.

So the click arms and the render decides. Each collapsing toggle carries `data-collapse-arm` with its
conversation id; every collapsed header carries `data-vl-render-script-collapsed-header`. The click is
the only point early enough to arm, and the render script — which runs inside `applyRender`, ahead of
`applyAppearances` — spends the arm when the collapse render actually arrives. A header render that
was not armed, which is what scrolling one into view produces, does nothing. The arm expires after 1s,
so a click that never reaches a render leaves nothing behind for the next appearance to spend.

The signal is deliberately *not* inferred from `data-vl-hold="keep-edge"`, which every shrinking
control happens to wear today: that attribute means "keep the edge pin if there is one" — an anchoring
instruction — and reading it as "this collapses" would break quietly the first time the two stop
coinciding.

### 3.11 The initial reveal

*The wrapper is hidden until the chain is confirmed placed, or 1.5s has passed. Revealing does not
re-derive the pin.*

The wrapper is `visibility: hidden` from the markup (`c-initially-hidden`) and revealed with an inline
`visibility: visible` — inline beats the class, so later renders that keep the class stay visible.
`visibility` rather than `display`, so items still lay out and measure while hidden.

`InfiniteList` polls by rAF until the content is *placed*: the `scrollToKey` item is on screen, or the
preferred edge is within `RevealEpsilon` (8px), or — for a chain that fits — the first item is not
clipped off the top; an empty list counts as placed once both ends are known. A `RevealTimeoutMs`
(1500ms) backstop reveals it regardless. Revealing deliberately does **not** re-derive the pinned
edge: on the timeout path the content has not finished settling, and re-deriving there would drop
the pin the initial placement just established, leaving a freshly opened chat at the bottom but not
following it.

### 3.12 Rules that came from painful debugging

*Four things that look like they should work and do not: freezing `style.height` does not freeze
`scrollHeight`; a 4M length does not survive a style round-trip; a boundary at the scroller's own end
cannot rubber-band; and a touch keeps the target it started on.*

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

`touchend` and `touchcancel` are listened for on the **document**, in the capture phase (§3.7). A
touch keeps the target it started on, and a virtualized list unloads items out from under the finger —
so a gesture that began on a row that has since been recycled delivers `touchend` to a detached node.
The element would never hear it, `isTouching` would latch on, and with it every clamp and the return
stay disarmed: the list ends up parked off its own content with no way back.

There is also a `TouchStaleMs` (3s) backstop, checked while `following`: if the finger has not moved the
scroller for that long, whatever we are waiting on is not a gesture. *Known limitation:* a genuinely
resting finger — a long press while holding a pull — trips it.

---

## 4. Browser, OS and device quirks

*Behaviours of a specific engine or device, not of this code, each of which forced a design decision.
The load-bearing ones: Chrome clamps element heights at 2²⁵ physical pixels; `scrollTop` is not a
reliable iOS inertia stop; a gesture that begins on an `overflow: hidden` element is not scrolled; and
one frame of `overflow: hidden` does nothing at all.*

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

### 4.3 A `scrollTop` write is not an iOS inertia stop

Older WebKit often ended momentum when `element.scrollTop` reached UIKit as a content-offset write.
Measured on an iPhone after a fling had slowed, a 300px write landed exactly and the position stayed
still for the next 40 frames. The same mechanism was already unreliable at speed: five consecutive
boundary writes were ignored while the scroll ran 493 → 745px past the edge, and only the sixth landed.

Safari/iOS 27 removes `scrollTop` assignment from the operations that stop inertia, so even the slowed
case cannot be a design dependency. The current code therefore does not use WebKit self-writes as
`killMomentum`. It force-locks overflow, flushes that state, and only then snaps the native coordinate
as one half of the compensated FLIP (§3.7, §4.5).

The read-back rule still applies everywhere. A catch, settle, cancel or handoff uses the value the
engine actually accepted, never the value requested, and the transform gives up or takes on only the
measured visual delta. A refused or partial write therefore cannot become a jump.

### 4.4 A gesture that begins on an `overflow: hidden` element gets no scrolling — on Chrome too

An element that was `overflow: hidden` at `touchstart` is not scrolled by that gesture. This was
documented as WebKit-only; it is not. Measured on Android Chrome: a finger landing on a return that had
1.6px left to run, with the lock still held from ending the previous fling, dragged 397px without
moving `scrollTop` by a pixel. Releasing the lock at `touchstart` is too late — the compositor has
already decided.

The ordinary non-iOS path therefore holds the lock for exactly two frames and releases it whatever
phase that leaves. The iOS takeover deliberately accepts the tradeoff and holds its forced lock through
the transform return, because that is the reliable inertia stop. Releasing it synchronously and forcing
layout in `touchstart` is still too late on iOS: a gesture that landed while locked remains unable to
scroll. Emulating that caught gesture and its release inertia was tried, but it became a second scroll
engine and could fling a short chat through its collapsed band to the opposite side. The chosen
mitigation is a much faster, non-oscillating spring, which halves the interval in which a catch can land.

### 4.5 Locking overflow ends a fling — but not in one frame

Making a scroller briefly unscrollable is the only thing that ends a fling on Chrome, and **one frame
of it does nothing at all** — the compositor, which is where the fling runs, never sees it. Measured in
Chrome against a real touch fling, as scroll still travelling from the moment the technique was applied:

| technique | leaked |
|---|---|
| nothing (control) | 1057px |
| `overflow: hidden`, **1 frame** | 1385px |
| `overflow: hidden`, 2 frames | 21px |
| `overflow: hidden`, 4 frames | 10px |
| `overflow: hidden`, 100ms | 16px |
| `scrollTop = scrollTop` | 1416px |
| `scrollTo({ behavior: 'instant' })` | 1915px |
| `touch-action: none` | 1354px |

The spread among the ineffective ones is fling-to-fling variance, not signal — the flings themselves
ranged 646–1007px by the time the technique landed. Only the held lock separates from the control.
`-webkit-overflow-scrolling: auto`, the classic iOS trick, stopped working in iOS 13 and is a no-op in
Chrome. A self-write is worthless on Chrome and unreliable or obsolete as an iOS stop (§4.3), so the
current implementation depends on neither.

On the ordinary path, `killMomentum` holds the lock for `MomentumKillFrames` (2) and then releases it,
whatever phase the controller is in. Even a held lock leaks ~20–50px before it takes effect; the
transform absorbs that like any other scroll motion, and after a release it is what feeds the carry
(§3.7). `?vllock=0` / `?vllock=1` exercises this choice in the rig.

The iOS takeover does not call that ordinary switch. It force-locks regardless of `vllock`, flushes the
style before snapping to the boundary, and holds the lock until the exact transform spring is home.
That both stops native inertia reliably and prevents a second, suspended native trajectory from
resuming underneath the spring.

#### What the standalone iOS property lab established

The iPhone lab compared nine cases on the same long fling: native; overflow lock only; locked and live
rAF `scrollTop`; native smooth `scrollTo`; content `translate3d` by rAF and by WAAPI; content `top` by
rAF; and scroller `translate3d` by rAF. It also tried freezing at release, edge entry, a speed threshold
and the predicted minimum absolute speed.

The `scrollTop` / `scrollTo` animation variants were visibly buggy. The property-driven variants —
content transform, content top and scroller transform — were smooth, with both rAF and WAAPI content
transforms viable. Content `translate3d` driven by rAF was chosen because it is compositor-friendly,
fits the controller's existing single transform owner, and lets native leaks be compensated in the
same frame. WAAPI remains a possible later implementation, not a different model.

Changing the freeze schedule did not remove the handoff artifact: delaying to the predicted minimum
absolute speed merely moved the out-of-sync frames. The visible improvement came from changing what
was animated, not when: stop native motion once, FLIP its rendered position into content
`translate3d`, then let one exact spring own the return.

### 4.6 WebKit can leave a composited scroller unrastered after a `scrollTop` write

On iOS the chat view lands on the new position showing nothing, and only the next touch brings it back.
Forcing layout (`offsetHeight`) does not help — layout is not paint. Invalidating the layer does, so
after a non-smooth programmatic scroll the controller applies a **sub-pixel transform nudge** (0.01px)
for one frame. It is composed into the same transform as everything else, and it stays out of the
rubber band's way in both directions: it never starts while a finger is down or an excursion is open,
and it only clears a value it still owns.

### 4.7 Programmatic scrolling is visibly jittery on Android

Driving the scroll position from JavaScript at frame rate is visibly jittery on Android even when every
frame lands on time — one missed frame in a per-frame write stream is visible. Nothing in the list
*animates* `scrollTop`: the only transform left is the rubber band's, and it shows and hides the native
scroll rather than interpolating anything (§3.7).

Two things write the scroll position repeatedly (§3.6): the follow, while the pinned edge is actually
moving — a message arriving, an item's height animating — and the screen-anchor hold, for the length of
an expand. Both write at most once per frame, and neither runs while the user is scrolling. It is
a correction and not an animation: every write is the whole remaining gap as measured in that frame, so
a missed frame costs lateness rather than a step.

`tools/virtual-list-rig/follow.mjs` drives both paths head to head — 2px of correction per frame for
six seconds each, recording a real item's on-screen position. In Chrome they are indistinguishable:

| | frames | mean step | still frames | step-to-step change (p50 / p90 / max) |
|---|---|---|---|---|
| `scrollTop` per frame | 719 | 2.00px | 0 | 0.00 / 0.00 / 0.00px |
| `transform` per frame | 686 | 2.00px | 0 | 0.00 / 0.00 / 0.00px |

Which is the engine this section is *not* about. Point the same script at a phone's debug port and it
answers the question properly; until someone does, the Android case stands as written and is listed
in §7.

### 4.8 iOS moves the *document* when the keyboard opens

Focusing the message editor scrolls the page itself, not the list. On iOS the list responds to a
viewport resize by pinning `documentElement` and `body` to `position: fixed` (and `overflow-x:
hidden`) while `visualViewport.offsetTop` is non-zero, and releasing them when it returns to zero.
Without it the editor ends up behind the keyboard.

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

For those, `adb shell input swipe` is the tool on a phone — CDP's `Input.dispatchTouchEvent` travels
over adb and arrives too unevenly for Chrome's velocity tracker to read as a throw, so it scrolls but
never flings. Page coordinates map to screen coordinates as `chrome + cssY * devicePixelRatio`, where
`chrome = screenHeight - innerHeight * devicePixelRatio` — about 532px on a 3120px-tall device, so
ignoring it puts every gesture in the URL bar.

### 4.11 Synthetic touch on desktop Chrome: what flings and what does not

CDP's `Input.synthesizeScrollGesture` delivers `touchstart: 1, touchmove: 0, touchend: 1` on this
page — no movement at all. `Input.dispatchTouchEvent` sequences do reach Chrome's real input pipeline
once `Emulation.setTouchEmulationEnabled` is on, and a fast drag followed by an immediate `touchEnd`
produces a genuine compositor fling — **but only if the moves arrive close together**: moves more than
~25ms apart get the fling dropped intermittently, so the rig fires them 12ms apart without awaiting
each acknowledgement, and treats a single zero coast as noise, not evidence. Also bring the tab to the
front (`Page.bringToFront`), check that the point you are aiming at hit-tests into the list, and give
the page a mobile viewport — at desktop width the chat view is not the element a touch scrolls.

### 4.12 Blazor render modes

The page runs Server, WebAssembly, or Auto (which upgrades to WebAssembly). After a rebuild, a plain
reload in WASM mode keeps serving the cached hashed bundle from the service worker — a hard reload with
caches cleared is required, or you will be testing the old code and drawing conclusions from it.

---

## 5. Measuring this stuff

*The overscroll model is verified by the rig — real touch gestures into a Chrome debug port, judged
mechanically against the rules in §3.7. For the list itself, the only honest measure is the on-screen
position of a real item, sampled every frame; `scrollTop` alone is meaningless here. Two checkers are
built in, and every measurement trap below produced a confidently wrong answer first.*

**The rig.** `tools/virtual-list-rig/` (see its README) drives fourteen scenarios into a Chrome debug
port: pulls and throws at both edges, swing-back, catch, catch-and-drag, up-down, brake, repeated
gestures, a control fling, a finger-up fling entering the edge, forced native leakage during takeover,
and cross-and-back. It records controller frames plus the phase at each touch event and judges rules:
no inverted band, no gesture beginning with unexplained transform debt, every excursion ending legal,
the finger followed through the curve's slope, no unexplained transform step, and no consistency-checker
violation.

`rig.mjs all <port>` runs the ordinary path, `nolock` removes its two-frame kill, and `takeover` forces
the iOS handoff on Chrome. `follow.mjs` is separate and answers one question: whether the pinned edge's
per-frame `scrollTop` write is as smooth as the transform it replaced (§4.7). The judge additionally
asserts that the composed transform minus the band's own share is zero on every frame, which is what
makes "the list writes no transform" a checked property. `soak.mjs` runs a long random mix. Run a long chat and a chat whose band collapses to a point;
the iPhone is still required for off-main-thread feel, because forced takeover in desktop Chrome proves
the state/geometry rules but cannot reproduce WebKit's compositor timing.

`scrollTop` is not a valid measure of what the user sees. The wrapper, the container offset and the
transform all move content without moving it.

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
  request and render activity, window sizes and mean item height, refreshed every 200ms. It is not
  free: positioning itself reads `getBoundingClientRect()` and `documentElement.clientWidth`, and it
  writes its own styles between one list's read and the next one's, so with several lists enabled each
  tick costs a synchronous layout. Debug-only and off by default, which is why it is left that way.

Measurement traps worth knowing, each of which produced a confidently wrong answer first:

- **`scrollTop` alone is not the visible position.** The chain moves under it — a re-anchor, a
  re-centre, a page of history arriving — and each of those changes `scrollTop`'s meaning without
  moving anything on screen. What the user sees move is `scrollOffset - chainStart`; in a harness that
  is `scrollTop - container.top` (or `+ container.bottom` in reverse), with the band's transform taken
  off if one is open.
- **A gesture measured one frame past the release is measuring the return.** The rig's "did the content
  follow the finger" rule compared the frame at or *after* `touchend`, by which point the spring has
  already moved the content back — or, entering the band, further out. On `catch-drag` that turned one
  correct 93px-of-120px drag into 31px, and the verdict then depended on which frame the release landed
  next to: the same gesture failed 4 times in 5 on one build and 2 in 5 on another, with nothing
  different about the drag. The finger's own travel ends at the release, and the sample has to as well.

- **A hidden tab gets no rAF.** A Chrome window behind another window reports
  `document.visibilityState === 'hidden'` and stops firing animation frames, so every rAF-driven probe
  hangs and every recording comes back empty. `Page.bringToFront` does not fix it. Run measurements in a
  dedicated `--headless=new` instance or a visible window instead — cookies can be copied across from
  the visible browser with `Storage.getCookies` / `Network.setCookies`, which is enough to reach an
  admin-only test page.
- **A recorder that samples on its own rAF can run before the controller's write** and see the new
  `scrollTop` against last frame's transform — a phantom step of exactly the scroll delta on every
  moving frame. The rig's recorder samples from the controller's `onTransform` and falls back to rAF.
- **A teleported scroll position is read as a huge velocity.** The controller estimates the scroll's
  speed from consecutive `scrollTop` values, and a fling arriving at an edge seeds the bounce from it —
  so a harness that reaches an edge by writing `scrollTop += 3000` in a loop hands the carry thousands
  of px/s and gets a bounce it did not earn, or, if the last write lands in the same millisecond, no
  velocity at all. Either walk out of the boundary over several frames the way a real fling does, or
  drive the scroll through `scrollController.scrollTo`, which zeroes the estimate for its 300ms
  suppression window; `resetMotionTracking` exists for callers that redefine what `scrollTop` means.
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

*The sidebar chat list. Position is a pure function of index, so nothing ever needs correcting: no
controller, no model beyond arithmetic, spacers that cover the unloaded ranges exactly, a real
scrollbar, and one `scrollTop` writer.*

**What it shares**, all of it in `virtual-list.ts`: the Blazor round trip (a `MutationObserver` on the
render index and the container, the render-state JSON parsed only when its index matches the attribute,
`RequestData` out, `UpdateItemVisibility` back, the request guard with its 2.5s timeout and 1s retry),
the DOM handles (wrapper, container, both spacers), and the initial reveal.

**What it deliberately does not have:**

| `InfiniteList` | `FiniteList` |
|---|---|
| a `ScrollController`: a band narrower than the scroller, resistance, a bounce and a floor | none — the browser's own scrolling, unmodified |
| a render direction | always natural |
| a 4M wrapper with an absolutely positioned chain floating in it | the container stays in flow (`position: relative`), so the wrapper's height is content-driven |
| spacers as a rough reservation | spacers that cover the unloaded ranges **exactly** |
| no scrollbar, because the range is mostly empty | a real scrollbar, which is therefore honest |
| re-anchoring, edge pinning, re-centring, stranded recovery | none of it |
| heights measured per item and animated | one measured item height plus one measured separator height |
| `scrollTop` written for jumps, re-placements, re-centres, direction switches, stranded recovery, and once per frame by a follow or an expand hold | one writer only: an explicit `scrollToKey` |

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
  plus one row gap. Items carrying a separator are irregular and would poison the estimate. The marked
  item is re-found on every render and is simply the first one in the DOM, so which row it is changes
  as the window moves; what keeps the value from flapping between two rows that differ by a sub-pixel
  is `ItemSizeEpsilon` (0.5px) alone, below which a change is ignored outright — every spacer rewrite
  is a chance for the browser to clamp `scrollTop`.
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

*What is known not to be done: a load that moves the limits under a dragging finger drops the band; the
two per-frame `scrollTop` writers are unmeasured on a device; the `auto` inset reading is verified on
Blink only; `reanchor` holds the viewport top in both directions; a fling read through loading history
is unmeasured; a touch that lands during the short iOS lock cannot scroll in that same gesture; and a
long hold trips the stale-touch backstop.*

- **A load that moves the limits outward under a dragging finger drops the excursion.**
  `ScrollController.onScroll` asks `getViolatedBoundary` where the scroll is, and a limit that moved
  past it while the finger was still pulling answers "in band" — so `endPhase(null)` runs, `over` goes
  to zero and the band transform is discarded in one frame. Measured with the soak's 60 gestures: six
  or seven occurrences per run, with `over` up to 180px, which is ~24px of transform vanishing in a
  frame; the rig's `bad-ends` catches it only when the next frame happens to be outside the limits, so
  a soak run passes or fails on timing. **It predates the translation work** — a soak against the
  previous commit reproduces the same six occurrences and the same `bad-ends 1`. The fix is for the
  crossing to be re-aimed rather than abandoned, the way `scrollTo`'s `reanchor` path already does it:
  a limit that moves under an open excursion should move the latched boundary with it.

- **Two things write `scrollTop` per frame, and §4.7 is about exactly that.** While an item's height
  animates, the pinned edge moves every frame, so the follow writes every frame; an expand hold does
  the same for the length of its animation. §4.7 records, from a device, that a per-frame write stream
  is visibly jittery on Android. Neither is an animation (each write is the whole remaining gap,
  measured in that frame, so a missed frame is late rather than stepped), neither runs while the user
  is scrolling, the rig sees no steps in Chrome, and driven head to head there the two write paths are
  identical to the pixel (§4.7) — but Chrome is not the engine §4.7 came from, and the Android case has
  not been re-measured since the change. If it does read as jitter, the same correction can move to the
  model instead — `chainStart -= delta` with a container write, which is sticky-exact for the same
  reason a scroll write is, at the price of a layout pass per frame.

- **`readInset`'s `auto` is verified on Blink, not on WebKit.** The sticky shift only moves an inset
  the element's own stylesheet set, and it tells them apart by `getComputedStyle` returning `auto` for
  one it did not. Probed on Chrome 151, including on a stuck element, that is what comes back. An engine
  that returned a length there instead would give the element a clamp it does not have — a header that
  sticks to the bottom edge as well as the top. iOS is a first-class target, so this wants one probe in
  Safari.

- **`reanchor` is not direction-aware.** It holds the item at the viewport top in both directions; in
  reverse it should hold the bottom, keeping `chainEnd` fixed. Today reverse gets the equivalent effect
  only for the in-flight-animation case (§3.2), and only because the model carries settled heights.

- **A fling read through history is still unmeasured against this design.** The Android numbers above
  cover the two mechanisms that were broken — the held overflow lock, and the per-render `scrollTop`
  write — but not the remaining claim, that moving the chain (a `container.top` write, which a
  re-anchor or a re-centre does at a render) during a live fling leaves it running. That rests on the churn measurement in the plan document and on §4.3, not on a device.
  Testing it needs a chat with enough history that flinging through it triggers loads, on an account the
  phone is signed into; §4.10 explains why the test page cannot stand in.

- **A touch landing during the iOS takeover lock cannot scroll in that same gesture.** The handler
  releases overflow synchronously and forces layout, but WebKit chose the gesture's scroll target before
  `touchstart` was delivered. The content can be caught and released, but not dragged onward until the
  next touch. Manual continuation and synthetic inertia were rejected: they duplicate the browser's
  scroll engine, and on a short chat with `min == max` the emulated release crossed into the opposite
  overscroll. The critically damped return is deliberately strong (`k = 1600`, no crossing or
  oscillation) to make this window short. The full mechanical suite can force the takeover in Chrome;
  the remaining iOS validation is qualitative device testing of compositor timing and feel.

- **The screen-anchor hold is not frame-coalesced.** The follow measures in a frame's read phase and
  writes in its write phase, at most once per frame; the hold does neither. It runs from a raw
  `requestAnimationFrame` *and* synchronously from `applyLayout`, so one render can read geometry after
  a container write and then write `scrollTop` twice — once for the hold, once for a re-centre. It is
  correct (the re-centre re-reads the position after the hold's write) and it only happens during an
  expand, where nothing else is moving, but it is the one per-frame writer that does not go through the
  frame's phases.

- **The sticky shift writes one inset per element per frame, not one property per frame.** The
  container-variable form it replaced was a single write; this is O(elements on screen), plus one
  `getComputedStyle` per element on the frame a band opens. It is the same order as the mechanism
  *both* of those replaced (a rect read and a transform write per element per frame) and no device
  regression has been measured, but it has not been profiled on a phone either.

- **`TouchStaleMs` fires on a genuine long hold** (§3.12).

---

This document describes what the code does. Why it was believed it would work — engine source paths,
bug links, the survey of what other virtualized lists do, and the phase plan this was built to — was
in `docs/plans/virtual-list-translation-scrolling.md`, removed once the work shipped; `git log` has
it if it is ever needed.
