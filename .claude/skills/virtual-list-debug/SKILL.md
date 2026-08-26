---
name: virtual-list-debug
version: 2.0.0
description: |
  Instruments and workflow for debugging the VirtualList — the chat transcript (`InfiniteList`) and
  the sidebar (`FiniteList`). Covers the built-in consistency checker, the on-screen overlay, the
  five `?vl*` URL flags, the touch-gesture rig, the frame recorder, attaching to a real Android
  device over CDP, and how to read a trace without being fooled by a measurement artifact. Use when
  the list jumps while scrolling, blanks, sticks on skeletons, or the overscroll/bounce misbehaves.
  The component's specification is `docs/ui/virtual-list.md`; this skill is only how to *measure* it.
  Pairs with /debug-ui (browser side) and /server-loop (server side).
allowed-tools:
  - Bash
  - Read
  - Edit
  - Write
  - Grep
  - Glob
  - mcp__chrome1__*
  - mcp__chrome2__*
  - mcp__chrome-devtools-1__*
  - mcp__chrome-devtools-2__*
  - AskUserQuestion
---

# /virtual-list-debug — instruments and workflow

## Ground rules before you measure anything

**The spec is [`docs/ui/virtual-list.md`](../../../docs/ui/virtual-list.md) (132KB).** Look invariants up
there; do not trust a summary, including this one. The section map you will actually use:

| Need | Section |
|---|---|
| Vocabulary — `chainStart`, `scrollOffset`, pinning vs. anchoring | §1 |
| The eleven enforced invariants | §2 |
| The model and the two position terms | §3.1 |
| Render direction (`Natural` \| `Reverse` — there is **no** `Auto`) | §3.2 |
| States, transitions, loops, which term may move when | §3.3–§3.6 |
| Overscroll: the band, resistance, bounce, floor — what the rig judges | §3.7 |
| Spacers, end anchor, short conversations | §3.8 |
| Re-anchoring | §3.9 |
| Item heights and stability | §3.10 |
| Rules that came from painful debugging | §3.12 |
| Browser/OS/device quirks — read §4.11 before injecting any gesture | §4 |
| Measuring, and the traps that gave confidently wrong answers | §5 |
| `FiniteList` | §6 |

Two facts that decide whether a measurement means anything:

- **`scrollTop` alone is meaningless.** What the user sees is the sum of three terms (§3.1):
  `scrollTop`, the container's position in the wrapper, and the composed transform. Each moves
  content without the others moving. The only honest measure is the on-screen position of a **real
  item, followed by key**, sampled every frame.
- **The wrapper is a fixed 4,000,000px** (`InfiniteSize`, mirrored in `InfiniteList.razor.cs` and
  `infinite-list.ts` — the two must agree). It never resizes for the life of the list. Nothing in the
  current design shrinks a spacer to re-origin the coordinate system.

---

## 1. The instruments

### 1.1 The consistency checker

```js
debugUI.virtualListDebug(true)                   // preferred; warns if InfiniteList isn't loaded yet
globalThis.InfiniteList.setDebugEnabled(true)    // same thing, one level down
```

Sets the static `InfiniteList.isDebugEnabled` flag, so it covers every live `InfiniteList` **and
every one created afterwards**. State is per page — a reload drops it. `FiniteList` is **not**
instrumented.

It runs `checkModelDrift(reason)` from exactly three places in `infinite-list.ts`:

- `render` — after a render has been applied,
- `settled` — when the list comes to rest,
- `setDebugEnabled` — once, immediately, on every live list when you turn it on.

`checkModelDrift` bails out early when there are fewer than two items or `stability.isAnimating`
(height animations in flight), so **a silent checker during an animation is not a pass**. It picks
the first non-`position: sticky` item as the baseline — conversation headers report where they are
stuck, not where they sit in the flow — and compares every other non-sticky item's real
`getBoundingClientRect().top` against `offsets[i] - offsets[base]`. Only the **worst** offender is
reported, past `DriftWarnThresholdPx` (8px).

