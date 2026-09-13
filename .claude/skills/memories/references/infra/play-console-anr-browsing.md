Play Console "Crashes and ANRs" for Voxt:
`https://play.google.com/console/u/0/developers/6291022562349091998/app/4974793859049349557/vitals/crashes?days=28`
(cluster detail: `.../vitals/crashes/<clusterId>/details?days=28`;
ANR rate metric: `.../vitals/metrics/details?days=28&metric=USER_PERCEIVED_ANRS`;
vitals overview is `.../vitals/metrics/overview`, **not** `/vitals/overview` — that redirects to app-list).

- **The page opens with a sticky `Type: User-perceived crashes and ANRs` filter chip, and it hides most ANRs.** With it on, Voxt shows 18 clusters / ~29 events; removing it shows 59 clusters / ~101 events, including the entire background-ANR population (FCM broadcast timeouts). Always click "Remove all filters" before concluding anything about ANR volume. The `?isUserPerceived=` URL param does not control it — the chip is server-side state.
- Pagination and the filter chips are Angular `<material-button>` elements, not `<button>`, so `querySelectorAll('button')` misses them. Find by `[...document.querySelectorAll('material-button')].find(b => b.getAttribute('aria-label') === 'Go to the next page')`; disabled state is the `is-disabled` class, not `.disabled`.
- After clicking next-page the grid empties for several seconds; read it in a separate `evaluate_script` that polls for `a[href*="/vitals/crashes/"]`, otherwise you get an empty array or the previous page.
- Cluster detail pages sometimes never load the "Stack trace" section (stays at the spinner). Scrolling it into view doesn't help; just re-navigate or use a sibling cluster from the same family.
- `take_snapshot`/`take_screenshot` with `filePath` writes inside the MCP container, unreachable from the Windows host — see [[chrome-mcp-and-cdp-rig]].

## Driving "Prompt users to update" (App Bundle Explorer → Recovery tools)

Play's server-side forced-update nag. It needs **no** Play Core / `AppUpdateManager` integration in the app — that was a wrong assumption; the app has none and the tool still works. It is dismissible but re-shows on every app restart. Path: bundle row → Recovery tools → Prompt users to update → step 1 (bundle, needs "Newer version available in all tracks") → Next → step 2 (targeting, default "all users on this version") → Initiate prompt.

- Bundle list is `bundle-explorer-selector`, fixed **10 rows/page**, no page-size control, 13 pages. Rows are not links: `artifactId` (e.g. `4860224365679940359`) only appears in the URL after clicking `button[aria-label="View app version"]` in the row's last gridcell.
- Target `document.querySelector('[role="grid"][aria-label="App versions"]')`. Do **not** use `[...document.querySelectorAll('[role="grid"]')].pop()` — a second grid exists and `.pop()` silently picks the wrong one.
- A bundle that already has a prompt shows a **"Recovery" chip** in the list and "Manage update prompt" on its Recovery tab — use both as the idempotency guard before re-triggering.
- "Install base" in the list (`≤ 100`, `1.4K`) is an upper bound over dead installs. The wizard's own **"Users targeted for update prompt"** at step 2 is the real number, and is often 0 for a `≤ 100` bundle — but not always (a `≤ 100` bundle showed 99). Read it before concluding a prompt is a no-op.
- **Batch size ~2 per `evaluate_script`.** Each bundle takes ~40 s; 3+ blows the CDP `protocolTimeout` (`Runtime.callFunctionOn timed out`) and the script dies mid-flight — some bundles get initiated with no result returned, so always re-verify via the Recovery chips rather than trusting the error.
- After any aborted script the SPA can wedge (0 rows, nonsense page indicator like "271 - 274"). A full `navigate_page` reload fixes it.
- The grid keeps its page position across in-app navigation, and a forward-only paging loop then never finds earlier rows. Always `navigate_page` to the selector before a batch instead of paging backwards — the back-paging loop is slow enough to blow the protocol timeout on its own.

Only `chrome2`'s Chrome is signed into Play Console; `chrome1` redirects to `/console/u/0/signup`, so work can't be split across the two MCP servers. Two agents on one server would race — `select_page` is global server state.
