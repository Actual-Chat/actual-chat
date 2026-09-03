---
title: Offline mode
description: Make the read-only UI keep working from the client cache no matter when the connection goes away, with the smallest set of app and Fusion changes.
---

# Offline mode: read-only Voxt without a connection

## Goal

A user who loses the connection - before launch, mid-session, or in the middle of a call the UI
was waiting on - keeps a working read-only app: the chat list with previews and unread counts,
places, notifications, and every chat the routine prefetch has reached, scrollable through its
cached tail. Nothing hangs on a skeleton indefinitely, nothing trips an error barrier, and when
the connection returns everything refreshes by itself.

The inventory of what the read path actually calls, and what each call does offline today, is in
[Offline render path](../architecture/offline-render-path.md). This document is the plan.

[[toc]]

## Definition of done

| Scenario | Expected |
|---|---|
| **Cold start offline** (app killed, no network, stored session) | The app opens on the last chat. The chat list, place badges and notifications render from cache. Every chat the tail prefetcher processed opens with its last ~100 entries, authors and reactions, and scrolls within them. Scrolling past the cached range shows an "older messages aren't available offline" edge, not endless skeletons. The banner says offline. |
| **Going offline mid-session**, including while a call is in flight | Everything on screen stays. Navigating between cached chats behaves as above within a couple of seconds of the OS reporting offline, not after the keep-alive timeout. |
| **Coming back online** | Stale values refresh, pending edges load, no manual reload. |
| **Never** | No `ErrorBarrier` from a connectivity failure. No component stuck *Computing* forever because a dependency can't be fetched. |

