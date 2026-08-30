# Command Queue Observability + Optimistic Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the client command queue observable (per-command stage, retries, errors) and let a queued command's effect show in the UI before the server confirms it, with reactions as the first consumer.

**Architecture:** `ClientCommandQueue` gains a status registry (`Waiting → Running → Retrying → Settled → gone`, or `Failed`) while `PartitionedCommandQueue` stays an untouched ordering coordinator. Status reaches the UI through Fusion trigger compute methods. A new `ReactionsUI` compute service folds a partition's queued `Reactions_React` commands over server data and confirms them back to the queue once the server reflects them, replacing `OptimisticReactions` entirely.

**Tech Stack:** C# / .NET 11, ActualLab.CommandR (`ICommandHandler`, `[CommandFilter]`), ActualLab.Fusion (`[ComputeMethod]`, `Invalidation.Begin()`, `FixedDelayer`), Blazor, xUnit + AwesomeAssertions.

**Spec:** `docs/superpowers/specs/2026-08-27-command-queue-observability-design.md`

## Global Constraints

- **Coding style** (`docs/CODING_STYLE.md`): no `Async` suffix; no XML docs on members (type-level `/// <summary>` only when the name isn't self-explanatory); Allman braces for types/methods, K&R everywhere else; control-flow statements on their own line followed by a blank line; `sealed` by default; primary-constructor parameters captured into properties in anything but a tiny type; no `StringComparer.Ordinal` / `CultureInfo.InvariantCulture` (invariant globalization).
- **Tests**: AAA with lowercase `// arrange` / `// act` / `// assert`; `Should`-form names (`<Subject>Should<ExpectedBehavior>`); `.Should()` assertions with a `because:` reason whenever a failure isn't self-explanatory.
- **Serialization**: `PartitionKey` and any other computed property on a command carries `[JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]`. Never add MemoryPack attributes.
- **Localization**: user-visible prose goes into `Strings.en.json` **and every hand-written catalog** (bg, bs, cs, de, es, fr, hi, id, it, ja, ko, pl, pt, ru, tr, uk, vi, zh) plus a typed member in `LocalizedStringsLocalizerExt.cs`; derived catalogs are regenerated (`scripts/derive-bcms.cmd`, then `scripts/derive-max.cmd`), never hand-edited. Developer surfaces (test pages, diagnostics) stay English.
- **No commits.** Dmitrii commits himself; never run `git commit` unless he asks in that turn. Each task ends with a build + test run instead.
- **TTLs**: `Settled` = 10 s, `Failed` = 1 min, enforced lazily on `Update` / `OnCompleted` / `GetEntries` — no timer, no background sweep.

## File Structure

| File | Responsibility |
|---|---|
| `src/dotnet/Core/Commands/IQueuedCommand.cs` (modify) | Marker + partition key + coalescing policy |
| `src/dotnet/Core/Commands/QueuedCommandEntry.cs` (create) | `QueuedCommandStage`, `QueuedCommandCoalescing`, `QueuedCommandEntry` — the shared status contract |
| `src/dotnet/UI.Blazor.App/Services/ClientCommandQueue.cs` (modify) | Filter + runner + status registry + `Confirm` |
| `src/dotnet/UI.Blazor.App/Services/ClientCommandQueueTriggers.cs` (create) | Fusion triggers the registry invalidates |
| `src/dotnet/UI.Blazor.App/Services/ReactionsUI.cs` (create) | Compute service: server reactions + queued overlay, animation flags |
| `src/dotnet/UI.Blazor.App/Services/ReactionsOverlay.cs` (create) | Pure folding/reconciliation functions — the main unit under test |
| `src/dotnet/UI.Blazor.App/Pages/CommandQueueTestPage.razor` (create) | Admin-only diagnostics table |
| `src/dotnet/Api.Contracts/Chat/IReactions.cs`, `Users/IChatPositions.cs` (modify) | Commands declare partition key + coalescing |
| Reaction components + `OptimisticReactions.cs` (modify/delete) | Switch to `ReactionsUI`, drop the old mechanism |

---

### Task 1: Status contract + registry in `ClientCommandQueue`

**Files:**
- Create: `src/dotnet/Core/Commands/QueuedCommandEntry.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/ClientCommandQueue.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/ClientCommandQueueTest.cs`

**Interfaces:**
- Consumes: `IQueuedCommand`, `PartitionedCommandQueue<TItem>`, `QueueEdits<TItem>` (all exist).
- Produces: `QueuedCommandStage`, `QueuedCommandEntry`, `ClientCommandQueue.GetEntries()`, `GetEntries(string)`, `Confirm(IQueuedCommand)`. Task 3 invalidates on registry change; Task 4 reads `GetEntries(string)` and calls `Confirm`; Task 6 renders `GetEntries()`.

- [ ] **Step 1: Write the status contract**

`src/dotnet/Core/Commands/QueuedCommandEntry.cs`:

```csharp
namespace ActualChat;

public enum QueuedCommandStage { Waiting, Running, Retrying, Settled, Failed }

/// <summary>
/// A queued command together with the stage of its execution;
/// see <see cref="QueuedCommandStage"/> for the lifecycle.
/// </summary>
public sealed record QueuedCommandEntry(
    string PartitionKey,
    IQueuedCommand Command,
    QueuedCommandStage Stage,
    int TryIndex,
    Exception? Error,
    Moment UpdatedAt);
```

- [ ] **Step 2: Write the failing tests**

`tests/Chat.UI.Blazor.UnitTests/ClientCommandQueueTest.cs`. The queue must be testable without a real commander, so Task 1 also adds an executor seam (Step 4).

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ClientCommandQueueTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task SuccessfulCommandShouldEndUpSettledUntilConfirmed()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new ClientCommandQueue((_, _) => Task.CompletedTask, clock);
        var command = new TestCommand("a");

        // act
        await queue.OnCommand(command, null!, CancellationToken.None);

        // assert
        var entries = queue.GetEntries("a");
        entries.Should().HaveCount(1);
        entries[0].Stage.Should().Be(QueuedCommandStage.Settled, because: "the effect survives until a consumer confirms it");

        queue.Confirm(command);
        queue.GetEntries("a").Should().BeEmpty(because: "confirmation drops the entry");
    }

    [Fact]
    public async Task PermanentFailureShouldBeKeptAsFailed()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new ClientCommandQueue((_, _) => throw new InvalidOperationException("nope"), clock);
        var command = new TestCommand("a");

        // act
        await queue.OnCommand(command, null!, CancellationToken.None);

        // assert
        var entry = queue.GetEntries("a").Single();
        entry.Stage.Should().Be(QueuedCommandStage.Failed);
        entry.Error.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task TransientFailureShouldRetryAndCountTries()
    {
        // arrange
        using var clock = new TestClock();
        var tryCount = 0;
        var queue = new ClientCommandQueue((_, _) => {
            tryCount++;
            return tryCount < 3 ? throw new TimeoutException() : Task.CompletedTask;
        }, clock) { RetryDelay = TimeSpan.Zero };

        // act
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);

        // assert
        tryCount.Should().Be(3, because: "two transient failures are retried");
    }

    [Fact]
    public async Task SettledEntryShouldExpireAfterTtl()
    {
        // arrange
        using var clock = new TestClock();
        var queue = new ClientCommandQueue((_, _) => Task.CompletedTask, clock);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);

        // act
        clock.OffsetBy(TimeSpan.FromSeconds(11));

        // assert
        queue.GetEntries("a").Should().BeEmpty(because: "an unconfirmed Settled entry expires after 10s");
    }

    // Nested types

    private sealed record TestCommand(string PartitionKey) : IQueuedCommand;
}
```

`TestClock` is ActualLab's own (`ActualLab.Time.Testing`, a `MomentClock` with
`OffsetBy(TimeSpan)`), already used across `Core.UnitTests` — do not write your own.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ClientCommandQueueTest"`
Expected: compile failure — `ClientCommandQueue` has no such constructor, no `GetEntries`, no `Confirm`.

