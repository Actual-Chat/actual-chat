# Distributed Services: Migrating off Operations-Framework Invalidation

## Goal

Migrate all services that rely on Fusion's Operations Framework (OF) multi-host
invalidation — the `if (Invalidation.IsActive) { ... }` blocks in command
handlers — to the distributed, single-writer-per-shard model already used by
user presence, Flows, and the live audio/video pipelines. After the migration,
no service should depend on cluster-wide operation-log replay for cache
invalidation.

## Why the current model doesn't scale

The classic pattern (used by ~30 `*Backend` services, 195 `Invalidation.IsActive`
occurrences across 61 files) works like this:

1. A command handler writes to the DB via `DbHub.CreateOperationDbContext`,
   which also stores a `DbOperation` row in the same transaction.
2. The originating node re-runs the handler in invalidation mode
   (`Invalidation.IsActive == true`) to invalidate its local computeds.
3. **Every other node** tails the operation log of **every database** and
   replays the invalidation block of **every operation** locally.

The costs grow with cluster size and write rate simultaneously:

- **O(nodes × writes) invalidation work.** Every node processes every write of
  every service, whether or not it holds a computed that depends on it.
- **O(everything) memory per node.** Replay-based invalidation only works if
  the computed being invalidated may exist on any node — so every node
  potentially caches every hot entity in the system.
- **Operation-log churn.** Every write produces an extra `_operations` row,
  and every node keeps a tail-reader per database (poll fallback + notify).
- **A fragile hard contract.** Invalidation blocks must never fail and are
  never retried ([Fusion PartO, "Invariants and Guarantees"]). As the number
  of handlers grows, so does the chance one violates the contract silently.

Backend calls are *already* routed to shard owners today (`ShardScheme` N=12,
Maglev `ShardMap`, `MeshRpcRoute`), so for most keys the command executes on
the node that computed the values it invalidates. What OF replay still covers
is the long tail: computeds left on former shard owners after a rebalance,
computeds created on non-owner nodes (nothing enforces ownership in
`ServiceMode.Server`), and cross-service invalidations like
`SessionsBackend.OnUpsert` invalidating `AccountsBackend.ListSessions`.
The migration replaces that long tail with explicit mechanisms instead of a
cluster-wide broadcast.

## The two patterns in the codebase today

### Classic: `DbServiceBase` + OF (the migration source)

```csharp
public virtual async Task<T> OnChange(Backend_Change command, CancellationToken cancellationToken)
{
    if (Invalidation.IsActive) {
        _ = Get(command.Id, default);       // replayed on EVERY node
        return default!;
    }
    var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
    // ... EF mutations; DbOperation row stored in the same transaction ...
    context.Operation.AddEvent(new SomethingChangedEvent(...));
}
```

### Distributed: single writer per shard (the migration target)

Role models, in increasing order of maturity as a template for DB-backed
services:

| Role model | State | What it demonstrates |
|---|---|---|
| `LiveAudioBackend`, `LiveVideoBackend`, `LiveSessionsBackend` (`src/dotnet/Streaming.Service/Backend/`) | Redis + in-memory, TTL | `ShardComputeService`, `RequireShardOwnership` preamble, primers, self-healing timed invalidation |
| `UserPresencesBackend` (`src/dotnet/Users.Service/UserPresencesBackend.cs`) | PostgreSQL | `ShardedDbServiceBase`, `Operation.MustStore(false)`, local invalidation via completion handler |
| `FlowBackend` (`src/dotnet/Flows.Service/FlowBackend.cs`) | PostgreSQL | The complete blueprint — see below |

`FlowBackend` (`IFlowBackend`, `ServiceMode.Distributed`) is the most complete
role model for DB-backed services because it shows every piece we need:

- **Delegating commands, no invalidation pass.** Commands implement
  `IDelegatingCommand` — the handler runs once, on the shard owner; there is
  no `Invalidation.IsActive` branch at all.
- **`Operation.MustStore(false)` + transactional events.** No `DbOperation`
  row is stored (no cluster replay), yet `Operation.AddEvent` still persists
  events to `_events` in the same transaction — the outbox survives intact.
