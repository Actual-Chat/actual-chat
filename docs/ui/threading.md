---
title: Threads in the UI
description: Which UI code runs on the Blazor dispatcher and which on the thread pool, the read-safe / dispatcher-mutation contract for scoped UI services, and the rules for ComputeState and IVirtualListDataSource.GetData.
---

# Threads in the UI

The client UI runs on two kinds of threads, and most of the code that reads a
service or a component field never gets to choose which one it's on. This document
is the contract that keeps the two from corrupting each other. It came out of a
Sep 2026 audit of the virtual list and everything its `GetData` reaches; the
findings are summarized at the end.

## Who runs where

**The Blazor dispatcher** (one thread per circuit; the main thread in MAUI and
WASM) runs everything Blazor calls on a component: `SetParametersAsync`,
`OnInitialized*`, `OnParametersSet*`, the render block, `ShouldRender`,
`OnAfterRender*`, DOM event handlers, `[JSInvokable]` callbacks, `Dispose`.
JS interop, `StateHasChanged`, `NavigationManager.NavigateTo`, `ElementReference`
and `History.Save`/`NavigateTo` are only legal here.

**`ComputeState` and `GetData` start on the dispatcher, by design.** Fusion
dispatches every `ComputedStateComponent` / `ComputedRenderStateComponent` compute
there (`ComputeStateOnThreadPool` is deliberately *not* set on our bases - it was
once, and that was a mistake), and `VirtualList` calls `GetData` from that compute
and from `SetParametersAsync`. So the block before their first `await` is ordinary
dispatcher code: it may read parameters and component fields with no
synchronization at all, and that is where every such read belongs.

**Leaving the dispatcher is explicit: `await ThreadPoolExt.Yield()`.** It
continues on a thread-pool thread unconditionally - unlike `Task.Yield`, which posts
back to the context it's on, and unlike `.ConfigureAwait(false)`, which only moves
a continuation that actually had to wait: a warm cache or an already-loaded state
completes synchronously and keeps the caller on the dispatcher. Put the yield right
after the snapshot, before the first `ConfigureAwait(false)`, and from there on the
method is pool code.

**The thread pool** runs everything else Fusion drives: `[ComputeMethod]`
recomputes, `UIWorkerBase` chains, `SyncedState`/`StoredState` read-write loops, and
every `ComputeState`/`GetData` past its yield. Anything those touch is read from the
pool.

## The contract for scoped `XxxUI` services

There is no assumption that a scoped service is fully thread-safe. What every
service must provide is narrower:

1. **Reads are safe from any thread.** A compute path may call any getter or
   `[ComputeMethod]` without marshalling.
2. **Mutation runs on the dispatcher** - or marshals itself there
   (`Dispatcher.CheckAccess()` + `Dispatcher.InvokeSafeAsync`, as `NavbarUI.SelectGroup`
   does), or is synchronized internally so a worker may call it.
3. **State both sides touch lives in something that is already safe:**
   `MutableState<T>` / `SyncedState<T>` / `StoredState<T>` (a lock-free read, a locked
   set, and an invalidation path that re-enters the dispatcher on its own),
   `ConcurrentDictionary`, the `Immutable*` collections, or a single reference
   published with `Volatile.Write` and read with `Volatile.Read`.
4. **Several fields that change together become one immutable record**, swapped
   as one reference. A reader must never see half of an update:
   `BrowserInfo.UILanguageState`, `DateFormatter.FormatCache`,
   `ChatView.NewMessagesLineState`. A nullable tuple is not a reference and cannot be
   swapped atomically.
5. **A read-modify-write is under a `lock` on every path**, not just the one where
   it was first noticed. `ChatUI`'s conversation-expansion overrides are modified
   from the toggle (dispatcher), the tile builder (pool) and `LiveBlockUI` (pool);
   all three take `ChatUI.Lock`.

