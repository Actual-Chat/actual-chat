# Command queue observability + optimistic effects — design

Date: 2026-08-27
Status: Designed (not implemented)
Branch: `feat/1xxx-cmd-part-key`
Depends on: `2026-07-16-client-command-queue-partition-key-design.md` (the queue itself)
First consumer: reactions (`Reactions_React`)

## Context

The client command queue orders and retries commands per partition key, but it is
invisible: nothing shows what is queued, what is retrying, or what failed, and the
UI still fakes the result of a command on its own. That was the half of Alex's
original "universal queue" idea (2026-06-18, `tmp/dialog-yakunin-2026-06-18.md`
section 2) that never got built — he described it as "a list of commands in
different stages of execution, which can both show what's queued and apply the
effect as if they had already run".

Today the "apply the effect" half exists twice, in two unrelated mechanisms:

- `SendingMessages` — heavy and message-specific: durable outbox, per-chat lanes,
  `SendingMessage` carrying its own status and error, spliced into the message list
  by `ChatUI.Tiles`.
- `OptimisticReactions` — a `ConcurrentDictionary<ChatEntryId, ...>` plus a `Changed`
  event. Every consuming component sets the pending value, subscribes to the event,
  and clears the value itself once server data catches up.

This design adds the missing half to the queue and moves reactions onto it, so the
count of parallel mechanisms goes from two to two (queue + `SendingMessages`) rather
than growing to three.

### Design decisions

- **The queue owns command status; the overlay owns domain meaning.** One registry,
  one source of truth about what is in flight.
- **`PartitionedCommandQueue<TItem>` is not modified.** It stays a dumb ordering
  coordinator; its 9 unit tests keep passing. Status lives in `ClientCommandQueue`,
  which already runs the commands, counts retries and sees the exceptions.
- **The effect reaches the UI through Fusion**, not through a hand-rolled event:
  the overlay is an `IComputeService` and components just read it. This removes the
  manual `Changed += ...` subscriptions that three components carry today.
- **The effect is dropped by domain reconciliation, not by command completion.**
  A command finishing does not mean its result is on screen — invalidation still has
  to travel. Dropping the effect at completion produces a frame with the reaction
  missing.
- **Coalescing becomes a per-command policy.** `Reactions_React` is a *toggle*
  (`ReactionsBackend.OnReact:87-91` removes the reaction when the same emoji arrives
  again), so collapsing two clicks into one changes the outcome. Read positions, by
  contrast, exist to be collapsed.
- **In-session only.** No durable outbox; a restart loses pending commands, exactly
  as the queue does today.

## Reuse

### Existing abstractions reused

- **`PartitionedCommandQueue<TItem>` / `QueueEdits<TItem>`** (`Core/Messaging/`) —
  unchanged, used as-is for ordering and coalescing.
- **`ClientCommandQueue`** (`UI.Blazor.App/Services/`) — extended in place; it is
  already the command filter, the runner and the retry loop.
- **The trigger-compute-method pattern of `ChatSendingMessagesTriggers`**
  (`Services/SendingMessages/`) — an empty `[ComputeMethod]` invalidated by hand,
  used to publish mutable state into the Fusion graph. Copied verbatim in shape.
- **`IReactions.ListSummaries` / `IReactions.Get`** — the overlay calls exactly what
  `MessageReactions.ComputeState` calls today; no new server API.
- **`ApplyOptimistic`** in `MessageReactions.razor` — moved out as-is and becomes the
  tested pure function.
- **Server-side dedup of `Reactions_React`** (`ApiCommandDeduplicator`, commit
  `bea93a29e1`) — this is what makes retrying a toggle safe; see Edge cases.
- **`FixedDelayer.NextTick`** — already used by `Countdown`, `MapView`,
  `RequirementChecker` for the same reason.
- **`FlowsTestPage` / `MeshTestPage`** — the admin-only test page pattern, route
  shape and layout for the diagnostics page.
- **`Chat.UI.Blazor.UnitTests`** — existing home for UI-layer unit tests.

### New components and placement

