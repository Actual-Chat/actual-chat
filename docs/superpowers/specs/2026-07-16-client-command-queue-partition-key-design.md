# Client command queue: partition key + commander-filter integration — design

Date: 2026-07-16 (updated 2026-07-18)
Status: Implemented
Branch: `feat/1xxx-cmd-part-key`
First consumer: read-position (`ChatPositions_Set`)

## Context

From the 2026-06-18 discussion with Alex (`tmp/dialog-yakunin-2026-06-18.md`,
section 2), the "universal command queue" is a background architectural track.
This slice delivers its foundation: a **client-side command queue keyed by a
partition key**, wired into the commander pipeline as a **filter command
handler** (Alex's "single entry point"), so any command marked `IQueuedCommand`
is automatically serialized and coalesced per partition — plus a small
enumeration API over what is still queued. The first consumer is chat
read-position writes.

Out of scope (follow-up specs): server-side dedup in Redis, editing/deleting
unsent *messages*, storing the client command id in the message, a durable
(restart-surviving) queue, and migrating `SendingMessages` onto the queue.

### Design decisions (as built)

- **Integration = a client `[CommandFilter]` handler**, not direct queue calls.
  A command implementing `IQueuedCommand` dispatched through the commander is
  intercepted and routed into its partition lane. Callers just do
  `Commander.Run(cmd)` / `UICommander.Run(cmd)`.
- **Partition key = a `string`** (`ChatId.Value` for chat commands), mirroring
  the Notifications `SimilarityKey` shape (structured, enumerable) rather than
  the hashed-`int` `ShardKey`. It is both the ordering lane and the coalescing
  unit.
- **No `CoalesceKey`.** Coalescing is expressed imperatively in the filter via
  the queue's `Update` transaction (remove the waiting predecessors, add the
  newcomer). The partition is the coalescing unit: at most one waiting command
  per partition.
- **The queue is a synchronous coordinator, not a background worker.** Per
  partition it tracks a "running" flag and a waiting buffer. Execution and retry
  are driven by the filter handler itself (`Commander.Run` in a loop), not by a
  per-lane thread.
- **Re-dispatch via an AsyncLocal pass-through flag** — the filter runs a queued
  command by calling `Commander.Run` again; the re-entrant filter sees the flag
  and runs it inline (`InvokeRemainingHandlers`). Each run is a fresh command
  execution (fresh `CommandContext`/DI scope), so **there is no deferred-context
  problem** and a retry is simply a repeated fresh run.
- **`SendingMessages` and the message editor are NOT touched.**
- **Enumeration is in-session** (in-memory), not durable.

## Reuse

### Existing abstractions reused

- **Commander filter seam** — the `[CommandFilter]` `ICommandHandler<T>` pattern
  used by `CommandTracer` (`ActualLab.CommandR/Diagnostics/CommandTracer.cs`);
  registered via `fusion.Commander.AddHandlers<T>()` (as the built-in filters
  are). Priorities in `CommanderCommandHandlerPriority` (RpcRouting = 800M).
- **`IApiCommand : IDelegatingCommand`** (`Core/Commands/IApiCommand.cs`) — the
  new `IQueuedCommand` sits next to it.
- **`ChatPositions_Set` + `IChatPositions.OnSet`**
  (`Api.Contracts/Users/IChatPositions.cs`) — the command routed through the
  queue; executed by the filter via `Commander.Run` exactly as before.
- **`ChatUI.CreateReadPositionState`** (`ChatUI.cs`) — its existing 1s
  `Debouncer.New<ICommand>` writer keeps calling `Commander.Run(command)`; the
  only change is that `ChatPositions_Set` is now `IQueuedCommand`.
- **Retry taxonomy** — transient = `OperationCanceledException`/`TimeoutException`
  (matches `SendingMessages.Queue.cs:126-135`).
- **Fusion `[ComputeMethod]` + `MutableState`** for the reactive enumeration API.

