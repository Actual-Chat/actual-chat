Measured on the iPhone 13 Pro (iOS 26.5, `devicePixelRatio` 3) against the virtual
list: every `scrollTop` value read back was an exact integer, across hundreds of
samples at a magnitude around 2,000,000. Chrome keeps fractional scroll offsets.

**Why:** any correction the list computes in fractions — an anchor hold, a follow,
a clamp — cannot land exactly on iOS. A residual below `RepinEpsilon` (1px) is then
never paid back, so a systematic sub-pixel error accumulates a whole pixel at a
time. This is the shape of the "expand/collapse moves the view 1px toward the end,
every time" bug.

**How to apply:** when a position bug is reported on iOS and won't reproduce in
Chrome, suspect this before suspecting the app. To simulate it locally, shadow the
property on the scroller — `Object.defineProperty(el, 'scrollTop', {...})` wrapping
`Element.prototype`'s descriptor with `Math.round` on both get and set. See
[[ios-debugging-via-macmini]] for measuring on the real device.