| Component | Placement | Why |
|---|---|---|
| `QueuedCommandStage`, `QueuedCommandEntry`, `QueuedCommandCoalescing` | `ActualChat.Core` (`Core/Commands/`, next to `IQueuedCommand`) | Part of the command contract, no UI or server dependency — shared by construction |
| `ClientCommandQueueTriggers` | `UI.Blazor.App/Services/` | Fusion trigger for a client-only service; belongs with its owner |
| `ReactionsUI` + `ReactionsOverlay` (pure functions) | `UI.Blazor.App/Services/` | Domain-specific to chat reactions; nothing else can consume it |

`ReactionsUI` is a new UI service rather than an extension of an existing one
(`CODING_STYLE.md` rule 14): it owns state nothing else tracks — the projection of
queued reaction commands onto server data — and there is no `*UI` service for
reactions today; components call `IReactions` directly.

## Architecture

### 1. Command status in the queue

```csharp
public enum QueuedCommandStage { Waiting, Running, Retrying, Completed, Failed }

public sealed record QueuedCommandEntry(
    string PartitionKey,
    IQueuedCommand Command,
    QueuedCommandStage Stage,
    int TryIndex,
    Exception? Error,
    Moment UpdatedAt);
```

`ClientCommandQueue` keeps a registry of running/completed/failed entries and reads
waiting ones out of the queue's lanes, exposing:

```csharp
IReadOnlyList<QueuedCommandEntry> GetEntries();             // whole snapshot, for diagnostics
IReadOnlyList<QueuedCommandEntry> GetEntries(string key);   // one partition, for an overlay
void Confirm(IQueuedCommand command);                       // "server data reflects this now"
```

Stage transitions: `Waiting → Running → (Retrying → Running)* → Completed → gone`,
or `→ Failed`.

- **`Completed`** — the command succeeded and the effect must survive until a consumer
  confirms it. Cleared by `Confirm`, or by a **10 s** TTL if no consumer ever does.
- **`Failed`** — a permanent failure, kept **1 min** so it can be seen, then dropped.
  Today such a command is silently discarded.

Both TTLs are enforced lazily, on the `Update`/`OnCompleted`/`GetEntries` paths — no
timer, and no background sweep. (The queue does not evict idle lanes today either;
that limitation is unchanged.)

### 2. Reactivity

```csharp
public class ClientCommandQueueTriggers : IComputeService
{
    [ComputeMethod] public virtual Task<Unit> OnChanged(string partitionKey) => TaskExt.UnitTask;
    [ComputeMethod] public virtual Task<Unit> OnAnyChanged() => TaskExt.UnitTask;
}
```

`ClientCommandQueue` invalidates both on every registry change. The existing
`Changed` event stays — it is what invalidation hangs off, and it keeps a non-Fusion
path available.

### 3. `ReactionsUI` — the overlay

```csharp
public sealed record ReactionsModel(
    ReactionSummary[] Summaries,
    Reaction? OwnReaction,
    QueuedCommandStage? PendingStage);   // null = fully confirmed

public sealed class ReactionsUI(AppUIHub hub) : IComputeService
{
    [ComputeMethod] public virtual Task<ReactionsModel?> Get(ChatEntryId entryId, CancellationToken ct);
    [ComputeMethod] public virtual Task<bool> HasVisible(ChatEntryId entryId, bool hasServerReactions);
}
```

`Get` depends on `Triggers.OnChanged(entryId.Value)`, reads `ListSummaries` + `Get`
from `IReactions`, then **folds every pending command of that partition in order**
over the server state. Folding, not "latest wins": `Reactions_React` is a toggle, so
two queued clicks on one emoji cancel out.

When the newest entry is `Completed`, `Get` reconciles — `own?.Emoji == pending.Emoji`
for an add, `!=` for a remove — and calls `Confirm` when it matches. Mutating state
inside `ComputeState` is the established pattern here: `MessageReactions.razor:61-66`
already calls `TryRemove` there. The loop converges in one extra recomputation,
because after `Confirm` the model equals the previous one.

The folding itself is a pure static function with no DI and no Fusion — the main
unit under test.

### 4. Coalescing policy

```csharp
public interface IQueuedCommand : ICommand
{
    string PartitionKey { get; }
    QueuedCommandCoalescing Coalescing => QueuedCommandCoalescing.None;
}
```

The default is `None` — losing a command must never be the accident of a missing
override. `ChatPositions_Set` declares `ReplaceWaiting`; `Reactions_React` keeps
`None` and gets `PartitionKey => Reaction.EntryId.Value`. `ClientCommandQueue`
stops unconditionally clearing the waiting list and honours the policy instead.

