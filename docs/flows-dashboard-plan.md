# Flows Dashboard — Implementation Plan

Companion to [flows-dashboard-requirements.md](flows-dashboard-requirements.md). Phases are
sequential and each compiles on its own; commit per phase.

Decisions folded in:

- **Console is drill-down only.** List rows carry `FlowId` / type / status / `Version` only — the
  `Console` column (up to 8 KB) is never read in the list query; the full `Console` + error is
  fetched per-instance on drill-down. (Minor deviation from requirements §4, approved.)
- DTOs live in the shared **`Api`** project (client-safe, `string` ids), not `Core.Server`.
- The frontend facade extends the existing admin-gated `IDiagnostics`.
- The `List` backend method is a **plain RPC** (UI polls; no `[ComputeMethod]`/invalidation).
- Shard pinning via a constant `ShardKeyResolver<FlowsQuery>`.

## Phase 0 — Confirmations (before coding)

- **`ClockBasedVersionGenerator` encoding** — confirm `Version ≈ time ticks` so the stuck cutoff is
  `cutoffVersion = (Now - StuckThreshold).EpochOffset.Ticks` (SQL filter). Fallback: compute `stuck`
  in C# over the small not-completed subset.
- **`Chat.Service → Core.Server`** reference — confirm `IFlowBackend` is reachable from
  `Diagnostics.cs` (it is — flows live under `Chat.Service/Flows/*`).

## Phase 1 — `IsFailed` column

1. `Core.Server/Flows/FlowData.cs` — `IFlowData`: add `bool IsFailed { get; }`.
2. `Core.Server/Flows/FlowData.cs` — `FlowData<TFlow>`: computed `IsFailed`
   - live flow: `flow.UntypedResult?.Error is not null`;
   - from data: non-empty `ResultData` whose deserialized `Result` has an error.
3. `Flows.Service/Db/DbFlow.cs` — `bool IsFailed` property, `[Index(nameof(IsFailed), nameof(Version))]`,
   set in `UpdateFrom(IFlowData)` (live flow in memory in `OnStore` → no deserialization).
4. Migration `AddDbFlowIsFailed` under `Flows.Service.Migration` → `is_failed boolean not null default false`
   + `ix_flows_is_failed_version`.

**Verify:** `dotnet build ActualChat.CI.slnf`; run the four `Flows SerializationTests`; inspect migration SQL.

## Phase 2 — Backend query (DTOs + `List` + shard pin)

5. `Api/Flows/` (namespace `ActualChat.Flows`, client-safe records like `MeshDiagInfo`):
   - `FlowStatus.cs` — `{ Active, Completed, Failed, Stuck }`.
   - `FlowSummary.cs` — `string FlowId, string Name, FlowStatus Status, long Version, Moment UpdatedAt`.
   - `FlowTypeStat.cs` — `string Name, int Active, int Completed, int Failed, int Stuck`.
   - `FlowsReport.cs` — `FlowTypeStat[] Aggregates, FlowSummary[] Rows, Moment GeneratedAt`.
   - `FlowsQuery.cs` — `string? Name, bool ProblematicOnly, int Limit`.
6. `Core.Server/Sharding/ShardKeyResolvers.cs` — `Register<FlowsQuery>(static _ => 0)`.
7. `Core.Server/Flows/IFlowBackend.cs` — plain RPC `Task<FlowsReport> List(FlowsQuery query, CancellationToken ct)`.
8. `Flows.Service/FlowBackend.cs` — implement `List`:
   - `DbHub.CreateDbContext(ct)` (read);
   - aggregates per type (`FlowHub.Defs.ByName`), counts of active/completed/failed/stuck (stuck via `cutoffVersion`);
   - rows: `Select` projection into `FlowSummary` **without** `data`/`result_data`/`console`; filter by
     `Name`/`ProblematicOnly`; `ORDER BY version DESC`; `LIMIT`;
   - `StuckThreshold` constant (6h); `UpdatedAt` from `Version`.

**Verify:** integration test in `Core.Server.IntegrationTests/Flows` — seed ok/failed/old-incomplete flows,
assert aggregates/rows/filters/order.

## Phase 3 — Admin-gated frontend facade

9. `Api.Contracts/Chat/IDiagnostics.cs` — add (plain methods, UI polls):
   - `Task<FlowsReport> GetFlowsReport(Session session, FlowsQuery query, CancellationToken ct)`;
   - `Task<FlowDetails?> GetFlowDetails(Session session, string flowId, CancellationToken ct)`;
   - new DTO `FlowDetails` (in `Api`): `FlowSummary Summary, string Console, string? Error`.
10. `Chat.Service/Diagnostics.cs` — admin check (`Accounts.GetOwn` → `IsAdmin` else `Unauthorized`);
    delegate to `IFlowBackend.List` / `TryGetData(FlowId.Parse(flowId))` (extract `Console` + `Result.Error`).
    Inject `IFlowBackend`.

**Verify:** build; call as an admin session.

## Phase 4 — Blazor page (template: `MeshTestPage`)

11. `UI.Blazor.App/Pages/FlowsTestPage.razor`:
    - `@page "/test/flows"`, `<RequireAccount MustBeAdmin="true"/>`, `<MainHeader>Flows</MainHeader>`;
    - `@inherits ComputedStateComponent<AppUIHub, FlowsReport?>`; `ComputeState` → `IDiagnostics.GetFlowsReport`;
    - aggregates table (Failed/Stuck on top), filters (type, problematic-only), rows list, expand row →
      `GetFlowDetails` (Console + error);
    - **Refresh** button → `State.Recompute()`; optional periodic poll.

**Verify:** `dotnet build ActualChat.CI.slnf` (C#/Razor — no `npm run build:Verify`); open `/test/flows`
as admin via `/debug-ui`.

## Phase 5 — Tests & finalize

- Keep the Phase 2 integration test as a regression.
- (Optional) update `docs/api-index*.md` if new public types are tracked there.

## Reuse

Reused: `IFlowBackend`/`FlowBackend`/`DbFlow`/`FlowsDbContext`, `FlowDefs.ByName`,
`FlowConsole`/`DbFlow.Console`, `TryGetData`, `IDiagnostics` + `Diagnostics.cs`, `MeshTestPage`,
`RequireAccount`, `DbHub.CreateDbContext`, `ShardKeyResolvers`.
New: 6 DTOs in `Api`, `List` method, 2 facade methods, 1 razor page, 1 migration, 1 resolver registration.

## Risks / traps

- **Version→time** (Phase 0) — only non-trivial dependency; has a fallback.
- **Aggregate grouping** — by type via `LIKE 'name:%'` per type (~18) or in-memory prefix parse over a
  bounded set; fine for a single shared DB at current volume. Add a denormalized `name` column if it grows.
- **Backfill** of pre-existing failures — out of MVP.

## Commit order

`feat(flows): IsFailed column` → `feat(flows): List backend query` →
`feat(flows): admin diagnostics facade` → `feat(flows): dashboard page`.