- **Local invalidation via completion handler + primer.**
  `Operation.AddCompletionHandler` runs after commit and calls
  `VersionedComputeMethodPrimer.Prime(...)`, which invalidates the compute
  method *and* hands it the freshly written value, so the recompute needs no
  DB read. The version guard makes out-of-order completions safe.
- **Optimistic concurrency + reroute-aware retries.** `VersionChecker` on
  `DbFlow.Version`; the retry policy treats `RpcRerouteException` as
  "re-route, don't retry here".
- **Cross-key reads pinned to one shard.** `ListStats(Unit, ...)` /
  `List(Unit, ...)` use a leading `Unit` argument to pin whole-table queries
  to the zero shard.

How coherence works without OF: the write command and every compute method for
a given shard key are routed to the *same* owner node
(`ShardOwner.RequireShardOwnership(key, addDependency: true, ct)` at the top
of each compute method reroutes callers and ties the computed's lifetime to
ownership). Since only one node ever computes values for a key, invalidating
locally on that node reaches every subscriber — replicas on API nodes are
invalidated through the normal Fusion RPC push. On rebalance, the old owner's
computeds invalidate via the ownership dependency, callers get
`RpcRerouteException`, and the new owner recomputes from the DB.

## Approaches considered

### Overall model

1. **Keep OF, optimize it** (batch replay, filter by dependency tracking).
   Rejected: replay-based invalidation fundamentally requires every node to
   process every write; optimizations change constants, not the asymptote.
2. **Replace the operation log with a pub/sub invalidation bus** (Redis/NATS
   broadcast of invalidation messages, everything else unchanged). Rejected:
   still O(nodes × writes) fan-out and every-node caching; loses OF's
   transactional coupling without gaining shard locality; adds a new failure
   mode (lost invalidation messages) to code that must never fail.
3. **Single writer per shard** (the presence/Flows model) — **chosen**. Writes
   and reads for a key converge on one owner; invalidation becomes a local,
   in-process operation; memory partitions across the cluster; the operation
   log's replay role disappears. All the infrastructure (mesh, shard maps,
   routing, ownership) already exists and is battle-tested by presence, Flows,
   and live A/V.
4. **Full in-memory/Redis state** (the live-A/V model) for everything.
   Rejected as a general answer: our services' state is durable by nature.
   Kept as the established pattern for genuinely ephemeral state.

### Events / outbox

`Operation.AddEvent` is used by Chat, Users, Notifications, Contacts, and
Flows; events flow from the `_events` table through `DbEventForwarder`
(`src/dotnet/Db/Internal/DbEventForwarder.cs`) into NATS queues.

1. **Keep the DB event log as the outbox** — **chosen**. Exactly what
   `FlowBackend.OnStore` already does: `MustStore(false)` drops only the
   `DbOperation` row; events stay transactional and at-least-once. Zero new
   infrastructure, zero semantic change for event consumers.
2. **New shard-local outbox** (new table + `ShardWorker` dispatcher).
   Rejected: duplicates `_events` + `DbEventForwarder` for no behavioral gain.
3. **Direct-to-NATS publish in the handler.** Rejected: loses atomicity — a
   crash between commit and publish drops the event.

### State store

1. **PostgreSQL stays authoritative; sharding is compute-ownership only** —
   **chosen**. No data migration; rebalance is trivial (the new owner just
   reads the DB); recovery is free. This is the presence/Flows model.
2. **Move hot ephemeral state to Redis while migrating.** Deferred: a valid
   follow-up for specific services (e.g. check-ins, typing indicators), but
   coupling data-store migration to this effort multiplies risk. The
   `NotificationsBackend` soft buffers already show in-memory shard-local
   state coexisting with a DB — that stays as is.

### Command execution semantics

1. **Delegating commands + optimistic versioning** — **chosen**. Commands
   become `IDelegatingCommand` (single execution on the owner, no invalidation
   pass). Where concurrent writers are possible, use `Version` checks like
   `FlowBackend`; where the write is naturally idempotent (check-ins, upserts),
   nothing extra is needed. Retries after `RpcRerouteException` re-route to
   the new owner; handlers must tolerate at-least-once execution.
2. **Keep the operation log for exactly-once dedup.** Rejected: today's OF
   pipeline is also at-least-once at the edges (completion listeners, queue
   redelivery); per-command dedup keyed on `Operation.Uuid` can be added
   selectively later if a handler truly needs it.

