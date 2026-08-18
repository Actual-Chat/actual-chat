# Virtual list overscroll rig

Drives real touch gestures into a Chrome debug port and judges the rubber band against the rules in
`docs/virtual-list.md` §3.7. This is how the overscroll model is verified; the phones are for feel.

Needs a Chrome started with remote debugging (`ai chrome`, port 9222; `ai chrome*2` adds 9223), a
Voxt chat open in it, and the server running. Give the page a mobile viewport first — on desktop
width the chat view is not the touch-scrolling element (chrome-devtools MCP: `emulate` with
`412x915x2.6,mobile,touch`, or DevTools device mode).

```
node tools/virtual-list-rig/rig.mjs all 9222            # every scenario, lock on
node tools/virtual-list-rig/rig.mjs all 9222 nolock     # ordinary path without the two-frame overflow kill
node tools/virtual-list-rig/rig.mjs all 9222 takeover   # force the iOS takeover on Chrome
node tools/virtual-list-rig/rig.mjs all 9222 takeover 1000 # keep folds based at tOffset=1000px
node tools/virtual-list-rig/rig.mjs swing-back 9222     # one scenario
node tools/virtual-list-rig/soak.mjs 60 9223            # 60 random gestures, judged as a whole
node tools/virtual-list-rig/soak.mjs 60 9223 takeover 1000
```

Run the matrix on two chats: one longer than the viewport (a real band) and one shorter (a band
collapsed to a point, `min == max`). Both must pass on the ordinary lock/nolock paths and with takeover
forced at a 1000px `tOffset` baseline.

The judge checks the rules, not feel: the band never inverts, no gesture starts inside a band, every
excursion ends with the band transform at zero and the position legal, the finger is followed through
the curve's slope, and the band never moves by more than the rules allow. It also enables the VirtualList
consistency checker and fails on any violation. The owner's translation must settle at the configured
baseline. With a non-zero baseline the rig also constructs a consistent translation/model pair, folds
it, and verifies that the rendered content does not move. Rendered motion is measured from container
geometry so folding it is not a false jump.
`coast after release` on
`swing-back` and `updown` should match `control-fling` — a throw from overscroll is a throw. On
`fling-edge` (a fling reaching the edge with the finger up) the excursion should go out to roughly
`MaxBouncePx` past where it was noticed before it comes home — that is the bounce. Traces land in
`tmp/traces/`.

Three things the rig cannot do. Synthetic CDP touch drops flings intermittently unless moves are ~12ms
apart and sent without awaiting each ack; a single zero coast proves nothing, repeat it. And desktop
Chrome does not scroll off the main thread the way WebKit does, so the iOS-specific jitter does not
reproduce here; `nolock` reproduces the "nothing stops a fling" half only.
It also cannot reproduce iOS choosing an unscrollable target before `touchstart`: `catch-drag` proves
that the controller releases its lock and preserves geometry, not that the same caught gesture can
resume native scrolling on iOS.
