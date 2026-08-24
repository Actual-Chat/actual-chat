# Virtual list overscroll rig

Drives real touch gestures into a Chrome debug port and judges the rubber band against the rules in
`docs/ui/virtual-list.md` §3.7. This is how the overscroll model is verified; the phones are for feel.

Needs a Chrome started with remote debugging (`ai chrome`, port 9222; `ai chrome*2` adds 9223), a
Voxt chat open in it, and the server running. Both scripts put the page in a 412x915x2.6 mobile
viewport themselves, because on desktop width the chat view is not the touch-scrolling element — and
because an emulation override belongs to the session that set it, so one applied from elsewhere
disappears the moment that client detaches, halfway through a matrix run. `VL_RIG_VIEWPORT=390x844x3`
picks another one; `VL_RIG_VIEWPORT=off` leaves the window as it is.

The window also has to be **visible**: an occluded or minimised Chrome window gets no
`requestAnimationFrame` at all, so the recorder collects nothing, the app never finishes mounting, and
every gesture reads as "finger ignored".

```
node tools/virtual-list-rig/rig.mjs all 9222            # every scenario, lock on
node tools/virtual-list-rig/rig.mjs all 9222 nolock     # ordinary path without the two-frame overflow kill
node tools/virtual-list-rig/rig.mjs all 9222 takeover   # force the iOS takeover on Chrome
node tools/virtual-list-rig/rig.mjs swing-back 9222     # one scenario
node tools/virtual-list-rig/soak.mjs 60 9223            # 60 random gestures, judged as a whole
node tools/virtual-list-rig/soak.mjs 60 9223 takeover
node tools/virtual-list-rig/follow.mjs 9223             # the follow's write path, scroll vs transform
```

`follow.mjs` answers one question and is not part of the matrix: the pinned edge follows content that
grew under it by writing `scrollTop` once per frame, and docs/ui/virtual-list.md §4.7 records that a
per-frame write stream was visibly jittery on Android. It drives 2px of correction per frame down each
path in turn and reports what a real item did on screen. Chrome shows no difference between them, so
the run that matters is against a phone's debug port.

Run the matrix on two chats: one longer than the viewport (a real band) and one shorter (a band
collapsed to a point, `min == max`). Both must pass on the ordinary lock/nolock paths and with takeover
forced.

The judge checks the rules, not feel: the band never inverts, no gesture starts inside a band, every
excursion ends with the band transform at zero and the position legal, the finger is followed through
the curve's slope, and the band never moves by more than the rules allow. It also enables the VirtualList
consistency checker and fails on any violation. It also checks that the band is the *only* thing
writing the transform: what is left of the composed transform once the band's own share is taken out
has to be zero on every frame, which makes "the list writes no transform of its own" a checked property
rather than a claim.
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
