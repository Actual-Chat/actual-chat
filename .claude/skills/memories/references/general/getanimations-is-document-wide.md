`element.getAnimations()` is **not** scoped to the element. Blink routes it through
`DocumentAnimations::getAnimations`, which collects every relevant animation in the
tree scope and **sorts them all** with `Animation::CompareAnimations` before any
subtree/target filtering. The comparator calls `Node::compareDocumentPosition`,
which walks siblings — so among many siblings it is O(siblings) per comparison.

Measured in Chrome on a probe element that owned exactly **one** animation, varying
only the rest of the document:

| document animations | ms per single `getAnimations()` call |
|---|---|
| 100 | 0.08 |
| 1,000 | 5.2 |
| 2,000 | 25.2 |
| 4,448 | 137 |

`{subtree: false}` is **not** cheaper (134 ms vs 143 ms) — the sort happens either way.
CSS *transitions* count toward the total too, not just `@keyframes` animations.

Consequence: any per-element loop calling it is O(N x A^2). Call
`document.getAnimations()` **once** per batch and group by `effect.target` instead —
that measured 134 ms vs 6,890 ms for 50 elements (51x). Grouping by `effect.target`
still exposes pseudo-element animations, so it is a drop-in for a `{subtree:true}`
call whose only purpose was reaching `::before`/`::after`.

Batching the obvious loop is only half of it. A freshly rendered element's animation is
still *pending* when a MutationObserver callback sees it - `startTime` is null - so it
yields no phase there and is aligned a frame later from `animationstart`, one event at a
time. Measured with the real bundle, 50 elements: 100 collections unbatched, **51** with
only the sweep batched, **2** once the `animationstart` burst is coalesced too. Always
measure the whole path; the isolated batch number was 51x and the end-to-end one was 2x.

Coalesce with the repo's own `fastRaf({ read, write, key })` (`src/nodejs/src/fast-raf.ts`),
not a hand-rolled rAF - and split the halves, because the collection is a read and the
phase is a write. A rAF registered from the first `animationstart` does run in that same
frame, after all of the frame's start events.

This wedged the prod Windows app for ~30 min of pegged CPU: see
[[webview2-native-stack-sampling]] for how it was found.