- [ ] **Step 4: Add the executor seam and the registry**

In `ClientCommandQueue`, replace the primary constructor with one that admits an
executor and a clock (both defaulted for DI), and add the registry:

```csharp
public sealed class ClientCommandQueue : ICommandHandler<IQueuedCommand>, IDisposable
{
    private static readonly AsyncLocal<bool> IsRunningFromQueue = new();
    public static readonly TimeSpan SettledTtl = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FailedTtl = TimeSpan.FromMinutes(1);

    private readonly PartitionedCommandQueue<IQueuedCommand> _queue = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly ConcurrentDictionary<IQueuedCommand, QueuedCommandEntry> _entries = new();
    private readonly Func<IQueuedCommand, CancellationToken, Task> _executor;
    private readonly MomentClock _clock;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    private ClientCommandQueueTriggers? Triggers { get; }

    // The DI constructor. Triggers is resolved in Task 3; until then pass null.
    public ClientCommandQueue(ICommander commander, ClientCommandQueueTriggers? triggers = null)
        : this((c, ct) => commander.Run(c, ct), null, triggers)
    { }

    // For tests: drives the queue without a commander or a real clock
    internal ClientCommandQueue(
        Func<IQueuedCommand, CancellationToken, Task> executor,
        MomentClock? clock = null,
        ClientCommandQueueTriggers? triggers = null)
    {
        _executor = executor;
        _clock = clock ?? MomentClockSet.Default.SystemClock;
        Triggers = triggers;
    }
}
```

