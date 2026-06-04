# Flows Dashboard — Requirements

A minimal, admin-only dashboard to monitor the state of [Flows](../src/dotnet/Core.Server/Flows)
(durable, resumable workflows). Scope is intentionally small: surface failed/stuck flows for
triage and give an at-a-glance system-health overview.

## 1. Goals & primary scenarios

The MVP is defined by two user scenarios:

1. **System health** — an overview: how many flows of each type are active / completed / failed.
2. **Triage** — quickly find failed and stuck flows, open their `Console` log, understand the cause.

Audience: admins only (`AccountFull.IsAdmin`). Not a public feature.

## 2. Surface

A routed Blazor admin page in `UI.Blazor.App`, modelled on `Pages/MeshTestPage.razor`
(`@page "/test/..."`, gated by `<RequireAccount MustBeAdmin="true"/>`, `<MainHeader>`, a manual
**Refresh** button driving `State.Recompute()`). Data is refreshed by **polling** (manual Refresh
and/or a periodic timer), not by Fusion invalidation — see §6. This keeps the backend a plain RPC
query with no invalidation logic.

## 3. Derived flow status

`DbFlow` stores `IsCompleted` (bool), `ResultData`, `Version`, `Console`. There is no explicit
"failed" / "next resume at" / "timestamp" column. Status is therefore **derived**, and an
`IsFailed` column is being added (see §5). Definitions:

| Status | Computed as |
| --- | --- |
| **Completed (OK)** | `IsCompleted == true` and not failed |
| **Failed** | `IsCompleted == true` and the `Result` carries an error (`IsFailed == true`) |
| **Active** | `IsCompleted == false` and `Version` updated recently (within threshold) |
| **Stuck (suspected)** | `IsCompleted == false` and `Version` older than threshold |

Two known traps:

- **`IsCompleted` is true for both success and failure.** A flow that ends via `SetError` is
  "completed". We distinguish failure via the new `IsFailed` column rather than deserializing
  `ResultData` on every read.
- **The resume schedule lives in `DbEvents` / the queue, not in `DbFlow`.** "Stuck" cannot be
  detected exactly; it is approximated from the age of `Version` (clock-based, confirmed) and must
  be labelled "suspected", not asserted.

## 4. Functional requirements

**Level A — aggregates (health):**

- Per registered flow type (`FlowDefs.ByName`, ~18 types): counts of Active / Completed / Failed / Stuck.
- Sort types with Failed/Stuck to the top.

**Level B — instance list (triage), drill-down from a type:**

- Columns: `FlowId`, type, status, `Version` (as "updated at"), last `Console` line.
- Filters: by type, by status (at minimum "problematic only").
- Opening an instance shows the full `Console` log (the ready-made step timeline) plus the error
  text from `Result`.

MVP is **read-only**. Resume/reset actions are explicitly out of MVP (next iteration; the mechanism
exists — `FlowResumeEvent` + `WithReset` — but it mutates production state).

## 5. Backend

### 5.1 `IsFailed` column

- `DbFlow` gains `bool IsFailed` plus a triage index (e.g. `(IsFailed, Version)`).
- `IFlowData` gains a computed `IsFailed`; in `FlowData<TFlow>` it is `flow.UntypedResult?.Error != null`
  when the live flow is present, or derived from `ResultData` when loaded from the DB.
- `DbFlow.UpdateFrom(IFlowData)` sets `IsFailed = flowData.IsFailed`. In `OnStore` the live flow is
  already in memory, so the boolean is computed without any deserialization.
- Column name will be `is_failed` (the context uses `UseSnakeCaseNaming()`).
- A new migration is added under `Flows.Service.Migration` (currently only `Initial` exists).

**Backfill trap:** existing already-failed flows default to `is_failed = false`. Failed flows are
terminal (never resumed) so they are never rewritten and stay unflagged; and they cannot be
distinguished by pure SQL (both success and error completed flows have non-empty `ResultData`).
MVP accepts this (only new failures are flagged). An optional one-time code backfill
(deserialize `ResultData`, set `is_failed`) can follow later.

### 5.2 List query

There is currently **no way to enumerate flows** — `IFlowBackend` only has `TryGetData(flowId)`.
A new query method is the central new backend component:

- New **regular RPC method** (not `[ComputeMethod]`) on `IFlowBackend`, e.g. `List(FlowsQuery query, …)`
  returning a lightweight report — filters by `Name` / completed / failed / "problematic only", a
  row limit, sorted by `Version desc`. Since the UI polls, no reactivity/invalidation is required.
