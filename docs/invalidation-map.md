# Compute-method invalidation map

A map of how invalidations flow through Voxt's Fusion compute graph: where they
originate, how they amplify, which edges are conditional, and where a
`ConsolidationDelay` would cut a cascade that is effectively a no-op.

Derived from source at commit `00f344d8a` (branch `dev`), with frequencies measured
from production FusionMonitor reports on 2026-07-26/27 (§11.4).

> **Read §11.4 before acting on anything else.** The measured data contradicted the
> a-priori ranking on two counts. The invalidations that dominate production are not
> the data-flow cascades this map spends most of its space on — those barely register
> on the server. They are (1) an **error-retry spin** on `SessionsBackend.Get` /
> `Accounts.GetOwn` (44 % of peak invalidations), (2) flow-state **priming** (23 %),
> and (3) **presence self-heal timers** (26 %). Bigger than all of them: the worst
> server-side cache-hit ratios are a *retention* problem (missing `MinCacheDuration` —
> `MediaBackend.Get` alone is 97.5 k reads at a **0.0 %** hit rate), not an
> invalidation problem at all.
> §8 and §9 are ordered by the measurements; §4–§7 remain the structural reference.

**Contents**

- [1. Mechanics you need to read the map](#1-mechanics-you-need-to-read-the-map)
- [2. Consolidation: what it does and when it silently does nothing](#2-consolidation-what-it-does-and-when-it-silently-does-nothing)
- [3. The two amplification dimensions](#3-the-two-amplification-dimensions)
- [4. Invalidation roots](#4-invalidation-roots)
- [5. Hub nodes and their blast radius](#5-hub-nodes-and-their-blast-radius)
- [6. Conditional edges](#6-conditional-edges)
- [7. Hot-path narratives](#7-hot-path-narratives)
- [8. Where invalidations hurt most](#8-where-invalidations-hurt-most)
- [9. Consolidation recommendations](#9-consolidation-recommendations)
- [10. Audit of existing `ConsolidationDelay` usages](#10-audit-of-existing-consolidationdelay-usages)
- [11. Measuring it in production](#11-measuring-it-in-production)
- [12. Methodology and limits](#12-methodology-and-limits)

---

## 1. Mechanics you need to read the map

### 1.1 How an invalidation starts

Every write goes through a CommandR handler. Fusion runs each handler **twice**:
once for real, once more in *invalidation mode* after the operation commits. The
second pass is the block guarded by `Invalidation.IsActive`, and the `_ = Foo(x, default)`
calls inside it are the **invalidation roots** — the compute methods declared stale.

```csharp
if (Invalidation.IsActive) {
    var invChatEntry = context.Operation.Items.KeylessGet<ChatEntry>();
    if (invChatEntry != null)
        InvalidateTiles(chatId, invChatEntry.LocalId, changeKind, ...);
    return null!;
}
```
`src/dotnet/Chat.Service/ChatsBackend.cs:1228`

The other three origin classes:

| Origin | Mechanism | Examples |
|---|---|---|
| Command invalidation block | `Invalidation.IsActive` | all `*Backend.OnXxx` handlers |
| Completion handler | `context.Operation.AddCompletionHandler` + `Invalidation.Begin()` | `UserPresencesBackend.OnCheckIn` (`UserPresencesBackend.cs:64`) |
| Self-invalidation (timer) | `Computed.GetCurrent().Invalidate(delay)` | `UserPresences.Get`, `LiveSessionsBackend.GetState`/`GetConsolidatedParticipants`/`GetConsolidatedHasRecorder`/`GetCallState`, `SharedLocationsBackend`, `LiveVideoBackend`, `LiveTime` |
| Non-command direct invalidation | `using (Invalidation.Begin())` outside a handler | `ChatUI.EnableSearch` (`ChatUI.cs:250`), `LiveSessionsBackend.InvalidateState` |

Once a root is invalidated, Fusion walks its **dependents** transitively. Every
compute method that awaited it during its own computation is invalidated too, then
their dependents, and so on to the UI.

### 1.2 What FusionMonitor actually reports

`FusionMonitor` is registered for every host and auto-started on non-dev servers
(`src/dotnet/UI.Blazor/Module/BlazorUICoreModule.cs:188-225`):

- `SleepPeriod` = 5 min ± 20 %, `CollectPeriod` = 60 s → roughly **one report set every 6 minutes per pod**.
- `AccessSampler` / `RegistrationSampler` = `EveryNth(8)` → printed numbers are already multiplied back up by 8; treat them as estimates.
- Three separate `LogInformation` messages per cycle: `Reads, sampled with …`, `Updates (+) and invalidations (-), sampled with …`, `Invalidation paths, sampled with …`.
- On servers, a preprocessor drops every accesses/registrations category that isn't `DbAuthService*` or `*Backend.*`. **The invalidation-paths tree is not filtered** — it shows every category.

### 1.3 What the reported numbers actually mean — verified against Fusion source

Every figure in §8 and §11.4 depends on the instrumentation's semantics, so those were
checked against `ActualLab.Fusion` rather than assumed. Results:

| Reported quantity | Verified meaning | Sound? |
|---|---|---|
| `EveryNth(12.5 %)` and the ×8 multiplier | `Sampler.EveryNth(8)`; `InverseProbability = 1/(1/8) = 8` (`ActualLab.Core/Diagnostics/Samplers.cs:23,56`) | ✅ |
| `N reads` | `ComputedRegistry.ReportAccess`, reached only from `Computed.RenewTimeouts` | ✅ |
| `% hits` | `isNew: false` ← `ComputedImpl.UseExisting`/`TryUseExisting` on a **consistent** computed = a genuine cache hit; `isNew: true` ← `UseNew` after a fresh computation = a genuine miss (`Internal/ComputedImpl.Helpers.cs:30,88,95`) | ✅ |
| `-M` (invalidations) | `ComputedRegistry.Unregister` **throws** unless `ConsistencyState == Invalidated` (`ComputedRegistry.cs:176`), so `OnUnregister` fires only for invalidated computeds — not for evictions | ✅ |
| `+N` (updates) | `OnRegister` fires *before* the `Invalidated` early-return and the dedup loop (`ComputedRegistry.cs:130`), so it counts `Register` **calls**, a slight over-count of distinct computeds | ⚠️ minor |
| invalidation-path multiplier | uses `RegistrationSampler.InverseProbability`, matching the `OnUnregistration` gate that feeds the tree (`FusionMonitor.cs:114,199`) | ✅ |

Four things that materially affect how you read a report:

1. **Invalidation-pass calls are not counted as reads.** `CallOptions.Invalidate`
   returns from `TryUseExistingWithCallOptions` before `RenewTimeouts`
   (`ComputedImpl.Helpers.cs:41-52`), so the `_ = Foo(x, default)` calls in every
   invalidation block don't inflate the reads report. Good — otherwise read counts
   would be meaningless.
2. **A leading `*` means a remote replica, not a local method.**
   `RemoteComputeMethodFunction.ToString()` returns `"*" + base.ToString()`
   (`Client/Interception/RemoteComputeMethodFunction.cs:56`), and `Category` is
   `Function.ToString()`. So `*FlowBackend.TryGetData` is *this* pod's outbound RPC
   stub for a call to another pod — its options come from
   `ComputedOptions.ClientDefault` (`MinCacheDuration` = 1 min,
   `RemoteComputedCacheMode.Cache`), not from `Default`. Starred and unstarred
   categories need different explanations for the same symptom.
3. **Consolidating methods reported source and target under one category — fixed in
   Fusion, but *after* the version these measurements came from.** `Category` is
   `MethodDef.FullName` = `$"{Type}.{Method}"` (`MethodDef.cs:34`), and the
   consolidation source and target defs are built from the *same* type and
   `MethodInfo`, so both used to report identically. A spinning consolidation
   *source* therefore inflates the category even when the target is successfully
   absorbing it and nothing downstream moves.

   **This applies to every consolidated method in §8 and §11.4** — notably
   `Accounts.GetOwn` (4,880 invalidations at peak) and `UserPresences.GetLastCheckIn`.
   Read those counts as "source + target combined". For `Accounts.GetOwn` the split
   matters: if its consolidation is working, most of that count is the source
   spinning on a cached error while the target absorbs it, so the *downstream*
   damage is smaller than the raw number implies — the wasted recomputes are real,
   the cascade is not. §9.1 is still the fix; the cascade half of its cost is
   overstated.

   Fusion `master` now prefixes the consolidation source with `~`, so a future
   report shows `Accounts.GetOwn` and `~Accounts.GetOwn` separately and the split is
   directly readable. Prod was running 14.1.47, which predates that, so the numbers
   here remain combined. Re-measure after the Fusion upgrade to size §9.1 properly.
4. **Counts are sums over sampled minutes, not over wall-clock windows.** In prod the
   monitor detaches its handlers during `SleepPeriod`, so it observes 60 s out of
   every ~5–6 min — roughly a 10 % duty cycle per pod. A "2-hour window" figure is
   really the sum over ~20 sampled minutes per pod. Ratios are unaffected; absolute
   totals are not wall-clock totals.

Two further notes, neither of which affects the figures used here: `RemoteComputed`
with `RemoteComputedCacheMode.Cache` uses `PseudoUnregister`
(`Client/RemoteComputed.cs:91`), firing the event without leaving the registry — so a
starred category's `-M` counts invalidations, not removals; and an invalidation whose
`InvalidationSource` is `None` would print as an empty origin key
(`InvalidationSource.ToString()` returns `""`). No empty rows appeared in the data.

> **Important caveat.** `Invalidation.TrackingMode` defaults to `OriginOnly` and
> Voxt never overrides it. In that mode `Computed.InvalidationSource` holds a
> *string* (a `file:member:line` code location), so `GetInvalidationOrigin()` always
> returns the computed itself and the printed tree collapses to **two levels**:
> `<code location> → <category>`. You get "which write site invalidated which
> method", **not** the propagation chain. Real chains require
> `InvalidationTrackingMode.WholeChain` — see §11.

---

## 2. Consolidation: what it does and when it silently does nothing

`[ComputeMethod(ConsolidationDelay = seconds)]` splits the method into two computeds
(`ComputeMethodDef.cs:36-63`):

- a **source** computed — the real one, keeps invalidation/auto-invalidation delays, `MinCacheDuration` forced to 0;
- a **target** `ConsolidatingComputed<T>` — what everyone else depends on; all invalidation delays disabled, holds the `MinCacheDuration`.

When the source is invalidated, the target does **not** propagate. It waits
`ConsolidationDelay`, recomputes the source, and compares
(`ConsolidatingComputed.cs:66-93`):

- **outputs equal** → the target stays consistent, re-subscribes to the new source, **the cascade stops here**;
- **outputs differ** → the target invalidates and the cascade proceeds, one `ConsolidationDelay` later.

So it is a *value-level* dedup with a debounce, not a throttle. It costs one extra
computed per key plus one recompute per source invalidation.

### 2.1 Precondition 1 — the output must compare equal

The comparison is `FusionDefaultDelegates.ComputedOutputEqualityComparer`, which
bottoms out at `Equals(x.Value, y.Value)`. **Most Voxt model types deliberately use
reference equality:**

| Type | Equality | Consolidation viable? |
|---|---|---|
| `bool`, `int`, `long`, `Presence`, `CallStatus`, `Trimmed<int>`, `Range<long>`, `Moment`, `ApiNullable8<T>`, all `*Id` structs | value | **yes** |
| `AccountFull` / `Account` | `ReferenceEquals` (`Api/Users/Account.cs:45`, `AccountFull.cs:79`) | only if the value is a cached upstream reference |
| `AuthorFull` / `Author` | `ReferenceEquals` (`Api/Chat/AuthorFull.cs:31`, `Author.cs:46`) | same |
| `ChatEntry` | `ReferenceEquals` (`Api/Chat/ChatEntry.cs:130`) | same |
| `AuthorRules` | `ReferenceEquals` (`Api/Chat/AuthorRules.cs:44`) | same |
| `ChatTile` | plain `class`, no `Equals` override | **no** |
| `ChatRangeMeta`, `ChatEntryRangeMeta` | record with `Range<long>[]` members → array reference equality | **no** |
| `ApiArray<T>` | `Equals(Items, other.Items)` → **array reference equality** (`ActualLab.Core/Api/ApiArray.cs:305`) | **no** |

The practical rule:

> **Consolidation can only suppress an invalidation when the recompute produces a
> value that is `Equals` to the old one.** For a reference-equality type that means
> the method must be a *pass-through or selector over upstream cached values* that
> did not themselves change. Any method that materializes a fresh object per
> computation — a DB read, a `.ToArray()`, a `.ToApiArray()`, a `new Xxx(...)` —
> will never compare equal, and its `ConsolidationDelay` becomes a pure **delay**
> with extra allocation and no suppression.

This single rule explains both the existing wins and the two existing no-ops in §10.

### 2.2 Precondition 2 — the method must be computed locally

`ConsolidationDelay` is honoured only by `ComputeMethodFunction`
(`ComputeServiceInterceptor.cs:46`). A client's **remote replica** of a server API
method goes through `RemoteComputeMethodFunction`
(`RemoteComputeServiceInterceptor.cs:51`), which ignores it — and
`[RemoteComputeMethod(ConsolidationDelay = …)]` throws outright
(`ComputedOptions.cs:72`).

So:

- server-side services (`*Backend`, `Chats`, `Authors`, `Accounts`, …) → consolidation applies;
- client-side *local* compute services (`ChatUI`, `ChatListUI`, `ChatVideoUI`, `TranslationUI`, …) → consolidation applies;
- the client's replica of `IChats.GetNews` & co → **does not**. To damp an
  RPC-delivered invalidation on the client, consolidate a client-local wrapper.

The sharp edge is a **`Distributed` backend service**: its RPC-exposed methods go
through `RemoteComputeMethodFunction` *even on the shard owner*, which executes the
body in-process yet still produces a plain `ComputeMethodComputed`. So consolidation
is dropped on both sides, not just on callers. Since Fusion 14.1.71 that's a startup
error rather than a silent no-op. The remedy is a non-RPC-visible (`protected
virtual`) compute method carrying the consolidation, with the public method deriving
from it — the shape `LiveSessionsBackend.GetConsolidated*` uses.

One consequence worth knowing when wiring the writers: invalidate the **consolidating**
method, not the public one. `ConsolidatingComputed` is an `IHasInvalidationTarget` whose
target is its consolidation source, and `Invalidation.Begin()` follows that
(`ComputedImpl.Helpers.cs:47-48`). Invalidating the public wrapper instead only
invalidates that derived computed, which then re-reads the still-consistent
consolidating one and keeps serving the stale value.

### 2.3 Related knobs, and when to prefer them

| Knob | Effect | Use when |
|---|---|---|
| `ConsolidationDelay` | debounce **+ value dedup**; stops no-op cascades | the value often doesn't change, and it compares by value |
| `InvalidationDelay` | debounce only; always propagates | the value does change but you want to batch bursts (`ChatListUI.GetUnreadChatCount`, `InvalidationDelay = 0.6`) |
| `MinCacheDuration` | keeps the computed alive in RAM | high re-read rate |
| Event `SetDelayBy(…, key)` | collapses N commands into 1 | dedup at the *write* side (`ChatPositionsBackend.cs:86`) |
| Throttle in the handler | skips the write entirely | `Constants.Contacts.MinTouchInterval` (`ContactsBackend.cs:950`) |

Write-side dedup is always cheaper than read-side dedup. Reach for
`ConsolidationDelay` when the write genuinely has to happen but most reads of the
derived value are unaffected by it.

---

## 3. The two amplification dimensions

An invalidation's cost is the product of two independent factors. Getting this
right is what makes the map predictive.

**A. Graph amplification** — how many compute methods transitively depend on the
root, in one process. Measured below as "downstream" counts.

**B. Fan-out amplification** — how many *keyed instances* and *subscribed clients*
each of those methods has. A server-side computed keyed by `chatId` that N clients
have subscribed to via Fusion RPC produces N invalidation messages and up to N
re-reads.

The dangerous combination is a **low-cardinality, high-subscription key** on a
**deep** node:

| Shape | Example | Cost |
|---|---|---|
| high graph × high fan-out | `ChatsBackend.GetRules(chatId, principal)` — every facade method gates on it | worst |
| high graph × low fan-out | `AccountsBackend.Get(userId)` — 109 downstream but per-user | moderate |
| low graph × moderate fan-out | `UserPresences.Get(userId)` — 2 downstream, one per viewing session | high in aggregate because it fires constantly |
| low × low | `ChatsBackend.GetChatCopyState` | negligible |

`UserPresences.Get` is the clearest case where graph depth understates real cost —
see §7.3. **Measured**, the server-side fan-out from one `UserPresences.Get`
invalidation to `Authors.GetPresence` is only ≈ 1.24× (§11.4), not the large
multiplier the keying suggests: a server pod holds `Authors.GetPresence` computeds
only for the sessions it is actively serving. Keying alone is not evidence of
fan-out — measure it.

---

## 4. Invalidation roots

Grouped by the write that triggers them. "Freq" is an a-priori estimate from the
code (cadence constants, throttles); replace with measured values from §11.

### 4.1 Chat entries — `ChatsBackend.OnChangeEntry` (`ChatsBackend.cs:1228`)

The single most consequential write in the system.

| Invalidated | Condition | Notes |
|---|---|---|
| `GetTile(chatId, smallestTileRange, includeRemoved: true)` | always | only the **smallest** tile; larger tiles and the `includeRemoved:false` variant are composed from it, so they invalidate through the graph rather than directly (`InvalidateTiles`, `ChatsBackend.cs:2120`) |
| `GetEntryRangeMeta(chatId, entryTileStart)` | `Create`/`Remove`/thread-rebind only | |
| `GetEntryRangeMeta` for previous/next tile | only if the neighbour lid falls outside the entry's own tile | |
| `GetMinLid(chatId)` | `Create` **and** no previous entry | i.e. only the very first entry |
| `GetMaxLid(chatId, true)` + `GetMaxLid(chatId, false)` | `Create`, or `Update` with thread-rebind | |
| `GetMaxLid(chatId, false)` | `Remove` | |

Cadence: a voice utterance produces exactly **two** `OnChangeEntry` calls — one
`Create` when transcription starts and one `Update`/`Remove` on finalize
(`AudioStreamingBackend.ProcessAudio.cs:534,599`). Live transcript text flows over
a separate stream and does **not** touch the entry. This is already well designed;
don't assume per-word writes.

Downstream events fired from the same command
(`context.Operation.AddEvent(new ChatEntryChangedEvent(...))`, `ChatsBackend.cs:1453`)
are handled by: `ChatsBackend` (flows/summarization), `ContactsBackend`,
`MentionsBackend`, `LinkPreviewsBackend`, `NotificationsBackend`, `SearchBackend`,
`ChatUsagesBackend`, `ChatEntryLanguagesBackend` — each with its own invalidation
roots.

### 4.2 Read positions — `ChatPositionsBackend.OnSet` (`ChatPositionsBackend.cs:36`)

| Invalidated | Condition |
|---|---|
| `ChatPositionsBackend.Get(userId, chatId, kind)` | only if the row actually changed (`hasChanges`) |
| `ChatsBackend.GetReadPositionsStat(chatId)` (via a queued `ChatsBackend_UpdateReadPositionsStat`) | only if `MightUpdateStat` says the new position could enter the top-N |

Two dedup layers already exist here: the `hasChanges` guard, and the
`ReadPositionChangedEvent` collapsed to one per `(user, chat)` per
`Constants.Notification.ReadReconcileWindow` via `SetDelayBy`. Still, `Get` is
invalidated on **every forward scroll step** of every user in every open chat.

### 4.3 Presence — `UserPresencesBackend.OnCheckIn` (`UserPresencesBackend.cs:64`)

Invalidates `UserPresencesBackend.GetLastCheckIn(userId)` from a completion handler,
unconditionally.

Cadence: active clients check in every `AwayTimeout * 0.75` = **45 s**; inactive
every 180 s (`AppPresenceReporter.cs:22-34`). So this fires ≈ once per 45 s **per
online user**, forever, whether or not anything is happening.

### 4.4 Authors — `AuthorsBackend.OnUpsert` / `OnRemove` (`AuthorsBackend.cs:120`, `:313`)

| Invalidated | Condition |
|---|---|
| `GetInternal(chatId, authorId)`, `GetInternal(chatId, userId)` | always |
| `ListAuthorIdsInternal(chatId)`, `ListUserIdsInternal(chatId)` | only when `HasLeft` flipped |

The `HasLeft` guard is a good example of a deliberately conditional edge: it keeps
avatar/name edits from invalidating the whole chat's member list.

`EnsurePlaceChatAuthorExists` inside `OnChangeEntry` (`ChatsBackend.cs:1428`) only
upserts when the author is missing or has left, so posting a message does **not**
normally invalidate author state.

### 4.5 Contacts — `ContactsBackend.OnChange` / `OnTouch` (`ContactsBackend.cs:330`, `:435`)

| Invalidated | Condition |
|---|---|
| `Get(ownerId, id)` | index changed |
| `ListIds(ownerId, placeId)` | on `OnChange`: always; on `OnTouch`: **only** if the contact was outside `Constants.Contacts.MinLoadLimit` or is new |
| `IsBlocked`, `ListBlockedIds`, peer's `Get` | peer chats only |

`OnTouch` is driven by `OnChatEntryChangedEvent` and throttled by
`Constants.Contacts.MinTouchInterval` (`ContactsBackend.cs:950`). The `invIndex`
guard on `ListIds` is the sharpest conditional edge in the codebase — worth
copying elsewhere.

### 4.6 Accounts / sessions

- `AccountsBackend.OnUpdate` etc. → `Get(userId)`, `GetIdByUserIdentity`, `GetIdByAlias`, `ListSessions`. Rare (real profile edits).
- `SessionsBackend.OnUpsert` (`SessionsBackend.cs:32`) → `Get(session)`, `AccountsBackend.ListSessions`. Fires on sign-in/out **and** on the hourly `LastSeenAt` refresh from `UserPresences.OnCheckIn` (`UserPresences.cs:68`, `SessionUpdatePeriod` = 1 h).

### 4.7 Live sessions / calls — `LiveSessionsBackend` (`LiveSessionsBackend.cs:1084-1120`)

Three explicit invalidators: `InvalidateState`, `InvalidateGet`,
`InvalidateListParticipants` / `InvalidateHasRecorder`, called from ~15 sites. On top
of that, four compute methods **self-invalidate on a timer** (`GetState`,
`GetConsolidatedParticipants`, `GetConsolidatedHasRecorder`, `GetCallState`) so stale
state heals without an explicit signal. During an active call this is a continuous
background invalidation source, per chat, independent of user activity — but most of
it now stops at the consolidating layer (§10) instead of reaching the conversation
metadata cache. `InvalidateLiveView` is gone: `GetVisibleStartLid` /
`GetLiveConversation` derive from `GetState` again, so they invalidate transitively.

### 4.8 Other roots

| Service | Root(s) | Trigger |
|---|---|---|
| `ConversationsBackend` | `Get`, `GetRangeMeta` × covering + prev + next tiles | summarization flows |
| `MentionsBackend` | `GetLast(chatId, mentionRef)` for changed mentions only | message with mentions |
| `ReactionsBackend` | `List(entryId)`, `Get(entryId, authorId)` | reaction added/removed |
| `NotificationsBackend` | `GetUserNotificationInfo(userId)` via `ApplyHardUpdate`; `ListDevices`; `GetExplicit` | notify / handle / device registration |
| `RolesBackend` | `Get(chatId, roleId)` + `PseudoList(chatId)` | role edits (rare) |
| `ChatsBackend.OnChange` | `Get(chatId)`, `GetTemplatedChatFor`, `GetPublicChatIdsFor`, `ListPlaceChatIds` | chat property edits |

**Pseudo methods** (`PseudoList`, `PseudoPlaceContact`, `PseudoChatContact`,
`PseudoGetAll`) are deliberate coarse hubs: a single invalidation nukes an entire
family of queries. They're correct for bulk operations (`RemoveChatContacts`) but
are the widest edges in the graph — never invalidate one on a hot path.

---

## 5. Hub nodes and their blast radius

Downstream counts are distinct compute methods reachable by transitive
invalidation, from static analysis of the whole `src/dotnet` tree.

| Hub | Downstream | Why it's a hub |
|---|---|---|
| `ChatsBackend.Get(chatId)` | **123** | every facade method and every rules computation reads the chat record |
| `AuthorsBackend.GetInternal` | **115** | reached via `Get`/`GetByUserId` from rules, presence, mentions, reactions, locations |
| `ChatsBackend.GetRules(chatId, principalId)` | **110** | `RequireCanRead`/`CanRead` gate on it in nearly every `Chats.*` / `Authors.*` / `Roles.*` / `LiveSessions.*` method |
| `AccountsBackend.Get(userId)` | **109** | feeds `Accounts.GetOwn` → almost every session-scoped method |
| `ContactsBackend.Get` | **88** | `Contacts.GetForChat` → `ChatUI.Get` → the whole chat list |
| `ChatsBackend.GetMaxLid` | **28** | `GetLidRange` → `GetNews` + `GetChatRangeMeta` + `ConversationsBackend.GetRangeMeta` |
| `ContactsBackend.ListIds` | **27** | the chat list's source of truth |
| `ChatsBackend.GetTile` | **23** | `GetNews`, `GetFirstEntryAuthors`, `Chats.GetTile` |
| `ChatPositionsBackend.Get` | **20** | `ChatPositions.GetOwn` + `Chats.IsEntryReadByMentionedUser` → `ChatUI.GetReadEntryLid` |
| `ChatsBackend.GetNews` | **19** | `ChatUI.Get` → all of `ChatListUI` |

Top fan-in (most distinct direct dependents): `Chats.Get` (27),
`Accounts.GetOwn` (21), `Authors.GetOwn` (14), `Chats.GetRules` (10),
`ChatsBackend.Get` (9), `AuthorsBackend.Get` (7), `AccountsBackend.Get` (6),
`MediaBackend.Get` (6), `ChatUI.Get` (6).

### 5.1 The canonical cascade

Nearly every hot invalidation converges on the same tail:

```
<root>
  → ChatsBackend.GetLidRange / GetTile / GetNews
    → Chats.GetFullNews → Chats.GetNews
      → ChatUI.Get                                  ← the client-side aggregator
        → ChatUI.GetState / GetUnreadCount / GetDetectedLanguage
        → ChatListUI.ListUnorderedRaw → ListUnordered → ListAllUnorderedRaw
          → ChatListUI.List / GetCount / GetUnreadChatCount /
            GetUnmutedUnreadChatCount / ListAllUnordered
            → ChatListUI.GetTile / IndexOf          ← re-renders the sidebar
```

`ChatUI.Get` is the choke point on the client: it aggregates contact + news +
mentions + read position + user settings + navbar settings, and everything the
sidebar renders hangs off it. Anything that invalidates `ChatUI.Get` for a chat
re-renders that chat's list item and recomputes every unread counter in the app.

### 5.2 The rules amplifier

```
AuthorsBackend.GetInternal ─┐
AccountsBackend.Get ────────┼→ ChatsBackend.GetRules ─→ Chats.GetRules
ChatsBackend.Get ───────────┤                        ─→ Chats.{GetTile, GetNews, GetIdRange,
RolesBackend.ListAuthorIds ─┘                             GetChatRangeMeta, GetContentPeriods, …}
                                                     ─→ Authors.*, Roles.*, Reactions.*,
                                                        LiveSessions.*, SharedLocations.*, …
```

`GetRules` is recursive for place chats (`GetPlaceChatRules` → `GetRules(rootChatId, …)`,
`ChatsBackend.cs:2282`) and for threads (`GetRules(parentChatId, …)`,
`ChatsBackend.cs:164`). A single root-place author change therefore invalidates
rules for every chat in the place, for that principal.

`AuthorRules` uses reference equality by explicit design
(`Api/Chat/AuthorRules.cs:44`), so consolidating `GetRules` is **not** possible
without changing that — see §9.8.

---

## 6. Conditional edges

"Conditional" here means an edge that exists only for some inputs or some states.
These are what keep the graph from being catastrophically dense, and the techniques
are worth naming because the right fix for a hot invalidation is often *adding one*
rather than consolidating.

**Six mechanisms are in use:**

1. **Payload-guarded invalidation.** The invalidation block reads
   `context.Operation.Items` and only invalidates what actually changed.
   `AuthorsBackend.OnUpsert` invalidates the member lists only when `HasLeft`
   flipped (`AuthorsBackend.cs:125`); `ContactsBackend.OnTouch` invalidates
   `ListIds` only when the contact moved across `MinLoadLimit`
   (`ContactsBackend.cs:441`). Cheapest and most effective technique available.

2. **`Computed.BeginIsolation()` — a dependency that isn't recorded.** Reads inside
   the scope don't register a dependency, so the caller never invalidates on their
   account. `ChatsBackend.GetChatRangeMeta` reads `GetLidRange` in isolation
   (`ChatsBackend.cs:393`) precisely so the warm tail's constant churn doesn't
   invalidate every page-map tile. `ChatUI.GetReadEntryLid` and `ChatUI.IsEmpty` do
   the same. **This silently breaks correctness if the isolated value must be
   fresh** — it's a deliberate staleness trade.

3. **Ordering-sensitive dependency registration.** A cache-hit compute call
   registers its dependency *synchronously, before any await*. `GetChatRangeMeta`
   was recently restructured so the "next tile" tasks are only started once the
   previous side hasn't already satisfied the request (`ChatsBackend.cs:452-461`) —
   otherwise the result depended on the warm tail tile, which invalidates on every
   message. This class of bug is invisible in a static graph; it needs a read of
   the actual control flow.

4. **Tile granularity.** `InvalidateTiles` invalidates only the smallest tile and
   only the `includeRemoved: true` variant; everything larger is a pure composition
   and invalidates through the graph. Similarly `GetEntryRangeMeta` for neighbour
   tiles is only invalidated when the neighbour lies outside the entry's own tile.

5. **Write-side collapse.** `SetDelayBy(…, "ReadPosChanged:{user}:{chat}")`
   (`ChatPositionsBackend.cs:86`) makes N read advances in a window produce one
   event; `Constants.Contacts.MinTouchInterval` skips the touch write entirely.

6. **Branch-conditional reads.** Ordinary `if`/`switch` in a compute method's body
   means the dependency only exists on the taken branch. E.g. `ChatUI.Get` depends
   on `Chats.Get(threadChatId)` + `ChatThreads.GetThreadCreator` **only** when the
   last entry is a thread start, and on `LocationUI.IsLive` **only** when it has a
   location (`ChatUI.cs:245-262`). `ChatsBackend.GetRules` takes one of four
   disjoint paths by chat kind. These edges are real but data-dependent, and a
   static map necessarily over-approximates them.

---

## 7. Hot-path narratives

### 7.1 A message is posted (or a voice utterance is transcribed)

```
ChatsBackend.OnChangeEntry(Create)
├─ GetTile(chat, smallestTile, true)   → GetTile(…,false) → GetNews → Chats.GetNews → ChatUI.Get → ChatListUI.*
├─ GetMaxLid(chat, true/false)         → GetLidRange → GetNews (same tail)
│                                                    → GetChatRangeMeta, ConversationsBackend.GetRangeMeta
├─ GetEntryRangeMeta(chat, tile)       → GetChatRangeMeta → Chats.GetChatRangeMeta (chat view page map)
└─ ChatEntryChangedEvent
   ├─ ContactsBackend.OnTouch (throttled) → Contact.Get [+ ListIds if it moved]
   ├─ MentionsBackend                     → GetLast(chat, mention)  → ChatUI.Get for mentioned users
   ├─ NotificationsBackend                → GetUserNotificationInfo(user) per recipient
   ├─ ChatUsagesBackend, SearchBackend, LinkPreviewsBackend, ChatEntryLanguagesBackend
   └─ flows: ChatEntryFixupFlow, ConversationSplitFlow, LiveConversationSummaryFlow
```

Amplification: **per chat member currently subscribed**. In a 500-member place chat
every message re-renders the sidebar item and recomputes unread counts for every
online member. This is inherent — the content genuinely changed — so the fix is
*batching* (`InvalidationDelay`, already applied on the `ChatListUI` counters), not
consolidation.

### 7.2 A user scrolls (read position advances)

```
ChatPositions.OnSet → ChatPositionsBackend.OnSet
├─ [hasChanges] ChatPositionsBackend.Get(user, chat, Read)
│   ├─ ChatPositions.GetOwn → ChatUI.GetReadEntryLid → ChatUI.Get → ChatListUI.* (all counters)
│   └─ Chats.IsEntryReadByMentionedUser(entry, mention)   ← per rendered entry with a mention
├─ [MightUpdateStat] ChatsBackend.GetReadPositionsStat(chat) → Chats.GetReadPositionsStat (seen-by ticks)
└─ ReadPositionChangedEvent (collapsed per user/chat/window) → NotificationsBackend
```

This fires continuously while scrolling. `ChatUI.GetReadEntryLid` returns a `long`
and `IsEntryReadByMentionedUser` returns a monotone `bool` — both are prime
consolidation targets (§9.3, §9.5).

### 7.3 A user checks in (presence) — the highest-frequency no-op cascade

```
AppPresenceReporter (every 45 s while active)
└─ UserPresences.OnCheckIn → UserPresencesBackend.OnCheckIn
   └─ UserPresencesBackend.GetLastCheckIn(user)          ← Moment, changes EVERY time
      └─ UserPresences.GetLastCheckIn(user)              ← no consolidation: the value really changed
         ├─ Authors.GetLastCheckIn(session, chat, author)
         └─ UserPresences.Get(user)  → Presence          ← almost ALWAYS unchanged (Online → Online); consolidated here
            ├─ Authors.GetPresence(session, chat, author)   ← subscribed per rendered author badge
            └─ ChatUI.GetState(chat)                        ← per open chat
```

`Authors.GetPresence` is keyed by `(session, chatId, authorId)`, so the same author's
presence is recomputed once per viewing session.

**What production actually shows (§11.4) — this corrects the obvious reading.**
Presence is indeed ~26 % of tracked server invalidations at peak, but it is
overwhelmingly driven by the **self-invalidation timers inside `UserPresences.Get`
itself**, not by check-ins:

| Origin | Peak count | Branch |
|---|---|---|
| `Get @ UserPresences.cs:38` | 4,912 | Online → schedules the Away boundary |
| `Get @ UserPresences.cs:34` | 1,712 | Away → schedules the Offline boundary |
| `OnCheckIn @ UserPresencesBackend.cs:63` | 408 | actual check-ins |

Those timers are scheduled to fire *exactly at* the Online→Away and Away→Offline
boundaries, so when they fire the recomputed `Presence` has genuinely changed. A
`ConsolidationDelay` on `Get` cannot suppress them. It suppresses only the
check-in-driven branch — ~6 % of presence invalidations, ~1.6 % of all of them.

So the shape of the problem is the opposite of what the code reads like: presence
churn is mostly *legitimate state transitions*, and the no-op component is small.
See §9.4 for what this does and doesn't justify.

### 7.4 A live call is in progress

`LiveSessionsBackend.GetState` / `GetConsolidatedParticipants` /
`GetConsolidatedHasRecorder` / `GetCallState` each self-invalidate on a
`SelfHealDelay` timer while the call is active, and are additionally invalidated by
~15 explicit call sites. Downstream: `LiveSessions.*` → `LiveSessionUI.*` → the call
UI, per participant. Both the participant list and the recorder flag consolidate
correctly now (§10); `GetCallState` still doesn't, and `GetState`'s own churn is
absorbed by the consolidating projections rather than by `GetState` itself.

### 7.5 A session's `LastSeenAt` is refreshed (hourly)

```
UserPresences.OnCheckIn → SessionsBackend_Upsert → SessionsBackend.Get(session)
└─ Accounts.GetOwn(session)   ← ConsolidationDelay = 0.01 stops it here ✅
```

`Accounts.GetOwn` is a pass-through of `AccountsBackend.Get(userId)`, which was not
invalidated, so the recompute returns the *same `AccountFull` reference* and
reference equality holds. This is the pattern that works, and the reason the 0.01
delay is enough — it exists to let the recompute resolve, not to batch anything.
`Accounts.GetOwn` has fan-in 21 and sits above ~109 downstream methods, so this one
attribute is doing a lot of work.

---

## 8. Where invalidations hurt most

Ranked by **measured** share of server-side invalidations at peak (§11.4), not by
graph depth. The three headline items were not on the a-priori list at all.

| # | Invalidation | Peak share | Character | No-op rate | Verdict |
|---|---|---|---|---|---|
| 1 | `StartAutoInvalidation @ Computed.cs:425` → `SessionsBackend.Get` (6,168) + `Accounts.GetOwn` (4,880) | **44.0 %** | error-retry spin on cached errors | 100 % — pure waste | §9.1, **fix first** |
| 2 | `Prime @ VersionedComputeMethodPrimer.cs:39` → `*FlowBackend.TryGetData` | **22.5 %** | deliberate invalidate-then-refill on flow writes | by design | §9.2 note |
| 3 | `Get @ UserPresences.cs:38` (19.5 %) / `:34` (6.8 %) → `Authors.GetPresence`, `UserPresences.Get` | **26.3 %** | self-heal timers at the Away/Offline boundaries | low — the value really changes | §9.4, limited upside |
| 4 | `MutableState<T>.Value = …` → `MutableState<Double>` | 3.0 % | server-side metric state | n/a | ignore |
| 5 | `OnCheckIn @ UserPresencesBackend.cs:63` | 1.6 % | real presence check-ins | **high** | §9.4 |
| 6 | `<FusionRpc>.Invalidate` | 1.1 % | cross-pod delivery of the above | n/a | follows from 3 & 5 |
| 7 | everything data-driven: `SessionsBackend_Upsert` (0.5 %), `ChatPositionsBackend_Set` (0.3 %), `ContactsBackend_Touch` (0.2 %), `LinkPreviewsBackend_Change` (0.1 %), **`ChatsBackend_ChangeEntry` (0.1 %)**, `LiveSessionsBackend.Invalidate*`, `ServerKvasBackend_SetMany`, `AuthorsBackend_Upsert`, `NotificationsBackend.ApplyHardUpdate` | **≈ 1.3 % combined** | genuine content changes | low | leave alone |

`ChatsBackend_ChangeEntry` — the write this document spends the most space on — is
**0.1 % of peak server invalidations** (24 events; 56 and 0.3 % off-peak). Chat-entry,
contact, author, role and read-position invalidations together are ~1 % of the total.
They are correctly built (§6: tile granularity, payload guards, write-side collapse)
and they are not the problem.

### 8.1 A separate problem class: retention, not invalidation

The reads report surfaces something the invalidation report can't — and it is the
largest single finding in this document.

| Category | Peak reads | Hit ratio | In the invalidation report? | `MinCacheDuration` set? |
|---|---:|---:|---|---|
| `MediaBackend.Get` | **97,528** | **0.0 %** | no | no |
| `ChatsBackend.GetTile` | **48,512** | 0.9 % | 24 events (0.1 %) | no |
| `*FlowBackend.TryGetData` | 20,912 | 0.9 % | yes (priming, #2 above) | n/a — remote, 1 min via `ClientDefault` |
| `AccountsBackend.Get` | 7,680 | 10.3 % | no | no |
| `ServerKvasBackend.Get` | 6,960 | 0.3 % | no | no |
| `AvatarsBackend.Get` | 6,408 | **0.0 %** | no | no |
| `SessionsBackend.Get` | 5,712 | 1.1 % | yes (error spin, #1 above) | yes, 10 s |
| `AuthorsBackend.Get` | 4,112 | **92.2 %** | no | no — held alive from above |
| `ChatsBackend.GetEntryAttachments` | 1,496 | 0.0 % | no | no |
| `ChatsBackend.Get` | 1,112 | 81.3 % | no | no |

A category with many reads, ~0 % hits, and **no** invalidations is not being
invalidated — it is not being *retained*. `ComputedOptions.Default.MinCacheDuration`
is `TimeSpan.Zero`, so a computed without an explicit `MinCacheDuration` is never
added to `Timeouts.KeepAlive` (`Computed.RenewTimeouts`, `Computed.cs:457-464`) and
survives only while something holds a strong reference to it. The public API contracts
under `Api.Contracts/` set it consistently; the **backend** contracts almost never do.

`MediaBackend.Get` is the extreme case: ~97.5 k reads with a **0.0 %** hit rate and
essentially no invalidations — every one of those reads is a DB round-trip that a
one-line attribute would have served from RAM. It is read from `ChatsBackend.Get`
(chat picture), from `ChatsBackend.GetTile`'s attachment and audio resolution, and
from avatar resolution, which is why its volume is an order of magnitude above
everything else.

`AuthorsBackend.Get` at 92.2 % with no `MinCacheDuration` either is the control that
proves the mechanism: it stays resident because the retained `IAuthors.*` computeds
above it hold a strong reference. Retention is inherited, and these methods aren't
inheriting it.

This costs far more than every invalidation issue in this document combined, and it
is a one-line-per-method fix — §9.2.

---

## 9. Consolidation recommendations

Ordered by measured value. §9.1 and §9.2 are **not** consolidation work — the
measurements say they dominate, so they come first. Consolidation proper starts at
§9.3.

### 9.1 Stop the error-retry spin on `SessionsBackend.Get` / `Accounts.GetOwn`

**44.0 % of all server-side invalidations at peak, and 73.7 % off-peak.**
Pure waste: nothing changed, an error was re-tried.

`StartAutoInvalidation @ Computed.cs:425` is the **error** branch of
`Computed.StartAutoInvalidation` (verified: in the Fusion revision deployed as
14.1.47, `this.Invalidate(timeout)` in the error branch sits at line 425; the success
branch is at 405 and is unreachable here because neither method sets
`AutoInvalidationDelay`). So these computeds are holding exceptions and re-invalidating
on the error horizon — `TransientErrorInvalidationDelay` = 1 s or
`NonTransientErrorInvalidationDelay` = 30 s — indefinitely, because the input that
causes the error never changes.

Two plausible sources, both reachable with ordinary client traffic:

- `SessionsBackend.Get` opens with `session.RequireValid()` (`Users.Service/SessionsBackend.cs:123`) — throws for a malformed or absent session id;
- `Accounts.GetOwn` ends with `Backend.Get(userId).Require()` (`Users.Service/Accounts.cs:41`) — throws `NotFound` when the resolved user (including a guest id) has no account row.

Both produce a *permanent* error for a given key, so the retry can never succeed.

**Diagnose first, then pick the fix** — the logs show the invalidation but not the
exception. Add a one-off `Log.LogWarning` on the throwing paths (or capture
`Computed.Error` for these two categories) on one pod for a few minutes.

Then, in order of preference:

1. **Return a value instead of throwing** for the expected-invalid cases. An invalid
   session is a normal client state, not an exception: `Get` returning `null` makes
   the computed cacheable and the spin disappears. Same for `GetOwn` on a
   guest-without-account — return the guest account rather than `Require()`-ing.
2. If the throw must stay, set `NonTransientErrorInvalidationDelay` high (or
   `TimeSpan.MaxValue`) on these two methods so a permanent error is cached rather
   than retried. `ComputedOptions.MutableStateDefault` already uses `MaxValue` for
   exactly this reason.
3. Reject invalid sessions at the RPC boundary so the compute method is never reached.

Expect this single fix to remove roughly a third of peak and four fifths of off-peak
server invalidations, plus the corresponding DB load.

### 9.2 Add `MinCacheDuration` to the hot backend contracts

Not an invalidation fix, but the largest measured win available and the cheapest to
apply. From §8.1, in descending order of measured waste:

```csharp
// src/dotnet/Media.Contracts/IMediaBackend.cs:10  — 97,528 reads, 0.0 % hits
[ComputeMethod(MinCacheDuration = 300)]
Task<Media?> Get(MediaId? mediaId, CancellationToken cancellationToken);

// src/dotnet/Chat.Contracts/IChatsBackend.cs:27   — 48,512 reads, 0.9 % hits
[ComputeMethod(MinCacheDuration = 60)]
Task<ChatTile> GetTile(ChatId chatId, Range<long> lidTileRange, bool includeRemoved, CancellationToken cancellationToken);

// src/dotnet/Chat.Contracts/IChatsBackend.cs:120  — 1,496 reads, 0.0 % hits
[ComputeMethod(MinCacheDuration = 60)]
Task<ChatEntryAttachment[]> GetEntryAttachments(ChatEntryId entryId, CancellationToken cancellationToken);

// src/dotnet/Users.Contracts/IAvatarsBackend.cs:10     — 6,408 reads, 0.0 % hits
// src/dotnet/Users.Contracts/IServerKvasBackend.cs:10  — 6,960 reads, 0.3 % hits
// src/dotnet/Users.Contracts/IAccountsBackend.cs:10    — 7,680 reads, 10.3 % hits
[ComputeMethod(MinCacheDuration = 60)]
```

All of these are invalidated correctly and rarely (§4), so a residency floor is safe —
it changes only how long an *already-consistent* value stays in RAM, never how stale a
served value can be. Match the `Api.Contracts/` convention: 60 s for stable entities,
10 s for volatile ones. `MediaBackend.Get` can take longer (300 s) — media records are
effectively immutable once uploaded.

`ChatsBackend.GetTile` deserves a moment's thought before you set it, because tiles
are the one entry here that *is* invalidated by normal traffic. But only the tail tile
of an active chat is (§4.1), and the measurement bears that out: 48 k reads against 24
invalidations. Historical tiles are immutable and are being re-queried from the DB for
no reason. Size the value against memory — a tile is much larger than a `Media` — and
consider 30 s if 60 s proves too hungry.

**Sequencing matters.** `MinCacheDuration` also keeps **errored** computeds resident,
so apply §9.1 first or in the same change — otherwise you retain the spin instead of
ending it. `SessionsBackend.Get` already has `MinCacheDuration = 10` and still shows
1.1 % hits, which is exactly what retaining an error looks like.

**Not this one:** `*FlowBackend.TryGetData` (20,912 reads, 0.9 % hits) is a *remote*
replica — the leading `*` marks `RemoteComputeMethodFunction` (§1.3), so it already
gets `MinCacheDuration` = 1 min from `ComputedOptions.ClientDefault`. Its low hit rate
is caused by `VersionedComputeMethodPrimer` invalidating it 5,664 times per window,
not by retention. The primer's invalidate-then-refill is deliberate
(`Core.Server/Priming/VersionedComputeMethodPrimer.cs:38-43`), but at 0.9 % hits
almost no read is served from cache between primes. Worth confirming the primed value
is actually picked up by the recompute rather than every reader re-querying.

### 9.3 `ChatUI.GetReadEntryLid` — add `ConsolidationDelay = 0.5`

```csharp
// src/dotnet/UI.Blazor.App/Services/ChatUI.cs:217
[ComputeMethod(ConsolidationDelay = 0.5)]
public virtual async Task<long> GetReadEntryLid(ChatId chatId, CancellationToken cancellationToken)
```

- **Equality:** `long`. ✅
- **Effect:** the method returns `max(leased local position, server position)`. The
  client's local lease usually already holds the newer value, so the server-side
  invalidation arriving a moment later recomputes to the **same** `long` and stops
  before `ChatUI.Get`. This kills the "my own scroll invalidates my own sidebar"
  loop.
- **Client-local, so consolidation applies** (§2.2).
- **Risk:** unread counts lag by ≤ 0.5 s. `ChatListUI`'s counters already carry
  `InvalidationDelay = 0.6`, so this is within existing tolerances.

### 9.4 `UserPresences.Get` — small win; remove the `GetLastCheckIn` one regardless

> **Applied.** `ConsolidationDelay = 0.5` now sits on `Get` and is gone from
> `GetLastCheckIn`. Kept at 0.5 s rather than the 1 s suggested below to stay closer
> to the previous latency budget; the attribute-only change is wire-compatible with
> `release/v2.13`.

**Downgraded from the pre-measurement draft.** The a-priori case was that check-ins
drive presence churn and the recomputed `Presence` almost never changes. Production
says presence churn is ~25 % of server invalidations but is dominated by the
self-heal timers, which fire *at* the transition boundaries where the value does
change (§7.3). Consolidation cannot suppress those.

```csharp
// src/dotnet/Api.Contracts/Users/IUserPresences.cs:8
[ComputeMethod(MinCacheDuration = 30, ConsolidationDelay = 1)]
Task<Presence> Get(UserId userId, CancellationToken cancellationToken);
```

- **Equality:** `Presence` is an enum → value equality. ✅
- **Effect:** suppresses the **check-in-driven** branch only — the 45 s check-in
  recomputes `Get`, sees `Online == Online`, and stops before `Authors.GetPresence`.
  Measured at ~6 % of presence invalidations and ~1.6 % of all of them. Real, but a
  rounding error next to §9.1 and §9.2. Do it when convenient, not first.
- **Interaction with self-invalidation:** correct but unhelpful here — the timer
  lives on the *source* computed (the target has `AutoInvalidationDelay` forced to
  `MaxValue`, `ComputeMethodDef.cs:42`), so it fires, the source recomputes, the
  value differs, and the target propagates as it should.
- **Risk:** transitions lag by up to `ConsolidationDelay`; `PresenceChangeDelay`
  already pads them by 0.25 s. Start at 1 s.

**Independently, remove `ConsolidationDelay = 0.5` from `GetLastCheckIn`**
(`Api.Contracts/Users/IUserPresences.cs:10`). Its `ApiNullable8<Moment>` value changes
on every check-in by construction, so it can never suppress; it just adds a computed,
a recompute and 0.5 s of latency per check-in per user. This holds regardless of what
you decide about `Get`.

If you want a real reduction in presence cost, the lever is **cardinality, not
consolidation**: `Authors.GetPresence` is keyed by `(session, chatId, authorId)`, so
the same author's presence is computed once per viewing session. Re-keying it to
`(chatId, authorId)` with the permission check factored out would collapse those.
Measured server-side fan-out is only ≈ 1.24×, so size this against a peak
measurement before committing to the refactor.

### 9.5 `Chats.IsEntryReadByMentionedUser` — add `ConsolidationDelay = 1`

```csharp
// src/dotnet/Chat.Service/Chats.cs:264
[ComputeMethod(ConsolidationDelay = 1)]   // declare on IChats
Task<bool> IsEntryReadByMentionedUser(Session session, ChatEntryId chatEntryId, MentionRef mentionId, CancellationToken cancellationToken);
```

- **Equality:** `bool`. ✅
- **Effect:** the value is monotone — `readPosition.EntryLid >= chatEntry.LocalId`
  goes false→true exactly once and never back. Every read-position advance by the
  mentioned user currently invalidates this for **every rendered entry that mentions
  them**, and the answer changes at most once. The code already carries a
  `// TODO: Do not track dependency after resulting to true` at
  `Chats.cs:292` — consolidation is the cheap version of that TODO.
- **Risk:** the ✓ appears ≤ 1 s later.

### 9.6 `ChatUI.GetUnreadCount` and the `ChatVideoUI` / `LiveStreamUI` / `TranslationUI` predicate family

All return `bool` / `int` / `Trimmed<int>` / enums, all sit downstream of hot roots,
and all are client-local. Candidates in descending order of dependents:

| Method | Returns | Site |
|---|---|---|
| `ChatVideoUI.IsVideoAvailable` | `bool` | `ChatVideoUI.cs:79` |
| `LocationUI.IsLive` | `bool` | `Location/LocationUI.cs:82` |
| `ChatVideoUI.IsOwnCameraRecording` / `IsOwnScreenCasting` | `bool` | `ChatVideoUI.Recording.cs:34,44` |
| `LiveStreamUI.IsAnyoneStreaming` / `IsAuthorStreaming` | `bool` | `LiveStreamUI.cs:24,31` |
| `ChatVideoUI.GetVideoStreamMemberCount` / `LiveVideoStreams.GetMemberCount` | `int` | `ChatVideoUI.Playback.cs:19`, `LiveVideoStreams.cs:65` |
| `TranslationUI.MustTranslate` / `NeedsTranslation` / `IsEnabled` | `bool(?)` | `TranslationUI/TranslationUI.cs:93,60,30` |
| `ChatUI.GetUnreadCount` | `Trimmed<int>` | `ChatUI.cs:267` |
| `LiveSessionUI.IsTranscriptionOn` / `GetCallStatus` | `bool` / `CallStatus` | `LiveSessionUI.cs:48,86` |

These are individually small but collectively broad: they're the leaves the Blazor
components actually subscribe to, so suppressing here prevents re-renders even when
an upstream invalidation was unavoidable. Apply `ConsolidationDelay` in the 0.2–0.5 s
range and measure. Treat this as a batch after §9.3 and §9.5 land.

### 9.7 `LiveSessionsBackend.GetState` — blocked on equality; consolidate its consumers instead

`GetState` self-invalidates on `SelfHealDelay` (`LiveSessionsBackend.cs:138`) and is
invalidated by most call mutations, yet during a stable call the state is unchanged.
It looks like the ideal target — but it isn't, for two compounding reasons:

- `LiveSessionState` is a record whose `AuthorIds` member is `IReadOnlyList<AuthorId>`
  (`Api/Live/LiveSessionState.cs:15`), so the synthesized `Equals` compares that list
  **by reference**;
- every compute deserializes a fresh instance from Redis (`SafeGet`, `LiveSessionsBackend.cs:105`).

The two together guarantee the outputs never compare equal under the *default*
comparer, so a bare `ConsolidationDelay` here would be a pure delay — the failure mode
`ListParticipants` used to have (§10). Since Fusion 14.1.71 a third option exists:
supply a `ConsolidationComparer`, which is how `GetConsolidatedLiveConversation`
consolidates a rebuilt `Conversation`.

Options, in order of preference:

1. Consolidate the **scalar consumers** instead — `LiveSessionUI.IsTranscriptionOn`
   (`bool`), `LiveSessions.GetCallStatus` / `LiveSessionUI.GetCallStatus`
   (`CallStatus` enum), `LiveSessionUI.GetState`'s derived predicates. Same effect
   for the UI, no type surgery.
2. Give `LiveSessionState` value equality (`AuthorIds` → `ApiArray` won't help;
   it needs an explicit sequence-comparing `Equals`), *and* have `GetState` return
   the previous instance when the deserialized value matches.

A custom `FusionDefaultDelegates.ComputedOutputEqualityComparer` would also work but
it's a global settable static — changing it affects every consolidating computed in
the process. Not worth it for one type.

### 9.8 `ChatsBackend.GetRules` — high value, blocked on equality

110 downstream methods make this the most valuable consolidation target by graph
depth. It is currently impossible: `AuthorRules.Equals` is `ReferenceEquals` by
explicit design (`Api/Chat/AuthorRules.cs:44`), and `GetRules` constructs a new
`AuthorRules` on most paths.

Two viable routes, in order of preference:

1. **Consolidate a narrower derived value instead.** Most callers only need
   `CanRead()`. Introduce `[ComputeMethod(ConsolidationDelay = 1)] Task<bool> CanRead(chatId, principalId)`
   and route `RequireCanRead`/`CanRead` through it. A `bool` consolidates perfectly,
   and this converts the widest edge in the graph into one that only fires when
   someone's read access actually changes. This is the highest-leverage structural
   change in this document.
2. Give `AuthorRules` value equality. Riskier — the reference-equality choice is
   deliberate and probably load-bearing for parameter comparison in Blazor
   (`ByValueParameterComparer` is applied selectively elsewhere).

### 9.9 What **not** to consolidate

| Method | Why not |
|---|---|
| `ChatsBackend.GetTile` | `ChatTile` is a plain class (reference equality) **and** the content genuinely changed |
| `ChatsBackend.GetChatRangeMeta` / `GetEntryRangeMeta` | records with `Range<long>[]` members → array reference equality; never suppresses |
| `AuthorsBackend.ListAuthorIds` / `ListUserIds` | return `AuthorId[]` / `UserId[]`; fresh array per compute |
| Anything returning `ApiArray<T>` built with `.ToApiArray()` | reference equality on the backing array — see §10 |
| `ChatsBackend.GetMaxLid` | genuinely changes on every message; use tile granularity instead |
| `Pseudo*` methods | `Task<Unit>`; they exist precisely to force propagation |

---

## 10. Audit of existing `ConsolidationDelay` usages

| Site | Delay | Return type | Works? |
|---|---|---|---|
| `IAccounts.GetOwn` (`Api.Contracts/Users/IAccounts.cs:29`) | 0.01 s | `AccountFull` (ref eq) | **✅ yes** — pass-through of an unchanged `AccountsBackend.Get` reference. Textbook usage; see §7.5 |
| `ChatsBackend.GetCurrentYear` (`Chat.Service/ChatsBackend.ContentItems.cs:151`) | 1 s | `int` | **✅ yes** — the documented intent (only the year-flip propagates) is exactly right |
| `LiveSessionsBackend.GetConsolidatedHasRecorder` | 0.5 s | `bool` | **✅ yes** — *now*. It used to sit on `ILiveSessionsBackend.HasRecorder`, where it was inert: an RPC-exposed method of a `Distributed` service is served by `RemoteComputeMethodFunction`, so §2.2 applied and the `bool`'s value equality never got a chance. Moved to a protected method that the public one derives from |
| `LiveStreamUI.GetLastActivityServerTime` (`UI.Blazor.App/Services/LiveStreamUI.cs:37`) | 0.5 s | `Moment?` | **✅ yes** — struct, value equality. While anyone streams it returns `null`; while idle it returns a cached first-idle `Moment`. Both are stable across recomputes, so the churn from `GetStreamingAuthorIds` is absorbed |
| `IUserPresences.Get` (`Api.Contracts/Users/IUserPresences.cs:10`) | 0.5 s | `Presence` | **✅ yes** — *now*. The consolidation used to sit on `GetLastCheckIn`, where the value moves on every check-in by construction, so it never suppressed anything; `Get` is where the value is stable across check-ins. `GetLastCheckIn`'s own readers (`Authors.GetLastCheckIn` → `AuthorPresenceText`) want each new timestamp anyway |
| `LiveSessionsBackend.GetConsolidatedParticipants` | 0.5 s | `ApiArray<AuthorId>` | **✅ yes** — *now*, via `ConsolidationComparer`. `ApiArray<T>.Equals` compares the backing array by reference (`ApiArray.cs:305`) and the method builds a fresh one via `.ToApiArray()` on every compute, so the default comparer could never suppress |
| `LiveSessionsBackend.GetConsolidatedLiveConversation` | 0 s | `Conversation?` | **✅ yes** — via `ConsolidationComparer`; `Conversation` compares by reference and `ToConversation()` rebuilds it every time |
| `LiveSessionsBackend.GetConsolidatedVisibleStartLid` | 0 s | `long?` | **✅ yes** — value equality, no comparer needed |

### 10.1 What the two former ❌ rows needed

Both are fixed as of Fusion 14.1.71, which added
`ComputeMethodAttribute.ConsolidationComparer` — the `IEqualityComparer<T>` used to
compare consecutive outputs. That removes the need for the workaround this section
used to recommend for `ListParticipants` (returning the previous instance so
reference equality holds), and for the Fusion-wide change it rejected (making
`ApiArray<T>` sequence-comparable). The per-method comparers now live next to the
types they compare: `ApiArrayComparer<T>` (`Core/Comparison/`) and
`ConversationContentComparer` (`Api/Chat/`).

14.1.71 also turned the §2.2 precondition into a startup error for `Distributed`
services, which is what surfaced the `HasRecorder` / `ListParticipants` rows as
inert rather than merely suboptimal.

---

## 11. Measuring it in production

### 11.1 Optional: turn on chain tracking

As established in §1.2, prod runs `InvalidationTrackingMode.OriginOnly`, so the
"Invalidation paths" report is **origin → category**, two levels, with no chain.
To harvest actual chains you need:

```csharp
Invalidation.TrackingMode = InvalidationTrackingMode.WholeChain;
```

set once at startup. This is memory-expensive (the docs say every inconsistent
computed then retains 3–5 more instances), so enable it **temporarily and on one
pod**, gate it behind a `Constants.DebugMode` flag next to
`Constants.DebugMode.ServerFusionMonitor` (`src/dotnet/Api/Constants.DebugMode.cs:15`),
and turn it off after collecting a few reports.

Without it, the queries below still answer "which write sites invalidate the most,
and what do they hit first" — which is enough to rank §8, just not to see the tails.

In practice the two-level report was **sufficient**: the §11.4 findings were all
first-hop facts. Enable `WholeChain` only if you need to attribute a specific deep
cascade, not as a prerequisite.

### 11.2 Queries

```bash
# 1. The invalidation-path trees (multi-line payloads — read them whole)
gcloud logging read \
  'resource.labels.container_name="actual-chat-app"
   AND timestamp>="<START>Z" AND timestamp<="<END>Z"
   AND textPayload:"Invalidation paths"' \
  --project=actual-chat-app-prod \
  --format='value(timestamp, resource.labels.pod_name, textPayload)' \
  --order=asc --limit=20

# 2. Per-category update/invalidation counts (the "+N -M" report)
gcloud logging read \
  'resource.labels.container_name="actual-chat-app"
   AND timestamp>="<START>Z" AND timestamp<="<END>Z"
   AND textPayload:"Updates (+) and invalidations (-)"' \
  --project=actual-chat-app-prod \
  --format='value(timestamp, resource.labels.pod_name, textPayload)' \
  --order=asc --limit=20

# 3. Read counts + cache hit ratio — a low hit ratio on a hot category means
#    invalidation is outrunning reuse
gcloud logging read \
  'resource.labels.container_name="actual-chat-app"
   AND timestamp>="<START>Z" AND timestamp<="<END>Z"
   AND textPayload:"reads -> "' \
  --project=actual-chat-app-prod \
  --format='value(timestamp, resource.labels.pod_name, textPayload)' \
  --order=asc --limit=20
```

Bound both ends of the window (PROD volume is high). Reports appear roughly every
6 minutes per pod; a 30-minute window over a busy period gives ~5 samples per pod.

### 11.3 How to read the output

- All printed counts are already scaled by 8 (`EveryNth(8)`); they're estimates.
- In the registrations report, `+N` is computeds created and `-M` is computeds
  unregistered (≈ invalidated). A category with `-M ≈ +N` and a **low hit ratio**
  in the reads report is churning: it's being recomputed about as often as it's read.
  That is the signature consolidation fixes.
- A category with a high hit ratio and a high `-M` is being invalidated a lot but
  still serving reads from cache — lower priority.
- The invalidation-paths tree is keyed by `file:member:line` of the
  `Invalidation.Begin(...)` / invalidation-block call site, so it maps directly onto
  the roots catalogued in §4.

### 11.4 Findings — measured 2026-07-26/27

Project `actual-chat-app-prod`, container `actual-chat-app`, 2 pods (`…-j94dq`,
`…-2zftq`). **41** report sets in the peak window and **42** off-peak — i.e. 41 and 42
sampled minutes, not 2 hours of wall clock (§1.3 note 4). Counts are the log's own
`EveryNth(8)`-scaled values.

> An earlier pass of this section used `--limit=25` and silently truncated to the
> newest 25 reports. That did not change the shares much, but it badly distorted the
> reads table and produced one wrong claim ("`ChatsBackend_ChangeEntry` does not
> appear at all"). Always set `--limit` above the report count and verify with a
> `wc -l` on a timestamp-only query first.

**Peak — 2026-07-26 15:00–17:00 UTC, 41 samples, 25,128 tracked invalidations**
(≈ 613 per pod-minute, ≈ 10/s per pod)

| Origin | Count | Share |
|---|---:|---:|
| `StartAutoInvalidation @ Computed.cs:425` | 11,048 | **44.0 %** |
| `Prime @ VersionedComputeMethodPrimer.cs:39` | 5,664 | **22.5 %** |
| `Get @ UserPresences.cs:38` | 4,912 | 19.5 % |
| `Get @ UserPresences.cs:34` | 1,712 | 6.8 % |
| `MutableState<T>.Value = …` | 760 | 3.0 % |
| `OnCheckIn @ UserPresencesBackend.cs:63` | 408 | 1.6 % |
| `<FusionRpc>.Invalidate` | 288 | 1.1 % |
| `SessionsBackend_Upsert`'s invalidation pass | 128 | 0.5 % |
| `ChatPositionsBackend_Set`'s invalidation pass | 64 | 0.3 % |
| `ContactsBackend_Touch`'s invalidation pass | 40 | 0.2 % |
| `LinkPreviewsBackend_Change`'s invalidation pass | 32 | 0.1 % |
| **`ChatsBackend_ChangeEntry`'s invalidation pass** | **24** | **0.1 %** |
| `InvalidateHasRecorder @ LiveSessionsBackend.cs:958` | 16 | 0.1 % |
| `ServerKvasBackend_SetMany`, `InvalidateGet @ LiveSessionsBackend.cs:952`, `AutoInvalidate @ Invites.cs:246`, `<Cancellation>` | 8 each | 0.0 % |

Origin → first-hop victim, peak:

```
6,168  StartAutoInvalidation @ Computed.cs:425     -> SessionsBackend.Get
5,664  Prime @ VersionedComputeMethodPrimer.cs:39  -> *FlowBackend.TryGetData
4,880  StartAutoInvalidation @ Computed.cs:425     -> Accounts.GetOwn
2,720  Get @ UserPresences.cs:38                   -> Authors.GetPresence
2,192  Get @ UserPresences.cs:38                   -> UserPresences.Get
  936  Get @ UserPresences.cs:34                   -> UserPresences.Get
  776  Get @ UserPresences.cs:34                   -> Authors.GetPresence
  760  MutableState<T>.Value = ...                 -> MutableState<Double>
  120  OnCheckIn @ UserPresencesBackend.cs:63      -> Authors.GetLastCheckIn
   96  OnCheckIn @ UserPresencesBackend.cs:63      -> Authors.GetPresence
   80  SessionsBackend_Upsert's invalidation pass  -> UserSettings.Get
   32  ChatPositionsBackend_Set's invalidation ... -> NotificationsBackend.GetUserNotificationInfo
```

Note the count granularity: every value is a multiple of 8. A row reading "8" is
**one** sampled event. Treat anything under ~40 as presence/absence only.

**Off-peak — 2026-07-27 00:30–02:30 UTC, 42 samples, 17,320 invalidations**
(≈ 412 per pod-minute). Same shape, more extreme: `StartAutoInvalidation @
Computed.cs:425` **73.7 %**, presence (`:38` + `:34`) 15.2 %, `MutableState` 4.6 %,
priming 1.7 %, `SessionsBackend_Upsert` 1.1 %, `ChatsBackend_ChangeEntry` 0.3 %. The
error spin is load-independent — it tracks the number of bad keys, not traffic — so it
dominates completely when real traffic drops.

**Reads and hit ratios, peak window** — see the full table in §8.1. Headlines:
`MediaBackend.Get` 97,528 reads / **0.0 %** hits; `ChatsBackend.GetTile` 48,512 / 0.9 %;
`AvatarsBackend.Get` 6,408 / **0.0 %**; against `AuthorsBackend.Get` 4,112 / **92.2 %**
as the control.

A single registrations sample makes the spin unmistakable:
`SessionsBackend.Get: +16 -528` — 528 unregistrations against 16 registrations in one
60 s collection, with a 0 % hit rate on 400 reads in the same window.

**What this confirms, and what it overturns:**

| Claim | Verdict |
|---|---|
| §5's hub analysis (`ChatsBackend.Get`, `GetRules`, `AuthorsBackend.GetInternal` are the structural hubs) | not contradicted, but they barely fire — structure ≠ frequency |
| §7.1 "a message posted is the most consequential write" | **overturned for the server** — `ChatsBackend_ChangeEntry` is 0.1 % of peak invalidations. Tile granularity and payload guards (§6) are doing their job |
| §7.2 read positions are a top-3 cost | **overturned for the server** — `ChatPositionsBackend_Set` is 0.3 %. May still hold client-side (§11.5) |
| §7.3 presence is expensive | **confirmed** (26.3 % at peak) — but driven by self-heal timers, not check-ins, which removes most of the consolidation upside |
| §3's "`UserPresences.Get` has the largest fan-out in the system" | **overturned** — measured server-side fan-out to `Authors.GetPresence` is ≈ 1.24× |
| The dominant cost is a data-flow cascade | **overturned** — it's an error-retry spin (§9.1) plus a retention gap (§9.2), neither of which is an invalidation-graph problem |

### 11.5 What these logs cannot tell you

These are `actual-chat-app` **server** pods. Everything in §5.1's canonical cascade
below `Chats.*` — `ChatUI.Get`, `ChatListUI.*`, `ChatVideoUI.*`, `TranslationUI.*` —
runs in the Blazor/MAUI **client** and never appears here. The client-side
recommendations (§9.3, §9.6) are therefore still unmeasured.

To measure them, `FusionMonitor` is already registered on app hosts; start it from
the browser console via `debugUI.StartFusionMonitor()`
(`UI.Blazor/Services/DebugUI/DebugUI.Monitors.cs:12` — the MAUI call site at
`MauiBlazorApp.cs:40` is currently commented out) and read the console output. Note
the client-side preprocessor strips `*.Pseudo*`, `FusionTime.*`, `LiveTime.*` and
`LiveTimeDelta*` categories (`BlazorUICoreModule.cs:210-220`).

---

## 12. Methodology and limits

**How the graph was built.** A static pass over all 646 `[ComputeMethod]` /
`// [ComputeMethod]` declarations under `src/dotnet`, extracting each method body by
brace matching and resolving intra-body calls to other compute methods via the
enclosing type's field/property type map (`IChatsBackend ChatsBackend` → `ChatsBackend`),
with `I`-prefixed interfaces canonicalised onto their implementations. 1 135 edges
resolved, 59 unresolved (mostly `IdTileStack`/`DbEntityResolver` false positives that
share a method name with a compute method). Fluent calls split across lines are
re-joined before resolution.

**What it misses — read these before trusting a number:**

1. **Blazor consumers.** Components consume compute methods through
   `ComputedStateComponent` / `IState`, not through other compute methods. Every UI
   leaf in §5 therefore shows a downstream count of 0 while having many real
   subscribers. Graph depth systematically understates client-side cost.
2. **RPC fan-out.** The static graph is per-process. A server-side computed with N
   subscribed clients costs N invalidation messages; the graph shows 1 edge. This is
   the §3 point and it's why `UserPresences.Get` outranks nodes with 50× its depth.
3. **Branch conditionality.** Section 6 mechanism 6 edges are marked `[cond]` by a
   crude heuristic (the call sits on an `if`/`switch`/ternary line). Treat the marks
   as hints; the narratives in §7 were verified by reading the code.
4. **Keyed cardinality.** Nothing here counts *instances* per method. `GetTile` has
   thousands of live keys per chat; `GetPublicChatIdsFor` has one per place. Only
   production data (§11) gives this.
5. **`Computed.BeginIsolation()` edges are still shown.** The static pass records the
   call; the isolation scope means no dependency is actually registered. Known
   affected sites: `ChatsBackend.GetChatRangeMeta:393`, `ChatUI.GetReadEntryLid:237`,
   `ChatUI.IsEmpty:277`. Treat those edges as absent.
6. **Structure is not frequency.** §11.4 is the cautionary tale: the static graph
   correctly identified the hubs, and the hubs turned out to be almost irrelevant to
   production cost. Rank with measurements; use the graph to understand *why* a
   measured hotspot is expensive and what a fix would break.

**Measurement caveats.** The §11.4 numbers are `EveryNth(8)`-sampled sums over 41 and 42
sampled minutes across two pods (not wall-clock totals — §1.3 note 4), so treat
ratios as sound, absolutes as rates per sampled minute, and any value under ~40 as
presence/absence only. The
attribution of `Computed.cs:425` to the error branch depends on the deployed Fusion
revision (14.1.47): the line moved to 429 in a later commit
(`4642b5cb5`, "IHasRetryDelay"). Re-derive the line number after any Fusion upgrade
before trusting that attribution.

**Keeping this current.** The analysis scripts are throwaway; regenerating takes a
few minutes. The parts that need re-verification when the code moves are the
equality table in §2.1 (a type gaining or losing value equality silently
enables/disables a consolidation) and the roots catalogue in §4.
