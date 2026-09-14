Production builds (`build.mjs --production` → `NODE_ENV=production` → cssnano
`preset: default`) run `postcss-merge-rules`, which hoists the declarations two
rules share into a merged rule placed **after** the first one. If the first rule
overrode one edge with a longhand (`top: var(--safe-area-top)`) while both rules
carried the `inset` shorthand, the hoisted `inset` lands later and resets it.

**Why:** the debug bundle the dev server serves is not minified, so these bugs are
invisible in the browser and appear only in MAUI Android/iOS and the production
web deploy. Diagnosing them from the browser is hopeless; read the *served* CSS.

**How to apply:** never pair `@apply inset-0` with a separate `top:`/`bottom:`/
`left:`/`right:` override — express it as one shorthand (`inset: var(--x) 0 0`).
To check the whole app, run cssnano over the built debug bundle and look for a
selector whose edge longhand is re-covered by a shorthand in a later rule
(`tmp/inset-scan.mjs` does exactly this; it takes ~1 min over the full bundle).
Hit in 2026-08: `.left-search-panel` lost the top safe area on phones, from
commit 14b41b695c. See [[ios-debugging-via-macmini]].
