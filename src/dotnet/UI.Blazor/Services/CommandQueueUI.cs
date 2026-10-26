using ActualChat.Messaging;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Sends commands to the server and keeps each one visible via <see cref="GetEntries()"/> - the only record
/// of their state. <see cref="IOrderedCommand"/>s run one per partition at a time;
/// <see cref="IRetriedCommand"/>s wait for the connection and are retried on timeouts until they get through.
/// </summary>
public class CommandQueueUI : IComputeService, IHasDisposeStatus, IDisposable
{
    public static readonly TimeSpan CompletedTtl = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FailedTtl = TimeSpan.FromMinutes(1);

    private readonly PartitionedCommandQueue<ICommand> _queue = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly CancellationToken _stopToken;

    private readonly ConcurrentDictionary<ICommand, CommandEntry> _entries =
        new(ActualLab.Collections.Slim.ReferenceEqualityComparer<ICommand>.Instance);

    // Sorted snapshots of _entries per partition: every message on screen reads its own, and most have none
    private readonly ConcurrentDictionary<string, CommandEntry[]> _entriesByPartition = new();
    private readonly Lock _entriesByPartitionLock = new();

    private readonly ConcurrentDictionary<ICommand, PendingCall> _pendingCalls =
        new(ActualLab.Collections.Slim.ReferenceEqualityComparer<ICommand>.Instance);

    private readonly Func<ICommand, CancellationToken, Task<object?>> _executor;
    private readonly Func<CancellationToken, Task>? _whenConnected;
    private readonly Func<bool>? _isConnected;
    private readonly MomentClock _clock;
    private long _lastIndex;

    // Volatile-accessed: Pause/Resume publish a new gate that RunAttempts must observe
    private TaskCompletionSource<Unit> _resumeGate =
        TaskCompletionSourceExt.New<Unit>().WithResult(default);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public bool IsPaused => !Volatile.Read(ref _resumeGate).Task.IsCompleted;
    public bool IsDisposed => _stopToken.IsCancellationRequested;

    public CommandQueueUI(UIHub hub)
        : this(
            async (command, cancellationToken) => {
                var context = await hub.Commander.Run(command, isOutermost: true, cancellationToken)
                    .ConfigureAwait(false);
                return context.UntypedResult.Value;
            },
            hub.Clocks.SystemClock,
            cancellationToken => hub.ConnectivityUI.WhenConnected(cancellationToken),
            () => hub.ConnectivityUI.IsConnected.Value)
    { }

    // Internal to let the tests drive the queue without a hub
    internal CommandQueueUI(
        Func<ICommand, CancellationToken, Task<object?>> executor,
        MomentClock? clock = null,
        Func<CancellationToken, Task>? whenConnected = null,
        Func<bool>? isConnected = null)
    {
        _stopToken = _stopCts.Token;
        _executor = executor;
        _clock = clock ?? MomentClockSet.Default.SystemClock;
        _whenConnected = whenConnected;
        _isConnected = isConnected;
    }

    public void Dispose()
    {
        _stopCts.CancelAndDisposeSilently();
        // Commands still waiting in a lane never reach RunAttempts, so nothing else ends their tasks
        foreach (var command in _pendingCalls.Keys)
            SetCancelled(command);
    }