**This is the final constructor shape** — Task 3 only fills in the `triggers`
argument at the registration site, it does not change the signature again.

Add `InternalsVisibleTo` for `ActualChat.Chat.UI.Blazor.UnitTests` if the project
doesn't already have it (check `src/dotnet/UI.Blazor.App/*.csproj` and
`Directory.Build.props` first — several projects here already expose internals to
their test project).

The run loop records stages:

```csharp
    private async Task Run(IQueuedCommand command, CancellationToken cancellationToken)
    {
        var tryIndex = 0;
        while (true) {
            SetStage(command, QueuedCommandStage.Running, tryIndex, null);
            try {
                await RunOnce(command, cancellationToken).ConfigureAwait(false);
                SetStage(command, QueuedCommandStage.Settled, tryIndex, null);
                return;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                if (e is not (OperationCanceledException or TimeoutException)) {
                    SetStage(command, QueuedCommandStage.Failed, tryIndex, e);
                    return;
                }

                tryIndex++;
                SetStage(command, QueuedCommandStage.Retrying, tryIndex, e);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
```

`OnCommand` records a `Waiting` entry for every command it accepts (right after
`_queue.Update` returns), so a command sitting in a busy lane is visible too; `Run`
then overwrites it with `Running`.

```csharp
private void SetStage(IQueuedCommand command, QueuedCommandStage stage, int tryIndex, Exception? error)
{
    _entries[command] = new QueuedCommandEntry(command.PartitionKey, command, stage, tryIndex, error, _clock.Now);
    OnEntriesChanged(command.PartitionKey);
}

public void Confirm(IQueuedCommand command)
{
    if (_entries.TryRemove(command, out var entry))
        OnEntriesChanged(entry.PartitionKey);
}

public IReadOnlyList<QueuedCommandEntry> GetEntries()
{
    Prune();
    return _entries.Values.OrderBy(x => x.UpdatedAt).ToArray();
}

public IReadOnlyList<QueuedCommandEntry> GetEntries(string partitionKey)
{
    Prune();
    return _entries.Values
        .Where(x => x.PartitionKey == partitionKey)
        .OrderBy(x => x.UpdatedAt)
        .ToArray();
}

private void Prune()
{
    var now = _clock.Now;
    foreach (var (command, entry) in _entries) {
        var ttl = entry.Stage switch {
            QueuedCommandStage.Settled => SettledTtl,
            QueuedCommandStage.Failed => FailedTtl,
            _ => (TimeSpan?)null,
        };
        if (ttl is { } t && entry.UpdatedAt + t <= now)
            _entries.TryRemove(command, out _);
    }
}
```

`OnEntriesChanged(partitionKey)` raises the existing `Changed` event for now; Task 3
makes it invalidate the Fusion triggers too.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ClientCommandQueueTest"`
Expected: PASS (4 tests).

- [ ] **Step 6: Build the app project**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: Build succeeded.

---

### Task 2: Coalescing policy

**Files:**
- Modify: `src/dotnet/Core/Commands/IQueuedCommand.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/ClientCommandQueue.cs`
- Modify: `src/dotnet/Api.Contracts/Users/IChatPositions.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/ClientCommandQueueTest.cs` (extend)

**Interfaces:**
- Consumes: Task 1's registry.
- Produces: `QueuedCommandCoalescing { None, ReplaceWaiting }` and `IQueuedCommand.Coalescing`. Task 5 relies on `Reactions_React` inheriting the `None` default.

- [ ] **Step 1: Write the failing tests**

Append to `ClientCommandQueueTest`:

