`tmp/pw-b.mjs` (recreate if gone): `chromium.launchPersistentContext('tmp/pw-b-profile', { headless: true, channel: 'chrome',
ignoreHTTPSErrors: true })`, goto `https://local.voxt.ai<path>`, wait for `window.debugUI`, then run node-side code with
`page`. Cookies persist across runs, so `debugUI.signIn('+1 555 555 5551')` once is enough (that phone was `7GI0pt` on
2026-09-11; `+1 555 555 5550` was `dPl6bk`).

Gotchas:
- From Git Bash, prefix `MSYS_NO_PATHCONV=1`, or `/chat` becomes `C:/Program Files/Git/chat`.
- Force server mode via `/fusion/renderMode/s?redirectTo=<path>` — the WASM path crashes headless with `ExitStatus`.
- Plain typing + Enter in the message editor works; typing `@name` does not drive the mention picker (text duplicates,
  nothing sends) — do mentions from chrome1.

Related: [[retained-circuits-mark-entries-read]], [[two-users-in-one-chrome-via-playwright-context]].
