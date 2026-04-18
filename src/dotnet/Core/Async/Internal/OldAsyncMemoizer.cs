using System.Buffers;
using System.Runtime.ExceptionServices;

namespace ActualChat.Internal;

/// <summary>
/// Legacy <see cref="IAsyncMemoizer{T}"/> implementation kept for benchmarking and
/// behavioral comparison. Do NOT use in new code — prefer <see cref="AsyncMemoizer{T}"/>.
///
/// Memoizes an async sequence into a buffer. Bounded mode uses a ring buffer that
/// hard-evicts items (lagging consumers may see gaps). Unbounded mode grows the
/// buffer. A dedicated Write sub-task fans out items to registered consumer channels
/// via a seqlock-published snapshot. The Read + Write sub-tasks run concurrently
/// inside <see cref="OnRun"/>.
/// </summary>
public sealed class OldAsyncMemoizer<T> : WorkerBase, IAsyncMemoizer<T>
{
    private readonly ArrayPool<T> _pool;
    private readonly int _capacity; // logical capacity (user-requested), int.MaxValue = unbounded
    private readonly int _mask; // buffer.Length - 1 for bounded mode; int.MaxValue for unbounded
    private IAsyncEnumerator<T>? _source;
    private readonly Dictionary<ChannelWriter<T>, int> _targets = new(); // value = next index to send
    private readonly Channel<(ChannelWriter<T> Target, int CopiedUpTo)> _newTargets;

    private T[] _buffer;
    private OldBufferNode? _oldBuffersHead; // linked list of grown-out-of buffers (unbounded mode)
    private int _totalWritten; // absolute write position
    private volatile Exception? _completion; // null = running, ChannelClosedException = success, other = error

    // Seqlock-protected snapshot data (struct, zero allocation per write)
    private SnapshotData _snapshotData;
    private long _version; // seqlock: even = consistent, odd = write in progress

    // Shared notification for the Write sub-task to wake on
    private TaskCompletionSource? _newTargetAddedSource;

    public int Capacity => _capacity;
    public bool IsUnbounded => _capacity == int.MaxValue;
    public int BufferedCount => _totalWritten;
    public Exception? Completion => _completion;
    public bool IsCompleted => _completion != null;

    public OldAsyncMemoizer(IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        : this(source, int.MaxValue, ArrayPool<T>.Shared, cancellationToken)
    { }

    public OldAsyncMemoizer(IAsyncEnumerable<T> source, ArrayPool<T> pool, CancellationToken cancellationToken = default)
        : this(source, int.MaxValue, pool, cancellationToken)
    { }

    public OldAsyncMemoizer(IAsyncEnumerable<T> source, int capacity, CancellationToken cancellationToken = default)
        : this(source, capacity, ArrayPool<T>.Shared, cancellationToken)
    { }

    public OldAsyncMemoizer(
        IAsyncEnumerable<T> source,
        int capacity,
        ArrayPool<T> pool,
        CancellationToken cancellationToken)
        : base(cancellationToken.CreateLinkedTokenSource())
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _pool = pool;
        if (capacity == int.MaxValue) {
            _buffer = _pool.Rent(16);
            _mask = int.MaxValue; // unbounded: i & int.MaxValue == i for positive values
        }
        else {
            var arraySize = (int)Bits.GreaterOrEqualPowerOf2((ulong)Math.Max(16, capacity + 1));
            _buffer = _pool.Rent(arraySize);
            _mask = _buffer.Length - 1; // ring buffer mask
        }
        _source = source.GetAsyncEnumerator(StopToken);
        _newTargets = Channel.CreateBounded<(ChannelWriter<T>, int)>(
            new BoundedChannelOptions(CoreConstants.AsyncMemoizer.TargetQueueSize) {
                SingleReader = true,
            });
        _snapshotData = new SnapshotData(_buffer, _buffer.Length - 1, 0, 0);
        _version = 0; // even = consistent
        Thread.MemoryBarrier(); // Just in case
        this.Start();
    }

