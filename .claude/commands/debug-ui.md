---
allowed-tools: Bash, Read, mcp__chrome1__*, mcp__chrome2__*, mcp__chrome-devtools-1__*, mcp__chrome-devtools-2__*
description: Drive the Voxt UI from Claude — chrome-devtools MCP setup, debugUI helpers, fast multi-user sign-in
---

# /debug-ui

Use this when the user wants Claude to interact with the running app in a
browser — log in as a test user, click around, take snapshots, send
messages, verify a fix in the UI. Also covers the multi-Chrome setup for
two-user scenarios.

For server lifecycle / rebuild concerns (stopping, restarting, reading
build errors), see `/server-loop`. They're meant to be used together: the
loop manages the .NET server; this skill manages the browser side.

## TL;DR

- One Chrome on `:9222`, one MCP at `localhost:8765`. Tools appear in
  Claude as `mcp__chrome1__*` (the older `mcp__chrome-devtools-1__*`
  names still resolve too). Two browsers? `ai chrome*2` on the host gives
  anonymous profiles on `:9222` and `:9223`; the second MCP at
  `localhost:8766` exposes `mcp__chrome2__*`.
- Sign in instantly with `await debugUI.signIn('+1 555 555 5550')` — the
  predefined phones (`+1 555 555 5550..5555`) and dev emails
  (`test-*@actual.chat`) all accept TOTP `111111`.
- Get the current user's id with `await debugUI.getUserId()` — also
  prints to console.
- One PM URL is `/chat/p-<userIdA>-<userIdB>` (sorted ordinally).

## When the user says "click around"

This is the right skill. Plan: confirm the setup is up (compose service +
host Chrome), drive the flow via the chrome-devtools MCP, fall back to
running JS via `evaluate_script` when the a11y tree is awkward.

### Prefer `debugUI` over UI clicks

If a step can be accomplished via a `debugUI.*` call, **do it that way**
— it's faster, deterministic, and dodges the modal/onboarding/bubble
soup. Use UI clicks only when:

- the user **explicitly** asked for the UI flow ("click Sign in", "go
  through the modal"), or
- the UI itself is what's under test (e.g. verifying the sign-in modal
  validates phone formatting, that the avatar uploader handles drag &
  drop, that a tutorial step renders correctly).

Concrete examples — defaults in italics, only switch to clicks if one of
the conditions above applies:

- Logging in a test user → *`await debugUI.signIn('+1 555 555 5550')`*
- Logging out → *`await debugUI.signOut()`*
- Getting the current user's id → *`await debugUI.getUserId()`*
- Switching render mode → *`await debugUI.setRenderMode('w')`*
- Restarting the server → *`debugUI.stopServer()`* (or curl)
- Skipping onboarding/bubbles → *`debugUI.resetOnboarding(false)` /
  `resetBubbles(false)` + reload*
- Navigating to a chat or settings page → *`location.assign('/chat/...')`*
  (regular browser navigation; `debugUI.navigateTo` exists but goes
  through the server-side `NavigationManager` and is rarely what you
  want — see the table below).

## 1. Setup overview

The browser-debug rig is three moving pieces:

```
host                      Claude container          docker-compose
─────────────────────     ─────────────────────     ─────────────────────
Chrome  :9222 (profile 1) ───CDP──┐                ┌─ chrome-devtools-mcp-1
                                  │                │  HTTP :8765 → Chrome :9222
Chrome  :9223 (profile 2) ───CDP──┼─ host.docker  ─┤
                                  │     .internal  │
                                  │                └─ chrome-devtools-mcp-2
                                  │                   HTTP :8766 → Chrome :9223
                                  │
        ┌─────────────────────────┴┐
        │  Claude MCP client       │  → tools `mcp__chrome1__*`
        │  (`.mcp.json`)           │           `mcp__chrome2__*`
        └──────────────────────────┘
```

The bridge is `supergateway --stateful --outputTransport streamableHttp`
wrapping `chrome-devtools-mcp` (which is stdio-only). Stateful is what
keeps `take_snapshot` → `click(uid)` working across calls.