### New components and placement

- **`IQueuedCommand : ICommand { string PartitionKey }`** → **`ActualChat.Core`**
  (`Core/Commands/IQueuedCommand.cs`), next to `IApiCommand`.
- **`PartitionedCommandQueue<TItem>` + `QueueEdits<TItem>`** → **`ActualChat.Core`**
  (`Core/Messaging/`). Reusable, no UI/server deps.
- **`ClientCommandHandler`** (the filter handler) → `UI.Blazor.App/Services/`.
  Client-only by construction; registered in `BlazorUIAppModule`.
- Read-position enumeration (`GetPendingReadPositionCount`) → `ChatUI`.

## Architecture

### 1. `IQueuedCommand` (Core)

```csharp
public interface IQueuedCommand : ICommand
{
    string PartitionKey { get; }
}
```

### 2. `PartitionedCommandQueue<TItem>` — synchronous coordinator (Core)

Per partition key, a `Lane` holds a `bool running` flag and an ordered
`List<TItem>` of waiting items (guarded by a `Lock`). No background thread.

```csharp
// Apply edits to the partition's waiting items (add/remove/replace via QueueEdits).
// If the lane was idle, mark it running, dequeue the head, and return it to run now;
// otherwise return null (the newcomer waits, coalesced by the caller's edits).
TItem? Update(string partitionKey, Func<IReadOnlyList<TItem>, QueueEdits<TItem>> update);

// Signal the current run finished. Dequeue & return the next waiting item (lane stays
// running), or clear running and return null.
TItem? OnCompleted(string partitionKey);

IReadOnlyList<TItem> GetPending(string partitionKey);  // waiting (excludes the running item)
int GetPendingCount(string partitionKey);
event Action? Changed;                                  // fires on Update / OnCompleted
```

`QueueEdits<TItem>` expresses `Replace`/`Remove`/`Add` by **reference**
identity, preserving positions.

### 3. `ClientCommandHandler` — the client filter handler (UI.Blazor.App)

A `[CommandFilter]` on `IQueuedCommand`, registered in the client commander
above RPC routing:

```csharp
[CommandFilter(Priority = CommanderCommandHandlerPriority.RpcRoutingCommandHandler + 1000)]
public async Task OnCommand(IQueuedCommand command, CommandContext context, CancellationToken ct)
{
    if (IsRunningFromQueue.Value) {                 // re-dispatched from a lane -> run for real
        await context.InvokeRemainingHandlers(ct);
        return;
    }
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token);
    var toRun = _queue.Update(command.PartitionKey, pending => {   // enqueue, coalescing
        var edits = new QueueEdits<IQueuedCommand>();
        foreach (var waiting in pending) edits.Remove(waiting);
        return edits.Add(command);
    });
    while (toRun != null) {                          // lane was idle -> drive it until drained
        await Run(toRun, cts.Token);                 // Commander.Run + transient retry
        toRun = _queue.OnCompleted(command.PartitionKey);
    }
}
```

- **Run pattern:** `Run` sets the `AsyncLocal<bool> IsRunningFromQueue` around
  `Commander.Run(command)`; the re-entrant filter passes through. Transient
  failures retry after a 1s delay; permanent failures drop the command and let
  the lane advance.
- **Acceptance semantics:** if the lane is busy, the newcomer coalesces into the
  single waiting slot and the filter returns immediately (command accepted); the
  in-flight run picks it up via `OnCompleted`. Only the run that found the lane
  idle blocks while driving it (offline: it retries; other dispatches still
  return immediately).
- **Lifetime:** a `_stopCts` (cancelled on `Dispose`, the service is Scoped)
  is linked into the run token, so retries stop when the circuit tears down.

### 4. Enumeration hook (primitive only)