Two things that look safe and aren't: `LruCache<TKey, TValue>` mutates on *read*
(`TryGetValue` moves the node) and has no synchronization - use
`ThreadSafeLruCache` or `ConcurrentLruCache` for anything a pool thread reads. And
a `lock` around a write is not a release for readers outside it; see
[Shared Fields and Memory Ordering](../CODING_STYLE.md#shared-fields-and-memory-ordering).

`field ??= Services.GetRequiredService<T>()` is fine from any thread: DI resolution
is synchronized and every racer stores the same reference. `field ??= new ...` or
`??= StateFactory.NewMutable(...)` is not - two racers get two objects, and a
subscriber to the loser never hears from the winner.

## `ComputeState` and `GetData`

- **Snapshot first.** Read every parameter and component field the computation
  needs into locals in the block before the first `await`. That block runs on the
  dispatcher, so the reads are consistent with each other and with the render;
  after it, Blazor may reassign the parameters at any time.
- **Then `await ThreadPoolExt.Yield()`** if anything heavier than a few
  lookups follows - an item build, a projection, anything that completes from a
  cache. `GetData` always yields: what it does is the heavy part by definition.
- **Then `.ConfigureAwait(false)` throughout.** Nothing after the snapshot needs
  the dispatcher, and a continuation posted back to it is a render frame stolen
  from scrolling.
- **Start independent fetches before awaiting any of them.** Create the tasks,
  then await - serial awaits multiply the round trips.
- **Nothing dispatcher-only after the snapshot:** no JS interop, `StateHasChanged`,
  navigation or `ElementReference`. Those belong in the render or a lifecycle
  method, driven by the state the computation produced.

What `GetData` adds on top:

- It may run **twice concurrently on the same instance**: the pre-render call the
  list abandons after 300ms keeps running beside the state's own compute.
- **The parameters that identify the list cannot change on a live instance.**
  Render the data source (or the list) under a `@key` built from them, the way
  `ChatList.GetKey` does, so a change re-creates the component instead.
  `VirtualList.SetParametersAsync` never re-runs `OnParametersSet` on a live
  instance, so a changed parameter is not even observed.
- **Side-effect free, or idempotent under discard.** `VirtualList.ComputeState` drops
  the result when a dependency was invalidated during the call and renders the
  previous data instead; a one-shot flag consumed in `GetData` (`ChatList`'s scroll
  restore, `ChatUI`'s "conversation just toggled" diff) is then lost with it.
- What `VirtualList` guarantees in return: `query` and `renderedData` are one
  consistent pair (both read on the dispatcher, where the render and `RequestData`
  write them); the query is consumed by the compute that answers it, so a render
  landing in between cannot wipe it, and a query nothing rendered stays pending for
  the next recompute; `VirtualListData` is immutable once built, `Count`,
  `FirstItem` and `LastItem` included.

## Checklist for a new service or component

- Does any pool code read it? (A `[ComputeMethod]`, a worker, a
  `ComputeState`/`GetData` past its yield.) If so, every field that code reaches is
  in the contract above.
- Every field written on one thread and read on another: Fusion state,
  concurrent/immutable collection, or `Volatile` on a single reference.
- Every read-modify-write: one `lock`, on all paths.
- Every mutation from a worker: marshalled to the dispatcher, or the state is
  internally synchronized.
- `ComputeState` / `GetData`: snapshot, `ThreadPoolExt.Yield()`,
  `ConfigureAwait(false)`, tasks first.

## Where the audit found the contract broken

Fixed in Sep 2026: `ComputeStateOnThreadPool` on both component bases (every
`ComputeState` had been starting on a pool thread, reading parameters and fields
with no snapshot); `VirtualList`'s query/rendered-data pair and its `GetData` running
on the dispatcher from a warm cache; `VirtualListData`'s lazy fields; `ChatView`'s
NewMessagesLine bookkeeping; `BrowserInfo`'s UI-language triple; `DateFormatter`'s
format cache; `NavbarUI`'s selection (read from compute paths, written from a
`ChatUI` worker); `ChatUI`'s unlocked expansion read-modify-writes; `HighlightUI`'s
word map (now a `MutableState`).

Known and not yet addressed: `ChatUI.LastConversationExpansionOverrides` is
per-service rather than per-chat; `BrowserInfo`'s remaining device flags are plain
properties written once before `WhenReady`; `History.TryStepBack` touches
`_itemById` outside `History.Lock`; `RenderVars.Get<T>` writes on a read path;
`TuneUI._resolvedTunes`, `CaptchaUI.WhenReady` and `RpcEndpointMonitor.MutableEndpoints`
are unsynchronized lazy inits that create objects; `SyncedState`'s
`*RecentlyWritten` members are public but must be called under its lock.