The image is built from `services/chrome-devtools-mcp/` with both packages
baked in (no `npx` at runtime). Its entrypoint runs a supervisor loop and
a Chrome watcher: when host Chrome becomes unreachable for ~10 s the
watcher kills supergateway, the loop waits for Chrome to come back, then
starts a fresh supergateway. Combined with `restart: unless-stopped`,
restarting host Chrome no longer requires recreating the compose service —
clients just need to re-issue `take_snapshot` after the recycle, since
the previous snapshot UIDs are tied to the dead Chrome session.

### Bring it up (host)

```pwsh
ai chrome           # 1 instance, default profile, port 9222
ai chrome:50000     # 1 instance, default profile, port 50000
ai chrome*2         # 2 instances, anonymous profiles, ports 9222 & 9223
ai chrome*2:50000   # 2 instances, anonymous profiles, ports 50000 & 50001
ai edge[:PORT][*N]  # same shape for Microsoft Edge (default port 9322)
```

The `*N` suffix forces **anonymous (per-port) profiles** so cookies don't
bleed across the instances — that's how a single user agent can host two
Voxt logins side by side.

```bash
docker compose up -d --force-recreate chrome-devtools-mcp-1 chrome-devtools-mcp-2
```

To re-target an MCP at a different Chrome port, set `CHROME_DEBUG_PORT_1`
/ `CHROME_DEBUG_PORT_2` in `.env` (defaults `9222` / `9223`) and recreate
just that service.

### Probe it

```bash
curl -s -o - -w "\n%{http_code}\n" http://localhost:8765/healthz   # → ok / 200
curl -s -o - -w "\n%{http_code}\n" http://localhost:8766/healthz   # → ok / 200
```

If the MCP tools don't appear in Claude after compose is up, the most
common cause is that Claude was started before the MCP service was
healthy — restart Claude.

### Why two MCPs

Each `chrome-devtools-mcp` instance is hard-bound to one Chrome via
`--browserUrl` at startup. Cookies are per Chrome profile, not per MCP
session, so two independent Voxt logins → two Chromes → two MCPs. The
`isolatedContext` parameter on `new_page` does **not** isolate cookies
when MCP is connected to an existing Chrome — verified empirically.

### Combine the MCP with Playwright when you need richer data

The `chrome-devtools` MCP is convenient — `take_snapshot`, `click(uid)`,
`evaluate_script`, request lists — but its `list_console_messages` is
paginated, type-filtered, and (in practice) misses some entries. When
you need ALL of a page's output (every `console.*` level, every page
error, every unhandled rejection, network failures) **it's totally OK
to attach Playwright via CDP in parallel**, just for that.

Use Playwright **alongside** the MCP, not as a replacement:

- **MCP**: continues to drive the UI (snapshot/click/evaluate). It's
  ergonomic for one-off interactions and exploratory poking.
- **Playwright**: a tiny script connects via `chromium.connectOverCDP`,
  attaches `page.on('console' | 'pageerror' | 'crash')`, and streams
  every event to a log file. Read the file as needed.