```csharp
    [Fact]
    public async Task NoneCoalescingShouldRunEveryCommand()
    {
        // arrange
        var runCount = 0;
        var gate = TaskCompletionSourceExt.New<Unit>();
        var queue = new ClientCommandQueue(async (_, _) => {
            runCount++;
            await gate.Task.ConfigureAwait(false);
        });

        // act
        var first = queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new TestCommand("a"), null!, CancellationToken.None);
        gate.SetResult(default);
        await first;

        // assert
        runCount.Should().Be(3, because: "a toggle command must never be collapsed");
    }

    [Fact]
    public async Task ReplaceWaitingShouldCollapseTheBacklog()
    {
        // arrange
        var runCount = 0;
        var gate = TaskCompletionSourceExt.New<Unit>();
        var queue = new ClientCommandQueue(async (_, _) => {
            runCount++;
            await gate.Task.ConfigureAwait(false);
        });

        // act
        var first = queue.OnCommand(new CoalescingTestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new CoalescingTestCommand("a"), null!, CancellationToken.None);
        await queue.OnCommand(new CoalescingTestCommand("a"), null!, CancellationToken.None);
        gate.SetResult(default);
        await first;

        // assert
        runCount.Should().Be(2, because: "the two waiting commands collapse into one");
    }

    private sealed record CoalescingTestCommand(string PartitionKey) : IQueuedCommand
    {
        public QueuedCommandCoalescing Coalescing => QueuedCommandCoalescing.ReplaceWaiting;
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ClientCommandQueueTest"`
Expected: compile failure — `QueuedCommandCoalescing` is not defined.

- [ ] **Step 3: Add the policy**

`IQueuedCommand.cs`:

```csharp
namespace ActualChat;

public enum QueuedCommandCoalescing
{
    None = 0,
    ReplaceWaiting,
}

public interface IQueuedCommand : ICommand
{
    string PartitionKey { get; }
    QueuedCommandCoalescing Coalescing => QueuedCommandCoalescing.None;
}
```

In `ClientCommandQueue.OnCommand`, honour it:

```csharp
        var toRun = _queue.Update(command.PartitionKey, pending => {
            var edits = new QueueEdits<IQueuedCommand>();
            if (command.Coalescing == QueuedCommandCoalescing.ReplaceWaiting)
                foreach (var waiting in pending) {
                    edits.Remove(waiting);
                    _entries.TryRemove(waiting, out _);
                }
            return edits.Add(command);
        });
```

- [ ] **Step 4: Opt read positions in**

`IChatPositions.cs` — `ChatPositions_Set` already implements `IQueuedCommand`; add:

```csharp
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public QueuedCommandCoalescing Coalescing => QueuedCommandCoalescing.ReplaceWaiting;
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ClientCommandQueueTest"`
Expected: PASS (6 tests).

---

### Task 3: Fusion triggers

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/ClientCommandQueueTriggers.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/ClientCommandQueue.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`

**Interfaces:**
- Consumes: `OnEntriesChanged` from Task 1.
- Produces: `ClientCommandQueueTriggers.OnChanged(string)` / `OnAnyChanged()`, which Tasks 4 and 6 await inside their compute methods.

- [ ] **Step 1: Add the triggers service**

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public class ClientCommandQueueTriggers : IComputeService
{
    [ComputeMethod]
    public virtual Task<Unit> OnChanged(string partitionKey)
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod]
    public virtual Task<Unit> OnAnyChanged()
        => ActualLab.Async.TaskExt.UnitTask;
}
```

- [ ] **Step 2: Invalidate from the registry**

The constructor from Task 1 already accepts `ClientCommandQueueTriggers?` — nothing
to change there. Only `OnEntriesChanged` grows the invalidation, in the same style as
`ChatSendingMessages.InvalidateCollection`:

```csharp
    private void OnEntriesChanged(string partitionKey)
    {
        Changed?.Invoke();
        if (Triggers is null)
            return;

        using (Invalidation.Begin()) {
            _ = Triggers.OnChanged(partitionKey);
            _ = Triggers.OnAnyChanged();
        }
    }
```

- [ ] **Step 3: Register it**

`BlazorUIAppModule.InjectServices`, next to the existing queue registration:

```csharp
        fusion.AddService<ClientCommandQueueTriggers>(ServiceLifetime.Scoped);
        services.AddScoped<ClientCommandQueue>();
        fusion.Commander.AddHandlers<ClientCommandQueue>();
```

- [ ] **Step 4: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: Build succeeded.

---

