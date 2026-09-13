Desktop Safari has **no CDP** — no `--remote-debugging-port`, and
`ios_webkit_debug_proxy` only bridges iOS devices/simulators, not the Mac's own
Safari. The equivalent is `safaridriver` (WebDriver + WebDriver BiDi).

A ready harness lives on macmini at `~/bin/safari` (→ `~/bin/safari.mjs`, plain
Node 22, no deps — uses built-in `fetch` + `WebSocket`):
`safari new [url] | size <w> <h> | go <url> | eval <expr> | eval -f <file> | shot [file] | watch [sec] | end`.
Session state is cached in `~/.safari-session.json` and reused; `eval` prints
console output to stderr and the value to stdout. Run it over plain `ssh macmini`
— no tunnel needed.

**Why:** Safari-specific bugs otherwise can't be measured from Windows, and the
setup has four non-obvious gotchas that cost a session to rediscover.

**How to apply — the four gotchas:**
1. **BiDi is gated behind a vendor capability.** Without
   `safari:experimentalWebSocketUrl: true` in the session capabilities, the reply
   contains `webSocketUrl: true` (a useless boolean) and no BiDi listener ever
   opens. With it, you get a real `ws://127.0.0.1:<port>/session/<id>`.
2. **`safaridriver --bidi <port>` is ignored.** The BiDi port is dynamic (observed
   8081, 8085, 8088 across restarts) — always read it from the session capabilities,
   never hardcode. `-p 9515` for the HTTP port *is* honored, and binds localhost-only.
3. **Safari reports exceptions as a bare `{type:'exception'}` with no
   `exceptionDetails`.** The message must be caught inside the page and returned as
   data. Also `e.stack` in Safari is just `@` lines — use `e.name + ': ' + e.message`.
4. **One session per Safari instance** — a second `POST /session` fails with
   "already paired with another WebDriver session"; `DELETE` the old one first.

Unlike the iOS path ([[ios-debugging-via-macmini]]), BiDi's `script.evaluate`
honors `awaitPromise` properly, so no arm-then-read dance is needed, and
`session.subscribe(['log.entryAdded'])` gives real console capture.

**Prerequisites, already done on macmini (2026-08-21):** Safari Settings →
Developer → "Allow remote automation", plus `sudo safaridriver --enable`.

**Limitation:** WebDriver drives its own automation window, never Alex's existing
tabs. Reaching a real open tab needs AppleScript `do JavaScript` + "Allow
JavaScript from Apple Events" + an Automation TCC grant for sshd — not set up;
`osascript` over ssh currently hangs on the unanswerable TCC prompt.