The same pass always calls `checkContentOverflow(reason)`, which walks every item that is not
`c-height-unsettled` and compares its box against the height its content actually requires (content
box + content margins + item padding + borders). Two warnings come out of it: an item whose content
needs more than `ContentOverflowThresholdPx` (2px) more than its box — that surplus paints straight
over the item below — and an item that renders more than one child element, since only the first is
measured.

**Output is `warnLog` only.** There is no violation buffer, no ring, nothing to drain, no return
value — you read the browser console. The `InfiniteList` scope defaults to `Warn`, so these appear
without any setup. To also see the flow (`repinEdge`, `jump`, `applyLayout: re-centred`):

```js
logLevels.override('InfiniteList', 1)   // 1 = Debug; persisted in localStorage + IndexedDB
logLevels.reset()                       // back to package defaults
```

`ScrollController`'s own backstop bypasses the log scopes entirely and calls `console.warn` directly:
`ScrollController: phase '<phase>' stopped advancing` means a phase ran `LockWatchdogMs` (1500ms)
without finishing and the scroller was handed back. That message is always a defect.

What the checker **does not** cover: blank viewports, gaps between items, scroll clamps, jumps
during an animation, anything in `FiniteList`, and anything about the band. For those, record a trace
(§3).

### 1.2 The on-screen overlay

```js
debugUI.showVirtualListOverlay(true)    // or false
```

A one-line readout pinned to the top-right of **every** virtual list, infinite and finite. It
persists in `UserAppSettings.IsVirtualListOverlayEnabled` and is re-applied at startup by
`DebugUI.RestoreVirtualListOverlay` → `debugUI.applyVirtualListOverlay` (which does not write back).
There is no Settings-UI toggle; the console is the only entry point. It refreshes every 200ms and
reads only inline styles, so it costs no layout. It lives in `document.body`, because
`.virtual-list` sets `contain: strict`, which clips even fixed-position descendants.

A chat scrolled to the newest with older messages still to load:

```
↑  ⇊⟳  ~(5/48]~  h=115.7
```

**The layout is fixed — glyphs never appear or disappear, only their colour changes**, so the bar
never reflows. Dark gray (`off`) means "not this / not happening".

| Field | Meaning |
|---|---|
| `↑` / `↓` | render direction — `↑` = reverse (cyan, the notable state), `↓` = natural (light gray). The chat view is always `Reverse` |
| `⇊` | a data request is in flight (amber) |
| `⟳` | a render/animation is in progress (cyan) |
| `~` … `~` | start / end spacer shown, amber when lit. They sit *outside* the brackets because that is where a spacer physically is |
| `[` `]` vs `(` `)` | square = that end of the data is loaded (`hasVeryFirstItem` / `hasVeryLastItem`), round = there is more that way |
| bracket **colour** | green = that end is the pinned edge. Glyph and colour are independent because loaded-ness and pinnedness are |
| `5/48` | **infinite**: visible / rendered. `total` is null for infinite lists, so nothing follows the slash before the closing bracket |
| `6(0-6)/6` | **finite**: visible(first-last rendered index) / total |
| `h=115.7` | mean height of the items rendered right now |

Two things make it watchable: `⇊` and `⟳` stay lit for `HoldMs` (500ms) after they end, driven by
`lastDataRequestAt` / `lastRenderAt`, so a request that begins and ends between two ticks is still
drawn; and a changed value flashes white for the same window, keyed on *text plus tone*, because with
a fixed layout most state changes are colour-only. Counters do not flash and have reserved widths, so
the bar does not strobe or resize as numbers cross a power of ten.

### 1.3 URL flags

Five, all read from `location.search`. Put them in the URL and reload — `?vlloaddelay` and
`?vlfriction` are read once at module load, `?vllock`, `?vltakeover` and `?vlwheel` are memoized on
first use.