- Reads the `DbFlow` set directly (indexes `(IsCompleted, Version)` / new `(IsFailed, Version)`).
- Returns a light DTO **without** the `Data` blob. The full `Console` for the drill-down reuses the
  existing `TryGetData(flowId)` (which already carries `Console`), so list responses stay small.
- **Sharding:** all shards currently read/write a single shared DB, so **no fan-out is needed** — one
  query sees every flow. `IFlowBackend` is a `Distributed` service sharded by `FlowId`, but the
  `List` call has no `FlowId` key, so it is pinned to a single node (e.g. shard 0) via one RPC call.

## 6. Non-functional requirements

- **Refresh:** polling, not Fusion — manual Refresh button plus an optional periodic timer.
  Simpler than reactive invalidation and sufficient for an admin monitoring view.
- **Performance:** never deserialize `Data`; the list query is pure SQL thanks to `is_failed`; cap row count.
- **Security:** `Console` may contain sensitive data → admin-only; never expose it in public logs/metrics.
- **Access:** `IsAdmin` is checked both in the UI and on the backend method.

## 7. Reuse

**Existing abstractions to reuse:**

- `IFlowBackend` / `FlowBackend` / `FlowsDbContext` / `DbFlow` — extend, do not duplicate.
- `FlowDefs.ByName` — source of the type list for aggregates.
- `FlowData.FromData` + `IFlowData.IsCompleted` / `ResultData` — status/error materialization.
- `FlowConsole.ToString()` / `DbFlow.Console` — ready-made step timeline; nothing new to log.
- `TryGetData(flowId)` — existing method reused for the drill-down `Console`/error view.
- `Pages/MeshTestPage.razor` — page template: routed page, `ComputedStateComponent<,>` shell, manual
  Refresh button (`State.Recompute()`), `<MainHeader>`.
- `<RequireAccount MustBeAdmin="true"/>` — admin gating (preferred over an `IsAdmin` model flag).
- `DbEntityResolver` / EF `Set<DbFlow>()` — data access.

**New components and placement:**

- `FlowsQuery` / `FlowsReport` / `FlowSummary` / `FlowStatus` DTOs — in the shared **`Api`** project
  (`ActualChat.Flows` namespace), next to `MeshDiagInfo`/`AccountFull`. They must be **client-safe**
  (the Blazor WASM/MAUI client cannot reference `Core.Server`, where `FlowId` lives), so ids/names
  are `string`. Both `Core.Server` (backend) and `Api.Contracts` (frontend contract) reference `Api`,
  so no mapping layer is needed.
- The backend `List` method — contract in `IFlowBackend` (`Core.Server`), implementation in `Flows.Service`.
- The admin-gated frontend facade — extend the existing `IDiagnostics` (`Api.Contracts`),
  implemented in `Chat.Service/Diagnostics.cs` (already has `IAccounts` + the `IsAdmin` check and a
  reference to `Core.Server` → `IFlowBackend`). The Blazor client cannot call `IFlowBackend` (a
  backend-only service) directly, so it goes through this facade.
- The Blazor `FlowsTestPage` page — feature-specific, in `UI.Blazor.App` (nothing to reuse; concrete UI).
- Shard pinning — register a constant `ShardKeyResolver<FlowsQuery>` (`_ => 0`) in
  `Core.Server/Sharding/ShardKeyResolvers.cs`, next to the `FlowId` / `FlowResumeEvent` resolvers.

## 8. Resolved decisions

1. **Sharding** — single shared DB → one query, pinned to one node, no fan-out.
2. **Failed classification** — add an `IsFailed` column + migration; backfill of pre-existing
   failures is out of MVP.
3. **Stuck threshold** — a single global threshold (e.g. 6h) plus a "suspected" label; not per-flow
   for MVP.
4. **`VersionGenerator`** — confirmed clock-based, so `Version` age is a valid "updated at" proxy
   (sanity-checked at implementation time).
5. **Refresh** — polling (manual + optional timer) over Fusion reactivity; backend `List` is a plain
   RPC method, no `[ComputeMethod]` / invalidation.
6. **Page template** — `MeshTestPage.razor` (routed admin page + `RequireAccount`), not the
   `DeveloperTools` settings-tile pattern.

## 9. MVP scope

**In:** admin page, per-type aggregates, drill-down list of problematic flows, `Console` + error
view, read-only, one global stuck threshold, `IsFailed` column.

**Out:** resume/reset actions, shard fan-out, trends/history, arbitrary `FlowId` search, export,
backfill of pre-existing failures.
