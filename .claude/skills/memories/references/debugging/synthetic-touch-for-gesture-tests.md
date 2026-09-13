The SideNav pull gesture subscribes to `DocumentEvents.captured*.touch*$`, so it can be driven
end-to-end from a single `mcp__chrome1__evaluate_script` call with **synthetic** events —
`new Touch({identifier, target, clientX, clientY, ...})` + `new TouchEvent(type, {touches,
targetTouches, changedTouches, bubbles, cancelable, composed})`, dispatched on
`document.elementFromPoint(x, y)`. No CDP `Input.dispatchTouchEvent`, no
`Emulation.setTouchEmulationEnabled`, no `ws` connection.

Timing that works: `touchstart`, wait 40ms (clears `PrePullDurationMs` = 20), then moves ~16ms
apart, then `touchend`, then ~700ms for the settle. Sample state in a `requestAnimationFrame`
loop started before the gesture.

**This does NOT generalise to the virtual list.** That one depends on Chrome's real input
pipeline and compositor fling, so it needs `Input.dispatchTouchEvent` over raw CDP —
`tools/virtual-list-rig/rig.mjs`, and see [[server-loop-iteration-gotchas]]. The dividing line
is whether the code under test reads *events* (synthetic is fine) or depends on the browser's
own *scrolling/fling* behaviour (it isn't).

Note `body` gets `device-chrome`, not `device-webkit`, so a Chrome run exercises the
`transition: transform 50ms linear` pull path — **not** the `transition: none` path WebKit takes.
Chrome can verify the logic and the DOM writes; it cannot verify iOS smoothness.
