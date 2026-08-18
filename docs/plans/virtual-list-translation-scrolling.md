# Virtual list: translation-based scrolling

> **Implemented.** What the code actually does, and what was measured, is in
> [docs/virtual-list.md](../virtual-list.md). This document is kept for the
> provenance behind those decisions — engine source paths, bug links, the survey
> of other implementations — and for the phases that are still open: sticky
> counter-translation (§8 there), a direction-aware `reanchor`, and measuring a
> real fling on WebKit and Android.

## What we are trying to achieve

The virtual list has to move content that the user is also moving. Loading history,
re-measuring an item, following a growing transcript, re-centring the chain in its
scroll space — all of these change where content should sit, and all of them can
land while a fling is in flight.

Today the only tool we have for that is `element.scrollTop`, and it is a bad tool
for the job, for a reason that is not going to change:

> **Writing `scrollTop` during momentum ends the fling on WebKit.** It is not a
> WebKit decision — the write reaches UIKit as `[scrollView setContentOffset:]`,
> and that is what UIKit does. Verified in WebKit source: `scrollTop` →
> `RemoteScrollingCoordinatorProxy::adjustMainFrameDelegatedScrollPosition` →
> `-[WKWebView _scrollToContentScrollPosition:…]` → `setContentOffset:`; and for
> subscrollers `ScrollingTreeScrollingNode::handleScrollPositionRequest` →
> `ScrollingTreeScrollingNodeDelegateIOS::repositionScrollingLayers()` →
> `setContentOffset:`. Firefox has the same complaint filed
> ([bug 1474196](https://bugzilla.mozilla.org/show_bug.cgi?id=1474196)).

The user experiences it as the list stopping dead for no reason. Every rule in the
current implementation about *when* we are allowed to write `scrollTop` — the
standstill gate, the direction-switch dwell, the deferred re-pin — exists to work
around that one fact. It is a large amount of machinery spent on avoiding a tool
rather than on doing the job.

**So we add a second term.** The content's visible position becomes a sum, and we
correct using the term the browser has no opinion about:

```
visible position  =  scrollTop        (the browser's, driven by the user)
                  +  container.top    (ours, layout — where the chain sits)
                  +  transform        (ours, composite — the continuous correction)
```

A correction expressed as a transform never touches `scrollTop`, so it cannot
interrupt a fling, cannot be clamped, and cannot fight the user. That is the whole
point of the change.

### What we are *not* doing

**We are not taking over scrolling.** The browser keeps driving the scroll — its
momentum curve, its rubber band, its input handling, its accessibility. Libraries
that take it over (iScroll, GSAP's `normalizeScroll`) buy control at the cost of
native feel, `position: fixed`, and every platform's scroll conventions. We keep
native scrolling and only add a correction layer on top.

This may be revisited. If it turns out that owning the scroll entirely lets us do
everything better, that is a legitimate future direction — but it is not this
plan, and nothing here should assume it.

**We are not committing to identical rules on every engine.** The rules below are
the starting point. Chrome, WebKit and Android differ in ways we have already
measured, and per-engine variation in *which property to change and when* is
expected and allowed. What must stay uniform is the model — the three terms and
what each one means.

---

## Foundations

Everything below is derived from this section. These are the properties we either
**enforce** — and must keep enforcing, because breaking one silently invalidates
the design — or **depend on** as facts about how browsers behave. If a decision
later in this document looks arbitrary, the reason is almost always here.

Provenance is marked on every factual claim: *measured* (by us, in this repo),
*source* (read in the engine's source or an engine bug), *researched* (secondary,
cited), *derived* (arithmetic from a documented format). Anything unmarked is a
choice we made, not a fact about the world.

### A. Invariants we enforce

**A1. `overflow-anchor: none`, everywhere inside the list.**
Applied to the scroller, the wrapper, the container, every `li`, items, groups,
both spacers, the end anchor, the skeleton container, and height-controlled item
children. This disables *scroll anchoring* — the browser silently adjusting
`scrollTop` to compensate for content that changes above the viewport.

Read it as *"don't help me, I'll do it myself."* It is not a promise from the
browser that `scrollTop` will stay put; it is an obligation we accepted, and it is
the reason `reanchor` exists at all. **A single anchor-eligible descendant that
misses this rule reintroduces the behaviour**, and the browser will then fight
every correction we make. Anything new added under the list needs it.

**A2. The wrapper's height never changes for the life of the list.**
Two consequences, both load-bearing. `scrollHeight` is constant, so the browser
never clamps `scrollTop` and `maxScrollTop` is stable. And in reverse the scroll
origin *is* the wrapper's bottom edge, so a resize would move every wrapper
coordinate at once — payable only with a jump or a fling-killing scroll write.

**A3. The wrapper's height is small enough that no engine clamps it.**
4,000,000px, and the realized height is measured every layout rather than assumed
(§B4). Combined with A2 this makes the scroll range a constant we can trust.

**A4. The chain is absolutely positioned inside the wrapper; items are in normal
flow inside the chain.**
Moving the chain therefore never reflows the wrapper and never re-lays-out the
items — only the container's own position changes. This is what makes the model
able to place the rendered window anywhere in the scroll space for free, and it is
why loading history does not push content around.

**A5. `contain: strict` on the scroller.**
Layout, style, paint and size containment. Beyond the general performance win, it
**bounds the cost of the layout invalidation that changing `container.top`
causes** — the invalidation cannot escape the scroller. Phase 1's "fold at render"
rule is affordable partly because of this.

**A6. The container is the only element we transform for scroll correction.**
One element, already on its own compositing layer (`will-change: transform`,
`translate3d(0,0,0)`, `backface-visibility: hidden`). If the correction were split
across several elements they would have to be kept in lockstep every frame.

**A7. `InfiniteList` has no visible scrollbar.**
Its scroll range is 4M of mostly-empty space, so a real scrollbar would be a sliver
whose position means nothing. This is what lets `scrollTop` sit anywhere in that
range without the user seeing something nonsensical. (`FiniteList` keeps its
scrollbar, because its spacers cover the unloaded ranges exactly and its scrollbar
is therefore honest.)

**A8. Every read of the list's position goes through one accessor.**
With the new model the visible position is a sum of three terms, and any code that
reads only one of them will disagree with what is on screen — including the loader
deciding what is visible. This is an invariant the refactor must establish and
then keep.

**A9. `scrollTop` is only ever written at a standstill.**
Stated as a rule below, repeated here because it is the invariant the whole design
protects. Once transforms carry the continuous corrections, this stops being a
constraint we work around and becomes one that is trivially satisfied.

### B. Browser behaviour we depend on

**B1. Writing `scrollTop` during momentum ends the fling on WebKit.** *(source)*
The write reaches UIKit as `[scrollView setContentOffset:]`, which ends
deceleration. This is a UIKit side effect, not a deliberate WebKit cancel. Path for
the main frame: `RemoteScrollingCoordinatorProxy::adjustMainFrameDelegatedScrollPosition`
→ `-[WKWebView _scrollToContentScrollPosition:…]` → `setContentOffset:`; for
subscrollers: `ScrollingTreeScrollingNode::handleScrollPositionRequest` →
`ScrollingTreeScrollingNodeDelegateIOS::repositionScrollingLayers()` →
`setContentOffset:`. Firefox has the same behaviour reported
([bug 1474196](https://bugzilla.mozilla.org/show_bug.cgi?id=1474196)).
**This single fact is why this plan exists.**

**B2. Transforms are applied after layout; `position: sticky` is resolved during
it.** *(measured)*
So a layout offset keeps sticky correct and a transform displaces an
already-pinned element by exactly the compensation — 400px of compensation moved a
stuck header 400px, while the same correction applied via `top` moved it 0px. One
cause, both effects: the same property that makes transforms cheap makes them
invisible to sticky.

**B3. `scrollTop` is negative under `flex-direction: column-reverse`.** *(measured)*
`scrollTop = 0` is the scroll *origin*, which sits where the flow starts — the
bottom edge in reverse. Measured range: `0 … 9800` natural, `−9800 … 0` reverse.
There is no way to move the origin independently of the flow direction, so the
sign is the browser's to decide. It is confined to two conversion functions.

**B4. Chrome clamps element sizes at 2²⁵ *physical* pixels.** *(measured)*
`LayoutUnit` is 1/64 px in an int32, giving 33,554,432 — spent in physical pixels,
so the CSS-pixel ceiling is `33,554,432 / devicePixelRatio`. Measured on a
devicePixelRatio 3.75 device: a 10,000,000px request came back as 8,947,847px,
matching `2²⁵ / 3.75` to the pixel. This is why the wrapper is 4M and why its
realized height is measured rather than assumed.

**B5. Compositor coordinates are float32, exact only to 2²⁴.** *(derived)*
16,777,216 physical pixels, i.e. half the hard ceiling in B4. Past it, positions
round to 2 physical px, then 4. No symptom observed; it is the reason not to sail
close to B4's limit rather than a number to design against.

**B6. Chrome hands Blink the compositor's current scroll delta in
`BeginFrameArgs`.** *(researched + measured)*
Which is why per-frame transform compensation is near-exact on Chrome desktop:
measured as an exact freeze at low speed and ~1px of shake at high fling speed.
Android is expected to be worse because of input prediction and Viz-thread input
routing — unmeasured.

**B7. iOS momentum is not implemented in WebKit at all.** *(source)*
It is UIKit `UIScrollView`, running in the **UI process**. Per WebKit's own
documentation: *"the content continues to scroll … this is implemented outside of
WebKit … by UIScrollView on iOS"*. The scroll offset JavaScript sees has therefore
made a cross-process round trip and is at least one hop stale — confirmed by
WebKit's Simon Fraser in
[bug 236312](https://bugs.webkit.org/show_bug.cgi?id=236312): *"Scrolling on iOS
involves an asynchronous round-trip to a different process, and it's possible we're
getting a stale scroll position back to JS because of that."*

**This is the one foundation that is genuinely uncertain for us.** rAF does fire
during momentum, scroll events do fire continuously, and transforms are composited
during a fling *(researched)* — so the technique runs. But the compensation is
computed from a stale offset and applied through an async commit, so the error is a
**velocity-proportional sag that recovers as the fling decays**, not the symmetric
~1px shake Chrome shows. Magnitude unknown; must be measured on device before
full-strength compensation is relied on there.

**B8. Setting `top` on an absolutely positioned element invalidates layout;
setting `transform` does not.** *(measured)*
Cost per write, with the layout forced to land: `scrollTop` 2.8µs, `transform`
3.2µs, `top` 11.8µs. So `top` is ~3.7× a transform, but one write per frame is
0.07% of a 16.7ms frame. Measured on simple rows; real message content is heavier.

**B9. With A1 and A2 both in force, the browser does not move `scrollTop` on its
own.** *(measured)*
Under continuous churn — items removed above, appended below, heights changed in
view — `browserMovedScrollTop` was 0. Note this is the product of *two independent*
protections: anchoring disabled (A1) and the scroll range fixed (A2). Neither alone
is sufficient.

### C. What we deliberately do not depend on

Listed because each is a plausible-looking foundation that would not hold.

- **That `scrollTop` is current when we read it.** On iOS it is stale during a
  fling (B7). Prefer deltas over absolute anchoring where the difference matters.
- **That the browser will re-anchor for us.** We turned it off (A1); the
  responsibility is ours, permanently.
- **`-webkit-overflow-scrolling: touch`.** Obsolete since iOS 13 and a no-op; every
  overflow scroller gets accelerated scrolling by default now. Do not use it to
  force a compositing layer — use `will-change` explicitly.
- **`overscroll-behavior` on iOS.** Only partially implemented since Safari 16, and
  suppressing the document-level rubber band with it is still widely reported not
  to work.
- **`scrollend`.** Safari 26.2+ only. Other implementations reconcile on it; we
  cannot yet, so our stability detection stays position-based.
- **Native scroll anchoring (`overflow-anchor` as a feature).** Safari 27 / iOS 27,
  and we disable it anyway — but worth knowing it may become a real alternative to
  some of this later.
- **Uniform rAF cadence.** Low Power Mode and cross-origin iframes throttle it;
  ProMotion changes it. Never assume 16.7ms.
- **That momentum survives unrelated rendering work on iOS.** View transitions and
  CSS scroll snap are both filed as killing it
  ([288795](https://bugs.webkit.org/show_bug.cgi?id=288795),
  [243582](https://bugs.webkit.org/show_bug.cgi?id=243582)). If we add either near
  the list, re-test flings.

## The rules

The starting rule, in the user's framing:

> **`scrollTop` and `container.top` may only change on big changes** — either when
> the scroll position (and preferably everything else) is completely static, or
> when we are re-rendering the list anyway because of some other user action and
> nobody cares whether a scroll was in progress.
>
> **Everything else is a transform update.**

Concretely:

### `scrollTop` — jumps only, at a standstill

`scrollTop` is *only ever a jump*, used exclusively where a jump is invisible or
irrelevant. There is no case where we use it to move somewhere gradually.

Permitted for: opening a chat, navigating to another chat, `scrollToKey` /
explicit jumps, re-centring the chain in the wrapper, a direction flip, stranded
recovery. In each case the list is still, or the user just asked for a
re-placement and a jump is the expected outcome.

### `container.top` — the model's placement, changed at render

`container.top` (or `bottom` in reverse) is where the chain sits in the 4M scroll
space. It changes when the *model* changes — which is to say, on a render, which
already pays for layout. Changing it costs a layout pass; doing it during a render
costs nothing extra.

**This is also where we fold.** At each render we add the accumulated transform
offset into `top` and reset the transform to zero. Reconciliation is therefore
free — the layout is already happening — and the transform offset never
accumulates across renders.

### `transform` — everything continuous

Every correction that happens between renders: the overscroll pull, the return
spring, the fling brake, following a growing transcript, sliding a new message
into view. Composite-only, so it cannot interrupt anything.

### Folding

Folding the transform into `top` is mostly not *required* — the rules above mean a
non-zero transform breaks nothing on its own. It is needed when something else has
to reason about real positions:

- **at each render**, because it is free there and keeps the offset near zero;
- **when the chain approaches the edge of the wrapper** and must be re-centred —
  a coordinate change that has to be real;
- **on any scroll we did not cause** (see below).

In all of these we wait for stability first.

### Scrolls we did not cause

`overflow-anchor: none` does not mean the browser never touches `scrollTop`. It
disables *scroll anchoring* specifically — the browser compensating for content
that changes above the viewport. The browser can still move `scrollTop` via
`scrollIntoView`, focus (tapping the message editor), the on-screen keyboard
resizing the viewport, find-in-page, fragment navigation, and history scroll
restoration. Clamping is prevented separately, by the wrapper being a fixed size
so `scrollHeight` never changes.

Read correctly, `overflow-anchor: none` says *"don't help me, I'll do it myself"*
— it is an obligation we took on, which is exactly why `reanchor` exists.

So: **on any scroll change we did not originate, fold the transform offset in and
zero it**, rather than letting our offset ride on top of a position we no longer
control.

---

## Situation by situation

| situation | mechanism |
|---|---|
| re-anchor after a model change | transform, folded at the same render |
| edge re-pin (new message while pinned to End) | transform |
| new message sliding into view | transform, animated |
| following a growing transcript | transform, continuous |
| overscroll pull (finger past the edge) | transform |
| return spring / fling brake | transform, animated |
| re-centring the chain in the wrapper | `top` + `scrollTop`, at a standstill |
| direction flip | `scrollTop`, at a standstill |
| `scrollToKey` / explicit jump | `scrollTop` |
| opening or switching chat | `scrollTop` |
| stranded recovery | `scrollTop` |

---

## Sticky items

A transform displaces elements that `position: sticky` has already pinned —
measured at **exactly** the compensation amount (400px of compensation moved a
stuck header 400px). A layout offset does not, because sticky is resolved during
layout and a transform is applied after it. Same cause for both facts.

We have several sticky variants: conversation headers (`sticky -top-px`) and author
badges (sticky with both `top` and `bottom`), nested at different depths.

**We do not pull them out of flow.** They stay `position: sticky`, and we cancel
our own displacement for the ones that are currently pinned:

```ts
header.style.transform = isPinned ? `translate3d(0, ${-tOffset}px, 0)` : '';
```

Which ones are pinned comes from the model, with no DOM reads. For a block spanning
`[blockStart, blockEnd]` with header height `h`, against the effective viewport top:

```
viewTop < blockStart                 → not reached; moves with content  → no counter-translate
blockStart ≤ viewTop ≤ blockEnd − h  → pinned at the edge               → counter-translate
viewTop > blockEnd − h               → pushed off, tracks blockEnd      → no counter-translate
```

Those are sticky's three regimes as arithmetic. Typically 0–2 elements are pinned,
so it is a couple of writes per frame, no reads, no forced layout. Author badges use
the same rule against their message's range.

**Phase 1 can skip this entirely.** With folding at each render the offset is near
zero except between renders, and the one sustained case where it isn't — the
overscroll family — is exactly where dragging stuck headers along is *correct*,
because that is what a native rubber band does. Counter-translation is only needed
once we add a sustained animated transform that is not an overscroll.

---

## Evidence

Measured in Chrome, so nobody re-derives them.

**The three mechanisms compared** (synthetic scroller shaped like the real one —
fixed wrapper, absolutely positioned container, items in flow, sticky headers,
`overflow-anchor: none`):

| | kills flings | sticky | cost per write |
|---|---|---|---|
| `scrollTop` | **yes, on WebKit** | correct | 2.8 µs |
| `top` (layout) | no | **correct** | 11.8 µs |
| `transform` (composite) | no | **dragged by the full offset** | 3.2 µs |

`top` is ~3.7× a transform — the cost of invalidating layout — but one write per
frame is 0.07% of a 16.7ms frame. Measured on simple rows; real message content is
heavier and this should be re-measured against the real list.

**Compensation accuracy** (drive the scroll, cancel it with a transform each frame):

- full freeze: exact at low speed, ~1px shake at high fling speed;
- half speed and asymptotic brake: exact;
- the residual is main-thread lag, so it is proportional to velocity.

**Under churn** (items removed above, appended below, heights changed in view,
continuously) with `top`-based correction: probe drift avg 0.00 / max 0.07px,
0 late frames, and **`browserMovedScrollTop` = 0** — the browser never adjusted the
scroll once. That is the product of two independent protections: anchoring
disabled, and the scroll range fixed.

**iOS is not covered by any of the above.** Chrome desktop gets ~1px because the
compositor hands Blink the current scroll delta in the same frame. iOS momentum
runs in a *different process* (UIKit `UIScrollView` — WebKit does not implement
momentum at all), so the compensation is computed from a scroll offset that is at
least one hop stale and applied back through an async commit. WebKit's Simon Fraser
confirms the staleness in [bug 236312](https://bugs.webkit.org/show_bug.cgi?id=236312).
Expect a **velocity-proportional sag that recovers as the fling decays**, not a
symmetric shake. Magnitude unknown: a two-frame round trip predicts tens of px at
hard-fling speed; the only published measurement of an analogous correction is
~4px transient / ~10px snaps
([TanStack/virtual#1250](https://github.com/TanStack/virtual/issues/1250)).
**This must be measured on device before we rely on full-strength compensation
there.** The softer modes (half speed, brake) degrade gracefully — lag reads as
softness rather than as a jump — and are the ones most likely to survive.

**What others do.** Nobody in the surveyed set does per-frame transform
compensation against a live native fling on iOS. TanStack Virtual defers the write
through the gesture plus a 150ms momentum window and flushes once settled;
`@rocicorp/zero-virtual` holds the correction as a **margin on the content wrapper**
(layout, not transform — which is why its sticky stays correct) and reconciles at
`scrollend`. Both are close relatives of what is proposed here.

---

## Risks and open questions

1. **iOS sag magnitude** — the blocking unknown. Measure on device before relying
   on full compensation there.
2. **Tile coverage.** Both engines rasterize tiles around where the *scroller*
   thinks it is. A large sustained transform moves visible content away from that
   centre; the failure mode is checkerboarding. This is the real bound on the
   offset — not legality, which the 4M space makes a non-issue. Folding at each
   render should keep it small; find the threshold anyway.
3. **Layout cost on real content**, versus the synthetic rows measured above.
4. **Sticky under a per-element transform** — whether it costs a compositing layer
   per pinned header, and behaviour when an element changes regime on the same
   frame the offset changes.
5. **Reading position.** Everything that reads `scrollOffset` must read all three
   terms, or the loader's idea of what is visible drifts from what is on screen.

---

## Code structure: separate the two lists

`InfiniteList` and `FiniteList` have turned out to be very different components
that happen to share a name:

- **InfiniteList** — unbounded, a fixed 4M scroll space with an absolutely
  positioned chain floating in it, items whose heights are measured after render,
  overscroll physics, direction flipping, re-anchoring, re-centring.
- **FiniteList** — known item count, uniform item height, spacers that cover the
  unloaded ranges exactly so the wrapper height is content-driven, an honest
  scrollbar, and **no programmatic scrolling at all** except `scrollToKey`.

Almost none of the machinery in this plan applies to `FiniteList`. It has no
overscroll physics, no direction, no re-anchoring, and its container stays in flow
(`position: relative`).

So: **push down, don't pull up.** The base type should keep only what both
genuinely use — element refs, the render-state plumbing, item/key bookkeeping,
disposal — and everything else should move into the subclass that uses it. Where
they currently share code that only one needs, that code belongs to that one.

This is worth doing *before* the translation work rather than after, so the new
model is implemented in one place instead of being written to accommodate a
component that will never use it.

---

## Phases

**Phase 0 — separate the JavaScript.** Push single-user machinery out of the base
type into `InfiniteList` / `FiniteList`. No behaviour change.

**Phase 1 — the offset term.** Introduce the transform offset as a first-class
term the list owns; make every position read account for all three terms; fold at
each render and on scrolls we did not cause. No behaviour change intended.

**Phase 2 — move the overscroll family fully onto transform.** The return spring
stops pinning `scrollTop` and stops locking overflow on non-WebKit engines. This
removes the cause of the Android "random stops" we reverted around, and it is
contained inside `ScrollController`.

**Phase 3 — re-anchor and re-pin onto transform.** The largest correctness win:
these are the writers that fire on any render, including mid-fling.

**Phase 4 — animated transforms.** New message sliding into view; following a
growing transcript by offset rather than by animating item height. This is the
first phase where sticky counter-translation may be needed.

**Phase 5 — measure on iOS and Android**, and set per-engine rules if the numbers
call for them.

Each phase updates `docs/virtual-list.md` in the same commit — the doc is the
design record for this component and a stale one is worse than none.
