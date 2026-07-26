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
   (`ActualLab.Fusion/Computed.cs:314-319`). Wrapping `GetState` in a narrower compute
   method does not stop the cascade — the wrapper is invalidated too. Narrowing the
   *type* is useless; only removing the dependency or reducing `GetState`'s own
   invalidation rate helps.

2. **`Computed.BeginIsolation()` alone is unsafe here.** The live range grows as
   `EndEntryLid` advances and `EffectiveVisibleStartLid` jumps at the latch, so
   "does not intersect now, skip the dependency" goes stale when the range later grows
   into the tile.

3. **The overlay cannot simply be deleted.** The client matches
   `ConversationId.New(chatId, r.Start) == liveBlockId` at `ChatUI.Tiles.cs:473-479` to
   substitute `liveBlockFoldRange`. `liveBlockId` is
   `ConversationId.New(ChatId, EffectiveVisibleStartLid)`, exactly the `Start` of the
   injected live range. Without the injection that match fails, collapsed solo-era
   conversations exclude id-tiles inside the live region, and live entries drop out of
   the view.

4. **A front-end-only overlay cannot preserve prev/next semantics.**
   `ConversationsBackend.GetRangeMeta:95-98` nulls `PreviousConversationLidRange` /
   `NextConversationLidRange` when they overlap the live range. Those feed
   `ChatsBackend.GetChatRangeMeta`'s outward walk and its `PreviousLidTileStart` /
   `NextLidTileStart` output. By the time a front-end service sees `ChatRangeMeta`, the
   per-tile prev/next values are gone and cannot be reconstructed.

5. Backend contracts (`IChatsBackend`, `IConversationsBackend`) carry no `[LegacyName]`
   anywhere, so adding a parameter to a backend method is routine in this codebase.
   Client-facing contracts (`IChats`, `IConversations`) must stay wire-compatible —
   shipped MAUI/iOS builds depend on the current meaning of
   `ChatRangeMeta.ConversationLidRanges` and `IConversations.GetTile`.

## Design

**Pass the live range down the backend chain as data instead of taking a Fusion
dependency on it.**

`ConversationsBackend` loses its `ILiveSessionsBackend` field entirely. The overlay
logic stays exactly where it is — so prev/next semantics are preserved bit for bit —
but its input arrives as a plain parameter.

### Signature changes (backend only)

```
IConversationsBackend.GetRangeMeta(ChatId chatId, long idTileStart,
    Range<long>? liveRange, CancellationToken ct)
IConversationsBackend.GetTile(ChatId chatId, Range<long> lidTileRange,
    Conversation? liveConversation, CancellationToken ct)
IChatsBackend.GetChatRangeMeta(ChatId chatId, long lidTileStart,
    Range<long>? liveRange, CancellationToken ct)
```

`liveRange` is **normalized per tile** by the caller: `null` when the tile does not
intersect the live range. This is what keeps the cache stable — tiles away from the live
session always get the key `null` and never churn. Only the one or two tiles the live
session actually overlaps get a changing cache key, which is correct and minimal.

Because these are compute-method arguments rather than dependencies, a change to the
live range does not *invalidate* anything; it selects a different cache entry. Entries
for the previous range stay valid for other readers and age out via `MinCacheDuration`.

### Where the live state is read

The session-scoped front-end services, which exist to compose backends for a view:

- `Chats.GetChatRangeMeta` (`Chats.cs:143-150`, currently a passthrough)
- `Conversations.GetTile` (`Conversations.cs:14-21`, currently a permission check +
  passthrough)

Client-facing wire contracts are unchanged, so shipped clients keep working.

### Internal callers pass null

`ConversationSplitFlow:99`, `LiveConversationSummaryFlow:133` and
`ConversationsBackend.OnAppendReply:394` pass `null` and get the pure persisted view,
which is what they actually want. This fixes a latent bug: `LiveConversationSummaryFlow`
asks for the persisted previous conversation to clamp `contextStart` ("Never re-claim a
persisted conversation's range") but can currently receive `null` because the overlay
suppressed it. `ConversationSplitFlow` already does its own `OverlapsLiveSession` check
at `:95` and does not want the merge either.

### Client: cut the serialized round-trips

1. **Parallelize `live` + `meta`** in `GetChatItemsInternal`. Round 1 issues
   `Chats.Get`, `LiveSessions.GetState`, `GetChatRangeMeta` and `GetIdRange`
   concurrently; round 2 issues `Conversations.GetTile`. Takes the chain from ~5 RTT to
   ~2-3.
2. **Fix the prefetch gap** at `:237-242` so it covers
   `idTiles[0].Start - FirstLayer.TileSize`, the tile `GetTile` always requests when
   `prevMessage == null`.
3. **Make `PrefetchChatTails` actually prefetch** — `ChatUI.StateSync.cs:272` calls
   `GetChatItemsInternal(..., isPrefetch: true, ...)`, removing the self-spawning
   duplicate chain and the misleading warnings.

## Explicitly out of scope

This change does **not** reduce how often the client rebuilds.
`GetChatItemsInternal` already depends on `LiveSessionUI.GetState` at
`ChatUI.Tiles.cs:74`, so the client is subscribed to the same churn regardless. It makes
each rebuild cheaper (server cache hits instead of Postgres queries) and stops one live
session from degrading the shared metadata cache for every other reader.

Cutting rebuild *frequency* requires two separate `LiveSessionsBackend` changes, tracked
as follow-up:

- make the 30s self-heal conditional on there actually being a pending deadline
  (`IsClosing` with a grace deadline, or `IsCall`/`IsDialing` with ring deadlines);
- make `UpdateSummary` a no-op when the semantic fields are unchanged, instead of always
  bumping `LastSummaryAt`/`Version`.

## Reuse

Existing abstractions used: `ILiveSessions.GetState` / `ILiveSessionsBackend.GetState`
(moved caller, not new), `LiveSessionState.VisibleEntryLidRange` and `ToConversation()`,
`Range<long>.IntersectWith`, `ConversationRangeMeta` / `ChatRangeMeta` (unchanged),
`EnsureMonotonic` / `Merge` / `Collect`.

No new shared component is introduced: the overlay code stays in place inside
`ConversationsBackend` and only its input changes, so there is nothing to promote to
`ActualChat.Core`. Per-tile normalization of `liveRange` is a one-line
`IntersectWith(...).IsEmpty ? null : liveRange` at each call site rather than a helper,
until a third caller appears.

## Testing

- `ConversationsBackend.GetRangeMeta` with `liveRange: null` returns raw persisted
  ranges during an active live session (new test).
- `Chats.GetChatRangeMeta` still suppresses a persisted conversation overlapping the
  live range and injects the live range keyed at `EffectiveVisibleStartLid` (new test).
- `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs` is the
  end-to-end regression guard for the rendered outcome.
- `tests/Chat.IntegrationTests/LiveSessionsTest.cs` covers session lifecycle.
