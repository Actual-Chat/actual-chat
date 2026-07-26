# Live-session state poisoning the conversation metadata cache

Date: 2026-07-26
Status: approved, ready for implementation

## Problem

Chat-view messages appear slowly (~1.1s) on WASM/MAUI clients whose RTT exceeds
~200ms. Blazor Server and low-RTT clients are unaffected.

Two independent causes, both confirmed against PROD logs (project
`actual-chat-app-prod`, window 2026-07-26T10:44Z-10:50Z).

### Cause 1 — server-side cache poisoning

`ConversationsBackend.GetRangeMeta` and `ConversationsBackend.GetTile` each take an
unconditional Fusion dependency on `ILiveSessionsBackend.GetState`. `GetState` is
invalidated ~3 times per minute per observed live session:

- `LiveSessionsBackend.cs:135` — `computed.Invalidate(SelfHealDelay)` with
  `SelfHealDelay = 30s`, unconditional, for as long as anyone observes the session.
- `LiveSessionsBackend.cs:447-463` — `UpdateSummary` always writes a fresh
  `LastSummaryAt` and `Version`, so the record is never equal to its predecessor and
  `InvalidateState` always fires, even when the summary content is unchanged. Called
  ~1/min by `LiveConversationSummaryFlow`.

Neither has anything to do with conversation ranges, but both invalidate the shared
per-chat metadata cache. PROD read sample (`EveryNth(8)`, ~5 min window, pod
`actual-chat-app-54c87c94c4-2zftq`):

| Method | reads | hit rate | updates / invalidations |
|---|---|---|---|
| `ChatsBackend.GetChatRangeMeta` | 808 | 28.71 % | +616 -488 |
| `ConversationsBackend.GetRangeMeta` | 1352 | 61.54 % | +536 -648 |
| `ConversationsBackend.GetTile` | 872 | 42.20 % | +416 -480 |
| `LiveSessionsBackend.GetState` | 2352 | 85.37 % | +296 -296 |
| `ChatsBackend.GetTile` (actual data) | 240 | 76.67 % | +32 -64 |

The metadata layer is invalidated 7.6x more often than the message data it describes.
These caches are shared across every reader of the chat, so one live session degrades
the metadata cache for everyone in it.

This is also a domain-boundary violation: `ConversationsBackend` owns persisted
conversations and should not know that live sessions exist.

### Cause 2 — serialized RPC round-trips on the client

`ChatUI.GetChatItemsInternal` is a chain of ~5 strictly sequential dependent RPC
batches. Client log, MAUI at ~220ms RTT:

```
GetChatItems: ... took 1109ms (live 220, meta 542, load 184, build 163)
```

- `live` (`ChatUI.Tiles.cs:67-121`) — 1 RTT
- `meta` (`:126-205`) — ~2.5 RTT: `GetChatRangeMeta` batch, then `Conversations.GetTile`
  batch, then `GetIdRange`
- `load` (`:236-252`) — 1 RTT
- `build` (`:269-338`) — 1 RTT

Total ~= 5 x RTT. At 220ms that is 1.1s; at 20ms it is invisible. Most of the chain is
false serialization — `metaIdTiles` is derived from `dataQuery` alone, `Conversations.GetTile`
depends only on `metaIdTiles`, and `GetIdRange` is independent.

Two further defects amplify it:

- `ChatUI.Tiles.cs:237-242` prefetches only the tiles inside `idTiles`, but `GetTile`
  widens its request by one tile when `prevMessage == null` (`:616-619`). The first
  build-loop iteration therefore always misses cache and costs a serial round-trip.
- `ChatUI.StateSync.cs:272` — `PrefetchChatTails` calls the public `GetChatItems`
  (`isPrefetch: false`), so every prefetch logs as a real query and spawns a second full
  chain via `:399-409`. It fires on every `ChatInfo` change for every visible chat.

Client log confirms the resulting backlog: `Tracking 57874 outbound calls (in progress:
7617)`. Verified in `ActualLab.Rpc/Infrastructure/RpcCallTrackers.cs:135-138` that
"in progress" counts calls whose `ResultTask` has not completed — genuinely unanswered
requests, not live subscriptions. The backend mesh by contrast shows `in progress: 0-4`,
so the server is not slow; it is thrashing its cache while the client queues.

## Constraints discovered during design

1. **Fusion invalidation is eager and transitive with no value diffing**
   (`ActualLab.Fusion/Computed.cs:314-319`). A narrower compute method *derived from*
   `GetState` does not stop the cascade — it is invalidated too. Narrowing only helps if
   the new method bypasses `GetState` and carries its own invalidation lifecycle.

2. **`Computed.BeginIsolation()` alone is unsafe.** The live block's start jumps at the
   latch and the session can vanish, so "does not intersect now, skip the dependency"
   goes stale. Isolation only works when something else guarantees invalidation.

3. **The overlay cannot simply be deleted.** The client matches
   `ConversationId.New(chatId, r.Start) == liveBlockId` at `ChatUI.Tiles.cs:473-479` to
   substitute `liveBlockFoldRange`. `liveBlockId` is
   `ConversationId.New(ChatId, EffectiveVisibleStartLid)`, exactly the `Start` of the
   injected live range. Without the injection that match fails and live entries drop out
   of the view.

4. **Backend contracts must not carry live-session vocabulary.** Threading a
   `Range<long>? liveRange` / `Conversation? liveConversation` parameter through
   `IChatsBackend` / `IConversationsBackend` was rejected: it spreads the same
   responsibility mixing one level up rather than removing it.