**Non-goals for this plan:** writes while offline (posting, reactions, read positions - see
[follow-ups](#follow-ups)), media that was never displayed, search, live sessions and streaming,
on-demand translation, and WASM cold start (there is no service worker; a loaded WASM app is
covered, a fresh page load is not). Blazor Server has no client cache and is out of scope.

## Where we are

What already works, and shouldn't be re-implemented:

- **Fusion serves cache hits immediately and re-serves stale values once the peer is disconnected.**
  A cold-start call with a persistent-cache hit returns at once and validates in the background;
  an invalidated computed that still carries its cache entry is re-served when the peer is not
  connected, and is invalidated again on reconnect (`RemoteComputeMethodFunction.ComputeRpc`).
  A disconnect invalidates nothing.
- **The persistent cache is in place** on MAUI (`KvasarRemoteComputedCache`) and WASM
  (`WebRemoteComputedCache`), keyed per session, versioned per API version.
- **The tail prefetcher** (`ChatUI.PrefetchChatTails`) already walks recent chats by recency and
  keeps its own progress, independent of what's on screen.
- **The escape hatch for calls that must not be awaited** exists: `ComputedExt.UseIfReady`, plus
  `ConnectivityUI.IsConnected` gates in `LiveStreamUI` and `TypingUI`.
- **The session is restored without asking the server** (`MauiSession.Acquire`), and
  `IAccounts.GetOwn` is force-flushed to disk.

What breaks, with the root cause behind each symptom:

| # | Root cause | Symptom |
|---|---|---|
| R1 | A compute call whose result was **never cached** waits for the connection with no timeout (`RpcCallTimeouts.Default.Query` is `(∞, ∞)`), and so does a never-cached call that was in flight when the link dropped. | A component - or an aggregate like `ChatUI.GetChatItems` that awaits many tiles - stays *Computing* until reconnect. Skeletons forever; scroll requests that never return. |
| R2 | `no-cache` methods on the render path are awaited. `LiveSessionUI.GetConversation` / `GetBlockSnapshot` and `LiveBlockUI.GetBlockState` all await `ILiveSessions.GetState` on the first read; `AuthorPresenceIndicator` awaits `ILiveSessions.GetAudioStreamingAuthorIds`; `ChatVideoUI.GetActiveVideoStreams` awaits `ILiveVideoStreams.List`. | **A chat opened for the first time in the process never renders offline**, however well cached its data is. Presence dots and PTT-armed rows pend. |
| R3 | The tail prefetcher didn't warm everything the chat view needs: not `IChats.GetChatRangeMeta` (every build awaits it), not `IAuthors.Get` / `GetOwn`, not reactions, threads, or pins. *Closed by T1.4-T1.6, except threads.* | A fully prefetched chat still couldn't render cold, and when it could, authors and reactions were blank. |
| R4 | The peer learns about a dead link late. Nothing disconnects the RPC peer when the OS says offline (the app only parks reconnects), so the serve-stale paths engage only after the ~25-35 s keep-alive timeout. | Half a minute of hangs after pulling the plug, and again after every app resume on a dead network. |
| R5 | Failures surface as errors. `ChatUI.Get` bounds `GetNews` with a 20 s `WaitAsync` and rethrows; a component whose state holds an error re-renders, `State.Value` throws, and the nearest `ErrorBarrier` takes over. `ChatPage` maps any non-timeout exception to "chat unavailable". | Once R1 is fixed by making misses fail, every fresh-mounted component with a miss would trip a barrier unless the failure is treated as "offline, keep what you have". |

## Principles

1. **A cached value is never dropped because of connectivity.** No timeout may invalidate a value
   that was served from cache; the background validation keeps parking until connected.
2. **A miss fails fast and recovers by itself.** When the peer is known to be offline, a call with
   nothing to serve fails at once with a connectivity error; the resulting computed is invalidated
   the moment the peer reconnects.
3. **The last render stays on screen.** A connectivity error never replaces what the user sees;
   it is neither a barrier nor a blank.
4. **Online behaviour doesn't change.** Transparent reconnects, call resend, and hit-to-call
   validation keep working exactly as today. A finite connect timeout must be long enough to
   outlive a normal reconnect.
5. **Warm what the view awaits, with the view's own arithmetic.** Fusion dedupes on exact
   arguments; a prefetch that drifts by one tile boundary warms nothing.

## The changes

Ordered so that each tier is shippable on its own. Tier 1 needs no Fusion change and turns the two
blockers into a working offline read path for prefetched chats; Tier 2 makes misses fail fast and
recover instead of hanging; Tier 3 is coverage and polish.

### Tier 1 - unblock the chat view (app only)

**T1.1 Disconnect the peer when the OS reports offline.** In `ReconnectUI` (or `ConnectivityUI`),
on `IsOnline` → `false` call `Peer.Disconnect()`. The reconnect delayer already parks attempts
while offline, so the peer stays *Disconnected* and every serve-stale path in Fusion engages
immediately instead of after the keep-alive timeout. On `IsOnline` → `true` the existing
`ResetReconnectDelays` reconnects at once. Fixes R4 for the common case; a silent drop with the OS
still claiming "online" keeps the ~30 s detection, which is acceptable.

**T1.2 Stop awaiting `ILiveSessions.GetState` on the first read.** In `LiveSessionUI.UseOrLastKnown`
stand in `null` (via `UseIfReady`) when there is no last-known value *and* the peer is not
connected; keep the await when connected so the online first-open still paints the live block in
one pass. `LiveBlockUI.GetBlockState` goes through the same helper, so it is covered. Fixes the
R2 blocker.

**T1.3 Gate the remaining `no-cache` reads on `IsConnected`**, the way `LiveStreamUI.HasActivity`
already does: `LiveStreamUI.GetAudioStreamingAuthorIds` (returns empty), and
`ChatVideoUI.GetActiveVideoStreams` (returns empty) so `HasOngoingCall` really does "degrade to
false while offline" as its comment claims. Three small edits.

**T1.4 Warm what every build awaits.** *Done.* The tail prefetcher fetches `IChats.GetChatRangeMeta`
for the meta tiles covering `tail.Expand(LoadLimit)`, and `PrefetchChatInfo` adds `IAuthors.GetOwn`.
The meta-tile computation is one helper, `ChatUI.GetMetaIdTiles`, shared by the build, the
pointer-down prefetch and the tail prefetch, so they cannot drift.

**T1.5 Warm the tail's authors.** *Done.* After the tiles land, `PrefetchEntryDependencies` calls
`IAuthors.Get` for every distinct author in them, and `IReactions.ListSummaries` / `Get` for the
entries that carry reactions; `PrefetchPinnedEntries` warms the pinned bar. All tail-only, so the
build's own load-zone prefetch stays as lean as it was.

**T1.6 Version the prefetch state.** *Done.* `ChatTailPrefetchState.Version` is compared against
`ChatUI.PrefetchVersion`; a mismatch discards the stored progress, so every chat is walked again
with the new set of calls.

With Tier 1, a prefetched chat opens cold and shows text and authors; a cache miss still parks
until reconnect, which is the R1 behaviour the next tier bounds.

### Tier 2 - misses fail fast and recover (Fusion + app)

The Fusion side is three targeted changes in `RemoteComputeMethodFunction` and `RpcClientPeer`;
the app side is one configuration line, one classifier, and one base-class guard.

**T2.1 (Fusion) Background validation never times out.** `ApplyRpcUpdate` currently waits for the
connection with the method's `ConnectTimeout` and, on failure, invalidates the cached computed
with an error. It has a value to show, so it must wait indefinitely (`TimeSpan.MaxValue`) - this is
what makes a finite query connect timeout safe. One line.

**T2.2 (Fusion) `WhenConnectedOrReroute` fails at once when the reconnect can't meet the deadline.**
`RpcClientPeer` publishes `ReconnectsAt`; when the caller's timeout is finite and
`ReconnectsAt - now` exceeds it, throw `Errors.ConnectTimeout` immediately instead of waiting it
out. With the app's delayer parking reconnects ten years out while offline, every cache-miss
compute call and every plain RPC query fails instantly when the OS says offline, and waits
normally through an ordinary reconnect.

**T2.3 (Fusion) A never-cached call in flight when the link drops gets the same bound.** In
`ComputeRpc`, when `existingCacheEntry is null`, race `sendTask` against
`ConnectionState.WhenDisconnected` (the race already exists for the cached case). On disconnect,
await `WhenConnectedOrReroute(ConnectTimeout)` - a reconnect within the deadline keeps the call
transparent because the tracker resends it; otherwise the call fails with the connect timeout.
And whenever `ComputeRpc` produces a computed from a connect-timeout error, schedule
`InvalidateWhenReconnected` on it, exactly as the stale path does, so recovery doesn't wait for
`ComputedState`'s 1 s → 1 min retry backoff.

**T2.4 (app) A finite query connect timeout.** In `ClientStartup`, next to the existing
`Command = (20, ∞)`, set `RpcCallTimeouts.Default.Query = new(15, null)`. Fifteen seconds outlives a
normal reconnect and a slow mobile startup connect; a longer one only delays the "nothing to show"
verdict. Apply it to `Debug` as well, or set `UseDebug = false`, so a debugger doesn't restore
the infinite wait.

**T2.5 (app) One classifier for connectivity errors.** `ConnectivityErrorExt.IsConnectivityError(this Exception)`
in `ActualChat.Core`: `TimeoutException` from the RPC connect path, `RpcReconnectFailedException`,
the `RpcException`s for "cannot reconnect / cannot resend", `WebSocketException`, and
`OperationCanceledException` whose token is not the caller's. Everything that decides "offline vs
broken" goes through it - `SendingMessages.Queue` already has an ad-hoc version to fold in.

**T2.6 (app) Keep the last render on a connectivity error.** In
`ComputedStateComponent<THub, TState>` and `ComputedRenderStateComponent<THub, TState>` override
`ShouldRender` to return `false` when `State.HasError` and the error is a connectivity error.
Blazor keeps the previous render tree, so a component that had a value keeps it and one that
never had one keeps its initial-value skeleton - the same picture as today's hang, but with a
retrying state underneath that recovers on reconnect thanks to T2.3. `ChatListItem` already does
this by hand through `LastNonErrorValue`; this generalises it. Audit the `catch (Exception)` sites
on the read path that map errors to a terminal model (`ChatPage` → "unavailable") and route
connectivity errors to "keep the last state" instead.

**T2.7 (app) Offline-aware `ErrorBarrier`.** For the cases T2.6 can't reach (a barrier that already
tripped, non-`ComputedStateComponent` renders), render the compact variant with an offline message
when the caught exception is a connectivity error, and `Recover()` automatically when
`ConnectivityUI.IsConnected` flips to true.

Fusion changes ship first (they are inert until a client sets a finite query timeout), then T2.4-7
land together.

### Tier 3 - coverage and polish

**T3.1 Prefetch the rest of what the tail shows.** Reactions and pins landed with T1.5; what is
left is thread cards: for thread-start entries `IChats.Get`, `IChatThreads.GetThreadCreator`,
`GetThreadStat` and the thread's last tile. Reply quotes, mention chips, link previews, forwards
and translations older than the tail stay component-local pending state.

**T3.2 Prefetch the left panel's leaves:** `IAuthors.Get` / `IAccounts.Get` / `IUserPresences.Get`
for every row's last-entry author or peer (extend `PreloadContacts` and run it for every place, not
only the selected one), and `IChats.Get` + `IAuthors.Get` per active reaction notification.

