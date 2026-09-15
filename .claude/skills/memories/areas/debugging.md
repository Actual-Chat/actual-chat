# Debugging quirks and rigs

Each entry is a couple of sentences. The full write-up — commands, evidence, dates — is in
`../references/debugging/<name>.md`; open it when the one-liner turns out to matter.

## Servers and builds

- **`/server-loop` owns the build and the server.** When it is running, trigger its rebuild;
  never build or restart by hand. See `/server-loop` for the full contract.
- **Port 7080 may not be this repo.** The host's server-loop often runs from the separate
  `D:\Projects\ActualChat-C1` clone, so `local.voxt.ai` can be serving code you did not edit —
  check the process path before assuming. → `../references/debugging/server-loop-runs-in-c1-clone.md`
- **Waiting for a rebuild means waiting for a *new* log iteration**, not for the log to merely
  contain a success line; and a rebundle only reaches the page after caches are cleared and the
  service worker unregistered. → `../references/debugging/server-loop-iteration-gotchas.md`
- **A second App.Server from a worktree needs `HostSettings__BasePort`.** Setting
  `ASPNETCORE_URLS` alone hijacks loopback 7080 and the instance gets mesh-killed. Sign in as an
  email test user, not phone TOTP. → `../references/debugging/worktree-server-baseport.md`
- **After an ActualLab.Fusion bump, re-run `update-aot-helpers.cmd` AND delete `artifacts/bin`.**
  Otherwise WASM boot-loops with "mono runtime and class libraries are out of sync".
  → `../references/debugging/fusion-bump-wasm-clean-rebuild.md`
- **Typical durations** for npm build, dotnet build and server start, for sizing waits and
  timeouts rather than guessing. → `../references/debugging/build-times.md`
- **Local infra containers stay `Exited` after a host reboot**, and the loop then restart-loops
  on Redis. → `../references/debugging/infra-containers-stop-after-reboot.md`

## Browser rigs

- **chrome1 / chrome2 MCP quirks**: screenshots written via `filePath` stay inside the MCP
  container, a third Chrome lives on :9224, and faked render state needs `requestData` stubbed.
  → `../references/debugging/chrome-mcp-and-cdp-rig.md`
- **When MCP calls hang, a renderer is wedged** — drive raw CDP over the :9222 HTTP endpoint
  instead of retrying the tool. → `../references/debugging/wedged-renderer-bypass-chrome-mcp.md`
- **A second Voxt user** fits in chrome2 via a Playwright `newContext`, and when chrome2 is down,
  in a headless Playwright persistent profile on the host.
  → `../references/debugging/two-users-in-one-chrome-via-playwright-context.md`,
  `../references/debugging/headless-second-user-playwright.md`
- **The host Chrome on :9222 is usually hidden, so rAF is throttled** — run UI measurements in a
  headless instance with the cookies copied over. → `../references/debugging/headless-chrome-for-ui-measurement.md`
- **The fake camera wedges into `NotReadableError`** and only a browser restart with the same
  command line clears it; CDP network throttling also breaks the join-modal warmup, so start the
  camera unthrottled. → `../references/debugging/fake-camera-wedge-restart-chrome.md`
- **Firefox is scriptable over WebDriver BiDi only**, one session at a time, and the daemon must
  call `session.end` or Firefox never frees it. → `../references/debugging/firefox-bidi-debug-rig.md`
- **CDP cannot block a WebSocket.** `Network.setBlockedURLs` lets the RPC socket through — use
  Playwright's `page.routeWebSocket` and assert `framesReceived == 0`.
  → `../references/debugging/cdp-block-websocket.md`
- **DOM-only touch gestures (the SideNav pull) can be driven with a synthetic `TouchEvent`** from
  `evaluate_script`; only compositor-dependent gestures need `Input.dispatchTouchEvent`.
  → `../references/debugging/synthetic-touch-for-gesture-tests.md`

## Measurements that lie

- **`video.currentTime` is not frame liveness.** It keeps advancing on a MediaStream-backed
  `<video>` whose picture is frozen. → `../references/debugging/video-currenttime-is-not-liveness.md`
- **A closed browser still holds a server-render circuit** that keeps reading the open chat, so
  notification tests see false negatives. → `../references/debugging/retained-circuits-mark-entries-read.md`
- **Release-only CSS bugs come from cssnano merging rules**: a shared declaration merged into a
  later rule lets a shorthand clobber an earlier longhand override, invisible in the dev browser.
  Check the served bundle. → `../references/debugging/cssnano-shorthand-clobber.md`
- **Disabling a CSS rule to A/B its cost also changes what renders**, so the delta is not the
  matching cost — A/B additively with inert declarations instead.

## Talking to a running server

- **Any Fusion compute service on dev or prod can be called from a throwaway console client**
  with a self-minted `!` API key (`tmp/api-client`). Note that calling a compute method can
  trigger real server-side work. → `../references/debugging/voxt-prod-api-client-rig.md`
