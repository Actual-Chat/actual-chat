# VirtualList Interactive-Pivot Scoping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the chat view from losing its sticky end (auto-follow of new messages) when the user taps items or when the live conversation block resizes — sticky end may be dropped only by a user scroll or an explicitly-declared "read history" control.

**Architecture:** Today ANY `click`/`touchend` on ANY list item arms an `isInteractive` pivot that never expires (only a user scroll clears pivots). While it exists, every later automatic render/resize pass takes the "interactive" path in `getScrollIntent`/`syncLayoutAfterRender`: it skips the sticky-End re-pin AND `applyScrollIntent` hard-clears `stickyEdge` (its `|scrollTop| > 50` guard is always true in the InfiniteSize coordinate space). The fix inverts the contract: only controls that opt in via a new `data-vl-hold` attribute arm an interactive pivot, the sticky decision is made synchronously at click time (`always` = expand/"read history" → drop sticky; `keep-edge` = collapse → keep sticky and skip the hold while pinned), the sticky-clear in `applyScrollIntent` is deleted as dead code, and interactive pivots additionally expire after a TTL so a stale hold can't anchor unrelated renders.

**Tech Stack:** TypeScript (`virtual-list.ts`, no test framework — validated via `npm run build:Verify` and scripted browser checks), Blazor Razor components.

**Root-cause reference:** discussion of 2026-07-24; the defect is fallout from commit `24db8dc8e` ("let interactive expand/collapse hold its header over the sticky-edge re-pin") — the hold was meant for the click's own render but leaks into every later pass.

## Global Constraints

- Read `docs/CODING_STYLE.md` rules before writing code: no comments that restate code; TS comments above the method declaration; no `Async` suffix; LF line endings; 4-space indent; max line length 120.
- Razor brace style is K&R (same line); TS flow-control statements never share a line with their condition.
- TypeScript changes under `src/dotnet/UI.Blazor*` MUST be validated with `npm run build:Verify` (runs `tsc --noEmit`, eslint, debug build) — EXCEPT when `/server-loop` or the host `./run-watch.cmd` watch is running; then trigger its rebuild and watch `tmp/watch-dotnet.log` / `tmp/watch-web.log` instead (much faster).
- Do not edit `AGENTS.md`/`CLAUDE.md` (auto-generated).
- Work on branch `fix/virtual-list-pivot-scoping` off `dev`. Do NOT push unless explicitly asked.
- Commit messages use conventional prefixes (`fix(virtual-list): ...`), matching `git log` style.

## Reuse

**Existing abstractions reused (no new ones needed):**
- The `data-anchor="below"` attribute pattern already read by `onInteractiveEvent` (`virtual-list.ts:995`) — `data-vl-hold` follows the same declared-marker approach.
- `Pivot.time` (`src/dotnet/UI.Blazor/Components/VirtualList/ts/pivot.ts`) — already stamped in `updateCurrentPivots`; the TTL uses it, no schema change.
- `setStickyEdge` (`virtual-list.ts:1886`) — the click-time sticky drop.
- `HeaderButton` (`src/dotnet/UI.Blazor/Components/Button/HeaderButton.razor`) — has `[Parameter(CaptureUnmatchedValues = true)]`, so `data-vl-hold` splats onto the underlying `<button>` with no component change.
- Verification: `debugUI.virtualListDebug(true)` + violation buffer from the `/virtual-list-debug` skill.

**Reusability of new components:** No new components/files. `data-vl-hold` is a VirtualList-owned attribute contract (its reader lives in `virtual-list.ts`, shared `UI.Blazor` project — already the shared place); consumers are feature Razor components. Nothing to promote.

## Behavior contract (`data-vl-hold`)

| Control state | Attribute value | Pinned to End (sticky set) | Not pinned |
|---|---|---|---|
| Expand / Show-more / Show items | `always` | Drop sticky at click, hold the item across the click's layout passes | Hold the item |
| Collapse / Hide items | `keep-edge` | Keep sticky, arm nothing (re-pin absorbs the shrink) | Hold the item |
| Anything unmarked | — | No pivot armed, sticky untouched | No pivot armed |

Interactive holds expire `InteractivePivotTtlMs` (2000 ms) after the click; a user scroll still clears them immediately (existing `onScroll` path). Known pre-existing gap left as-is: `ConversationMenu.razor:110` toggles expansion from an overlay outside the list — it never armed a pivot before this change either.

---

