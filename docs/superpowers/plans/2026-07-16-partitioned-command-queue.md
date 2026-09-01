# Partitioned Command Queue (read-position) Implementation Plan — as built

> Updated 2026-07-18 to match the implemented **commander-filter** design. See the
> spec: `docs/superpowers/specs/2026-07-16-client-command-queue-partition-key-design.md`.

**Goal:** A client-side, partition-keyed command queue wired into the commander
pipeline as a `[CommandFilter]`, so any `IQueuedCommand` is serialized and
coalesced per partition; first consumer is chat read-position writes.

**Architecture:** `PartitionedCommandQueue<TItem>` (Core) is a synchronous
per-partition coordinator (running flag + single waiting slot, coalesced via
`QueueEdits`). `ClientCommandHandler` (UI.Blazor.App) is the client filter handler:
it enqueues a marked command via `Update`, and when the lane is idle it drives
`Commander.Run` in a loop (transient retry) advancing the lane via `OnCompleted`.
Re-dispatch uses an `AsyncLocal` pass-through flag, so each run is a fresh
`Commander.Run` (no deferred `CommandContext`). Read-position marks
`ChatPositions_Set` as `IQueuedCommand`; `ChatUI` keeps firing `Commander.Run`.

**Tech Stack:** C# / .NET, xUnit + AwesomeAssertions (`tests/Core.UnitTests`),
ActualLab.CommandR (`ICommandHandler`, `[CommandFilter]`, `CommandContext`),
ActualLab.Fusion (`[ComputeMethod]`, `MutableState`).

## Global Constraints

- **Coding style (`docs/CODING_STYLE.md`):** no `Async` suffix; no XML doc
  comments; tests use AAA with lowercase `// arrange` / `// act` / `// assert`.
- **Placement:** `IQueuedCommand`, `PartitionedCommandQueue`, `QueueEdits` in
  `ActualChat.Core` (no UI/server deps); `ClientCommandHandler` in `UI.Blazor.App`.
- **Transient errors:** `OperationCanceledException` or `TimeoutException` retry;
  everything else is permanent.

---

### Task 1: `QueueEdits<TItem>` — done

**Files:** `src/dotnet/Core/Messaging/QueueEdits.cs`, test `tests/Core.UnitTests/QueueEditsTest.cs`.

- [x] `sealed class QueueEdits<TItem> where TItem : class` with fluent
  `Replace(original, replacement)`, `Remove(item)`, `Add(item)` and
  `internal void ApplyTo(List<TItem>)`. Identity is **reference** equality;
  replacements keep position, adds go to the tail.
- [x] 3 tests: replace-keeps-position/remove/add; reference-not-value equality;
  missing target is a no-op.

### Task 2: `PartitionedCommandQueue<TItem>` — synchronous coordinator — done

**Files:** `src/dotnet/Core/Messaging/PartitionedCommandQueue.cs`, test
`tests/Core.UnitTests/PartitionedCommandQueueTest.cs`.

**Produces:**
- `TItem? Update(string partitionKey, Func<IReadOnlyList<TItem>, QueueEdits<TItem>> update)`
  — applies edits under the lane `Lock`; if the lane was idle (`!running`) and
  something is waiting, sets `running`, dequeues the head, returns it; else null.
- `TItem? OnCompleted(string partitionKey)` — dequeues the next waiting item
  (lane stays running) or clears `running` and returns null.
- `IReadOnlyList<TItem> GetPending(string)`, `int GetPendingCount(string)`,
  `event Action? Changed` (fires on Update/OnCompleted).
- Internal `Lane` = `Lock` + `List<TItem> _pending` + `bool _running`. No
  background worker, no executor, no dispose.

- [x] 6 tests: idle→returns item & marks running / busy→null; coalesces a burst
  to one waiting item; `OnCompleted` promotes then clears; partitions
  independent; offline backlog collapses to a single pending then drains;
  `Changed` fires on Update and OnCompleted. **All green.**

### Task 3: `IQueuedCommand` marker — done

**Files:** `src/dotnet/Core/Commands/IQueuedCommand.cs`.

- [x] `public interface IQueuedCommand : ICommand { string PartitionKey { get; } }`
  (namespace `ActualChat`, next to `IApiCommand`). No `CoalesceKey`.

### Task 4: `ClientCommandHandler` — client filter handler + registration — done

**Files:** `src/dotnet/UI.Blazor.App/Services/ClientCommandHandler.cs`;
`src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`.

- [x] `sealed class ClientCommandHandler(ICommander commander) : ICommandHandler<IQueuedCommand>, IDisposable`
  holding a `PartitionedCommandQueue<IQueuedCommand>` and a `_stopCts`.
- [x] `[CommandFilter(Priority = CommanderCommandHandlerPriority.RpcRoutingCommandHandler + 1000)]`
  `OnCommand`: if `IsRunningFromQueue.Value` → `InvokeRemainingHandlers` (run for
  real); else `Update` (remove waiting, add newcomer) → drive the lane with a
  `Run` + `OnCompleted` loop while it yields items. Run token is
  `CreateLinkedTokenSource(ct, _stopCts.Token)`.
- [x] `Run` retries transient failures (1s delay), drops permanent ones;
  `RunOnce` sets `AsyncLocal<bool> IsRunningFromQueue` strictly around
  `commander.Run(command)`.
- [x] `int GetPendingCount(string)`, `event Action? Changed` (delegated to the
  queue), `Dispose()` → `_stopCts.CancelAndDisposeSilently()`.
- [x] Registration in `BlazorUIAppModule.InjectServices`:
  `services.AddScoped<ClientCommandHandler>(); fusion.Commander.AddHandlers<ClientCommandHandler>();`

### Task 5: read-position through the queue — done

**Files:** `src/dotnet/Api.Contracts/Users/IChatPositions.cs`;
`src/dotnet/UI.Blazor.App/Services/ChatUI.cs` (comment only).

- [x] `ChatPositions_Set` implements `IQueuedCommand`:
  `[JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember, MemoryPackIgnore] public string PartitionKey => ChatId.Value;`
  (all four ignore attributes needed — MemoryPack errors without `MemoryPackIgnore`).
- [x] `ChatUI` needs **no functional change**: the read-position debouncer sink
  stays `Commander.Run(command, CancellationToken.None)`; the filter does the
  routing now (only a clarifying comment was added). `SendingMessages` / editor
  untouched.
- [x] **Enumeration removed as YAGNI:** an earlier
  `ChatUI.GetPendingReadPositionCount` compute method + `MutableState<long>`
  version + `ClientCommandHandler.Changed` subscription were dropped (no consumer).
  The primitive's `GetPendingCount`/`Changed` hooks stay for a future consumer.

## Verification status

- [x] Unit: `QueueEditsTest` + `PartitionedCommandQueueTest` — **9/9 green**.
- [x] Build: `UI.Blazor.App` compiles; full server rebuild (server-loop) **Build
  succeeded**, healthz 200.
- [x] Runtime: server boots with the filter registered (startup validates
  `AddHandlers`); a Blazor circuit constructs `ChatUI` with no exception.
  `ClientCommandHandler`'s DI + `ICommander` wiring constructed cleanly in an
  earlier run (when `ChatUI` still resolved it); it is now lazy on first dispatch.
- [ ] Manual E2E (offline coalescing drain in the browser) — blocked by a
  background/`document.hidden` automation tab (message list doesn't render);
  environmental, not a code issue. Run when a foreground Voxt window is available:
  throttle network → scroll to advance read position repeatedly → pending count
  stays ≤1 → server position converges to the final value on reconnect.