## Design problems and their solutions

### 1. Replacing the invalidation block

Mechanical transformation, per command handler:

```csharp
// Before
if (Invalidation.IsActive) {
    _ = Get(id, default);
    return default!;
}
...
// After (inside the handler, before SaveChanges)
context.Operation.MustStore(false);
context.Operation.AddCompletionHandler(scope => {
    if (scope.IsCommitted != true)
        return Task.CompletedTask;

    using (Invalidation.Begin())
        _ = Get(id, default);
    return Task.CompletedTask;
});
```

For hot read paths, use `VersionedComputeMethodPrimer` /
`LockingComputeMethodPrimer` (`src/dotnet/Core.Server/Priming/`) instead of a
bare `Invalidation.Begin()` so the recompute is fed the just-written value.
A shared helper should wrap the boilerplate (see "New shared abstractions").

### 2. Cross-shard-key invalidation (the hard one)

Some invalidation blocks invalidate compute methods keyed by a *different*
identity than the command's shard key. Example:
`SessionsBackend.OnUpsert` (keyed by `SessionId`) invalidates
`AccountsBackend.ListSessions(userId)` (keyed by `UserId`) — after migration
those computeds live on a different owner node, so a local
`Invalidation.Begin()` can't reach them.

Solution: such invalidations become **operation events** routed to the owning
shard, exactly like today's `ChatChangedEvent` → `ContactsBackend` flow. The
event handler runs on the target shard's owner and invalidates locally. This
adds queue latency (tens of ms) to those specific invalidation paths — an
acceptable trade; anything genuinely latency-critical can instead be
restructured so the computed depends on a compute method of the originating
shard.

Every migrated service needs an audit pass listing its cross-key
invalidations; the audit is part of the per-service migration checklist.

### 3. Cross-service invalidation ordering hazard

If service A (not yet migrated) has an invalidation block that calls a compute
method of service B (already migrated), the OF replay of A's command on
arbitrary nodes invalidates only each node's *replica* of B's computed — the
owner's computed stays stale and immediately re-serves stale data. Therefore:

**Rule: when migrating service B, every other service's invalidation block
that references B's compute methods must be converted (to an event handled on
B's shard, or removed if redundant) in the same change.**

A grep for the migrated service's interface across all `Invalidation.IsActive`
blocks is mandatory in the checklist. In practice most cross-service
references already flow through events, so the audit is expected to find few
cases (Sessions↔Accounts being the known one).

### 4. Compute methods not keyed by the shard key

- **Whole-table / cross-key queries** (`ListStats`-style): add a leading
  `Unit` parameter to pin them to the zero shard, as `IFlowBackend` does.
- **Methods keyed by a secondary identity** (e.g. `ChatsBackend` methods keyed
  by `UserId` while the service shards by `ChatId`): these are effectively a
  second shard scheme inside one service. Options, in order of preference:
  route them by their own key (the `ShardKeyResolvers` registry already
  resolves per-argument-type), and treat writes that affect them as
  cross-shard invalidations (problem 2); or split them into a separate
  service sharded by the right key.

### 5. API facades

Facade services (`Chats`, `Accounts`, `Contacts`, ... — `Invalidation.IsActive`
users with no DbContext) run per-API-node and their invalidation blocks
already execute only locally (their commands are never stored in any operation
log). They keep working unchanged **provided every value they cache derives
from backend compute methods** — then replica invalidation propagates to them
via RPC. The facade audit is therefore cheap: flag any facade computed that
caches state *not* backed by a backend compute method dependency; those are
bugs already (stale on other API nodes today), and the migration surfaces
them.

### 6. Shard rebalance and failure semantics

Inherited from the existing machinery, no new design needed:

- `ShardOwner` acquires a Redis ownership lock, waits `LockToUseDelay` (1s)
  for the previous owner to drain, and only then serves
  (`src/dotnet/Core.Server/Sharding/ShardOwner.cs`).
- In-flight calls on a node losing ownership get `RpcRerouteException`;
  `MeshRpcRoute` reconnects clients to the new owner.
- DB-backed state makes the new owner's warm-up a plain DB read; no state
  transfer exists or is needed.
- Brief unavailability of a shard during rebalance (~1–2s) is accepted — it's
  the same window presence and Flows live with today.