### Task 4: `ReactionsUI` overlay

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/ReactionsOverlay.cs`
- Create: `src/dotnet/UI.Blazor.App/Services/ReactionsUI.cs`
- Modify: `src/dotnet/Api.Contracts/Chat/IReactions.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/ReactionsOverlayTest.cs`

**Interfaces:**
- Consumes: `ClientCommandQueue.GetEntries(string)`, `Confirm`, `ClientCommandQueueTriggers.OnChanged`.
- Produces: `ReactionsModel(ReactionSummary[] Summaries, Reaction? OwnReaction, QueuedCommandStage? PendingStage)`, `ReactionsUI.Get(ChatEntryId, CancellationToken)`, `ReactionsUI.HasVisible(ChatEntryId, bool)`, plus the animation methods Task 5 moves over.

- [ ] **Step 1: Make `Reactions_React` queueable**

`IReactions.cs`:

```csharp
public sealed partial record Reactions_React : ApiCommand<Unit>, IQueuedCommand
{
    [DataMember(Order = 2), Key(2)] public required Reaction Reaction { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string PartitionKey => Reaction.EntryId.Value;
}
```

It keeps the default `None` coalescing — `Reactions_React` is a toggle.

- [ ] **Step 2: Write the failing tests for the pure folding**

`tests/Chat.UI.Blazor.UnitTests/ReactionsOverlayTest.cs`:

```csharp
using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ReactionsOverlayTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatEntryId EntryId = new(ChatId.Parse("aaaaaaaaaaaaaaaaaaaa"), ChatEntryKind.Text, 1, AssumeValid.Option);
    private static readonly Emoji ThumbsUp = Emojis.ThumbsUp;
    private static readonly Emoji Heart = Emoji.Parse("❤️");

    [Fact]
    public void PendingAddShouldAppearBeforeServerConfirms()
    {
        // arrange
        var summaries = Array.Empty<ReactionSummary>();

        // act
        var model = ReactionsOverlay.Fold(summaries, null, [ThumbsUp], EntryId);

        // assert
        model.OwnReaction!.Emoji.Should().Be(ThumbsUp);
        model.Summaries.Single().Count.Should().Be(1);
    }

    [Fact]
    public void TwoPendingClicksOnSameEmojiShouldCancelOut()
    {
        // arrange
        var summaries = Array.Empty<ReactionSummary>();

        // act
        var model = ReactionsOverlay.Fold(summaries, null, [ThumbsUp, ThumbsUp], EntryId);

        // assert
        model.OwnReaction.Should().BeNull(because: "React is a toggle, so an even number of clicks is a no-op");
        model.Summaries.Should().BeEmpty();
    }

    [Fact]
    public void PendingEmojiChangeShouldReplaceOwnReaction()
    {
        // arrange
        var own = new Reaction { Id = Symbol.Empty, AuthorId = null!, EntryId = EntryId, Emoji = ThumbsUp };
        var summaries = new[] { new ReactionSummary { EntryId = EntryId, Emoji = ThumbsUp, Count = 1 } };

        // act
        var model = ReactionsOverlay.Fold(summaries, own, [Heart], EntryId);

        // assert
        model.OwnReaction!.Emoji.Should().Be(Heart);
        model.Summaries.Should().ContainSingle(x => x.Emoji == Heart);
    }

    [Fact]
    public void ServerReflectingThePendingAddShouldBeReconciled()
    {
        // arrange
        var own = new Reaction { Id = Symbol.Empty, AuthorId = null!, EntryId = EntryId, Emoji = ThumbsUp };

        // act
        var isReflected = ReactionsOverlay.IsReflected(own, ThumbsUp);

        // assert
        isReflected.Should().BeTrue(because: "the server already shows the pending emoji");
    }
}
```

Adjust the `Reaction` / `ReactionSummary` initializers to their real shapes — read
`src/dotnet/Api.Contracts/Chat/Reaction.cs` and `ReactionSummary.cs` first; the test
must construct them exactly as the records require.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ReactionsOverlayTest"`
Expected: compile failure — `ReactionsOverlay` is not defined.

- [ ] **Step 4: Implement the pure functions**

`ReactionsOverlay.cs` — no DI, no Fusion:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public sealed record ReactionsModel(
    ReactionSummary[] Summaries,
    Reaction? OwnReaction,
    QueuedCommandStage? PendingStage);

public static class ReactionsOverlay
{
    public static ReactionsModel Fold(
        ReactionSummary[] summaries,
        Reaction? ownReaction,
        IReadOnlyList<Emoji> pendingEmojis,
        ChatEntryId entryId)
    {
        // Mirrors ReactionsBackend.OnReact:87-99 - same emoji removes, a different one
        // replaces, none adds - so the fold predicts exactly what the server will store.
        var counts = summaries.ToDictionary(x => x.Emoji, x => x.Count);
        var own = ownReaction;
        foreach (var emoji in pendingEmojis) {
            if (own is { } o && o.Emoji == emoji) {
                Decrement(o.Emoji);
                own = null;
            }
            else {
                if (own is { } o2)
                    Decrement(o2.Emoji);
                counts[emoji] = counts.GetValueOrDefault(emoji) + 1;
                own = new Reaction { Id = Symbol.Empty, AuthorId = null!, EntryId = entryId, Emoji = emoji };
            }
        }
        var newSummaries = counts
            .Where(x => x.Value > 0)
            .Select(x => new ReactionSummary { EntryId = entryId, Emoji = x.Key, Count = x.Value })
            .ToArray();
        return new ReactionsModel(newSummaries, own, null);

        void Decrement(Emoji emoji) {
            var count = counts.GetValueOrDefault(emoji) - 1;
            if (count > 0)
                counts[emoji] = count;
            else
                counts.Remove(emoji);
        }
    }

