using System.Buffers;
using System.Runtime.ExceptionServices;

namespace ActualChat;

public abstract class AsyncMemoizer : IDisposable
{
    public static readonly ChannelClosedException SuccessfulCompletion = new();

    private volatile int _isDisposed;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        Dispose(true);
    }

    protected abstract void Dispose(bool disposing);
}

/// <summary>
/// Memoizes an async sequence into a buffer.
/// When capacity is finite, uses a ring buffer that evicts old items.
/// When capacity is <see cref="int.MaxValue"/>, uses a growing buffer that keeps all items.
/// Multiple consumers can replay the tail of the buffer and receive live updates.
/// Uses a seqlock for zero-allocation snapshot publishing and a single Write task for fan-out.
/// </summary>
public sealed class AsyncMemoizer<T> : AsyncMemoizer, IAsyncMemoizer<T>
{
    private readonly ArrayPool<T> _pool;
    private readonly int _capacity; // logical capacity (user-requested), int.MaxValue = unbounded
    private readonly int _mask; // buffer.Length - 1 for bounded mode; int.MaxValue for unbounded
    private readonly IAsyncEnumerator<T> _source;
    private readonly HashSet<ChannelWriter<T>> _targets = new();
    private readonly Channel<(ChannelWriter<T> Target, long CopiedUpTo)> _newTargets;

    private T[] _buffer;
    private OldBufferNode? _oldBuffersHead; // linked list of grown-out-of buffers (unbounded mode)
    private long _totalWritten; // absolute write position
    private volatile Exception? _completion; // null = running, ChannelClosedException = success, other = error

    // Seqlock-protected snapshot data (struct, zero allocation per write)
    private SnapshotData _snapshotData;
    private long _version; // seqlock: even = consistent, odd = write in progress

    // Shared notification for the Write task (replaces _notify channel)
    private TaskCompletionSource? _newDataSignal;

    public Task ReadTask { get; }
    public Task WriteTask { get; }

    /// <summary>
    /// Completion state: null if still running, <see cref="ChannelClosedException"/> for successful completion,
    /// or the actual exception for error completion.
    /// </summary>
    public Exception? Completion => _completion;
    public bool IsCompleted => _completion != null;
    public int Capacity => _capacity;
    public long BufferedCount => _totalWritten;

    public bool IsUnbounded => _capacity == int.MaxValue;