Read-your-writes actually *improves*: once a command returns, the owner — the
only compute source for that key — is already invalidated, whereas OF only
guaranteed the originating node was.

### 7. Service-specific notes

- **`ChatsBackend`** — the biggest and last. Single-writer-per-chat unlocks a
  side benefit: chat-entry local-ID generation (`IDbShardLocalIdGenerator`)
  can become an in-memory counter on the owner instead of a DB-contended
  sequence. The `Unit`-keyed and `UserId`-keyed methods need the problem-4
  treatment.
- **`NotificationsBackend`** — already `ShardedDbServiceBase` with per-shard
  in-memory soft buffers; closest to done conceptually, but has the highest
  single-file handler count (21) and mixes buffered + DB state, so it migrates
  late with care.
- **`InvitesBackend`** — single shard (N=1), 6 occurrences: the ideal pilot.
- **Sessions/auth** — `SessionsBackend` is already custom (Fusion's `IAuth` is
  gone); it migrates like any Users service, sharded by `SessionId`, with the
  `ListSessions` cross-key invalidation converted to an event. High request
  rate makes it one of the biggest wins after Chat.
- **MLSearch/Search, Media** — standard cases, no special concerns identified.

## Reuse

### Existing abstractions to reuse (no new equivalents allowed)

| Abstraction | Path | Role in migration |
|---|---|---|
| `ShardedDbServiceBase<TDbContext>` | `src/dotnet/Core.Server/Sharding/ShardedDbServiceBase.cs` | Base class for all migrated DB-backed services |
| `ShardComputeService` | `src/dotnet/Core.Server/Sharding/ShardComputeService.cs` | Base class for non-DB distributed services |
| `ShardOwner.RequireShardOwnership` | `src/dotnet/Core.Server/Sharding/ShardOwner.cs` | Compute-method preamble: reroute + ownership dependency |
| `ShardWorker` | `src/dotnet/Core.Server/Sharding/ShardWorker.cs` | Owner-only background workers |
| `VersionedComputeMethodPrimer`, `LockingComputeMethodPrimer` | `src/dotnet/Core.Server/Priming/` | Invalidate + feed fresh value to recompute |
| `IHasShardKey<T>`, `IDelegatingCommand`, `IHasNodeRef` | `src/dotnet/Core/Sharding/`, ActualLab | Command routing declarations |
| `ShardKeyResolvers`, `MeshRefResolvers` | `src/dotnet/Core.Server/Sharding/` | Registering shard keys for new id types |
| `BackendServiceAttribute(role, ServiceMode.Distributed)` | `src/dotnet/Core/Attributes/` | Per-interface migration switch |
| `_events` + `DbEventForwarder` + `IQueues` (NATS) | `src/dotnet/Db/Internal/DbEventForwarder.cs`, `src/dotnet/Core.Server/Queues/` | Outbox, unchanged |
| `IMeshLocks`, `MeshWatcher`, `ShardMap` | `src/dotnet/Core.Server/Mesh/` | Ownership arbitration and topology (unchanged) |
| `VersionChecker`, `RetryPolicy` with `RpcRerouteException` filter | ActualLab, `FlowBackend.cs` | Concurrency + retry semantics |
| `ShardRoutingMonitor` | `src/dotnet/Chat.Service/ShardRoutingMonitor.cs` | Routing/invalidation correctness probe |

### New shared components (and where they belong)

All are generic and belong in shared projects, not feature projects:

1. **`OperationExt.AddLocalInvalidation(this Operation, Action)`** — wraps
   `MustStore(false)` + `AddCompletionHandler` + committed-check +
   `Invalidation.Begin()`. Placement: `ActualChat.Core.Server` (alongside
   `Priming/`). Candidate for eventual promotion into `ActualLab.Fusion`
   itself, since nothing in it is ActualChat-specific.
2. **A distributed-service conformance guard** — debug/CI-mode check that a
   `ServiceMode.Distributed` service (a) has no `Invalidation.IsActive`
   usage, (b) calls `RequireShardOwnership` in every `[ComputeMethod]`.
   First iteration: a reflection-based test in `Core.Server` test project
   scanning handler IL/source; a Roslyn analyzer only if that proves too weak.
   Placement: shared test infrastructure.