    public async Task<TResult> Call<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        var result = await Run(command, CommandSource.Direct, cancellationToken).ConfigureAwait(false);
        // A replaced command ends with no result: the command replacing it carries its intent
        return result is null ? default! : (TResult)result;
    }

    public Task<object?> Run(
        ICommand command,
        CommandSource source = CommandSource.Direct,
        CancellationToken cancellationToken = default)
    {
        var call = new PendingCall(TaskCompletionSourceExt.New<object?>(), source, cancellationToken);
        _pendingCalls[command] = call;
        var partitionKey = GetPartitionKey(command);
        if (partitionKey is null) {
            _ = RunAttempts(command, call, isOrdered: false);
            return call.Result.Task;
        }

        // Registered before it joins the lane: a lane already draining may start it right away,
        // and a later Waiting would overwrite its Running
        SetStage(command, CommandStage.Waiting, CommandWaitReason.Lane, 0, null);
        var coalescing = ((IOrderedCommand)command).Coalescing;
        IReadOnlyList<ICommand> replaced = [];
        var toRun = _queue.Update(partitionKey, pending => {
            var edits = new QueueEdits<ICommand>();
            if (coalescing == CommandCoalescing.ReplaceWaiting) {
                replaced = pending;
                foreach (var waiting in pending)
                    edits.Remove(waiting);
            }
            return edits.Add(command);
        });
        if (replaced.Count > 0) {
            foreach (var waiting in replaced) {
                _entries.TryRemove(waiting, out _);
                // Its intent is carried on by the command replacing it
                SetResult(waiting, null);
            }
            OnEntriesChanged(partitionKey);
        }
        // A non-null result means the lane was idle, so this call drains it
        if (toRun is not null)
            _ = DrainLane(partitionKey, toRun);
        return call.Result.Task;
    }

    public IReadOnlyList<CommandEntry> GetEntries()
        // Enumerated rather than read via Values, which takes every lock and copies the lot
        => _entries.Select(x => x.Value).OrderBy(x => x.Index).ToArray();

    public IReadOnlyList<CommandEntry> GetEntries(string partitionKey)
        => _entriesByPartition.TryGetValue(partitionKey, out var entries) ? entries : [];

    public void Pause()
    {
        // Acts as a lost connection would: the attempt already in flight is left to finish, every retried
        // command after it waits, and an ordered one with no retries fails
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

    private async Task DrainLane(string partitionKey, ICommand first)
    {
        // Every bit of state here is thread-safe, so none of the awaits needs the dispatcher
        var command = first;
        while (command is not null) {
            if (_pendingCalls.TryGetValue(command, out var call))
                await RunAttempts(command, call, isOrdered: true).ConfigureAwait(false);
            command = _queue.OnCompleted(partitionKey);
        }
    }

    private async Task RunAttempts(ICommand command, PendingCall call, bool isOrdered)
    {
        var isRetried = command is IRetriedCommand;
        using var linkedCts = call.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_stopToken, call.CancellationToken)
            : null;
        var cancellationToken = linkedCts?.Token ?? _stopToken;
        var tryIndex = 0;
        try {
            while (true) {
                if (isRetried)
                    await WhenReady(command, tryIndex, cancellationToken).ConfigureAwait(false);
                else if (isOrdered && (IsPaused || _isConnected?.Invoke() == false)) {
                    // With no retries the attempt would just wait for its connect timeout, holding up the lane.
                    // A pause stands for a lost connection, so it fails the same way.
                    throw StandardError.Unavailable("No connection.");
                }

                SetStage(command, CommandStage.Running, CommandWaitReason.None, tryIndex, null);
                try {
                    var value = await _executor.Invoke(command, cancellationToken).ConfigureAwait(false);
                    SetStage(command, CommandStage.Completed, CommandWaitReason.None, tryIndex, null);
                    SetResult(command, value);
                    return;
                }
                catch (Exception e) when (isRetried
                    && e is OperationCanceledException or TimeoutException
                    && !cancellationToken.IsCancellationRequested) {
                    tryIndex++;
                    SetStage(command, CommandStage.Retrying, CommandWaitReason.None, tryIndex, e);
                }

                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            // Stopped along with its scope: nobody is left to show the command to
            if (_stopToken.IsCancellationRequested) {
                SetCancelled(command);
                return;
            }

            SetStage(command, CommandStage.Failed, CommandWaitReason.None, tryIndex, e);
            if (cancellationToken.IsCancellationRequested)
                SetCancelled(command);
            else
                SetError(command, e);
        }
    }

    private async Task WhenReady(ICommand command, int tryIndex, CancellationToken cancellationToken)
    {
        // Covers the first attempt, every retry and the next command of the lane -
        // they all pass through here, so one wait point is the whole pause.
        // A retry keeps its Retrying stage while it waits: it has been sent once already.
        var whenResumed = Volatile.Read(ref _resumeGate).Task;
        if (!whenResumed.IsCompleted) {
            if (tryIndex == 0)
                SetStage(command, CommandStage.Waiting, CommandWaitReason.Paused, 0, null);
            await whenResumed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        if (_whenConnected is null)
            return;

        // With no connection, an attempt would only wait inside the RPC call for its connect timeout
        var whenConnected = _whenConnected.Invoke(cancellationToken);
        if (!whenConnected.IsCompleted) {
            if (tryIndex == 0)
                SetStage(command, CommandStage.Waiting, CommandWaitReason.Connection, 0, null);
            await whenConnected.ConfigureAwait(false);
        }
    }

    private void SetResult(ICommand command, object? value)
    {
        if (_pendingCalls.TryRemove(command, out var call))
            call.Result.TrySetResult(value);
    }

    private void SetError(ICommand command, Exception error)
    {
        if (!_pendingCalls.TryRemove(command, out var call))
            return;

        call.Result.TrySetException(error);
        // Marks the error as observed: the entry already shows it, and a caller may ignore the task
        _ = call.Result.Task.Exception;
    }

    private void SetCancelled(ICommand command)
    {
        if (_pendingCalls.TryRemove(command, out var call))
            call.Result.TrySetCanceled();
    }

    private void SetStage(
        ICommand command,
        CommandStage stage,
        CommandWaitReason waitReason,
        int tryIndex,
        Exception? error)
    {
        var now = _clock.Now;
        if (_entries.TryGetValue(command, out var entry)) {
            entry = entry with {
                Stage = stage, WaitReason = waitReason, TryIndex = tryIndex, Error = error, UpdatedAt = now,
            };
        }
        else {
            var source = _pendingCalls.TryGetValue(command, out var call) ? call.Source : CommandSource.Direct;
            entry = new CommandEntry(
                Interlocked.Increment(ref _lastIndex), command, source, GetPartitionKey(command),
                stage, waitReason, tryIndex, error, now, now);
        }
        _entries[command] = entry;
        OnEntriesChanged(entry.PartitionKey);
        if (entry.IsFinished)
            _ = ExpireLater(command, entry);
    }

    private async Task ExpireLater(ICommand command, CommandEntry entry)
    {
        // Removed on a timer rather than on read: whoever shows the entry must be invalidated when it goes
        var ttl = entry.Stage == CommandStage.Completed ? CompletedTtl : FailedTtl;
        try {
            await _clock.Delay(ttl, _stopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            return;
        }

        // An entry that changed since is no longer this one, and its own change planned its expiry
        if (_entries.TryRemove(KeyValuePair.Create(command, entry)))
            OnEntriesChanged(entry.PartitionKey);
    }

    private void OnEntriesChanged(string? partitionKey)
    {
        if (partitionKey is not null)
            UpdateEntriesByPartition(partitionKey);

        using var __ = Invalidation.Begin();
        if (partitionKey is not null)
            _ = OnChanged(partitionKey);
        _ = OnAnyChanged();
    }

    private void UpdateEntriesByPartition(string partitionKey)
    {
        // Every change rebuilds from the current _entries under one lock,
        // so whichever rebuild runs last sees all the changes made before it
        lock (_entriesByPartitionLock) {
            var entries = new List<CommandEntry>();
            foreach (var (_, entry) in _entries) {
                if (entry.PartitionKey == partitionKey)
                    entries.Add(entry);
            }
            if (entries.Count == 0) {
                // Keeps the index as small as the queue: a partition with no entries has no snapshot
                _entriesByPartition.TryRemove(partitionKey, out _);
                return;
            }

            entries.Sort(static (x, y) => x.Index.CompareTo(y.Index));
            _entriesByPartition[partitionKey] = entries.ToArray();
        }
    }

    private static string? GetPartitionKey(ICommand command)
        // No key means nothing to order the command against, and a shared empty-key lane
        // would serialize every such command behind the rest
        => command is IOrderedCommand { PartitionKey: { Length: > 0 } partitionKey } ? partitionKey : null;

    // Nested types

    private sealed record PendingCall(
        TaskCompletionSource<object?> Result,
        CommandSource Source,
        CancellationToken CancellationToken);
}