The reverse direction also works: Playwright is also great for
**long predictable UI sequences** (e.g. "sign in, navigate to chat,
start audio + video, wait 60s, stop, re-enter") that you want to
re-run multiple times without retyping. MCP is poor for that — every
step is a separate evaluate.

Sketch (`tmp/console-tap.mjs`):

```js
import { chromium } from 'playwright';
const port = Number(process.argv[2] ?? 9222);
const browser = await chromium.connectOverCDP(`http://localhost:${port}`);
const ctx = browser.contexts()[0];
const page = ctx.pages().find(p => p.url().includes('local.voxt.ai')) ?? ctx.pages()[0];
page.on('console',   m => process.stdout.write(`[${m.type()}] ${m.text()}\n`));
page.on('pageerror', e => process.stdout.write(`[PERR] ${e.stack ?? e.message}\n`));
page.on('crash',     () => process.stdout.write(`[CRASH]\n`));
await new Promise(() => {}); // keep alive; SIGINT to stop
```

Run with `node tmp/console-tap.mjs 9222 > tmp/console.log 2>&1 &`
(or via the harness's `run_in_background` Bash flag), then drive the
page from the MCP as usual. Tail / grep the log file when something
mysterious happens.

**Caveats:**
- Playwright connects via CDP to the same Chrome — don't try to take
  exclusive control (no `browser.close()` until you're done with MCP
  too). Just attach listeners.
- Same realm, same cookies. Playwright sees what the MCP sees; this
  isn't isolation, it's an extra observer.
- `connectOverCDP` works because `ai chrome` already starts Chrome
  with `--remote-debugging-port=...`. Same flag the MCP uses.

## 2. The debugUI helpers (browser console)

`window.debugUI` is the single entry point — exposed in every page after
load. Most methods are async. Source lives in
`src/dotnet/UI.Blazor/Services/DebugUI/` (`debug-ui.ts` for the JS
surface, `DebugUI*.cs` partials for the server side).

### Auth (`DebugUI.Auth.cs` / `debug-ui.ts`)

| Method | What it does |
|---|---|
| `await debugUI.signIn(phoneOrEmail, opts?)` | **Local-dev-only.** Scripts the full sign-in: send TOTP, validate, confirm pending registration, wait for `AccountUI.OwnAccount` to flip to non-guest (5s timeout, throws on miss), then optionally clears onboarding/bubbles. `opts = { register?: true, skipOnboarding?: true, skipBubbles?: true }`. |
| `await debugUI.signOut()` | Goes through `AccountUI.SignOut` (deregister notifications, full client teardown, page reload). |
| `await debugUI.getUserId()` | Returns the signed-in user's UserId; also `console.log`s it. Works for guests too — returns the guest id (prefixed with `~`). |

**TOTP shortcut:** `signIn` always sends `111111`. That's the dev-bypass
code for `test-*@actual.chat` emails *and* the predefined phones the
loop wires up (`+1 555 555 5550..5555`). Other inputs will fail TOTP
validation — feature, not bug.

### Settings / state (`DebugUI.Settings.cs`)

| Method | What it does |
|---|---|
| `await debugUI.setRenderMode('a' \| 's' \| 'w')` | Switches Auto / Server / WASM. Mirrors `RenderModeSelector.ChangeMode` and triggers a page reload. |
| `debugUI.getCurrentRenderMode()` | Returns `'s'` or `'w'` — pure client-side, derived from whether the WASM runtime is loaded. Note: `'a'` (Auto) starts as `'s'` and upgrades to `'w'`. |
| `debugUI.resetOnboarding(enable)` | `false` skips all onboarding steps; `true` resets them. |
| `debugUI.resetBubbles(enable)` | Same shape for feature-tip bubbles. |
| `debugUI.enableAudioSync(enable)` | Toggles the `IDebugAudioSync` flag. |
| `await debugUI.getThreadPoolSettings()` | Inspect current thread-pool min/max/available. |
| `debugUI.changeThreadPoolSettings(min, minIO, max, maxIO)` | Runtime tune. Local-dev only. |

### Lifecycle / RPC (`DebugUI.cs`)

| Method | What it does |
|---|---|
| `debugUI.stopServer()` | Local-dev only. Same path as `/health/stop` and `s` keypress in the loop terminal. |
| `debugUI.disconnectBlazorRpc()` | Drops the Blazor circuit (server mode) or the WASM RPC peer. |
| `debugUI.disconnectJSRpc(workerKind?)` | Disconnects the JS-side RPC peer for one or all workers. |
| `debugUI.navigateTo(url)` | Server-side `NavigationManager.NavigateTo`. **Usually avoid** — prefer `location.assign(url)` or `location.href = url`, which is both faster and more predictable. Reach for this only when you specifically want the server's NavigationManager to drive the routing. |

### Diagnostics / monitors (`DebugUI.Monitors.cs`)

| Method | What it does |
|---|---|
| `debugUI.startFusionMonitor()` | Opens the Fusion compute graph monitor (WASM/MAUI only). |
| `debugUI.startTaskMonitor()` | Starts `TaskMonitor` + `TaskEventListener`. |
| `debugUI.startDOMEventSniffer()` | Records the last 50 Blazor + DOM events, dumps on `NullReferenceException` from a stale handler. Useful when you see the "Blazor event dispatch failed" red error. |

### Other handy ones

| Method | What it does |
|---|---|
| `debugUI.fakeSleep(seconds)` | Simulates the `OnDeviceAwake` go-to-sleep / wake transition. |
| `debugUI.showSafeAreas(true \| false \| undefined)` | Toggles the safe-area visualization classes on `<body>`. |
| `debugUI.clearSvgCache()` | Resets the SVG icon cache. |
| `debugUI.setAudioRecorderOffset(ms)` | Inject audio drift for catch-up policy testing. `0` clears. |
| `debugUI.testVideoRecordingQualityChange(periodSeconds=30)` / `testVideoPlaybackQualityChange` | Drive the quality controller through a synthetic -1/0/+1 sweep. |

## 3. Recipes

### Sign in two users in one go

```js
// Chrome 1 (mcp__chrome1)
await debugUI.signIn('+1 555 555 5550');           // user A
const a = await debugUI.getUserId();
// Chrome 2 (mcp__chrome2)
await debugUI.signIn('+1 555 555 5551');           // user B
const b = await debugUI.getUserId();
// Build the PM URL — peer chat IDs sort their two UserIds ordinally
const pm = '/chat/p-' + [a, b].sort().join('-');
location.assign(pm);
```

Skipping the modal/captcha/onboarding/bubbles takes ~1s per user — well
inside the 5s `When(!IsGuest)` window built into `signIn`.

### Switch render mode mid-session

```js
debugUI.getCurrentRenderMode();   // 's' or 'w'
await debugUI.setRenderMode('w'); // page reloads in WASM
// after reload:
debugUI.getCurrentRenderMode();   // 'w'
```

`Auto` first prerenders server, then upgrades to WASM — that's why
`getCurrentRenderMode` may return `'s'` on the very first paint of an
`'a'` selection and `'w'` once the runtime is up.

### Hard-reload after WASM-affecting changes

After a server restart, a plain reload is enough when render mode is
`'s'` (Server). When the page is in `'w'` (WASM) or `'a'` (Auto,
which generally upgrades to WASM), the SW keeps serving the stale
hashed `bundle.<hash>.js` and the cached `_framework` payload — your
fix won't take. You need a hard reload with caches cleared whenever:

- you edited any `.ts` / `.css` (the bundle filename gets a new
  fingerprint), **OR**
- you edited any `.cs` that runs in WASM (`UI.Blazor.App`,
  `UI.Blazor`, `Core`, etc., as opposed to server-only assemblies).

One-liner:

```js
const regs = await navigator.serviceWorker.getRegistrations();
for (const r of regs) await r.unregister();
const cks = await caches.keys();
for (const k of cks) await caches.delete(k);
location.reload();
```

From the chrome-devtools MCP, `navigate_page` with `type:"reload"` and
`ignoreCache:true` does the same, but the SW unregister is what makes
the reload actually pick up the new hashed bundle.

**Recommended workflow for an edit-and-test loop:**

1. `await debugUI.setRenderMode('s')` once at the start of the
   session. Server-render reloads are noticeably cheaper — no WASM
   runtime to re-download, no service-worker dance — so iteration is
   tighter.
2. Run all your scenarios. If any fail, fix and retry under `'s'`.
3. When everything passes under `'s'`, switch with `await
   debugUI.setRenderMode('w')` and re-run the same scenarios end to
   end. This is the final confirmation that the WASM build behaves
   the same.

**Step 1 is not just a speed tip — it is what keeps a session recoverable.**
In WASM the browser can end up holding cached assemblies that no longer match
the ones the server serves. The page then reload-loops on an assembly
mismatch, and **nothing you can do from inside that tab fixes it**: reloading
is what is already looping, so an agent driving that page is stuck and the run
is finished. Server mode has no such state.

If you do hit it, the way out is outside the browser — a hard restart of the
loop, which purges the WASM build outputs: `touch tmp/server-loop-hard-restart`
(or `h` in the loop terminal). See `/server-loop` → **Hard restart**.

Mixing in `'a'` (Auto) muddies the picture — the same session ends up
running partly server-rendered, partly WASM, and "is this a
server-only bug or a WASM bug?" gets harder to localize.

### Stop the server from the browser

```js
debugUI.stopServer();
```

If `server-loop` is running on the host, it'll rebuild and relaunch
automatically. See `/server-loop` for the full restart story.

### Find the test-user's UserId without going to Settings

```js
await debugUI.getUserId();   // logs to console + returns
```

Pre-`getUserId` workflow (Settings → Share → Copy link, then read the
`/u/<id>` URL) still works as a fallback for older builds.

## 4. Peer chat URLs

Direct messages between two users live in a *peer chat*. Its id is
formed from both UserIds, sorted ordinally (string comparison —
`StringComparer.Ordinal`), prefixed with `p-`, joined with a hyphen:

```
ChatId = "p-" + min(userIdA, userIdB) + "-" + max(userIdA, userIdB)
URL    = /chat/<ChatId>
```

UserIds in this codebase are 6-character base-32-ish strings (e.g.
`4An15V`, `7GI0pt`). The corresponding peer chat URL is therefore:

```
/chat/p-4An15V-7GI0pt    # since '4' < '7' (0x34 < 0x37)
```

The order is fixed by the server (`PeerChatId.New` sorts internally), so
**you must sort before constructing the URL** — feeding it in the wrong
order yields a "Chat not found" page even though both ids are valid.

### Building it from JS

```js
// In either Chrome (already signed in)
const me = await debugUI.getUserId();      // e.g. '4An15V'
const peer = '7GI0pt';                     // user B's id, however you got it
const chatId = 'p-' + [me, peer].sort().join('-');
location.assign('/chat/' + chatId);
```

`Array.prototype.sort` defaults to lexicographic comparison, which
matches the server's `StringComparer.Ordinal` for ASCII-only ids.

### Other chat-id shapes you'll see

- **Notes** (self-chat) — a 10-character random ChatId, e.g.
  `/chat/YSLweHvRYa`. Not a `p-...` form.
- **Group chats / places** — also 10-character random ChatIds.
- **Voxt Announcements** — a stable alias `/chat/announcements`.

### Privacy gate (relevant to PM tasks)

A user can post **up to 2 messages** to a peer they're not in contacts
with before the third send is server-rejected with
`InvalidOperationException: You can send up to 2 messages until this
user adds you to their contacts or replies.` The recipient sending one
message back, or adding the sender to contacts, opens the gate
permanently.

The client *currently* silently retries the rejected send (the message
sits with a clock icon), so a UI test that doesn't account for this
will look like the message simply never arrived. When testing PM flows,
either stay under 2 unanswered messages or arrange a reply first.

## 5. Available pages (`@page` routes)

All Razor `@page` routes in the codebase, by category. Use these with
`location.assign('/...')` (preferred) or `debugUI.navigateTo('/...')`.
For an up-to-date list, run `Grep` for `^@page` over `**/*.razor`.

### Core app

| Route | Component | Notes |
|---|---|---|
| `/` | `HomePage` | Landing — sign-in entry point. |
| `/chat` | `ChatPage` | Default chat ("Notes"). |
| `/chat/{*Route}` | `ChatPage` | Specific chat. `Route` is the ChatId, e.g. `p-<a>-<b>`, `YSLweHvRYa` (random), `announcements`. |
| `/u` / `/u/{PageRoute}` | `UserPage` | Resolves a UserId or `@alias` and redirects to the peer chat. |
| `/place/{PlaceSid}` | `PlaceInfoPage` | A "Place" (chat hub) by short id. |
| `/join/{InviteId}` | `ChatInvitePage` | Accept a chat invite. |
| `/settings` / `/settings/{SettingsOption}` | `SettingsPage` | Settings modal — option = tab key (`account`, `ui`, etc.). |

### Public / landing

| Route | Component |
|---|---|
| `/docs` / `/docs/privacy` | `DocsPrivacyPage` |
| `/docs/terms` | `DocsTermsPage` |
| `/docs/faq` | `DocsFaqPage` |
| `/docs/cookies` | `DocsCookiesPage` |

### System / error

| Route | Component | Notes |
|---|---|---|
| `/Error` | `ErrorServerPage` | Server-rendered error page. |
| `/unavailable` / `/unavailable/{What}` | `UnavailablePage` | Maintenance / unsupported-browser etc. |
| `/fusion/close` | `FusionCloseServerPage` | Sign-in / close-flow callback. |

### Test pages

Everything under `/test/*` is a developer-only page — wired up to
exercise a specific component, integration, or system probe. Most
require sign-in. Use them when you need a focused stage for the thing
they test.

| Route | What it exercises |
|---|---|
| `/test/auth` | Sign-in / claims plumbing. |
| `/test/blazor` | Blazor render / RPC sandbox. |
| `/test/copy-chat2place` | Admin-style chat→place migration. |
| `/test/digest` | Email digest preview. |
| `/test/discover` | Discover feed component. |
| `/test/dive-in-modal` | Dive-in modal animation. |
| `/test/email-templates` | All email templates with sample data. |
| `/test/embedded` | Embedded-host (iframe-like) shell. |
| `/test/emojis` | Emoji picker / rendering. |
| `/test/error-barrier` | Error boundary behaviour. |
| `/test/external-contacts` | Phone-book sync stub. |
| `/test/features` | Feature flag inspector. |
| `/test/incoming-share-modal-test` | Inbound share → chat-picker modal. |
| `/test/js` | Misc JS interop probes. |
| `/test/landing-background` | Landing-page background animation. |
| `/test/markup-editor` | Rich-text composer. |
| `/test/maui` | MAUI-only probes (won't render in web). |
| `/test/mesh` | Mesh diagnostics. |
| `/test/mic-permission-guides` | Per-browser mic permission guides. |
| `/test/photo-permission` | Photo / camera permission flow. |
| `/test/place-info` | Place info card. |
| `/test/reconnect-overlay` | Connectivity reconnect overlay. |
| `/test/render-slot` | `RenderSlot` slot mechanics. |
| `/test/requirements` | UI requirement banners. |
| `/test/skeletons` | Skeleton-loader components. |
| `/test/svg-cats` | SVG cat illustrations gallery. |
| `/test/system` | System-level probes (clock, env, runtime). |
| `/test/toasts` | Toast / banner styles. |
| `/test/totp` | TOTP input component. |
| `/test/ui-colors` | Theme color palette. |
| `/test/video-input` | Video capture device picker. |
| `/test/video-transcoding` | Video upload + transcoder. |
| `/test/virtual-list` | Virtual list scroll/bench. |
| `/test/web-splash` | Web splash screen. |

## 6. Audio recording, transcription, video, screencast

The chat footer next to "Write a message" is where the call controls live.
Clockwise from the message field on a single-user view:

```
[+] [Write a message ...] [bell] [video] [BIG record button] [sliders]
```

After recording starts, two extra in-call controls become reachable on the
caller's side: a **screencast (screen-share)** toggle and a video toggle for
the second/third participant once they've joined.

### The order matters: audio first, then video

**You can't start video without audio** (recording must be on). The expected
ordering for the *initiator* is:

1. Click the **big record button** — recording (mic + transcription) starts.
   You see a transcription stream appear in the chat as your audio is
   recognised.
2. Click the **video** toggle (camera icon next to the record button) — a
   "Video preview" modal appears. Click **Start video**.
3. Your own video tile shows up labelled "You".

For the *joiners*: when someone else is recording in the chat, the chat
header shows **Join muted**. After clicking it they're in the call as
listeners. They can then enable their own video — and crucially, **without
enabling their own recording**. So a typical N-way video session has only
the initiator paying the transcription cost.

### Stop recording the moment the test isn't about transcription

**Treat audio recording as resource consumption — not a default-on
state.** Every second of recording bills against the speech-recognition
provider. The rule:

- Don't turn it on unless the test actually needs it.
- The instant you don't need it any longer, turn it off — even if
  you're "about to use it again in a minute". You can always
  re-enable later with the same record button. Cost during off-time
  is zero.
- **Before any longer edit / read / investigation cycle, stop
  recording first.** A long edit pass with a mic still hot is the
  most expensive shape of "Claude is thinking" — and it's pure
  waste, since transcription continues regardless of whether the
  assistant is actively driving the UI.

To stop recording: click the **big record button** again. It's now
glowing red/pink; clicking it returns it to its idle state. **The
active video tiles keep streaming** — stopping audio doesn't drop
video calls, so there's no penalty for being aggressive about it.

You can also stop recording chat-by-chat from the **Active Chats**
strip that appeared in the bottom-left when recording started — each
entry has a mic icon you can click to mute that chat specifically.

If you forget, the **idle video monitor** stops recording (and the
call) after **15 minutes of inactivity**. Don't rely on this — it's a
cleanup safety net, not a substitute for stopping deliberately.

### Screencast

The screen-share toggle is in the same row of in-call controls (icon looks
like a monitor / outward arrows). Clicking it triggers `getDisplayMedia`.
Because `ai.ps1` launches Chrome/Edge with
`--auto-select-desktop-capture-source=Voxt`, the picker is bypassed and the
Voxt window is auto-selected as the source — what gets shared is whatever
that Chrome instance is rendering (the call UI itself, by default). See
`/server-loop` skill / `ai.ps1` notes for the flag rationale.

Caveats — same as for the camera/mic flags:

- The shared content is **real pixels** of the selected window. Chrome has
  no `--use-file-for-fake-display-media` equivalent. If you specifically
  need a known video feed for the screen-share, point a sibling tab at a
  page playing the file, then switch the auto-select flag to
  `--auto-select-tab-capture-source-by-title=<that title>`.
- With `ai chrome*N` (multiple Voxt-titled windows), the desktop-capture
  auto-pick may grab a sibling Chrome's window. The tab variant of the flag
  scopes the match to tabs in *the calling browser*, which is usually what
  you want for multi-instance tests.

### How to verify each piece is alive

| Signal | Where to look |
|---|---|
| **Audio recording** active | Big record button glowing red/pink; "Active Chats" strip in bottom-left of the chat list shows a red mic icon next to the active chat; sidebar chat row shows a small red mic indicator. |
| **Transcription** working | New transcribed messages from your user appear in the chat panel within 1–2s of audio playing. Compare against `lib/data/test-audio-1.wav` content if you're using the fake mic — text will match the recording. |
| **Local video** captured | "You" tile in the chat (PiP corner if there's a peer, otherwise full-bleed). Frames cycle (not a static photo). |
| **Peer video** received | A tile labelled with the peer's name. With both Chromes feeding `test-video-1.mjpeg`, both tiles will show identical frames in lockstep. |
| **Screencast** active | A second self-tile or screen-share status indicator appears; recipients see a tile with content matching the auto-picked window. |

### About `--mute-audio` (browser playback) vs recording

The debug Chromes are typically launched with `--mute-audio` so the
testing host doesn't blast every transcription playback or call ringtone.
That flag suppresses **audio output rendering only** — it does not affect
`getUserMedia` capture, so the **mic still records** and the server still
transcribes. Stopping recording is the lever that actually saves cost; the
mute flag just keeps your speakers quiet.

## 7. Caveats

- **Rebuild required**: `debugUI.signIn` / `signOut` / `getUserId` /
  `setRenderMode` / `getCurrentRenderMode` were added recently. If
  they're missing, the page is still on an older bundle — reload after
  the next loop rebuild.
- **`signIn` is local-dev-only** — it auto-uses TOTP `111111`, which
  only works against the local dev-bypass paths. The server enforces
  `BaseUrlKind.Local` + `IsDevelopmentInstance` and will throw
  `Unauthorized` elsewhere.
- **PM privacy gate**: a sender can post up to 2 messages to a stranger
  before the recipient must contact-add or reply. The third send
  *server-rejects* with `InvalidOperationException`, but the client
  currently silently retries — don't be surprised when a third PM hangs
  with a clock icon. (Known UX gap; tracked separately.)
- **Stale snapshot uids**: every `take_snapshot` invalidates the prior
  snapshot's uids. If a `click(uid)` fails with "Element with uid X no
  longer exists", re-snapshot.