### 5. UI changes

| File | Change |
|---|---|
| `MessageReactions.razor` | `ComputeState` calls `ReactionsUI.Get` instead of two `IReactions` calls; `ApplyOptimistic`, the reconciliation block and the `Changed` subscription are deleted; `UpdateDelayer = FixedDelayer.NextTick` added |
| `ChatEntryMessageView.razor` | `OptimisticReactions.HasPendingAdd(...)` in markup → `ReactionsUI.HasVisible` in the model; subscription and `_forceRender` deleted |
| `ReactionBadge.razor` | `ToggleReaction` keeps only `UICommander.Run`; the effect now appears because the command entered the queue |
| `MessageHoverMenuContent.razor`, `MessageMenuContent.razor`, `ReactionSelect.razor` | `OptimisticReactions.Set` calls removed |
| `OptimisticReactions.cs` | Deleted, along with its registration in `BlazorUIAppModule` and the property on `AppUIHub` |

The animation pair `AddPendingAnimation` / `RemovePendingAnimation` is not an
optimistic effect — it is a flag that makes `ReactionBadge` play an svg animation
once — so it moves to `ReactionsUI` unchanged.

**Why the explicit delayer.** `UpdateDelayer.Defaults.UpdateDelay` is **1 second**;
it is bypassed only while `UIActionTracker.AreInstantUpdatesEnabled()` holds, i.e.
while a UI action is running or for 300 ms after one completed. With the queue in
place a click can be *accepted* and return immediately (busy lane), so the action
completes at once and a later invalidation can fall into the 1 s delay — precisely
in the bad-network case this work exists for. `FixedDelayer.NextTick` removes that
dependency.

**Pending status on the element.** `PendingStage` dims the badge while
`Waiting/Running/Retrying` and marks it on `Failed`. The "not sent" hint is
user-visible prose: it needs a key in every hand-written catalog plus the typed
member, per `i18n.md`.

### 6. Diagnostics page

`/test/command-queue`, modelled on `FlowsTestPage`: `ComputedStateComponent`,
`<RequireAccount MustBeAdmin="true"/>`, model = `GetEntries()` with a dependency on
`Triggers.OnAnyChanged()` so it refreshes itself. Columns: partition, command type,
stage, try index, updated at, error. English, not localized — a developer surface.

Page registration lives in the generated `BlazorUIAppAotSource.g.cs`, which must be
regenerated with `App.AotHelper -g` rather than edited.

## Manual pause (added 2026-08-28)

A global pause that makes the queue behave as it does with no connection, so the
backlog can be watched piling up and draining on demand.

`ClientCommandQueue` holds a resume gate — a `TaskCompletionSource<Unit>`, completed
while running — in the shape of `GatedUpdateDelayer`. `Pause()` replaces it with a
pending one, `Resume()` completes it, `IsPaused` reports the state, and `Run` awaits
it **before every attempt**. That single wait point covers the first attempt, every
retry and the next command of the lane, since all three pass through `Run`. An
attempt already in flight is left to finish — pausing does not cancel it. While
paused, commands stay in `Waiting`/`Retrying`; no separate `Paused` stage is
introduced, because the existing ones already say what is true.

The gate field is read and written through `Volatile.Read`/`Volatile.Write` rather
than a `volatile` modifier, per the memory-ordering rule in `CODING_STYLE.md`.

`Pause`/`Resume` invalidate only `Triggers.OnAnyChanged()` (via a private
`InvalidateAll`), since a pause has no partition. The diagnostics page gets the
toggle and a status line.

## Corrections found during implementation (2026-08-27)

Two defects surfaced only under the integration test; both are inherited from the
July queue rather than introduced here, and both are now fixed.

- **The re-dispatch marker cannot be an `AsyncLocal`.** `Commander.Run` wraps an
  outermost command in `ExecutionContextExt.TrySuppressFlow()` + `Task.Run`
  (`Internal/Commander.cs:36-41`) precisely so AsyncLocals don't flow into it. The
  flag was therefore always false on re-entry, the filter re-queued the command
  forever, and the test host died with no stack. The marker is now a
  `ConcurrentDictionary` on the queue keyed by
  `ActualLab.Collections.Slim.ReferenceEqualityComparer<T>.Instance` — reference
  identity matters, because commands are records with value equality. The entry
  registry uses the same comparer for the same reason. Pinned by
  `ClientCommandQueueTest.ReDispatchFlagShouldSurviveSuppressedExecutionContext`.