| Flag | Where | What it does |
|---|---|---|
| `?vlloaddelay=<ms>` | `virtual-list.ts` `readLoadDelay` | Holds **every** data load for that long. A fast fling into history is only interesting while the loads cannot keep up with it, and that state is otherwise a race to catch. This is how you reproduce continuous "load more" on demand |
| `?vllock=0` / `=1` | `scroll-controller.ts` `canLockOverflow` | Forces the two-frame `overflow: hidden` fling kill off / on. Default is on everywhere except WebKit. `0` reproduces the "nothing stops a fling" half of the iOS problem on desktop |
| `?vltakeover=0` / `=1` | `scroll-controller.ts` `canTakeOverMomentum` | Forces the WebKit release takeover off / on. Default is iOS+WebKit only. `1` exercises the FLIP handoff and the spring on desktop Chrome |
| `?vlfriction=<max>x<ramp>` | `scroll-controller.ts` `readFriction` | Overrides the resistance curve (default `0.667x444`) for feel tuning on a device. `max` must be in (0, 1) |
| `?vlwheel=0` | `scroll-controller.ts` `canOwnWheelGestures` | Stops the controller driving precise-device (trackpad / precision touchpad) wheel gestures near a limit, handing them back to the browser. Reproduces the edge jitter the takeover exists to remove — useful for A/B on a device |

Also useful: `/test/virtual-list` (admin only) takes `RangeSeed`, `ContentSeed`, `DefaultEdge`,
`RenderDirection` (0 = Natural, 1 = Reverse), `AnimateItemHeight`, `HeightTransition`, `HeightDelay`.
**Pin `RangeSeed` and `ContentSeed`** unless the churn is the subject — otherwise the page re-seeds
its range every 3s and item content every 10s and moves under your measurement. Note §4.10: at phone
width the chat-list panel is painted over that page and hit-tests every point of it, so injected
gestures never reach the list there; real-gesture tests have to run against a chat.

### 1.4 The rig — `tools/virtual-list-rig/`

`rig.mjs` drives **real touch gestures** into a Chrome debug port over CDP, records the controller
frame by frame with `recorder.js`, and judges the result mechanically against the rules in §3.7. This
is how the overscroll model is verified; the phones are for feel.

Needs Chrome started with remote debugging (`ai chrome`, port 9222; `ai chrome*2` adds 9223), a Voxt
chat open in it, and the server running. **Give the page a mobile viewport first** — at desktop width
the chat view is not the touch-scrolling element (chrome-devtools MCP: `emulate` with
`412x915x2.6,mobile,touch`).

```bash
node tools/virtual-list-rig/rig.mjs all 9222              # every scenario, lock on
node tools/virtual-list-rig/rig.mjs all 9222 nolock       # ordinary path without the two-frame overflow kill
node tools/virtual-list-rig/rig.mjs all 9222 takeover     # force the iOS takeover on Chrome
node tools/virtual-list-rig/rig.mjs swing-back 9222       # one scenario
node tools/virtual-list-rig/soak.mjs 60 9223              # 60 random gestures, judged as a whole
node tools/virtual-list-rig/soak.mjs 60 9223 takeover
node tools/virtual-list-rig/follow.mjs 9223               # the follow's write path, scroll vs transform
```

Scenarios: `pull-release`, `throw-out`, `throw-top`, `swing-back`, `catch`, `catch-drag`, `updown`,
`brake`, `repeat-catch`, `repeat-updown`, `control-fling`, `fling-edge`, `native-resume` (takeover
only), `cross-and-back`. Traces land in `tmp/traces/rig-<scenario>.json`; the soak's in
`tmp/traces/soak.json`. `soak.mjs` uses a fixed seed, so a failing run re-runs identically.