**T3.3 A visible edge instead of skeletons.** When `GetChatItems` fails on a tile outside the
cached range with a connectivity error, `ChatView.GetData` returns the cached items with an
"older messages aren't available offline" item at that edge and `HasVeryFirstItem`/`HasVeryLastItem`
set so the list stops asking; the item disappears on reconnect. `ReconnectBanner` gains a
"showing cached content" line.

**T3.4 Spread the reconnect burst.** Every cache hit served while offline leaves one parked
`ApplyRpcUpdate`; on reconnect they all fire, alongside the stale-computed invalidations. Reuse
`RemoteComputedCache.HitToCallDelayer` (already wired at startup for 1.5 s) to rate-limit the
validation calls for the first seconds after a reconnect.

## How the three paths look after Tier 2

```mermaid
sequenceDiagram
    participant UI as Component / ChatUI
    participant F as Fusion (RemoteComputeMethodFunction)
    participant C as Kvasar cache
    participant P as RpcClientPeer

    Note over UI,P: Cold start, cached value
    UI->>F: compute call
    F->>C: Get(key)
    C-->>F: hit
    F-->>UI: cached computed, consistent
    F->>P: wait for connection (no timeout, T2.1)
    Note over F,P: parks until online, value stays on screen

    Note over UI,P: Invalidated while offline, cache entry attached
    UI->>F: recompute
    F->>P: connected?
    P-->>F: no (disconnected by T1.1)
    F-->>UI: stale computed; invalidated on reconnect

    Note over UI,P: Never cached, offline
    UI->>F: compute call
    F->>C: Get(key)
    C-->>F: miss
    F->>P: WhenConnectedOrReroute(15 s)
    P-->>F: ReconnectsAt is years away → ConnectTimeout now (T2.2)
    F-->>UI: error computed; invalidated on reconnect (T2.3)
    Note over UI: ShouldRender = false, last render stays (T2.6)
```