    public static bool IsReflected(Reaction? ownReaction, Emoji pendingEmoji)
        => ownReaction?.Emoji == pendingEmoji;
}
```

Two shapes to verify against the real records before writing this: `ReactionSummary`
may carry more required members than `EntryId`/`Emoji`/`Count`, and `Reaction.Id` may
not accept `Symbol.Empty` — read `Api.Contracts/Chat/Reaction.cs` and
`ReactionSummary.cs` and match them exactly. `IsReflected` covers the add case;
a pending *remove* is reflected when the own reaction is gone, which the caller
expresses as `!IsReflected(own, emoji)` — see `ReactionsUI.Get` in Step 6.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ReactionsOverlayTest"`
Expected: PASS (4 tests).

- [ ] **Step 6: Add the compute service**

`ReactionsUI.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public sealed class ReactionsUI(AppUIHub hub) : IComputeService
{
    private readonly HashSet<(string EntryId, string EmojiId)> _pendingAnimations = new();

    private AppUIHub Hub { get; } = hub;
    private IReactions Reactions => Hub.Reactions;
    private ClientCommandQueue Queue => Hub.ClientCommandQueue;
    private ClientCommandQueueTriggers Triggers => Hub.ClientCommandQueueTriggers;
    private Session Session => Hub.Session();

    [ComputeMethod]
    public virtual async Task<ReactionsModel?> Get(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        await Triggers.OnChanged(entryId.Value).ConfigureAwait(false);
        var summaries = await Reactions.ListSummaries(Session, entryId, cancellationToken).ConfigureAwait(false);
        var ownReaction = summaries.Length > 0
            ? await Reactions.Get(Session, entryId, cancellationToken).ConfigureAwait(false)
            : null;
        var entries = Queue.GetEntries(entryId.Value);
        if (entries.Count == 0)
            return summaries.Length == 0 ? null : new ReactionsModel(summaries, ownReaction, null);

        // A Settled command whose result the server already shows is confirmed here,
        // so the effect never blinks between completion and invalidation.
        foreach (var entry in entries) {
            if (entry.Stage != QueuedCommandStage.Settled || entry.Command is not Reactions_React react)
                continue;
            if (ReactionsOverlay.IsReflected(ownReaction, react.Reaction.Emoji))
                Queue.Confirm(entry.Command);
        }
        var pending = Queue.GetEntries(entryId.Value);
        var emojis = pending
            .Where(x => x.Stage != QueuedCommandStage.Failed)
            .Select(x => ((Reactions_React)x.Command).Reaction.Emoji)
            .ToArray();
        var model = ReactionsOverlay.Fold(summaries, ownReaction, emojis, entryId);
        return model with { PendingStage = pending.Count > 0 ? pending[^1].Stage : null };
    }

    [ComputeMethod]
    public virtual async Task<bool> HasVisible(ChatEntryId entryId, bool hasServerReactions)
    {
        if (hasServerReactions)
            return true;

        await Triggers.OnChanged(entryId.Value).ConfigureAwait(false);
        return Queue.GetEntries(entryId.Value).Count > 0;
    }

    public void AddPendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Add((entryId, emojiId));

    public bool RemovePendingAnimation(string entryId, string emojiId)
        => _pendingAnimations.Remove((entryId, emojiId));
}
```

Register in `BlazorUIAppModule`: `fusion.AddService<ReactionsUI>(ServiceLifetime.Scoped);`
and add `ClientCommandQueue`, `ClientCommandQueueTriggers`, `ReactionsUI` properties
to `AppUIHub` in the existing `field ??= Services.GetRequiredService<T>()` style.

- [ ] **Step 7: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: Build succeeded.

---