    public AsyncMemoizer(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
        : this(source, int.MaxValue, ArrayPool<T>.Shared, cancellationToken)
    { }

    public AsyncMemoizer(IAsyncEnumerable<T> source, ArrayPool<T> pool, CancellationToken cancellationToken)
        : this(source, int.MaxValue, pool, cancellationToken)
    { }

    public AsyncMemoizer(IAsyncEnumerable<T> source, int capacity, CancellationToken cancellationToken)
        : this(source, capacity, ArrayPool<T>.Shared, cancellationToken)
    { }

    public AsyncMemoizer(
        IAsyncEnumerable<T> source,
        int capacity,
        ArrayPool<T> pool,
        CancellationToken cancellationToken)
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
        _source = source.GetAsyncEnumerator(cancellationToken);
        _newTargets = Channel.CreateBounded<(ChannelWriter<T>, long)>(
            new BoundedChannelOptions(CoreConstants.AsyncMemoizer.TargetQueueSize) {
                SingleReader = true,
            });
        _snapshotData = new SnapshotData(_buffer, _buffer.Length - 1, 0, 0);
        _version = 0; // even = consistent
        WriteTask = BackgroundTask.Run(() => Write(cancellationToken).SuppressCancellation(), cancellationToken);
        ReadTask = BackgroundTask.Run(() => Read(cancellationToken).SuppressCancellation(), cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        _newTargets.Writer.TryComplete();
        // Wake the Write task so it can exit
        Interlocked.Exchange(ref _newDataSignal, null)?.TrySetResult();
        // Wait for tasks to stop accessing buffers before returning them to the pool
        try {
            Task.WhenAll(ReadTask, WriteTask).Wait(TimeSpan.FromSeconds(5));
        }
        catch {
            // Best-effort — tasks may have faulted or been cancelled
        }
        var clearOnReturn = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        _pool.Return(_buffer, clearOnReturn);
        for (var node = _oldBuffersHead; node != null; node = node.Next)
            _pool.Return(node.Buffer, clearOnReturn);

        // Break all reference chains to allow GC of buffered items
        _buffer = Array.Empty<T>();
        _oldBuffersHead = null;
        _snapshotData = default;
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
        var fromIndex = Math.Max(snapshot.StartIndex, snapshot.EndIndex - Math.Max(0, tailSize));
        var isCompleteCopy = await CopyTo(snapshot, channel, fromIndex, cancellationToken).ConfigureAwait(false);
        if (!isCompleteCopy)
            return;

        var copiedUpTo = snapshot.EndIndex;
        while (await _newTargets.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        while (_newTargets.Writer.TryWrite((channel, copiedUpTo))) {
            // Signal the Write task that a new target is available
            Interlocked.Exchange(ref _newDataSignal, null)?.TrySetResult();
            return;
        }

        if (!WriteTask.IsCompleted)
            await WriteTask.SuppressCancellationAwait(false);
        var (finalSnapshot, _) = ReadSnapshot();
        await CopyTo(finalSnapshot, channel, copiedUpTo, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task Read(CancellationToken cancellationToken)
    {
        try {
            var moveNext = _source.MoveNextAsync();
            while (await moveNext.ConfigureAwait(false)) {
                cancellationToken.ThrowIfCancellationRequested();
                WriteItem(_source.Current);
                moveNext = _source.MoveNextAsync();
            }
            Complete(SuccessfulCompletion);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Complete(e);
            ExceptionDispatchInfo.Capture(e).Throw();
        }
        finally {
            if (!IsCompleted)
                Complete(SuccessfulCompletion);
            _newTargets.Writer.TryComplete();
            // Signal Write task to wake up and see the completion / channel close
            Interlocked.Exchange(ref _newDataSignal, null)?.TrySetResult();
            await _source.DisposeAsync().ConfigureAwait(false);
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
                // Old buffers are cleared when returned to the pool in Dispose().
                _oldBuffersHead = new OldBufferNode(oldBuffer, _oldBuffersHead);
                _buffer = newBuffer;
            }
            _buffer[writePos] = item;
        }
        else {
            // Ring buffer mode
            _buffer[(int)(writePos & _mask)] = item;
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
        var closedTargets = new HashSet<ChannelWriter<T>>();
        long lastVersion = -1;
        var lastEndIndex = 0L;

        while (true) {
            // Wait for new data or new target registration
            var currentVersion = Volatile.Read(ref _version);
            if (currentVersion == lastVersion) {
                var signal = EnsureNewDataSignal();
                currentVersion = Volatile.Read(ref _version);
                if (currentVersion == lastVersion) {
                    // Check if _newTargets channel is closed (Read task exited)
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
            if (data.EndIndex > lastEndIndex || data.IsCompleted) {
                var skipUpTo = Math.Max(lastEndIndex, data.StartIndex);

                foreach (var target in _targets) {
                    try {
                        for (var i = skipUpTo; i < data.EndIndex; i++) {
                            var item = data.Buffer[(int)(i & data.Mask)];
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
                if (closedTargets.Count != 0) {
                    foreach (var closedTarget in closedTargets)
                        _targets.Remove(closedTarget);
                    closedTargets.Clear();
                }

                lastEndIndex = data.EndIndex;
            }

            // 2. Catch up new targets to current snapshot (after fan-out, so no duplication)
            while (_newTargets.Reader.TryRead(out var newTarget)) {
                var success = await CopyTo(data, newTarget.Target, newTarget.CopiedUpTo, cancellationToken)
                    .ConfigureAwait(false);
                if (success)
                    _targets.Add(newTarget.Target);
            }

            if (data.IsCompleted) {
                // Ensure late AddReplayTarget callers fall through to the fallback path
                _newTargets.Writer.TryComplete();
                break;
            }
        }
    }

    // Seqlock read: returns a consistent SnapshotData struct copy and its version (allocation-free)
    private (SnapshotData Data, long Version) ReadSnapshot()
    {
        while (true) {
            var v1 = Volatile.Read(ref _version);
            if ((v1 & 1) != 0) { Thread.SpinWait(1); continue; } // odd = writer mid-write, spin
            var data = _snapshotData; // struct copy
            Thread.MemoryBarrier(); // ensure struct read completes before v2 read (needed on ARM64)
            var v2 = Volatile.Read(ref _version);
            if (v1 == v2) return (data, v1);
        }
    }

    // Seqlock write: publishes new snapshot data and wakes the Write task
    private void PublishSnapshotData(SnapshotData data)
    {
        Volatile.Write(ref _version, _version + 1); // odd = write in progress (release barrier)
        _snapshotData = data;
        Volatile.Write(ref _version, _version + 1); // even = write complete (release barrier)
        Interlocked.Exchange(ref _newDataSignal, null)?.TrySetResult(); // wake Write task
    }

    // Lazy-creates a shared TCS for the Write task to wait on
    private TaskCompletionSource EnsureNewDataSignal()
    {
        var existing = Volatile.Read(ref _newDataSignal);
        if (existing != null) return existing;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return Interlocked.CompareExchange(ref _newDataSignal, tcs, null) ?? tcs;
    }

    // Copies items from snapshot to a channel writer
    private static async ValueTask<bool> CopyTo(
        SnapshotData snapshot,
        ChannelWriter<T> channel,
        long fromIndex,
        CancellationToken cancellationToken)
    {
        try {
            var start = Math.Max(fromIndex, snapshot.StartIndex);
            for (var i = start; i < snapshot.EndIndex; i++) {
                var item = snapshot.Buffer[(int)(i & snapshot.Mask)];
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
        T[] Buffer, int Mask, long StartIndex, long EndIndex, Exception? Completion = null)
    {
        public bool IsCompleted => Completion != null;
    }

    private sealed class OldBufferNode(T[] buffer, OldBufferNode? next)
    {
        public readonly T[] Buffer = buffer;
        public readonly OldBufferNode? Next = next;
    }
}
