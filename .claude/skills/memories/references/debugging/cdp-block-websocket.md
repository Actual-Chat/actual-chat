`Network.setBlockedURLs` silently lets WebSocket handshakes through, so blocking
`*/rpc/ws*` that way produces a fully **online** app that looks like a passing
offline test. Always assert the block held before trusting results — subscribe to
`Network.webSocketCreated` / `Network.webSocketFrameReceived` and require
`framesReceived === 0`.

What works (Playwright >= 1.48, repo has 1.58):

```js
await page.routeWebSocket(/\/rpc\/ws/, ws => ws.close({ code: 1011 }));
```

This kills every RPC connect attempt while HTTP keeps working, which is the only
way to boot the WASM app **cold with dead RPC** in a browser — Chrome's offline
emulation can't do it, because the Voxt service worker is the Firebase messaging
one and does not serve the app shell offline, so an offline reload just yields
`chrome-error://chromewebdata/`.

Chrome offline emulation (`emulate networkConditions: Offline`) is still the right
tool for **mid-session** offline: go offline on a loaded page, then client-side
navigate (`debugUI.navigateTo`) to a chat not yet opened in that session.

Measuring the chat view: `.chat-view` carries `data-identity="<chatId>"`. Poll
`document.querySelector('.chat-view[data-identity="X"]')` — reading `.chat-view`
without that filter attributes the *previous* chat's items to the new one during
the content swap.

**How to apply:** reach for this before writing another offline repro; see
[[chrome-mcp-and-cdp-rig]] for the rest of the rig.
