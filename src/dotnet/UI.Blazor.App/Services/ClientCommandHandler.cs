using ActualChat.Messaging;
using ActualLab.CommandR.Internal;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Routes every <see cref="IQueuedCommand"/> dispatched on the client into its partition lane:
/// one command per partition runs at a time, transient failures are retried until the command
/// reaches the server, and every command's stage stays visible via <see cref="GetEntries()"/>.
/// </summary>
public sealed class ClientCommandHandler : ICommandHandler<IQueuedCommand>, IDisposable
{
    public static readonly TimeSpan CompletedTtl = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FailedTtl = TimeSpan.FromMinutes(1);

    private readonly PartitionedCommandQueue<IQueuedCommand> _queue = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly ConcurrentDictionary<IQueuedCommand, QueuedCommandEntry> _entries =
        new(ActualLab.Collections.Slim.ReferenceEqualityComparer<IQueuedCommand>.Instance);

    // Commands being re-dispatched right now. This can't be an AsyncLocal: Commander.Run
    // suppresses the execution context flow for an outermost command, so the flag wouldn't
    // survive the re-dispatch and the filter would queue the command again, forever.
    private readonly ConcurrentDictionary<IQueuedCommand, Unit> _runningFromQueue =
        new(ActualLab.Collections.Slim.ReferenceEqualityComparer<IQueuedCommand>.Instance);
    private readonly Func<IQueuedCommand, CancellationToken, Task> _executor;
    private readonly MomentClock _clock;
    // Volatile-accessed: Pause/Resume publish a new gate that Run must observe
    private TaskCompletionSource<Unit> _resumeGate =
        TaskCompletionSourceExt.New<Unit>().WithResult(default);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public bool IsPaused => !Volatile.Read(ref _resumeGate).Task.IsCompleted;
    public event Action? Changed;

    private ClientCommandHandlerTriggers? Triggers { get; }

    public ClientCommandHandler(ICommander commander, ClientCommandHandlerTriggers triggers)
        : this((command, cancellationToken) => commander.Run(command, cancellationToken), null, triggers)
    { }

    // Internal to let the tests drive the queue without a commander, a clock or Fusion
    internal ClientCommandHandler(
        Func<IQueuedCommand, CancellationToken, Task> executor,
        MomentClock? clock = null,
        ClientCommandHandlerTriggers? triggers = null)
    {
        _executor = executor;
        _clock = clock ?? MomentClockSet.Default.SystemClock;
        Triggers = triggers;
    }

    public void Dispose()
        => _stopCts.CancelAndDisposeSilently();

    // Above ApiCommandDeduplicator (CommandTracer - 1_000_000): the queue re-dispatches the
    // command, and the deduplicator must see only that actual run - otherwise it waits on the
    // Uuid claim held by the dispatch that's waiting for the re-dispatch.
    [CommandFilter(Priority = CommanderCommandHandlerPriority.CommandTracer - 500_000)]
    public async Task OnCommand(IQueuedCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        if (IsRunningFromQueue(command)) {
            // Re-dispatched from the lane below, so this is the actual run
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);
        if (command.PartitionKey.IsNullOrEmpty()) {
            // No partition key means there's nothing to order this command against, so it gets
            // the retry loop and the registry but no lane. Letting it share an empty-key lane
            // would serialize every such command behind the rest - the exact head-of-line
            // blocking partitioning exists to remove.
            SetStage(command, QueuedCommandStage.Waiting, 0, null);
            await Run(command, cts.Token).ConfigureAwait(false);
            return;
        }

        // Enqueue, coalescing away the waiting predecessors if the command allows it.
        // A non-null result means the lane was idle, and this dispatch drives it until it drains.
        var toRun = _queue.Update(command.PartitionKey, pending => {
            var edits = new QueueEdits<IQueuedCommand>();
            if (command.Coalescing == QueuedCommandCoalescing.ReplaceWaiting) {
                foreach (var waiting in pending) {
                    edits.Remove(waiting);
                    _entries.TryRemove(waiting, out _);
                }
            }

            return edits.Add(command);
        });

        SetStage(command, QueuedCommandStage.Waiting, 0, null);
        while (toRun != null) {
            await Run(toRun, cts.Token).ConfigureAwait(false);
            toRun = _queue.OnCompleted(command.PartitionKey);
        }
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

    // Drops the entry once a consumer no longer needs it - e.g. an overlay that has seen the
    // server data reflect this command. Everything else just waits for the TTL.
    public void Confirm(IQueuedCommand command)
    {
        if (_entries.TryRemove(command, out var entry))
            OnEntriesChanged(entry.PartitionKey);
    }

    public int GetPendingCount(string partitionKey)
        => _queue.GetPendingCount(partitionKey);

    public void Pause()
    {
        // The attempt already in flight is left to finish; everything after it waits
        if (Volatile.Read(ref _resumeGate).Task.IsCompleted)
            Volatile.Write(ref _resumeGate, TaskCompletionSourceExt.New<Unit>());

        InvalidateAll();
    }

    public void Resume()
    {
        Volatile.Read(ref _resumeGate).TrySetResult(default);
        InvalidateAll();
    }

    // Protected/internal methods

    internal bool IsRunningFromQueue(IQueuedCommand command)
        => _runningFromQueue.ContainsKey(command);

    // Private methods

    private async Task Run(IQueuedCommand command, CancellationToken cancellationToken)
    {
        var tryIndex = 0;
        while (true) {
            // Covers the first attempt, every retry and the next command of the lane -
            // they all pass through here, so one wait point is the whole pause
            await Volatile.Read(ref _resumeGate).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            SetStage(command, QueuedCommandStage.Running, tryIndex, null);
            try {
                await RunOnce(command, cancellationToken).ConfigureAwait(false);
                SetStage(command, QueuedCommandStage.Completed, tryIndex, null);
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

    private async Task RunOnce(IQueuedCommand command, CancellationToken cancellationToken)
    {
        _runningFromQueue[command] = default;
        try {
            await _executor.Invoke(command, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _runningFromQueue.TryRemove(command, out _);
        }
    }

    private void SetStage(IQueuedCommand command, QueuedCommandStage stage, int tryIndex, Exception? error)
    {
        // A command may declare no partition at all, and the registry is keyed by string
        var partitionKey = command.PartitionKey ?? "";
        _entries[command] = new QueuedCommandEntry(
            partitionKey, command, stage, tryIndex, error, _clock.Now);
        OnEntriesChanged(partitionKey);
    }

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

    private void InvalidateAll()
    {
        Changed?.Invoke();
        if (Triggers is null)
            return;

        using (Invalidation.Begin())
            _ = Triggers.OnAnyChanged();
    }

    private void Prune()
    {
        var now = _clock.Now;
        foreach (var (command, entry) in _entries) {
            var ttl = entry.Stage switch {
                QueuedCommandStage.Completed => CompletedTtl,
                QueuedCommandStage.Failed => FailedTtl,
                _ => (TimeSpan?)null,
            };
            if (ttl is { } expiration && entry.UpdatedAt + expiration <= now)
                _entries.TryRemove(command, out _);
        }
    }
}