### Task 1: Declare hold intent on expand/collapse controls (`data-vl-hold` markers)

Inert until Task 2 lands (the TS reader doesn't exist yet), so this commit is behavior-neutral.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageHeader.razor:11-15`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderView.razor:16`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor:43,82,104,112`

**Interfaces:**
- Consumes: `HeaderButton`'s `CaptureUnmatchedValues` attribute splatting (already present).
- Produces: `data-vl-hold="always"` / `data-vl-hold="keep-edge"` attributes in the rendered DOM, read by Task 2's `onInteractiveEvent`.

- [ ] **Step 1: Mark the summary toggle in `ConversationMessageHeader.razor`**

`IsSeparated == true` means currently expanded (tooltip "Collapse summary"), so a click collapses. Replace the `HeaderButton` opening at lines 12-15:

```razor
            <HeaderButton
                Class="btn-summary-toggle"
                Tooltip="@tooltip"
                data-vl-hold="@(IsSeparated ? "keep-edge" : "always")"
                Click="@OnConversationToggleClick">
```

- [ ] **Step 2: Mark the live-header toggle in `LiveConversationHeaderView.razor`**

Replace line 16:

```razor
        <HeaderButton Class="c-lc-expand" data-vl-hold="@(s.IsExpanded ? "keep-edge" : "always")" Click="@Toggle">
```

- [ ] **Step 3: Mark the three controls in `ConversationMessageView.razor`**

Line 43 (live-card toggle):

```razor
                        <HeaderButton Class="c-lc-expand" data-vl-hold="@(state.IsExpanded ? "keep-edge" : "always")" Click="@ToggleShowMessage">
```

Line 82 (Show-more pill — keep the existing `data-anchor="below"`):

```razor
                    <button type="button" class="c-lc-showmore" data-anchor="below" data-vl-hold="always" @onclick="@OnRevealMore">
```

Lines 104 and 112 (local summary details toggles):

```razor
                        <span class="show-more-btn" data-vl-hold="always" @onclick="@ToggleDetails"> Show items</span>
```

```razor
                        <span class="show-more-btn" data-vl-hold="keep-edge" @onclick="@ToggleDetails"> Hide items</span>
```

- [ ] **Step 4: Build**

If `/server-loop` / host watch is running: touch nothing else, poll `tmp/watch-dotnet.log` until `Now listening on:` (or `error`). Otherwise run:

```bash
cd /home/undead/projects/actual-chat && dotnet build ActualChat.CI.slnf 2>&1 | tail -5
```

Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation
git commit -m "feat(virtual-list): declare hold intent on expand/collapse controls via data-vl-hold"
```

---

### Task 2: Marker-gated pivot arming + click-time sticky decision

**Files:**
- Modify: `src/dotnet/UI.Blazor/Components/VirtualList/virtual-list.ts:985-1005` (`onInteractiveEvent`)
- Modify: `src/dotnet/UI.Blazor/Components/VirtualList/virtual-list.ts:2186-2196` (`applyScrollIntent`)

**Interfaces:**
- Consumes: `data-vl-hold` attributes from Task 1; existing `setStickyEdge(null)`, `getFirstItemKeyBelow`, `updateCurrentPivots`/`scheduleUpdateCurrentPivots`.
- Produces: `onInteractiveEvent` arms an interactive pivot ONLY for `data-vl-hold` controls; `applyScrollIntent(scrollIntent, hasInteractiveLayoutAnchor)` keeps its signature but no longer touches `stickyEdge`.

- [ ] **Step 1: Rewrite `onInteractiveEvent` (lines 985-1005)**

Replace the whole method with:

```ts
    private onInteractiveEvent = (event: Event): void => {
        const itemRef = event.currentTarget as HTMLElement;
        let key = getItemKey(itemRef);
        if (!key)
            return;

        // Only controls opted in via data-vl-hold arm an interactive pivot; plain taps (play, links,
        // text selection) must not affect anchoring or stickiness. 'always' (expand / Show-more) holds
        // the item and leaves the End edge - a deliberate "read history" action; 'keep-edge' (collapse)
        // holds only when not pinned - when pinned the sticky re-pin absorbs the shrink instead.
        const target = event.target as HTMLElement | null;
        const holdRef = target?.closest<HTMLElement>('[data-vl-hold]');
        if (!holdRef)
            return;

        const isPinned = this.state.stickyEdge != null;
        if (holdRef.dataset.vlHold === 'keep-edge' && isPinned)
            return;

        if (isPinned)
            this.setStickyEdge(null);

        // A control marked data-anchor="below" (the live block's Show-more pill) reveals rows ABOVE
        // itself. Hold the first item BELOW this one as the interactive pivot instead of this item, so
        // the revealed rows grow upward while everything from the control down keeps its screen position.
        if (target?.closest('[data-anchor="below"]')) {
            const belowKey = this.getFirstItemKeyBelow(itemRef);
            if (belowKey)
                key = belowKey;
        }

        if (BrowserInfo.appKind === 'Wasm')
            this.updateCurrentPivots(key); // Required to do it synchronously at WASM
        else
            this.scheduleUpdateCurrentPivots(key);
    };
```

Note the parameter type changes from `TouchEvent` to `Event` (the handler is registered for both `touchend` and `click`; only `currentTarget`/`target` are used).

- [ ] **Step 2: Remove the sticky-clear from `applyScrollIntent` (lines 2186-2196)**

With arming gated and sticky decided at click time, an interactive pivot and a sticky edge no longer coexist through this path; the `|scrollTop| > 50` clear (always true at InfiniteSize scroll offsets) was the resize/render-driven sticky killer. Replace the whole method with:

```ts
    private applyScrollIntent(scrollIntent: ScrollIntent | null, hasInteractiveLayoutAnchor: boolean): void {
        if (hasInteractiveLayoutAnchor) {
            debugLog?.log(`applyScrollIntent: held by interactive pivot`, scrollIntent?.reason);
            return;
        }

        scrollIntent?.scroll?.();
        debugLog?.log(`applyScrollIntent: scroll set synchronously`, scrollIntent?.reason);
    }
```

- [ ] **Step 3: Validate TypeScript**

If `/server-loop` / host watch is running, trigger its rebuild and watch `tmp/watch-web.log` for errors. Otherwise:

```bash
cd /home/undead/projects/actual-chat && npm run build:Verify 2>&1 | tail -15
```

Expected: tsc, eslint, and the debug build all pass (no `error` lines).

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor/Components/VirtualList/virtual-list.ts
git commit -m "fix(virtual-list): arm interactive pivots only from data-vl-hold controls, decide sticky at click time"
```

---

### Task 3: Expire interactive pivots (TTL)

A hold is meant to anchor the click's own layout passes (the Blazor roundtrip render plus any height animation). A pivot older than that must stop carrying interactive semantics, otherwise it keeps suppressing the sticky-End re-pin in `getScrollIntent` and keeps hijacking cornerstone selection for unrelated renders.

**Files:**
- Modify: `src/dotnet/UI.Blazor/Components/VirtualList/virtual-list.ts` (constants block ~line 29; new private helper near `updateCurrentPivots`; read sites at lines 1189, 1619, 1974, 2279-2291)

**Interfaces:**
- Consumes: `Pivot.time` (stamped in `updateCurrentPivots`), `Pivot.isInteractive`.
- Produces: `private getFreshInteractivePivot(): Pivot | null` — the ONLY way interactive pivots are read from now on.

- [ ] **Step 1: Add the TTL constant**

In the constants block at the top of `virtual-list.ts`, after `const ScrollDebounce = 200;` (line 29), add:

```ts
const InteractivePivotTtlMs = 2000;
```

(2000 ms covers the toggle's Blazor roundtrip + expand/collapse height animation with margin; a user scroll still clears pivots instantly.)

- [ ] **Step 2: Add the helper**

Right after the `updateCurrentPivots` method (ends ~line 1531), add:

```ts
    // A stale interactive pivot must not hijack later unrelated renders (it used to silently kill the
    // sticky edge), so interactive semantics apply only within a short window after the click.
    private getFreshInteractivePivot(): Pivot | null {
        const pivot = this.state.pivots.find(p => p.isInteractive);
        return pivot != null && Date.now() - pivot.time <= InteractivePivotTtlMs ? pivot : null;
    }
```

- [ ] **Step 3: Route all four read sites through the helper**

`getScrollIntent` (line 1189):

```ts
        const hasInteractivePivot = this.getFreshInteractivePivot() != null;
```

`captureViewportAnchor` (line 1619):

```ts
        const interactiveKey = this.getFreshInteractivePivot()?.itemKey;
```

`syncLayoutAfterRender` (lines 1974-1977):

```ts
        const hasInteractiveLayoutAnchor = this.getFreshInteractivePivot() != null
            && scrollIntent?.reason !== 'sticky-edge'
            && scrollIntent?.reason !== 'last-item'
            && scrollIntent?.reason !== 'item';
```

`ensureItemRangeCalculated` (lines 2288-2291) — replace:

```ts
        const interactivePivots = pivots.filter(p => p.isInteractive);
        if (interactivePivots.length > 0) {
            // Use interactive pivot as cornerstone item
            const interactivePivot = interactivePivots[0];
```

with:

```ts
        const interactivePivot = this.getFreshInteractivePivot();
        if (interactivePivot) {
```

and since `pivots` becomes unused in that method, change the destructuring at line 2279 from:

```ts
        const { renderState: rs, orderedItems, pivots } = this.state;
```

to:

```ts
        const { renderState: rs, orderedItems } = this.state;
```

(If eslint reports `pivots` used elsewhere in the method, keep the destructuring — but as of this writing line 2288 is its only use.)

- [ ] **Step 4: Validate TypeScript**

Same as Task 2 Step 3: `/server-loop`-watch rebuild, or:

```bash
cd /home/undead/projects/actual-chat && npm run build:Verify 2>&1 | tail -15
```

Expected: pass, and in particular no `no-unused-vars` for `pivots`.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor/Components/VirtualList/virtual-list.ts
git commit -m "fix(virtual-list): expire interactive pivots so stale holds can't hijack later renders"
```

---

### Task 4: Browser verification (sticky survives taps and live-block resizes)

No TS unit-test harness covers `virtual-list.ts`; verification is a scripted browser pass per the `/virtual-list-debug` and `/debug-ui` skills. Requires the dev server (host `./run-watch.cmd` watch or `/server-start`) and host Chrome (`ai chrome`, chrome-devtools MCP on ports 8765/8766).

- [ ] **Step 1: Rebuild + reload**

Ensure the watch picked up all commits (`tmp/watch-dotnet.log` shows `Now listening on:`; `tmp/watch-web.log` clean), then reload the chat page in host Chrome and sign in (debug-ui helpers).

- [ ] **Step 2: Regression — unmarked tap must not break auto-follow**

1. Open a busy chat, scroll to the bottom; confirm the list element has class `sticky-end` (`document.querySelector('.virtual-list').classList.contains('sticky-end')` via `evaluate_script`).
2. Click the BODY of any visible message (not a button).
3. Post a new message to the chat (second signed-in session, or `mcp__voxt-robokitty__post_message` to a chat the bot can reach).
4. Expected: the view auto-scrolls to the new message and `sticky-end` is still present. (Before this fix: the render after the tap silently dropped sticky and the message appeared below the viewport.)

- [ ] **Step 3: Expand while pinned — deliberate sticky drop, header held**

1. At the bottom (`sticky-end` present), click an expand toggle (conversation header chevron or Show-more pill).
2. Expected: the clicked header/pill keeps its screen position across the expansion render (no jump — the `24db8dc8e` behavior is preserved), and `sticky-end` is removed (reading-history mode).
3. Post another message: the view must NOT scroll — content stays put.

- [ ] **Step 4: Collapse while pinned — sticky kept**

1. Scroll back to the bottom (sticky re-pins), collapse the same conversation.
2. Expected: `sticky-end` remains; the view stays flush at the bottom through the shrink; a newly posted message auto-scrolls into view.

- [ ] **Step 5: No-jump invariant check**

```js
debugUI.virtualListDebug(true)
```

Repeat Steps 3-4 once, then:

```js
debugUI.listVirtualListViolations(true)
```

Expected: no `render-jump`/`anchor-jump` violations attributable to the expand/collapse renders (sub-tolerance noise aside).

- [ ] **Step 6: Report**

No commit in this task. Record the outcomes (pass/fail per step) in the task summary; if any step fails, STOP and return to root-cause analysis — do not stack fixes.

---

## Out of scope (known follow-ups, do NOT do here)

- `turnOffIsEndAnchorVisible` (`virtual-list.ts:1082-1088`) still clears `stickyEdge` when the end anchor stays invisible > `ScrollDebounce` past `lastProgrammaticScrollAt` — the remaining non-user-scroll clear path (matters for the no-tap timing case). Separate change.
- Smooth-scroll follows (`'item-resize'`, sticky-edge re-target) opening "anchor not flush" windows.
- `ConversationMenu`-driven expand has no interactive hold (pre-existing).