5. **`ConversationsBackend.GetRangeMeta` is invalidated by cache key**
   (`ConversationsBackend.cs:157,162,166`), so adding an argument to it would silently
   break invalidation. `ChatsBackend.GetChatRangeMeta` and `ConversationsBackend.GetTile`
   have no explicit invalidation sites — they invalidate transitively.

6. **The 30s self-heal poll is load-bearing and must stay.** `IsSessionLive` reads raw
   Redis (`SafeGetHashMap`, `_invites.GetHashMap`) and `HasFreshRecorder` compares against
   a time-based 90s staleness cutoff, so nothing invalidates `GetState` on its own.
   Gating the poll would stop ambient sessions from ever auto-closing. (An earlier draft
   of this design proposed making it conditional — that was wrong.)

7. **A latched live session owns `[V, +inf)`, not a bounded range.**
   `ConversationSplitFlow.cs:74-76` computes its territory as
   `new Range<long>(…, long.MaxValue)`, and the client models it the same way
   (`ChatUI.Tiles.cs:436-438`, `hiddenLiveTailRange`, `GroupExpandedConversations`).
   `LiveSessionState.VisibleEntryLidRange` instead ends at the last *summarized* lid
   (`LiveConversationSummaryFlow.cs:191`), which lags by up to a minute.

## Design

**Extend `ILiveSessionsBackend` with two narrow queries that bypass `GetState`.**

```
Task<long?> GetVisibleStartLid(ChatId chatId, CancellationToken ct);
Task<Conversation?> GetLiveConversation(ChatId chatId, CancellationToken ct);
```

Both read Redis directly via `SafeGet` rather than calling `GetState`, so they inherit
none of its invalidations — not the 30s liveness poll, not a summary rewrite that changed
nothing. They are kept fresh by a new `InvalidateLiveView(chatId)`, called from the five
writers that can move the block's start or change its card: `OnStreamRegistered`,
`UpdateSummary`, `StartCall`, `AcceptCall`, and `Close`. The other three state writers
(`SetRules`, `SetContextStart`, `EvaluateLiveness`) cannot affect either value.

`LiveSessionsBackend` is the right owner of that contract: nothing else knows when the
range moves. `IChatsBackend` and `IConversationsBackend` are untouched.

`ConversationsBackend` then depends on the two narrow queries instead of `GetState`. Per
constraint 7, `GetVisibleStartLid` returns a lid with `[V, +inf)` semantics; the range
*emitted* into `ConversationLidRanges` still needs a finite end for the downstream range
math (`ChatsBackend.EstimateMinimumCount`), so it is capped at the chat's end via an
isolated `GetLidRange` read — isolated because depending on the lid range would invalidate
`GetRangeMeta` on every message, which is worse than the problem being fixed.

Additionally, `UpdateSummary` now bails out when the summary is unchanged instead of
always rewriting `LastSummaryAt`/`Version`, so a scheduled re-run no longer looks like a
change.

### Client: cut the serialized round-trips

1. **Parallelize `live` + `meta`** in `GetChatItemsInternal`: `Chats.Get`,
   `LiveSessionUI.GetConversation`, `GetState`, `LiveBlockUI.GetBlockState`, the
   `GetChatRangeMeta` batch and the isolated `GetIdRange` are all issued before the first
   await. Verified safe: `ComputeContext.Current` is an `AsyncLocal` read synchronously by
   the interceptor, so both dependency capture and `BeginIsolation` apply at call time,
   not await time.
2. **Fix the prefetch gap** at `ChatUI.Tiles.cs:237-242` so it covers
   `idTiles[0].Start - FirstLayer.TileSize`, the tile `GetTile` always requests when
   `prevMessage == null`.
3. **Make `PrefetchChatTails` actually prefetch** — `ChatUI.StateSync.cs:272` now calls
   `GetChatItemsInternal(..., isPrefetch: true, ...)`.

## Explicitly out of scope

This change does **not** reduce how often the client rebuilds.
`GetChatItemsInternal` already depends on `LiveSessionUI.GetState` at
`ChatUI.Tiles.cs:74`, so the client is subscribed to the same churn regardless. What it
does is stop one live session from degrading the shared per-chat metadata cache for every
other reader, and make each rebuild cheaper — server cache hits instead of Postgres
queries, and ~2 round-trips instead of ~5 on the client.

## Reuse

Existing abstractions used: `SafeGet` and `ShardOwner.RequireShardOwnership` (the same
pattern `GetState` uses), `LiveSessionState.EffectiveVisibleStartLid` / `ToConversation()`,
`ChatsBackend.GetLidRange`, `Range<long>.IntersectWith`, `Collect`, `EnsureMonotonic`.
`ConversationRangeMeta` and `ChatRangeMeta` are unchanged.

No new shared component: the overlay stays inside `ConversationsBackend` and only its
input changes, so there is nothing to promote to `ActualChat.Core`.

## Testing

Covered by existing suites, all green after the change:

- `tests/Chat.IntegrationTests` — 300 passed, 6 skipped (includes `LiveSessionsTest`, 45).
- `tests/Chat.UI.Blazor.IntegrationTests` — 62 passed, 2 skipped (includes
  `LiveConversationDisplayTest` and `SendingMessagesDisplayTest`, the end-to-end guards
  for the rendered live block).

Still worth adding: a focused test that a persisted conversation in the summary-lag window
`[EndEntryLid+1, chatEnd)` is now suppressed — the behaviour constraint 7 corrects. It is
latent today because `ConversationSplitFlow` refuses to create one there, so it needs a
hand-built fixture rather than the normal flow.