### Task 5: Switch the reaction components, delete `OptimisticReactions`

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Reactions/MessageReactions.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Reactions/ReactionBadge.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/MessageMenu/MessageHoverMenuContent.razor`, `MessageMenu/MessageMenuContent.razor`, `Components/Reactions/ReactionSelect.razor`
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs`, `Module/BlazorUIAppModule.cs`
- Delete: `src/dotnet/UI.Blazor.App/Services/OptimisticReactions.cs`
- Modify: `src/dotnet/UI.Blazor/Resources/Strings.*.json` (18 hand-written catalogs), `Resources/LocalizedStringsLocalizerExt.cs`

**Interfaces:**
- Consumes: `ReactionsUI.Get`, `HasVisible`, `AddPendingAnimation`, `RemovePendingAnimation`.
- Produces: nothing new; this is the cut-over.

- [ ] **Step 1: Rewrite `MessageReactions.razor`**

`ComputeState` becomes a single call and the reconciliation block disappears:

```csharp
    protected override ComputedState<ReactionsModel?>.Options GetStateOptions()
        => new() {
            InitialValue = Entry.HasReactions ? new ReactionsModel([], null, null) : null,
            UpdateDelayer = FixedDelayer.NextTick,
            Category = GetStateCategory(GetType()),
        };

    protected override Task<ReactionsModel?> ComputeState(CancellationToken cancellationToken)
        => ReactionsUI.Get(Entry.Id, cancellationToken);
```

The markup drops `ApplyOptimistic` and reads `State.Value` directly; `OnInitialized`
and `DisposeAsync` lose the `Changed` subscription.

Pending styling comes from `m.PendingStage` — one CSS class on the list plus a title
on the failed case:

```razor
@{
    var m = State.Value;
    if (m is null)
        return;

    var pendingClass = m.PendingStage switch {
        QueuedCommandStage.Failed => "message-reactions-failed",
        null => "",
        _ => "message-reactions-pending",
    };
}

<ul class="message-reactions @pendingClass @Class"
    title="@(m.PendingStage is QueuedCommandStage.Failed or QueuedCommandStage.Retrying ? L.Reactions_NotSent : null)">
```

In `message-reactions.css` (next to the component, per `ui/components.md`):

```css
.message-reactions-pending { opacity: 0.6; }
.message-reactions-failed { opacity: 0.6; text-decoration: line-through; }
```

- [ ] **Step 2: Simplify `ReactionBadge.ToggleReaction`**

```csharp
    private Task ToggleReaction(Emoji emoji) {
        _ = Hub.TuneUI.Play(Tune.React);
        var isRemove = OwnReaction?.Emoji == emoji;
        if (!isRemove)
            ReactionsUI.AddPendingAnimation(Entry.Id.Value, emoji.Id.Value);
        var reaction = new Reaction {
            Id = Symbol.Empty,
            AuthorId = null!,
            EntryId = Entry.Id,
            Emoji = emoji,
        };
        return UICommander.Run(new Reactions_React { Session = Session, Reaction = reaction });
    }
```

`OnParametersSet` keeps using `RemovePendingAnimation`, now via `ReactionsUI`.

- [ ] **Step 3: Move the visibility check into `ChatEntryMessageView`'s model**

Both markup sites (`:84`, `:205`) become `@if (m.HasReactions)`, where the model gains
`bool HasReactions` computed in `ComputeState` as
`await ReactionsUI.HasVisible(entry.Id, entry.HasReactions).ConfigureAwait(false)`.
Delete `OnOptimisticReactionChanged`, the `Changed` subscription in `OnInitialized`,
the unsubscribe in `DisposeAsync`, and the `_forceRender` flag it set.

- [ ] **Step 4: Drop the remaining `OptimisticReactions.Set` calls**

In `MessageHoverMenuContent.razor:66-67`, `MessageMenuContent.razor:210`, and
`ReactionSelect.razor`, keep only `AddPendingAnimation` (via `ReactionsUI`) plus the
`UICommander.Run`. Then delete `OptimisticReactions.cs`, its registration
(`BlazorUIAppModule.cs:98`) and its `AppUIHub` property (`AppUIHub.cs:88`).

- [ ] **Step 5: Add the "not sent" string**

Key `Reactions_NotSent`. English: `"Not sent yet"`. Add it to `Strings.en.json` and
every hand-written catalog — bg, bs, cs, de, es, fr, hi, id, it, ja, ko, pl, pt, ru,
tr, uk, vi, zh — plus the typed member in `LocalizedStringsLocalizerExt.cs`:

```csharp
        public string Reactions_NotSent => l["Reactions_NotSent"].Value;
```

Then regenerate the derived catalogs, in this order:

```bash
scripts/derive-bcms.cmd
scripts/derive-max.cmd
```

- [ ] **Step 6: Verify no references remain**

Run: `rg "OptimisticReactions" src/dotnet`
Expected: no matches.

- [ ] **Step 7: Build and run the localization test**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AppLocalizationTest"`
Expected: Build succeeded; localization test PASS (it fails the build if a catalog is missing the key).

---

### Task 6: Diagnostics page

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Pages/CommandQueueTestPage.razor`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs` (regenerated, not hand-edited)

**Interfaces:**
- Consumes: `ClientCommandQueue.GetEntries()`, `ClientCommandQueueTriggers.OnAnyChanged()`.
- Produces: nothing.

- [ ] **Step 1: Write the page**

Modelled on `FlowsTestPage.razor`; English, not localized:

```razor
@using ActualChat.UI.Blazor.App.Services
@inherits ComputedStateComponent<AppUIHub, IReadOnlyList<QueuedCommandEntry>>
@page "/test/command-queue"

<RequireAccount MustBeAdmin="true"/>
<MainHeader>Command queue</MainHeader>

@{ var entries = State.Value; }
<div class="test-page-wrapper">
    <div class="flex-y h-full overflow-y-auto select-text p-2 gap-y-2">
        <p class="text-gray-500 text-sm">Client-side queued commands and their stages. Updates live.</p>
        @if (entries.Count == 0) {
            <span class="text-gray-500">Queue is empty.</span>
        }
        else {
            <table class="w-full border-collapse text-sm">
                <thead>
                    <tr class="border-b text-gray-500 bg-white sticky top-0">
                        <th class="text-left p-2">Partition</th>
                        <th class="text-left p-2">Command</th>
                        <th class="text-left p-2">Stage</th>
                        <th class="text-right p-2 w-16">Try</th>
                        <th class="text-left p-2">Updated</th>
                        <th class="text-left p-2">Error</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var entry in entries) {
                        <tr class="border-b">
                            <td class="p-2">@entry.PartitionKey</td>
                            <td class="p-2">@entry.Command.GetType().Name</td>
                            <td class="p-2">@entry.Stage</td>
                            <td class="p-2 text-right">@entry.TryIndex</td>
                            <td class="p-2">@entry.UpdatedAt.ToDateTime().ToString("HH:mm:ss", null)</td>
                            <td class="p-2 text-red-500">@entry.Error?.Message</td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    </div>
</div>

@code {
    private ClientCommandQueue Queue => Hub.ClientCommandQueue;
    private ClientCommandQueueTriggers Triggers => Hub.ClientCommandQueueTriggers;

    protected override ComputedState<IReadOnlyList<QueuedCommandEntry>>.Options GetStateOptions()
        => new() {
            InitialValue = [],
            UpdateDelayer = FixedDelayer.NextTick,
            Category = GetStateCategory(GetType()),
        };

    protected override async Task<IReadOnlyList<QueuedCommandEntry>> ComputeState(CancellationToken cancellationToken) {
        await Triggers.OnAnyChanged().ConfigureAwait(false);
        return Queue.GetEntries();
    }
}
```

- [ ] **Step 2: Regenerate the AOT source**

Run: `App.AotHelper -g` (see the header of `BlazorUIAppAotSource.g.cs`)
Expected: the file gains `CodeKeeper.Keep<...CommandQueueTestPage>()` and its
`AotTypeKind.Component` entry.

- [ ] **Step 3: Build the server**

Run: `dotnet build src/dotnet/App.Server/App.Server.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Manual verification**

Start the app, sign in as admin, open `/test/command-queue`. Throttle the network in
DevTools, click a reaction repeatedly on one message, and confirm: the badge appears
immediately, the page lists the entries moving `Waiting → Running → Retrying`, the
toggle parity on reconnect matches the number of clicks, and entries disappear once
the server data catches up.

---

## Verification summary

- `dotnet test tests/Chat.UI.Blazor.UnitTests` — `ClientCommandQueueTest` (6), `ReactionsOverlayTest` (4), `AppLocalizationTest` green.
- `dotnet test tests/Core.UnitTests --filter "FullyQualifiedName~Queue"` — the existing 9 queue tests still green (the primitive is untouched).
- `dotnet test tests/Chat.IntegrationTests --filter "FullyQualifiedName~ReactionDeduplicationTest"` — server-side toggle + dedup still green; this is what makes retrying a toggle safe.
- `dotnet build src/dotnet/App.Server/App.Server.csproj` — Build succeeded.
- `rg "OptimisticReactions" src/dotnet` — no matches.