    protected override async Task DisposeAsyncCore()
    {
        // Wake the Write sub-task so it can observe the stop signal and exit.
        _newTargets.Writer.TryComplete();
        Interlocked.Exchange(ref _newTargetAddedSource, null)?.TrySetResult();

        await base.DisposeAsyncCore().ConfigureAwait(false);

        var clearOnReturn = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        _pool.Return(_buffer, clearOnReturn);
        for (var node = _oldBuffersHead; node != null; node = node.Next)
            _pool.Return(node.Buffer, clearOnReturn);

        // Break all reference chains to allow GC of buffered items
        _buffer = Array.Empty<T>();
        _oldBuffersHead = null;
        _snapshotData = default;
        _source = null;
    }

    public IAsyncEnumerable<T> Replay(CancellationToken cancellationToken = default)
        => Replay(int.MaxValue, cancellationToken);

    public async IAsyncEnumerable<T> Replay(
        int tailSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // AY: SingleWriter should be false!
        var channel = tailSize == int.MaxValue
            ? Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true })
            : Channel.CreateBounded<T>(new BoundedChannelOptions(tailSize) {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        await AddReplayTarget(channel, tailSize, cancellationToken).ConfigureAwait(false);
        try {
            var reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (reader.TryRead(out var item)) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
        finally {
            channel.Writer.TryComplete();
        }
    }

    public Task AddReplayTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
        => AddReplayTarget(channel, int.MaxValue, cancellationToken);

    public async Task AddReplayTarget(
        ChannelWriter<T> channel,
        int tailSize, // Can be int.MaxValue
        CancellationToken cancellationToken = default)
    {
        var (snapshot, _) = ReadSnapshot();
        // Compute via long to avoid overflow when (EndIndex - tailSize) underflows int.
        // Result lies in [StartIndex, EndIndex], so the cast back to int is safe.
        var fromIndex = (int)Math.Max(snapshot.StartIndex, snapshot.EndIndex - (long)Math.Max(0, tailSize));
        var isCompleteCopy = await CopyTo(snapshot, channel, fromIndex, cancellationToken).ConfigureAwait(false);
        if (!isCompleteCopy)
            return;

        var copiedUpTo = snapshot.EndIndex;
        while (await _newTargets.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false)) {
            if (!_newTargets.Writer.TryWrite((channel, copiedUpTo)))
                continue;

            // Signal the Write sub-task that a new target is available
            Interlocked.Exchange(ref _newTargetAddedSource, null)?.TrySetResult();
            return;
        }

        // _newTargets is closed — wait for the Write sub-task to fully drain before
        // copying the tail ourselves. WhenRunning covers both Read and Write finishing.
        var whenRunning = WhenRunning;
        if (whenRunning != null && !whenRunning.IsCompleted)
            await whenRunning.SuppressCancellationAwait(false);
        var (finalSnapshot, _) = ReadSnapshot();
        await CopyTo(finalSnapshot, channel, copiedUpTo, cancellationToken).ConfigureAwait(false);
    }

    // Protected and private methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var readTask = Read(cancellationToken);
        var writeTask = Write(cancellationToken);
        // Await both so that faults from either propagate and WhenRunning only completes
        // after the Write sub-task has drained (needed by AddReplayTarget's fallback).
        try {
            await readTask.ConfigureAwait(false);
        }
        finally {
            await writeTask.ConfigureAwait(false);
        }
    }

    private async Task Read(CancellationToken cancellationToken)
    {
        var source = _source!;
        try {
            var moveNext = source.MoveNextAsync();
            while (await moveNext.ConfigureAwait(false)) {
                cancellationToken.ThrowIfCancellationRequested();
                WriteItem(source.Current);
                moveNext = source.MoveNextAsync();
            }
            Complete(AsyncMemoizer.SuccessfulCompletion);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Complete(e);
            ExceptionDispatchInfo.Capture(e).Throw();
        }
        finally {
            if (!IsCompleted)
                Complete(AsyncMemoizer.SuccessfulCompletion);
            _newTargets.Writer.TryComplete();
            // Signal Write sub-task to wake up and see the completion / channel close
            Interlocked.Exchange(ref _newTargetAddedSource, null)?.TrySetResult();
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void WriteItem(T item)
    {
        var writePos = _totalWritten;

        if (IsUnbounded) {
            // Growing mode: ensure capacity and append
            if (writePos >= _buffer.Length) {
                var oldBuffer = _buffer;
                var newBuffer = _pool.Rent(oldBuffer.Length * 2);
                Array.Copy(oldBuffer, newBuffer, oldBuffer.Length);
                // Note: do NOT clear oldBuffer here — concurrent snapshot readers
                // (AddReplayTarget / CopyTo) may still be reading from it.
                // Old buffers are cleared when returned to the pool in DisposeAsync().
                _oldBuffersHead = new OldBufferNode(oldBuffer, _oldBuffersHead);
                _buffer = newBuffer;
            }
            _buffer[writePos] = item;
        }
        else {
            // Ring buffer mode
            _buffer[writePos & _mask] = item;
        }
        _totalWritten = writePos + 1;

        var startIndex = IsUnbounded
            ? 0
            : _totalWritten > _capacity
                ? _totalWritten - _capacity
                : 0;

        PublishSnapshotData(new SnapshotData(_buffer, _buffer.Length - 1, startIndex, _totalWritten));
    }

    private void Complete(Exception completion)
    {
        _completion = completion;
        var startIndex = IsUnbounded
            ? 0
            : _totalWritten > _capacity
                ? _totalWritten - _capacity
                : 0;
        PublishSnapshotData(new SnapshotData(_buffer, _buffer.Length - 1, startIndex, _totalWritten, completion));
    }

    private async Task Write(CancellationToken cancellationToken)
    {
        var closedTargets = new List<ChannelWriter<T>>();
        long lastVersion = -1;
        var lastEndIndex = 0;
        while (true) {
            // Wait for new data or new target registration
            var currentVersion = Volatile.Read(ref _version);
            if (currentVersion == lastVersion) {
                var signal = EnsureNewTargetReadySource();
                currentVersion = Volatile.Read(ref _version);
                if (currentVersion == lastVersion) {
                    // Check if _newTargets channel is closed (Read sub-task exited)
                    if (_newTargets.Reader.Completion.IsCompleted && !_newTargets.Reader.TryPeek(out _))
                        break;
                    // Check for pending targets to prevent missed wakeups from AddReplayTarget
                    if (_newTargets.Reader.TryPeek(out _))
                        continue;
                    await signal.Task.ConfigureAwait(false);
                }
            }

            // Read current snapshot via seqlock
            var (data, version) = ReadSnapshot();
            lastVersion = version;

            // 1. Fan out new frames to existing targets first
            // Per-target tracking: each target has its own "next index to send" so that
            // a newly-added target with copiedUpTo > lastEndIndex doesn't receive items
            // it already got from AddReplayTarget's initial CopyTo.
            if (data.EndIndex > lastEndIndex || data.IsCompleted) {
                foreach (var (target, targetNext) in _targets) {
                    try {
                        var skipUpTo = Math.Max(targetNext, data.StartIndex);
                        for (var i = skipUpTo; i < data.EndIndex; i++) {
                            var item = data.Buffer[i & data.Mask];
                            if (!target.TryWrite(item))
                                await target.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                        }
                        if (data.IsCompleted) {
                            if (data.Completion is ChannelClosedException)
                                target.TryComplete();
                            else
                                target.TryComplete(data.Completion);
                        }
                    }
                    catch (ChannelClosedException) {
                        closedTargets.Add(target);
                    }
                }
                // Advance all live targets to data.EndIndex (in a separate pass to avoid mutating during enumeration)
                foreach (var target in _targets.Keys.ToList()) {
                    if (_targets[target] < data.EndIndex)
                        _targets[target] = data.EndIndex;
                }
                if (closedTargets.Count != 0) {
                    foreach (var closedTarget in closedTargets)
                        _targets.Remove(closedTarget);
                    closedTargets.Clear();
                }

                lastEndIndex = data.EndIndex;
            }

            // 2. If we're about to exit, close _newTargets BEFORE draining so that any
            // racing AddReplayTarget call either lands in _newTargets (and we drain it below)
            // or fails its TryWrite and falls through to the fallback path. Closing after
            // the drain would orphan targets added in between.
            if (data.IsCompleted)
                _newTargets.Writer.TryComplete();

            // 3. Catch up new targets to current snapshot (after fan-out, so no duplication)
            while (_newTargets.Reader.TryRead(out var newTarget)) {
                var success = await CopyTo(data, newTarget.Target, newTarget.CopiedUpTo, cancellationToken)
                    .ConfigureAwait(false);
                if (success) {
                    // After CopyTo, target has received items up to max(copiedUpTo, data.EndIndex).
                    // When copiedUpTo > data.EndIndex (AddReplayTarget saw a newer snapshot than
                    // Write sub-task's current view), we must record copiedUpTo so the next fan-out
                    // doesn't re-send items the target already received.
                    _targets[newTarget.Target] = Math.Max(newTarget.CopiedUpTo, data.EndIndex);
                }
            }

            if (data.IsCompleted)
                break;
        }
    }

    // Seqlock read: returns a consistent SnapshotData struct copy and its version (allocation-free)
    private (SnapshotData Data, long Version) ReadSnapshot()
    {
        while (true) {
            var v1 = Volatile.Read(ref _version);
            if ((v1 & 1) != 0) {
                Thread.SpinWait(1);
                continue;
            } // odd = writer mid-write, spin

            var data = _snapshotData; // struct copy
            Thread.MemoryBarrier(); // ensure struct read completes before v2 read (needed on ARM64)
            var v2 = Volatile.Read(ref _version);
            if (v1 == v2)
                return (data, v1);
        }
    }

    // Seqlock write: publishes new snapshot data and wakes the Write sub-task
    private void PublishSnapshotData(SnapshotData data)
    {
        Volatile.Write(ref _version, _version + 1); // odd = write in progress (release barrier)
        _snapshotData = data;
        Volatile.Write(ref _version, _version + 1); // even = write complete (release barrier)
        Interlocked.Exchange(ref _newTargetAddedSource, null)?.TrySetResult(); // wake Write sub-task
    }

    // Lazy-creates a shared TCS for the Write sub-task to wait on
    private TaskCompletionSource EnsureNewTargetReadySource()
    {
        if (Volatile.Read(ref _newTargetAddedSource) is { } existingSource)
            return existingSource;

        var newSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return Interlocked.CompareExchange(ref _newTargetAddedSource, newSource, null) ?? newSource;
    }

    // Copies items from snapshot to a channel writer
    private static async ValueTask<bool> CopyTo(
        SnapshotData snapshot,
        ChannelWriter<T> channel,
        int fromIndex,
        CancellationToken cancellationToken)
    {
        try {
            var start = Math.Max(fromIndex, snapshot.StartIndex);
            for (var i = start; i < snapshot.EndIndex; i++) {
                var item = snapshot.Buffer[i & snapshot.Mask];
                if (!channel.TryWrite(item))
                    await channel.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            if (snapshot.IsCompleted) {
                if (snapshot.Completion is ChannelClosedException)
                    channel.TryComplete();
                else
                    channel.TryComplete(snapshot.Completion);
            }
            return true;
        }
        catch (ChannelClosedException) {
            return false;
        }
    }

    // Nested types

    private readonly record struct SnapshotData(
        T[] Buffer, int Mask, int StartIndex, int EndIndex, Exception? Completion = null)
    {
        public bool IsCompleted => Completion != null;
    }

    private sealed class OldBufferNode(T[] buffer, OldBufferNode? next)
    {
        public readonly T[] Buffer = buffer;
        public readonly OldBufferNode? Next = next;
    }
}