## Reuse

Existing abstractions this plan builds on rather than replacing:

- `RemoteComputeMethodFunction`'s serve-stale and `InvalidateWhenReconnected` paths, `RpcCallTimeouts`,
  `RpcClientPeer.ReconnectsAt`, `Errors.ConnectTimeout` - in Fusion.
- `ComputedExt.UseIfReady`, `ConnectivityUI.IsOnline/IsConnected`, `ReconnectUI`,
  `AppRpcClientPeerReconnectDelayer`, `ChatUI.PrefetchLoadZone/PrefetchChatInfo`,
  `ChatTailPrefetchState`, `LiveSessionUI.UseOrLastKnown`, `ErrorBarrier`, `ReconnectBanner`,
  `RemoteComputedCache.HitToCallDelayer` - in the app.

New pieces and where they live:

| New | Placement | Why |
|---|---|---|
| Connect-deadline check in `WhenConnectedOrReroute`, indefinite wait in `ApplyRpcUpdate`, in-flight miss race and reconnect invalidation in `ComputeRpc` | `ActualLab.Rpc` / `ActualLab.Fusion` | Generic client behaviour; every Fusion client benefits. Nothing app-specific. |
| `ConnectivityErrorExt.IsConnectivityError` | `ActualChat.Core` | Used by UI components, `SendingMessages`, and any worker that must tell offline from broken; no UI dependency. |
| `ShouldRender` guard for connectivity errors | `ActualChat.UI.Blazor` base components | Already the shared base of every computed component; nothing new to register. |
| Shared meta-tile helper for `GetChatItemsInternal` and `PrefetchLoadZone` | `ChatUI.Tiles` | Chat-view specific by construction. |
| Offline edge item for the transcript | `ChatView` / `ChatMessage` | Chat-view specific. |