`PartitionedCommandQueue` exposes `GetPending`/`GetPendingCount` and a `Changed`
event; `ClientCommandHandler` re-exposes `GetPendingCount`/`Changed`. These are a
low-cost hook left in place for a future consumer (e.g. a visible "unsent"
counter). **No reactive UI surface is built in this slice** — an earlier
`ChatUI.GetPendingReadPositionCount` compute method (plus its `MutableState`
version + `Changed` subscription) was removed as YAGNI, since read positions are
not shown as an "unsent tail" and it had no consumer.

## First consumer: read-position

- `ChatPositions_Set` implements `IQueuedCommand` with
  `PartitionKey => ChatId.Value` (the computed property is
  `[JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember, MemoryPackIgnore]`
  so no serializer treats it as data).
- `ChatUI.CreateReadPositionState`'s debouncer keeps firing
  `Commander.Run(command, CancellationToken.None)` **unchanged** — the filter now
  intercepts it: per chat at most one waiting read-position command, latest wins,
  retried until it reaches the server. `ChatUI` needs no functional change beyond
  a clarifying comment; `SendingMessages` and the editor are untouched.
- **Win over the old path:** an offline read-position previously fired
  `Commander.Run` fire-and-forget (lost / overwritten); now it is serialized per
  chat, coalesced, and retried until it succeeds on reconnect.

## Registration

```csharp
services.AddScoped<ClientCommandHandler>();
fusion.Commander.AddHandlers<ClientCommandHandler>();
```

(`AddHandlers<T>` registers the filter but not the DI service, hence the
explicit `AddScoped`; `CommanderBuilder.AddService` was avoided because it
requires `ICommandService` and builds an unneeded proxy.)

## Edge cases handled

- **Coalesce vs run:** `Update` decides run-now-vs-wait under the lane lock, so a
  newcomer can never start a second concurrent run for the same partition.
- **Running item is never coalesced:** it is dequeued out of the waiting list
  before it runs; `Update` only sees waiting items.
- **Retry lifetime:** linked to `_stopCts`; on `Dispose` retries stop instead of
  looping forever against a torn-down scope (important because read-position
  dispatches with `CancellationToken.None`).
- **Enumeration cleanup:** waiting count returns to 0 as items promote/complete;
  `Changed` invalidates the compute method.

## Known limitations (accepted for this slice)

- **In-session only.** A pending read-position is lost if the app restarts while
  offline (only an unsent *edit* of an already-committed position — no user
  content lost).
- **`GetPendingCount` excludes the running item.** While a lane is mid-run
  (e.g. retrying offline) the count is 0; it reflects *waiting* commands, not
  "in flight". Fine for read-position (enumeration isn't surfaced).
- **Lanes are not evicted** — one small `Lane` object per partition key lives for
  the circuit's lifetime. Cheap (no thread), unbounded only across very many
  chats; a candidate for idle eviction later.

## Verification

- **Unit** (`PartitionedCommandQueueTest`, `QueueEditsTest`): `Update` returns
  the item to run on an idle lane and null when busy; coalescing collapses a
  burst to one waiting item (latest wins); `OnCompleted` promotes then clears
  running; partitions are independent; `Changed` fires; `QueueEdits`
  replace/remove/add semantics by reference. **9 tests green.**
- **Build:** `UI.Blazor.App` and the full server compile; the server boots with
  `AddHandlers<ClientCommandHandler>()` (registration is validated at startup), and
  a Blazor circuit constructs `ChatUI` without error. `ClientCommandHandler` is now
  instantiated lazily on the first `IQueuedCommand` dispatch (its DI + `ICommander`
  wiring was confirmed to construct cleanly in an earlier run when `ChatUI` still
  resolved it).
- **Manual (pending a foreground browser):** throttle the network, scroll a chat
  to advance the read position repeatedly, observe the pending count stay at ≤1
  and the server position converge to the final value on reconnect. Blocked in
  automation by a background (`document.hidden`) tab where the message list does
  not render; not a code issue.