Run the matrix on **two chats**: one longer than the viewport (a real band) and one shorter (a band
collapsed to a point, `min == max`). Both must pass on the ordinary lock/nolock paths and with
takeover forced.

The judge checks rules, not feel: the band never inverts, no gesture starts inside a band, every
excursion ends with the band transform at zero and the position legal, the finger is followed through
the curve's slope, the band never moves by more than the rules allow, and the transform is the band's
alone - what is left of it once the band's share is taken out has to be zero on every frame, which is
what makes "the list writes no transform" a checked property. `coast after release` on
`swing-back` and `updown` should match `control-fling` — a throw from overscroll is a throw. On
`fling-edge` the excursion should go out to roughly `MaxBouncePx` (150px) past where it was noticed
before coming home; that is the bounce.

**Three things the rig cannot do.** Synthetic CDP touch drops flings intermittently unless moves are
~12ms apart and sent without awaiting each ack — so a single zero coast proves nothing, repeat it.
Desktop Chrome does not scroll off the main thread the way WebKit does, so the iOS-specific jitter
does not reproduce here; `nolock` reproduces the "nothing stops a fling" half only. And it cannot
reproduce iOS choosing an unscrollable target before `touchstart`: `catch-drag` proves that the
controller releases its lock and preserves geometry, not that the same caught gesture can resume
native scrolling on iOS.

> **Known gap:** both `rig.mjs` and `soak.mjs` still call
> `globalThis.debugUI?.listVirtualListViolations?.(true) ?? []`, which no longer exists. The
> `violations` column in their output is therefore **always 0** and proves nothing. The checker they
> enable still warns to the console — read it there until those two call sites are replaced.

### 1.5 The recorder — `tools/virtual-list-rig/recorder.js`

A self-contained IIFE you evaluate in the page. It finds `.virtual-list.infinite-list`, waits for its
`scrollController` (the controller sets that expando on its element in its constructor), and installs
itself, re-arming every 500ms so it survives the list being re-created by navigation. It exposes:

```js
window.__vlt.rows      // per-frame samples (capped at 12000)
window.__vlt.events    // touchstart/move/end/cancel (capped at 6000)
window.__vlt.stop()    // restores onTransform, removes the document listeners
```

**Why it does not sample only on rAF — read this before writing your own tracer.** A recorder that
samples on its own `requestAnimationFrame` can run *before* the controller's frame callback and so
reads the **new** `scrollTop` against **last** frame's transform. That produces a phantom step of
exactly the scroll delta on every moving frame — a perfectly convincing, entirely fictional jump on
every frame of every fling. `recorder.js` therefore hooks `sc.onTransform` (chaining the previous
handler) and takes an *authoritative* sample in a microtask right after the controller writes; its
rAF loop is only a fallback for frames where nothing was written, and an authoritative sample
replaces a rAF sample from the same frame. Samples are throttled to 4ms apart.

Row fields:

| Field | Meaning |
|---|---|
| `t` | ms since the recorder armed |
| `top` | `list.scrollTop`, native |
| `tf` | the composed `translate3d` y on `.c-virtual-container` |
| `base` | `tf - band` — whatever is on the transform that is not the band's. The list writes no transform, so this is 0 on every frame, and the judge fails a run where it is not |
| `phase` | `in-band` \| `following` (finger down past an edge) \| `engaged` (past an edge, nobody holding it) |
| `decision` | `momentumPhase`: `none` \| `arming` \| `transform` (the WebKit takeover) |
| `vis` | what the band puts on screen — `signedOverscroll(over)`, i.e. after resistance. 0 when in-band |
| `band` | `scrollController.bandOffset` — the band's own share of the transform |
| `drift` | the **raw** pull `over` behind `vis`. 0 when in-band |
| `spr` | signed `springVisible` during a takeover |
| `sp` | `scrollSpeed` in px/s (the return phase's measurement; 0 unless `engaged`) |
| `lock` | 1 while `overflow-y: hidden` is forced on the scroller |
| `min`, `max` | `getEffectiveScrollLimits()`, in the same `scrollTop` coordinate as `top` |
| `cy` | the container's top relative to the list's top |
| `ch`, `cl` | container height, list `clientHeight` |

Event fields: `t`, `type`, `n` (touch count), `y` (mean clientY), `top`, `phase`, `decision`.

---

## 2. Attaching to a real device over CDP

### Android Chrome

```bash
adb devices                                             # confirm the phone is authorized
adb forward tcp:9333 localabstract:chrome_devtools_remote
curl -s http://localhost:9333/json/list | jq -r '.[] | select(.type=="page") | "\(.url)\t\(.webSocketDebuggerUrl)"'
```

**Forward to a free port.** Host Chrome usually owns 9222 and 9223 (`ai chrome`, `ai chrome*2`), and
`adb forward tcp:9222 …` will either fail or shadow the desktop browser the rig expects. 9333 is
free in practice.

Then attach with `ws` (already a dependency, `^8.18.0`, installed) and follow the pattern in
`rig.mjs`: open the socket from `webSocketDebuggerUrl`, `Runtime.enable`, and evaluate with
`Runtime.evaluate` + `returnByValue: true` (add `awaitPromise: true` for async expressions, and
always check `exceptionDetails` — a page-side throw comes back as a *successful* CDP response).

```js
import { createRequire } from 'node:module';
import fs from 'node:fs';
// Resolved against this script's own location — adjust the hops if it doesn't sit in tools/<dir>/.
const require = createRequire(new URL('../../package.json', import.meta.url));
const WebSocket = require('ws');

const pages = await (await fetch('http://localhost:9333/json/list')).json();
const target = pages.find(x => x.type === 'page' && (x.url || '').includes('voxt.ai'));
const ws = new WebSocket(target.webSocketDebuggerUrl, { perMessageDeflate: false });
// ... id/pending plumbing exactly as in rig.mjs ...
await send('Runtime.enable');
await ev(fs.readFileSync('tools/virtual-list-rig/recorder.js', 'utf8'));
// ... gesture ...
const trace = JSON.parse(await ev('JSON.stringify({ rows: window.__vlt.rows, events: window.__vlt.events })'));
```

The recorder is device-agnostic — it is exactly what the phones used. Screen on and the tab in the
foreground, or you get no animation frames and an empty trace.

### What actually moves the list, and what does not

- **`Input.synthesizeScrollGesture` with `gestureSourceType: 'touch'` does not move the list.**
  Verified: delta 0. On this page it delivers `touchstart: 1, touchmove: 0, touchend: 1` (§4.11).
  Do not build anything on it.
- **`Input.dispatchTouchEvent` sequences do**, once `Emulation.setTouchEmulationEnabled` is on — but
  only if the moves arrive close together. Moves more than ~25ms apart get the fling dropped
  intermittently. `rig.mjs`'s `fling()` is the working pattern: fire moves **12ms apart without
  awaiting each ack**, collect the promises, `Promise.all` them, then `touchEnd`.
- **A mouse or wheel gesture is useless for anything past an edge.** `ScrollController.onScroll`
  snaps `scrollTop` back to the boundary whenever the motion is not a finger
  (`!(isTouching || isTouchMotion)`) — by design, and that includes middle-button autoscroll. You
  will never see a band from one.
- **Over adb, `Input.dispatchTouchEvent` arrives too unevenly** for Chrome's velocity tracker to read
  as a throw — it scrolls but never flings (§4.10). On a phone use `adb shell input swipe`, and map
  page → screen coordinates as `chrome + cssY * devicePixelRatio` where
  `chrome = screenHeight - innerHeight * devicePixelRatio` (~532px on a 3120px device; ignoring it
  puts every gesture in the URL bar).
- **Bring the tab to the front** (`Page.bringToFront`) and check that the point you aim at hit-tests
  into the list.

---

## 3. Reading a trace

Load a trace and go through these in order. Each one found a real defect; none of them is
`scrollTop` on its own.

**1. Distance past `min`/`max` while `phase === 'in-band'`.** In-band means the controller believes
the position is legal. If `top < min` or `top > max` while in-band, the crossing was never noticed
and no band was drawn — the list is simply somewhere it must not be.

```js
const escapes = rows.filter(r => r.phase === 'in-band' && (r.top < r.min - 1 || r.top > r.max + 1));
const worst = Math.max(0, ...escapes.map(r => Math.max(r.min - r.top, r.top - r.max)));
```

A frame or two just after a load is not automatically a bug — the limits are recomputed from the
model and a page of history arriving moves them. A **sustained** run, or an excursion of hundreds of
px, is.

**2. Unbroken `following` runs.** `following` means a finger is down past an edge. A long run of it
with no `touchend` in `events` is a release that never registered — the list stays parked and the
band never returns. There are three backstops (`TouchStaleMs` 3000, `TouchSilenceMs` 400,
`LockWatchdogMs` 1500), so a run longer than ~1.5–3s means one of them fired; find the matching
`console.warn`.

**3. `lock` frames.** `lock === 1` is `overflow-y: hidden` forced on the scroller — the user cannot
scroll at all during those frames. Two-frame runs are the intended fling kill. Long runs, or a run
that ends the trace, mean the scroller was never handed back.

**4. Peak `|vis|`, and how far the position travelled.** `vis` is what the band actually puts on
screen after resistance, so it is bounded by the curve (`0.667` max resistance over a `444px` ramp)
and by `MaxBouncePx` (150) for a release. A large `|vis|` is a band that was seeded wrong. Keep it
separate from ordinary travel: the limits themselves already grant up to **three screens**
(`MaxOverscrollScreens`) past *loaded* content whenever that end of the data is not yet known
(§3.7) — travelling into that space is legal and is how reading further back begins. It is not
overscroll, and "fixing" it re-breaks catch-up.

**5. Then, and only then, rendered motion.**

> **The container moving is not a jump.** `cy` (and the container's `top`) move on every prepend by
> construction: loading history changes `chainStart` and the container's position **by the same
> amount**, so the items inside it do not move at all (§3.1). A check on `cy`, or on container geometry
> alone, will report a screenful of motion that never happened.

The only honest rendered-motion check **follows a specific item by key** across frames:

```js
const el = document.querySelector('.virtual-list.infinite-list .item[data-key="<key>"]');
const y = el.getBoundingClientRect().top;   // sample this every frame, compare consecutive samples
```

Items carry `data-key` on the `li.item` (groups are `li.group` and carry none), which is the same
handle `infinite-list.ts` itself indexes by.

When something does move, decompose it into the three terms that can cause it (§3.1): the container's
position in wrapper coordinates, the item's offset within the chain, and the scroll position.

### Artifacts that look exactly like bugs

- **A rAF-only recorder reports a phantom step of exactly the scroll delta on every moving frame.**
  See §1.5. This is the single most expensive trap here.
- **rAF sampling lags a composited scroll**: during a fling the main thread can read 0 for one frame
  and double for the next. It cancels over three frames — discontinuity tests must run on a smoothed
  series, never on a single-frame delta.
- **A hidden tab gets no rAF at all.** A Chrome window behind another window reports
  `visibilityState === 'hidden'` and stops firing animation frames; every probe hangs and every
  recording comes back empty. `Page.bringToFront` does **not** fix it — use a visible window or a
  dedicated `--headless=new` instance.
- **A teleported scroll position reads as a huge velocity.** Reaching an edge with
  `scrollTop += 3000` in a loop hands the bounce thousands of px/s it did not earn (or, if the writes
  land in the same millisecond, none at all). Walk out over several frames, or drive it through
  `scrollController.scrollTo`, which zeroes the estimate for its 300ms suppression window.
- **Test data that changes under the measurement** — pin `RangeSeed` and `ContentSeed` on
  `/test/virtual-list`.
- **A stale WASM bundle.** After a rebuild, a plain reload in WebAssembly mode keeps serving the
  cached hashed bundle from the service worker. Hard-reload with caches cleared, or you are measuring
  the old code (§4.12).

---

## 4. Symptom → first instrument

| Symptom | Start with |
|---|---|
| "It jumps when I scroll past certain messages" | Checker on, watch for `model drift after render/settled`. Then a trace, followed by item key — not by container geometry |
| One message paints over the next | Checker on — `content overflow after …` names the item and the surplus |
| Blank / partial-blank viewport | Trace: `top` vs `min`/`max`, and whether `cy + ch` still covers `cl`. Check `repinIfStranded: gap=…` warnings |
| Stuck on skeletons / cannot reach the bottom | Overlay: is `⇊` lit forever, is the end bracket square? Then `?vlloaddelay=0` vs. a large value to see whether it is a load-ordering problem |
| Overscroll / bounce / edge feel wrong | The rig, both chats, all three modes. Then §3.7 |
| Scrolling dies entirely | `lock` frames in a trace; the `ScrollController: phase '…' stopped advancing` warning |
| Continuous "load more" during a fling | `?vlloaddelay=<ms>` reproduces it on demand |
| Anything about the sidebar | `FiniteList` — §6. It is not instrumented by the checker; the overlay does cover it |

---

## 5. Files

| Path | What it is |
|---|---|
| `docs/ui/virtual-list.md` | **The specification.** Invariants live here, not in this skill |
| `src/dotnet/UI.Blazor/Components/VirtualList/infinite-list.ts` | The chat list. `isDebugEnabled` / `setDebugEnabled`, `checkModelDrift`, `checkContentOverflow`. Registers `globalThis.InfiniteList` (and `InfiniteList.instances`) |
| `src/dotnet/UI.Blazor/Components/VirtualList/virtual-list.ts` | The shared base. `readLoadDelay` (`?vlloaddelay`), request guard and retry |
| `src/dotnet/UI.Blazor/Components/VirtualList/finite-list.ts` | The sidebar list. Registers `globalThis.FiniteList` |
| `src/dotnet/UI.Blazor/Components/VirtualList/virtual-list-overlay.ts` | The on-screen overlay |
| `src/nodejs/src/scroll-controller.ts` | The band, the resistance, the takeover, the precise-wheel gesture drive. `getDebugState`, `getEffectiveScrollLimits`, `bandOffset`, `onTransform`; `?vllock`, `?vltakeover`, `?vlfriction`, `?vlwheel`. Sets the `element.scrollController` expando |
| `src/dotnet/UI.Blazor/Services/DebugUI/debug-ui.ts` | `virtualListDebug()`, `showVirtualListOverlay()`, `applyVirtualListOverlay()` |
| `src/dotnet/UI.Blazor/Services/DebugUI/DebugUI.cs`, `.Settings.cs` | Overlay persistence via `UserAppSettings.IsVirtualListOverlayEnabled` |
| `tools/virtual-list-rig/rig.mjs` | Scenario rig + judge. Also the reference CDP client |
| `tools/virtual-list-rig/soak.mjs` | Long seeded random gesture mix, judged as a whole |
| `tools/virtual-list-rig/recorder.js` | The frame recorder — desktop and phones alike |
| `tools/virtual-list-rig/README.md` | Rig usage, folded into §1.4 above |
| `src/dotnet/UI.Blazor.App/Testing/VirtualListTestPage.razor` | `/test/virtual-list`, admin only |

Validation of any TypeScript change: **do not run `npm`/`dotnet` yourself if `/server-loop` is
running** — trigger its rebuild and read the errors there. Otherwise `npm run build:Verify`.