No new `*UI` service: the connectivity state already lives in `ConnectivityUI`, and the guard is a
base-class concern.

## Verification

- **Fusion unit tests** (in the Fusion repo): a disconnected client peer with a parked reconnect
  delayer - cache hit renders and its validation keeps parking; invalidated cached value is
  re-served; cache miss fails within the deadline and is invalidated on reconnect; an in-flight
  miss survives a reconnect inside the deadline and fails outside it.
- **Device rig:** the Windows MAUI app with the adapter disabled, and Android in airplane mode, for
  cold start; the WASM app in Chrome with DevTools "Offline" for mid-session, driven through the
  chrome-devtools MCP so the run is repeatable. Both walks: list, places, notifications, open a
  prefetched chat, scroll to its cached edge, open a non-prefetched chat, reconnect.
- **Telemetry:** Fusion's `RemoteComputedCacheStaleValueCount` already counts stale serves; add a
  counter for connect-timeout failures and one for prefetch misses per method, so the coverage
  tables in the render-path doc can be re-derived from a real session instead of by hand.

## Risks and open questions

- **A finite query timeout during startup.** Calls issued before the first connection now fail
  after 15 s on a very slow link and are retried by `ComputedState`. Cache hits are unaffected
  (T2.1). Watch the retry churn on cold-start-slow-network in the rig.
- **`ReconnectsAt` as the offline signal.** It is exactly right for the parked-while-offline case
  and neutral otherwise; a captive portal or a dead server still costs the full 15 s per miss.
  Good enough, and the alternative - a first-class "offline" flag on the peer - can be added later
  without changing the app side.
- **Silent link drops** keep the ~30 s keep-alive detection. Tightening `RpcLimits.KeepAliveTimeout`
  on clients trades battery and false disconnects for faster serve-stale; leave it.
- **T2.6 hides errors that aren't connectivity.** The guard is strictly scoped by the classifier;
  every other error still renders and still reaches the barrier.
- **Reconnect burst** (T3.4) is real on a device with a long offline session; measure before
  deciding whether the delayer is enough.

## Follow-ups

- **Offline writes.** Read positions advanced offline are dropped after the command's 20 s timeout;
  reactions and posts fail with a toast. The backlog already lists an offline action queue; the
  read-position writer is the first customer.
- **Media cache.** Attachments and avatars live in the WebView's HTTP cache only. A deliberate
  on-device cache for avatars and thumbnails of prefetched tails is a separate, small project.
- **WASM cold start** needs a service worker to serve the app shell; the runtime side is covered by
  this plan.
- **Presence offline.** Cached `GetPresence` values render a stale "online" dot; consider showing
  presence as unknown while disconnected.
