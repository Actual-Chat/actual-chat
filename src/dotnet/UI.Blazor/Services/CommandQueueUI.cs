using ActualChat.Messaging;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Runs <see cref="IQueuedCommand"/>s one per partition at a time, retries transient failures until
/// a command gets through, and keeps every command's stage visible via <see cref="GetEntries()"/>.
/// </summary>
public class CommandQueueUI : IComputeService, IHasDisposeStatus, IDisposable
{
    public static readonly TimeSpan CompletedTtl = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FailedTtl = TimeSpan.FromMinutes(1);

    private readonly PartitionedCommandQueue<IQueuedCommand> _queue = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly CancellationToken _stopToken;

    private readonly ConcurrentDictionary<IQueuedCommand, QueuedCommandEntry> _entries =
        new(ActualLab.Collections.Slim.ReferenceEqualityComparer<IQueuedCommand>.Instance);

    private readonly Func<IQueuedCommand, CancellationToken, Task> _executor;
    private readonly Action<Exception>? _failureReporter;
    private readonly MomentClock _clock;
    private long _lastIndex;

    // Volatile-accessed: Pause/Resume publish a new gate that RunWithRetries must observe
    private TaskCompletionSource<Unit> _resumeGate =
        TaskCompletionSourceExt.New<Unit>().WithResult(default);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public bool IsPaused => !Volatile.Read(ref _resumeGate).Task.IsCompleted;
    public bool IsDisposed => _stopToken.IsCancellationRequested;

    public CommandQueueUI(UIHub hub)
        : this(
            (command, cancellationToken) => hub.Commander.Call(command, isOutermost: true, cancellationToken),
            hub.Clocks.SystemClock,
            e => hub.UICommander.ShowError(e))
    { }

    // Internal to let the tests drive the queue without a hub
    internal CommandQueueUI(
        Func<IQueuedCommand, CancellationToken, Task> executor,
        MomentClock? clock = null,
        Action<Exception>? failureReporter = null)
    {
        _stopToken = _stopCts.Token;
        _executor = executor;
        _clock = clock ?? MomentClockSet.Default.SystemClock;
        _failureReporter = failureReporter;
    }

    public void Dispose()
        => _stopCts.CancelAndDisposeSilently();

    public void Enqueue(IQueuedCommand command)
    {
        // Registered before it joins the lane: a lane already draining may start it right away,
        // and a later Waiting would overwrite its Running
        var partitionKey = command.PartitionKey ?? "";
        SetStage(command, partitionKey, QueuedCommandStage.Waiting, 0, null);
        if (partitionKey.Length == 0) {
            // No key means nothing to order the command against: it gets the retries but no lane,
            // since a shared empty-key lane would serialize every such command behind the rest
            _ = RunWithRetries(command, partitionKey);
            return;
        }

        var hasRemovedWaiting = false;
        var toRun = _queue.Update(partitionKey, pending => {
            var edits = new QueueEdits<IQueuedCommand>();
            if (command.Coalescing == QueuedCommandCoalescing.ReplaceWaiting) {
                foreach (var waiting in pending) {
                    edits.Remove(waiting);
                    hasRemovedWaiting |= _entries.TryRemove(waiting, out _);
                }
            }
            return edits.Add(command);
        });
        if (hasRemovedWaiting)
            OnEntriesChanged(partitionKey);
        // A non-null result means the lane was idle, so this call drains it
        if (toRun is not null)
            _ = DrainLane(partitionKey, toRun);
    }

    public IReadOnlyList<QueuedCommandEntry> GetEntries()
    {
        Prune();
        return _entries.Values.OrderBy(x => x.Index).ToArray();
    }

    public IReadOnlyList<QueuedCommandEntry> GetEntries(string partitionKey)
    {
        Prune();
        return _entries.Values
            .Where(x => x.PartitionKey == partitionKey)
            .OrderBy(x => x.Index)
            .ToArray();
    }

    public void Confirm(IQueuedCommand command)
    {
        // Drops the entry once a consumer no longer needs it - e.g. an overlay that has seen the
        // server data reflect this command. Everything else just waits for the TTL.
        if (_entries.TryRemove(command, out var entry))
            OnEntriesChanged(entry.PartitionKey);
    }

    public void Pause()
    {
        // The attempt already in flight is left to finish; everything after it waits
        if (Volatile.Read(ref _resumeGate).Task.IsCompleted)
            Volatile.Write(ref _resumeGate, TaskCompletionSourceExt.New<Unit>());

        OnEntriesChanged(null);
    }

    public void Resume()
    {
        Volatile.Read(ref _resumeGate).TrySetResult(default);
        OnEntriesChanged(null);
    }

    [ComputeMethod]
    public virtual Task<Unit> OnChanged(string partitionKey)
        // Carries no data: it exists to be depended upon and invalidated
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod]
    public virtual Task<Unit> OnAnyChanged()
        => ActualLab.Async.TaskExt.UnitTask;

    // Private methods

    private async Task DrainLane(string partitionKey, IQueuedCommand first)
    {
        // Every bit of state here is thread-safe, so none of the awaits needs the dispatcher
        var command = first;
        while (command is not null) {
            await RunWithRetries(command, partitionKey).ConfigureAwait(false);
            command = _queue.OnCompleted(partitionKey);
        }
    }

    private async Task RunWithRetries(IQueuedCommand command, string partitionKey)
    {
        var cancellationToken = _stopToken;
        var tryIndex = 0;
        try {
            while (true) {
                // Covers the first attempt, every retry and the next command of the lane -
                // they all pass through here, so one wait point is the whole pause
                await Volatile.Read(ref _resumeGate).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                SetStage(command, partitionKey, QueuedCommandStage.Running, tryIndex, null);
                try {
                    await _executor.Invoke(command, cancellationToken).ConfigureAwait(false);
                    SetStage(command, partitionKey, QueuedCommandStage.Completed, tryIndex, null);
                    return;
                }
                catch (Exception e) when (e is OperationCanceledException or TimeoutException
                    && !cancellationToken.IsCancellationRequested) {
                    tryIndex++;
                    SetStage(command, partitionKey, QueuedCommandStage.Retrying, tryIndex, e);
                }

                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            // Stopped along with its scope: nobody is left to show the command to
            if (cancellationToken.IsCancellationRequested)
                return;

            SetStage(command, partitionKey, QueuedCommandStage.Failed, tryIndex, e);
            _failureReporter?.Invoke(e);
        }
    }

    private void SetStage(
        IQueuedCommand command,
        string partitionKey,
        QueuedCommandStage stage,
        int tryIndex,
        Exception? error)
    {
        var index = _entries.TryGetValue(command, out var entry)
            ? entry.Index
            : Interlocked.Increment(ref _lastIndex);
        _entries[command] = new QueuedCommandEntry(
            index, partitionKey, command,
            stage, tryIndex, error, _clock.Now);
        OnEntriesChanged(partitionKey);
    }

    private void OnEntriesChanged(string? partitionKey)
    {
        using var __ = Invalidation.Begin();
        if (partitionKey is not null)
            _ = OnChanged(partitionKey);
        _ = OnAnyChanged();
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
