A hung `mcp__chrome1__*` call (even `list_pages`) usually means one tab's
renderer main thread is wedged: chrome-devtools-mcp enumerates every target and
blocks on the bad one. The browser process is still fine.

Bypass the MCP with the CDP HTTP endpoints, which are served by the browser
process and never block on a renderer:

- `curl http://127.0.0.1:9222/json/list` — targets (grep `"id"`/`"url"`)
- `curl -X PUT http://127.0.0.1:9222/json/close/<targetId>` — close the bad tab
- `curl -X PUT 'http://127.0.0.1:9222/json/new?<url>'` — open a new one

Closing the wedged tab makes the MCP work again.

To tell *which* tab and *why*:
- `SystemInfo.getProcessInfo` on the browser WebSocket, sampled twice, gives
  per-process `cpuTime`; a renderer at ~100% of one core is the culprit. Confirm
  by closing the tab and checking the PID disappears.
- Per-thread: `(Get-Process -Id <pid>).Threads` deltas plus
  `GetThreadDescription` P/Invoke names the hot thread (`CrRendererMain`).
- A page whose dedicated workers are *also* unresponsive = whole renderer wedged.

**Getting the JS stack out of a wedged tab:** `Debugger.pause` only works if
`Debugger.enable` was processed *before* the hang - both need the main thread's
task queue. So open `about:blank`, connect, `Debugger.enable`, then
`Page.navigate` to the real URL over the same session. When it wedges, send
`Debugger.pause` and read `Debugger.paused.callFrames`. Resume + re-pause a few
times to sample. Helper scripts pattern: keep one outstanding `ReceiveAsync`
task and poll `task.Wait(250)` - cancelling a receive aborts a ClientWebSocket.

Blazor WASM release frames are `$funcNNNN` (no name section), but the JS frames
below `mono_wasm_invoke_jsexport` still name the entry point, and the console
tail right before the wedge names the C# code that was looping.

Related: [[headless-chrome-for-ui-measurement]],
[[two-users-in-one-chrome-via-playwright-context]]