3. **Generalized `ShardRoutingMonitor`** — parameterize the existing
   Chat-specific probe by shard scheme + probe delegate so every migrated
   service gets a production routing/invalidation canary. Placement:
   `ActualChat.Core.Server` (move from `Chat.Service`).
4. **Multi-node integration test harness** — a test fixture spinning N
   in-process hosts sharing one mesh (Redis) to verify reroute + invalidation
   propagation per service. The Fusion repo's `tests/.../MeshRpc/` infra and
   the `MeshRpc` sample are the reference implementations. Placement:
   `ActualChat.Testing` (server-side).

No missing-abstraction gaps were found beyond these four — the runtime
building blocks all exist already.

## Migration recipe (per service)

1. Add `ServiceMode.Distributed` via `[BackendService]` on the backend
   interface (per-interface override, like `IUserPresencesBackend`); verify
   the shard scheme and every command's `IHasShardKey<T>` key match the
   compute methods' first-argument keys.
2. Convert commands to `IDelegatingCommand`; delete the
   `Invalidation.IsActive` branch; add `AddLocalInvalidation` (or a primer)
   before `SaveChanges`; keep `Operation.AddEvent` calls as they are.
3. Add `RequireShardOwnership(key, addDependency: true, ct)` at the top of
   every compute method; pin cross-key queries to the zero shard with a
   leading `Unit`.
4. Audit: (a) this service's cross-shard-key invalidations → events;
   (b) all *other* services' invalidation blocks referencing this service's
   compute methods → events or removal; (c) facade computeds that cache
   values not derived from backend compute methods.
5. Idempotency check per handler (version checks or natural idempotency).
6. Wire the service into the generalized routing monitor; add/extend the
   multi-node test.
7. Roll out; rollback is reverting the attribute (both patterns coexist
   freely, as presence proves today).

## Phases

| Phase | Services | Rationale |
|---|---|---|
| 0. Infrastructure | `AddLocalInvalidation`, conformance guard, generalized monitor, multi-node harness | Everything after this is repetition of a proven recipe |
| 1. Pilot | `InvitesBackend` (N=1 shard), then `ChatPositionsBackend` | Smallest OF service; then a hot, write-heavy, naturally idempotent one sharded by `UserId` |
| 2. Low-risk breadth | Media (`MediaBackend`, `UploadsBackend`, `LinkPreviewsBackend`, ...), `MLSearch.SearchBackend` | Simple entities, no cross-key invalidation found |
| 3. Contacts | `ContactsBackend`, `ExternalContactsBackend`, `ExternalContactHashesBackend` | Event-driven already; exercises event-based cross-shard invalidation at scale |
| 4. Users | `AccountsBackend`, `AvatarsBackend`, `ServerKvasBackend`, `SessionsBackend`, `ChatUsagesBackend`, remaining Users services | Includes the Sessions↔Accounts cross-key case; big win from session read traffic |
| 5. Chat | `ChatsBackend` + partials, `AuthorsBackend`, `PlacesBackend`, `RolesBackend`, `MentionsBackend`, `ReactionsBackend`, `ConversationsBackend`, ... | The bulk (~71 occurrences) and the highest-value target; by now the recipe and helpers are proven |
| 6. Notifications | `NotificationsBackend` | Highest handler density + hybrid soft-buffer state; benefits from everything learned |
| 7. Cleanup | Disable operation-log tail readers for DBs with no OF services left; keep `_events` + `DbEventForwarder` | The log readers' replay role ends; the outbox role stays |

Each phase lands independently; there is no big-bang cutover anywhere.

## Non-issues

- **Shard count.** N=12 is a count of *logical* shards — the DB is unsharded
  and Maglev maps shards to nodes, so bumping N is a constant change that can
  happen at any point, independently of this migration.

## Open questions

1. **Selective exactly-once.** Do any handlers (payments-like flows, if they
   ever appear) need `Operation.Uuid`-keyed dedup on top of at-least-once?
   None identified today.
2. **Redis follow-ups.** Which DB-backed states are worth moving to Redis+TTL
   afterwards (check-ins, chat positions)? Out of scope here; each is a small
   standalone plan once its service is distributed.
3. **Upstreaming.** `AddLocalInvalidation`, the primers, and the conformance
   guard are Fusion-generic; propose them for `ActualLab.Fusion` once they
   stabilize here.
