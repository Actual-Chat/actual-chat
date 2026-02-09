# Virtual List Scroll Jump Bug Investigation

## Problem

When rapidly scrolling in ChatView, the list sometimes jumps to a completely different part of the chat — not just a few days, but potentially a year forward/backward.

## Architecture Overview

ChatView uses `VirtualList` with `DefaultEdge = End` (reverse column layout for chat). The scroll/data flow:

1. User scrolls → `onScroll` → clears pivots, throttled `updateViewport()`
2. `updateViewport()` → `calculateViewport()` → `requestData()`
3. `getDataQuery()` computes a `VirtualListDataQuery` (key range + pixel-based virtual range + move range)
4. JS calls C# `RequestData()` → triggers `State.Recompute()` → `ChatView.GetData()`
5. C# converts the pixel-based query to entry ID ranges, loads tiles, returns items
6. Blazor re-renders → `onItemSetChange` → `endRender()` → `restoreScrollPosition()`

### Key files

| File | Role |
|------|------|
| `src/dotnet/UI.Blazor/Components/VirtualList/virtual-list.ts` | Core scroll/positioning engine |
| `src/dotnet/UI.Blazor/Components/VirtualList/ts/virtual-list-statistics.ts` | Item size estimation |
| `src/dotnet/UI.Blazor/Components/VirtualList/ts/virtual-list-data-query.ts` | Query model (TS side) |
| `src/dotnet/UI.Blazor/Components/VirtualList/ts/range.ts` | NumberRange utilities |
| `src/dotnet/UI.Blazor/Components/VirtualList/VirtualList.razor.cs` | Blazor component, dispatches to data source |
| `src/dotnet/UI.Blazor/Components/VirtualList/VirtualList.razor` | Render template, produces render state JSON |
| `src/dotnet/UI.Blazor/Components/VirtualList/Internal/VirtualListRenderState.cs` | Render state DTO (C# → JS) |
| `src/dotnet/UI.Blazor/Components/VirtualList/VirtualListDataQuery.cs` | Query model (C# side) |
| `src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor.cs` | `IVirtualListDataSource<ChatMessage>` implementation |
| `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` | Tile loading, `GetChatItems`, `GetTile` |
| `src/dotnet/UI.Blazor.App/Services/ChatDataQuery.cs` | Chat-specific query (ID range + offsets) |

## Potential Issues (ordered by suspicion level)

### 1. Pivots cleared during scroll + no anchor for new data — HIGH

**Location:** `virtual-list.ts:914-916` (`onScroll`)

```typescript
private onScroll = (ev: Event): void => {
    ...
    this.pivots = [];  // All pivots wiped on every scroll event
    this.updateViewportThrottled();
};
```

Every scroll event clears all pivots. When new data arrives from the server and `endRender()` → `restoreScrollPosition()` runs, `ensureItemRangeCalculated()` (line 1501) looks for a "cornerstone" item to anchor positioning. The fallback chain is:

1. Interactive pivots → **empty** (cleared by scroll)
2. Visible items with existing ranges → **may be stale** during rapid scrolling
3. Last fallback: just use the last/first ordered item → likely has **no range** on new items
4. If no cornerstone found → calls `resetItemRange(canUseViewport=true)` which **repositions all items based on viewport center**

During rapid scrolling, new items from the server get positioned relative to the **current scroll position**, not relative to where they logically belong in the list. If the server returns items for a different part of the chat (because the query was based on a stale viewport), those items get anchored at the current viewport, causing a visual jump.

### 2. `resetItemRange(canUseViewport=true)` repositioning — HIGH

**Location:** `virtual-list.ts:1657-1693` (inside `resetItemRange`)

When `canUseViewport=true` and `!rs.hasVeryLastItem`:

```typescript
const viewportCenter = viewport
    ? viewport.start + viewport.size / 2
    : ...;
cornerstoneItemIndex = findCenterItemIndex();
cornerstoneItem = orderedItems[cornerstoneItemIndex];
cornerstoneItem.range = new NumberRange(
    Math.floor(viewportCenter - cornerstoneItem.size! / 2),
    Math.ceil(viewportCenter + cornerstoneItem.size! / 2)
);
```

This places the center item at the viewport center. If the viewport is at pixel position -50000 (deep into the scroll) but the new items logically belong at -5000, centering them at -50000 makes the user see content from much further in the history.

### 3. `moveRange` calculation amplified by wrong `statistics.itemSize` — MEDIUM

**Location:** `virtual-list.ts:1924-1925` (inside `getDataQuery`)

```typescript
const moveRangeStart = Math.floor((loadZone.start - firstItem.range!.start) / itemSize / 5) * 5;
const moveRangeEnd = Math.ceil((loadZone.end - lastItem.range!.end) / itemSize / 5) * 5;
```

If `statistics.itemSize` is much smaller than real item sizes (e.g., 48px default vs 200px real chat messages with images), the `moveRange` becomes enormous. A 1000px gap / 48px = ~21 items, but if real items are 200px, there should only be ~5.

The C# side (`ChatView.razor.cs:502-505`) uses this `moveRange` directly:

```csharp
_ => new ChatDataQuery(keyRange, query.MoveRange.Start, query.MoveRange.End),
```

This could cause the server to load data much further from the current position than intended.

### 4. Wrapper height delayed during scroll, then snapped — MEDIUM

**Location:** `virtual-list.ts:1392-1406` (inside `restoreScrollPosition`, write phase)

```typescript
if (totalSizeDiff != 0 && this.isScrolling && rs.renderIndex > 0) {
    const setWrapperHeight = () => fastRaf({
        write: () => {
            if (this.isScrolling)
                this.turnOffScrollingCallback = setWrapperHeight;
            else {
                this.wrapperRef.style.height = `${totalSize}px`;
            }
        }});
    this.turnOffScrollingCallback = setWrapperHeight;
}
```

Only **one callback** is stored at a time (`turnOffScrollingCallback`). If multiple renders happen during scrolling, each overwrites the previous. When scrolling stops, the wrapper height snaps from the old value to whatever the latest render computed. If this is a large change (e.g., estimated size went from 500,000px to 4,000,000px), the browser's scroll position can jump dramatically because the scroll thumb position is relative to the total scrollable height.

### 5. `estimatedCount` from `chatIdRange` produces huge virtual space — MEDIUM

**Location:** `ChatView.razor.cs:444` + `virtual-list.ts:1269`

C# side:
```csharp
EstimatedCount = (int?)(chatIdRange.End - chatIdRange.Start),
```

For a chat with 100K entries and `itemSize = 48px`, estimated total = 4.8M pixels. TS side:

```typescript
const estimatedTotalSize = rs.estimatedCount
    ? clamp(Math.floor(rs.estimatedCount * this.statistics.itemSize), knownRange.size, 5E6)
    : 0;
```

The virtual space can be up to 5M pixels. Repositioning within this huge space (especially during `resetItemRange`) means even small errors in the cornerstone position get amplified into large logical jumps.

### 6. Race between rapid viewport updates and pending data requests — MEDIUM

**Location:** `virtual-list.ts:1784-1788` (inside `requestData`)

```typescript
const whenRequestDataCompleted = this.whenRequestDataCompleted;
if (whenRequestDataCompleted && !whenRequestDataCompleted.isCompleted()) {
    debugLog?.log(`requestData: the previous request is not completed yet`);
    return;  // Silently drops the new query
}
```

During rapid scrolling, the viewport moves fast but only one request can be in-flight. The request is based on an old viewport position. By the time the response arrives and items are rendered, the user has scrolled far away. Then `restoreScrollPosition()` tries to position items that are logically far from where the user is now.

### 7. `binarySearch` returning -1 with fallback logic — LOW-MEDIUM

**Location:** `virtual-list.ts:1903-1921` (inside `getDataQuery`)

When `binarySearch` returns -1 (no item matches the predicate), the fallback selects `orderedItems[0]` or `orderedItems[length-1]` based on simple comparisons. These fallbacks may select items at the extreme ends of the loaded range, which then generate queries with large `moveRange` values.

### 8. Scroll direction detection could be wrong — LOW

**Location:** `virtual-list.ts:839-844` (inside `calculateViewport`)

```typescript
if (viewport.start < oldViewport.start)
    this.scrollDirection = 'up';
else
    this.scrollDirection = 'down';
```

With `VirtualListEdge.End`, scrollTop is negative. `oldViewport` might be `this.lastViewport` from a previous render cycle. If old/new viewports are from very different states, direction detection is misleading. However, `scrollDirection` is only used in `turnOffIsScrolling`, so the direct impact is limited.

## Most Likely Root Cause Scenario

1. User scrolls rapidly upward (into older messages)
2. Multiple scroll events clear all pivots
3. A `requestData()` call is made based on an intermediate viewport position
4. While the request is in-flight, the user continues scrolling; new requests are dropped (line 1787)
5. Server returns items for the intermediate position
6. Blazor renders new items → `endRender()` → `restoreScrollPosition()`
7. `ensureItemRangeCalculated()` finds no pivots, no visible items with ranges → falls through to `resetItemRange(canUseViewport=true)`
8. `resetItemRange` places all items centered at the current viewport position (which the user has now scrolled far from)
9. The `containerRef.style.bottom` offset is set based on the new item ranges
10. This causes a visual jump because items are now positioned at incorrect virtual coordinates

## Debugging Plan

Add logging at these critical points to correlate what happens at the exact moment of the jump:

### TS-side logging

1. **`ensureItemRangeCalculated()`** — Log which cornerstone was selected (interactive pivot / visible item / fallback), whether `resetItemRange` was called, and the resulting item range
2. **`resetItemRange()`** — Log the `canUseViewport` flag, the viewport position used, and the cornerstone positioning result
3. **`restoreScrollPosition()`** — Log `scrollTopOffset`, `offset`, `totalSize`, `totalSizeDiff`, `spacerSize`, `endSpacerSize`, and whether the wrapper height was delayed
4. **`getDataQuery()`** — Log the `moveRange`, `keyRange`, `loadZone`, and how `firstItem`/`lastItem` were selected
5. **`onScroll` / `requestData`** — Log when requests are dropped due to in-flight requests

### C#-side logging

6. **`ChatView.GetData()`** — Log the incoming `query`, the computed `ChatDataQuery`, and the resulting item count/range
7. **`ChatView.GetChatDataQuery()`** — Log the key range conversion and the final data query with offsets
