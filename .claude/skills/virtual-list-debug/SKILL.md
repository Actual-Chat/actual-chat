---
name: virtual-list-debug
version: 1.0.0
description: |
  Debug the VirtualList component — enable the on-demand consistency checker, drain its
  violations to a file via the watchdog, and reason about WHY a violation happened. Use when
  the chat-view (or sidebar) list jumps while scrolling, shows a blank/empty screen, gets stuck
  (can't reach the bottom / shows endless skeletons), or over-scroll-and-catch-up misbehaves.
  Also the canonical reference for the list's invariants (anchoring, the no-jump rule, the three
  reset cases). Pairs with /server-loop (server side) and /debug-ui (browser side).
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

# /virtual-list-debug — debug the VirtualList

There are **two** virtualized lists, with deliberately different machinery:

- **`InfiniteList`** (`infinite-list.ts`) — the absolutely-positioned list used for chat messages,
  the content tabs and the log view. No scrollbar; the wrapper is a fixed huge scroll space and
  items are held in place by anchoring. Everything below is about this one.
- **`FiniteList`** (`finite-list.ts`) — used by `ChatList` (the sidebar). Known item count, uniform
  item height, real scrollbar. An item's position is `index * itemSize`, so loading a different
  window cannot move what is on screen — the no-jump invariant is structural rather than
  maintained. See "FiniteList" at the end; most of this skill does not apply to it.

`InfiniteList` is subtle: every visible item is positioned by a precomputed model, and the most
common bugs are **visual jumps while scrolling**, **blank/partial-blank viewport**, and
**stuck loading**.

This skill gives you (1) the instrumentation to *catch* those bugs with enough detail to fix
them, and (2) the invariants to *reason* about them. Read both halves before touching geometry —
naive "fixes" here routinely trade one symptom (blank) for another (jumps).

The checker lives in a separate file, `virtual-list-debug.ts`, and is **off by default** — it
only runs when explicitly enabled, so it costs nothing in normal use.

---

## Part 1 — The instruments

### What the checker does

When enabled, every live list runs `VirtualListDebug.check()`:

- on a **timer**, ~10×/s (100 ms interval),
- right after a **data request** is sent (`onRequestData`), and
- right after a **render** (`onEvent('render')`).

Each run captures a `VlSnapshot` (scrollTop, scrollHeight, itemRange, viewport, spacer sizes,
DOM content extents, …) plus DOM geometry, then runs every check. Violations are logged (deduped)
via `warnLog` and accumulated in a ring buffer (last 200) per list.

### Turning it on

Three equivalent entry points — all set the same static flag and start a checker on every live
list and every new one:

```js
// In the page console / via evaluate_script:
debugUI.virtualListDebug(true)          // preferred; warns if InfiniteList isn't loaded yet
globalThis.InfiniteList.setDebugEnabled(true)   // same thing, lower level
```

Disable with `false`. State is per-page, so a **reload drops it** — re-enable after navigating.

### Reading violations live

```js
debugUI.listVirtualListViolations()        // array of all violations across all live lists
debugUI.listVirtualListViolations(true)    // same, then DRAINS each list's buffer (clear=true)
```

Each list also registers itself at `globalThis.__vlDebugs[identity]`, with `globalThis.__vlDebug`
pointing at the most-recently-started one. The chat-view list's `identity` is the **chat id**
(`Chat.Id.Value`); the sidebar list has its own identity. Use `__vlDebugs` to inspect one list
in isolation:

```js
Object.keys(globalThis.__vlDebugs)         // which lists are live
globalThis.__vlDebug.violations            // newest list's buffer (no drain)
globalThis.__vlDebug.lastRequest           // snapshot at the last data request
```

### The watchdog (drain to file — don't pollute chat)

For anything beyond a glance, run the watchdog instead of pasting buffers into the conversation.
It connects to host Chrome over CDP, finds the `local.voxt.ai` page, enables the checker, and
every second drains violations (`clear=true`) into `tmp/vl-violations.jsonl` (one JSON object per
line, each stamped with `at`). It re-enables the checker after reloads automatically.

```bash
node tmp/vl-violation-watch.mjs            # defaults: chromePort=9222, pollMs=1000
node tmp/vl-violation-watch.mjs 9223 500   # other Chrome / faster poll
```

It truncates `tmp/vl-violations.jsonl` on start. Workflow: start the watchdog → reload the page →
reproduce (scroll) → read the jsonl. Tail it or load it:

```bash
wc -l tmp/vl-violations.jsonl
# group by violation code:
cat tmp/vl-violations.jsonl | jq -r .code | sort | uniq -c | sort -rn
# look at the jump cases with their geometry:
cat tmp/vl-violations.jsonl | jq 'select(.code=="render-jump" or .code=="anchor-jump")'
```

### Driving a reproduction

Use `/debug-ui` (chrome-devtools MCP) to sign in and open a long chat. To reproduce the user's
"vicious scroll" (fast, half-to-full-screen per frame, both directions, variable speed), drive
`scrollTop` from the page. **Caveat:** directly setting `el.scrollTop` each frame *masks* the
list's own scrollTop-compensation — so the timer-based `anchor-jump` check can miss real jumps
that way. The within-render `render-jump` check is immune (it brackets the layout write itself),
so trust `render-jump` for programmatic scrolls and `anchor-jump` more for human/wheel scrolls.

### The checks (what each violation means)

Defined in `virtual-list-debug.ts`. Tolerances: `Eps=8`, `GapEps=24`, `JumpEps=12` px (geometry
is integer-floored and shifts a few px on reflow, so sub-tolerance deviation isn't real).

| code | meaning | typical cause |
|------|---------|---------------|
| `blank-viewport` | content is loaded but neither an item nor a skeleton-spacer intersects the viewport — user sees blank wrapper | wrapper sized far larger than the chain, scroll parked in the void |
| `viewport-gap` | a hole between rendered elements inside the viewport (partial blank) — skipped when content is shorter than the viewport (legit for short lists) | missing/short spacer, chain not covering |
| `void-below-newest` | infinite list settled (not scrolling), pinned at its maximum scroll, with a gap *past the newest* item. Checked when End-preferred or content taller than viewport | the model overshoots the DOM: `itemRange.end` exceeds the real content end, so the magnet pins to a max that sits below the last item. Measured **against the DOM** (`lastContentBottom`), not `itemRange` — `CutVirtualSpaceAtBottom` sizes the wrapper to `itemRange.end + endAnchorSize`, so `(scrollTop + clientHeight) - contentBottom` is identically ≤ 0 in model coordinates and a model-side check is structurally blind to this, the same way `anchor-jump` is blind to `scroll-clamp` |
| `void-above-oldest` | symmetric: settled with a gap *past the oldest* item. Checked when Start-preferred or content taller than viewport (a short list's non-preferred edge legitimately has a gap) | edge magnet not pinning flush |
| `scrolltop-out-of-range` | `scrollTop` outside `[0, scrollHeight-clientHeight]` (top-to-bottom convention) | bad scroll write |
| `anchor-jump` | timer-detected: a keyed item still on screen moved by more than scrollTop should account for (`Δtop + Δscroll > JumpEps`) | re-layout without compensating scroll |
| `render-jump` | render-detected: a visible anchored item moved during a render with no intentional scroll (bracketed inside `restoreScrollPosition`) | **the no-jump-invariant violation** — chase these for "it jumps when I scroll" |
| `scroll-clamp` | the scroll range shrank out from under the viewport, leaving `scrollTop` pulled back and pinned at the new maximum | a wrapper resize while parked near the end. `anchor-jump` is *structurally blind* to this — content and scroll move together, so its `Δtop + Δscroll` is exactly 0 — which is why it needs its own check. The signature is deliberately narrow (range must shrink, scroll must go back, and the result must be pinned at the new max): the list has several scroll-write paths and only one stamps `lastProgrammaticScrollAt`, so a looser "scroll moved by itself" test is far too noisy |

`render-jump` detail carries `{ key, before, after, drift, scrollType, renderIndex }` — the item
that jumped and by how much, which is what you need to fix anchoring.

### Adding a new check

Write a `Check` (pure read: `(vl, snapshot, geom) => VlViolation | null`) as a hoisted `function`
(the `checks` record references them before their textual position) and add it to
`VirtualListDebug.checks`. Prefer model-level, scroll-independent assertions (compare `itemRange`
vs `scrollHeight`) over pixel reads where possible — they're stabler. Validate with
`npm run build:Verify` (or trigger the `/server-loop` rebuild).

---

## Part 2 — The invariants (what "correct" means)

This section is about **`InfiniteList`**. The scrollbar case (sidebar / contacts, `chat-list`) is
`FiniteList` now — a separate component with its own, much smaller invariant set; see the
"FiniteList" section below.

### `InfiniteList` — no scrollbar (chat view) — the hard one

Everything is **absolutely positioned**: the container is positioned, and spacers plus
fixed-size item slots place every item on one tall virtual vertical space. scrollTop=0 is the
bottom (newest); negative is up. The list is End-anchored.

**Anchoring.** On each render the list always re-renders at least one **anchored item** — an item
retained from the previous render. Anchored items are not just kept in the DOM, they are kept at
the **same on-screen position**. A render typically adds items at one end, removes items at the
other end, and resizes/removes the top/bottom spacers — all while the anchored part stays put.
When scrolling up, the list loads items *above* the current set and grows the top; the bottom
stays anchored (and vice-versa).

**The no-jump invariant (the big one).** Items currently on screen — *or even just off-screen* —
must **not change position** from render to render, unless the list makes a deliberate decision to
**reposition**. Equivalently: across a render, a still-visible keyed item moves by exactly
`-Δscroll` and no more. Extra movement is a jump (`render-jump` / `anchor-jump`). This is the
invariant the user means by "it jumps when you scroll past certain items."

**Reposition = reset.** A reposition is a *controlled* decision to reset the anchors: re-render
everything and scroll to the desired location. After a reset the view usually still doesn't
visibly jump — or it changes only because, e.g., the top spacer was removed (we're explicitly
showing there's nothing above now). There are essentially **three reset cases**:

1. **New item set.** We discard everything currently displayed and jump to a completely different
   location (e.g. navigate / "jump to message"). We're now at a new position and render from there.

2. **Reached an edge → drop a spacer.** We reach the top or bottom (or both) and conclude we no
   longer need one or both spacers, so we remove them and resolve the reset.

3. **Need a spacer again.** Symmetric to (2): while scrolling we conclude an edge item is no
   longer visible and we now *need* a spacer there again — add it and **explicitly scroll**
   (unless the current scroll position is already correct).

**The spacer subtlety — not every spacer change is a reset.** There's a precomputed (anticipated)
list size before each write; anchored items live somewhere on that tall space. Removing the **top
spacer** simply shrinks the region above the topmost item to zero — that's a size change of the
existing space, not a re-anchor. The reset/explicit-scroll obligation is specifically case (3):
when scrolling down far enough that the top item leaves the view and a top spacer must reappear.

### Over-scroll and catch-up (must keep working)

The wrapper is intentionally sized from a big estimate (`count × geometryItemSize`), so the user
can fling-scroll far **above/below** the currently-loaded region into empty space, and on pause the
list loads and settles to the right place. This is desired behavior — do **not** "fix" blanks by
shrinking the wrapper to fit the chain. The correct fix for blanks/jumps is **correct anchoring
and catch-up**, not a smaller scroll range. (This was learned the hard way: shrinking the wrapper
killed over-scroll and introduced jumps; all such changes were reverted.)

#### The model: a fixed, huge virtual space

Think of the no-scrollbar list as a fully-virtualized vertical space with **fixed boundaries** and
a stable coordinate system. We start rendering near zero and assume the user could scroll, say, a
million screens (≈ a billion px) up and the same down. Spacers at the top and bottom represent that
space. The key intent: **pretend the spacers are huge all the time** and keep the coordinate
system stable, rather than mutating it as the user moves.

This matters because shrinking the top spacer to zero (what the code does today at an edge)
**changes the list's coordinate system** — every anchored position is now measured against a
different origin. That re-origin is a frequent source of jumps. The intended direction is to avoid
that.

#### Edge handling: rubber-band, not re-origin (intended direction)

The simple rule for the empty space at an edge: **if there is a spacer *after the last item* or
*before the first item*, the visible empty gap should be reduced to zero — and no skeletons are
shown in it** (the spacer element stays, but renders nothing).

The intended *mechanism* (a change from today's shrink-to-zero) is a **rubber-band**: keep the
spacer notionally huge, and run a continuous job (a timer/tick) that, whenever the list is
currently stuck to the top or bottom edge, **drags the list back toward that boundary** to close
the empty gap — like a rubber end snapping shut. A gentle/animated drag on desktop; the right feel
on mobile is TBD. Because we never resize the spacer or move the origin, nothing in the top/bottom
spacer-size bookkeeping changes — the only new logic is the drag-to-boundary tick. This keeps the
coordinate system fixed and sidesteps the re-origin jumps.

> Status: today the edge is handled by shrinking the spacer to zero. The rubber-band model above is
> the intended replacement. Documented here so the desired end-state is unambiguous even before the
> code changes.

#### Desired properties (the acceptance bar)

1. **Catch-up from anywhere.** From any current view, the user may scroll to essentially *any*
   reasonable position — including instantly flinging ~a million items past what's loaded. The list
   is expected to **eventually catch up** and display what belongs there. It catches up
   **part-by-part**: each iteration renders more items than the last (accelerating, so it closes a
   huge gap in a bounded number of steps) — but **every render still keeps its anchors**, so
   **nothing jumps** along the way. Scrolling past the loaded region, and even far past it, must
   still converge.

2. **Bounded ends.** The user must **not** be able to scroll past the very first item or the very
   last item. (Today that bound is enforced by shrinking the edge spacers to zero; that mechanism
   will likely change to the rubber-band above — but the *property* to preserve is simply: no
   scrolling beyond the first/last item.)

### Symptom → invariant → where to look

- **Jumps while scrolling** → no-jump invariant → `render-jump`/`anchor-jump`; inspect
  `restoreScrollPosition` (read captures `jumpAnchor`, write compares via `captureViewportAnchor`)
  and whether a non-reset render moved the anchor.
- **Blank / partial-blank** → `blank-viewport` / `viewport-gap`; content not covering the viewport.
- **Settles past an edge (gap above oldest / below newest)** → `void-above-oldest` / `void-below-newest`;
  the edge magnet (`edge-bounce.ts`) isn't pinning flush.
- **Stuck (endless skeletons / can't reach bottom)** → check the skeleton watchdog warning and
  whether a needed reset (case 2/3) isn't firing; confirm `hasVeryLastItem` and end-edge anchoring.

---

## Files

- `src/dotnet/UI.Blazor/Components/VirtualList/infinite-list.ts` — the anchored list; debug hooks:
  `static enableDebug` / `setDebugEnabled`, `startDebug`, `captureViewportAnchor`,
  `noteRenderJump` wiring in `restoreScrollPosition`. Registers `globalThis.InfiniteList`.
- `src/dotnet/UI.Blazor/Components/VirtualList/finite-list.ts` — the index-positioned list.
  Registers `globalThis.FiniteList`; `FiniteList.debugDataLoadDelayMs` injects data latency.
- `src/dotnet/UI.Blazor/Components/VirtualList/virtual-list-debug.ts` — the checker. Currently
  instruments `InfiniteList` only.
- `src/dotnet/UI.Blazor/Services/DebugUI/debug-ui.ts` — `virtualListDebug()`, `listVirtualListViolations()`.
- `tmp/vl-violation-watch.mjs` — the drain-to-file watchdog.
- `.claude/skills/virtual-list-debug/vl-scroll-trace.js` — per-frame scroll/position tracer; the only thing that catches a scroll clamp.
- `InfiniteSize` (`10_000_000`) is defined in both `InfiniteList.razor.cs` and `infinite-list.ts`
  and must stay in sync; it is well under the browser's max element height.

## FiniteList

`ChatList` is the only user. Model:

```
top(i)       = i * itemSize
startSpacer  = firstLoadedIndex * itemSize
endSpacer    = (N - lastLoadedIndex - 1) * itemSize
wrapper      = content-driven (no explicit height write)
```

`itemSize` is measured from a **designated row** rather than estimated: `IVirtualListItem.HasRegularSize`
(false for `ChatListItemModel.IsLastItemInBlock`, the pinned-block separator) projects to
`data-vl-size-source` on the `li`, and the list measures the first such row and sticks to it. No running
average, so nothing drifts.

Two things that bit during development and will bite again:

- **A render arrives in several Blazor batches.** The items and the render-state JSON land in
  different ones (that is what `RenderAtDepth Depth="5"` is for). Laying spacers out against a
  half-updated pair moves everything below them by the whole spacer delta. `endRender` gates on the
  JSON's own `renderIndex` advancing — the only reliable "everything is consistent" signal.
- **Capture the scroll anchor in the rAF read phase**, immediately before writing spacers — not on
  scroll. An anchor captured at scroll time and restored later scrolls the user back to where they
  were, which is the bug this component exists to remove.

### Validating it

`render-jump`/`anchor-jump` measure `dTop + dScroll`, which is identically 0 when the browser clamps
`scrollTop` — so the checker is structurally blind to a clamp, and it equally *false-positives* on a
correct compensation (content held still while scrollTop moves). For `FiniteList` the honest metric is:
**while the user is not scrolling, does anything on screen move?**

Rig: `/bot-army 400 0` as an admin test agent, mobile emulation, `FiniteList.debugDataLoadDelayMs = 2500`,
then fling with a relative `scrollBy` loop and measure `dTop` of items visible in consecutive frames,
outside the fling windows. Baseline: 618/620 idle frames with zero movement; the 2 outliers are a
single-frame 250px flicker when landing at the very top mid-load.

Note the tab must be foregrounded (`select_page` with `bringToFront`) — a backgrounded tab throttles
rAF to ~1 Hz and every number you measure will be garbage.