- **The filter must outrank `ApiCommandDeduplicator`.** The deduplicator sits at
  `CommandTracer - 1_000_000`; the queue's original `RpcRoutingCommandHandler + 1000`
  put it *after*, so the outer dispatch claimed the `Uuid`, then waited for a
  re-dispatch that the deduplicator held on that very claim — a deadlock that
  resolved only after the 5-minute `InProgressTtl`. The filter now runs at
  `CommandTracer - 500_000`, so the deduplicator sees just the actual run.
  Additionally the filter is **registered only on non-`Server` hosts**, which is what
  the original design intended anyway ("there's no queue on the server").

### The queue must be a singleton (found 2026-08-30, in the browser)

The two fixes above still left the queue broken outside the unit tests, and the
symptom looked unrelated: after one message the send button stopped working, in
every chat except `Notes`.

`CommandContext` creates **a fresh DI scope per outermost command**
(`CommandContext.cs:180`: `ServiceScope = Commander.Services.CreateScope()`), and
`MethodCommandHandler.GetHandlerService` resolves the handler from
`context.Services`. The queue was registered `Scoped`, so every dispatch got a
*different* queue instance with empty state: the re-dispatch marker set by
instance A was invisible to instance B, which queued the command again and
re-dispatched it, forever. In the browser trace one command (same `Uuid`) walked
through `queue@201912091`, `queue@784611214`, `queue@940396784`, … — an unbounded
loop that saturated the single WASM thread, which is what froze the UI.

`Notes` was the exception only because it has a single author, so its read-position
writer returns early and never sends `ChatPositions_Set` — the one queued command
a plain "send a message" flow produces.

The same defect silently disabled everything scope-bound: `/test/command-queue`
read the UI scope's queue and always showed it empty, and `debugUI.suspendCommandQueue`
paused an instance no command ever ran on.

Registration now lives in `ClientCommandQueueExt.AddClientCommandQueue()` and makes
both the queue and its triggers **singletons**; the queue holds no scope-bound state,
so this is safe. Pinned by
`ClientCommandQueueTest.DiRegisteredQueueMustSurviveThePerCommandScope`, which
asserts two DI scopes hand out the same instance — the invariant the whole
re-dispatch design rests on.

## Edge cases

- **Retrying a toggle is safe only because of server-side dedup.** A timeout retry
  re-sends the same `Uuid`; `ApiCommandDeduplicator` replays the stored result
  instead of toggling a second time. `Reactions_React` must therefore never be
  marked `INotDeduplicated`. A test pins this.
- **Effect during `Completed` with no consumer** — the 10 s TTL drops it, so a page
  with no `MessageReactions` mounted cannot leak entries.
- **Circuit teardown** — `_stopCts` already cancels the retry loop; the registry dies
  with the scoped service, since everything is in-session.
- **Two commands, one partition, opposite direction** — folding handles it: click,
  click again while offline, and the model shows the original state, which is what
  the server will end up with.
- **`Failed` clears the effect immediately** — the user sees the reaction gone rather
  than a false "applied".

## Limitations (accepted)

- In-session only: a restart loses queued commands and their effects.
- `SendingMessages` keeps its own mechanism; merging it is a separate spec.
- No end-user list of queued operations — only the admin diagnostics page.
- `GetEntries` returns a snapshot; nothing streams incremental changes.

## Verification

- **Unit (`Chat.UI.Blazor.UnitTests`)** — the folding function: toggle chain cancels
  out, emoji replacement, removal, `Completed` reconciliation, `Failed` yields no effect.
- **Unit (`Chat.UI.Blazor.UnitTests`)** — `ClientCommandQueue` with a fake `ICommander`: stage
  transitions, `TryIndex` growth on retries, `Completed`/`Failed` TTLs, `Confirm`,
  and both coalescing policies.
- **Integration** — `ReactionDeduplicationTest` (`Chat.IntegrationTests`) must stay
  green; it covers the server-side toggle+dedup behaviour the retry path now leans on.
- **Manual** — throttle the network, click a reaction repeatedly, confirm the badge
  appears at once, shows the pending state while retrying, converges to the correct
  toggle parity on reconnect, and that `/test/command-queue` lists the entries.
