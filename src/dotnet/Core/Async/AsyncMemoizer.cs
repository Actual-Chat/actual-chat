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
/// Uses a seqlock for zero-allocation snapshot publishing. Consumers pull directly
/// from the shared buffer — there is no dedicated write task or per-consumer channels.
/// </summary>
/// <remarks>
/// <para><b>Bounded mode safety:</b> In bounded (ring buffer) mode, a consumer that stalls
/// mid-iteration for longer than it takes the producer to wrap the entire ring could
/// theoretically read an overwritten slot. The seqlock ensures a consistent
/// (StartIndex, EndIndex) tuple, and consumers skip to StartIndex when they fall behind.
/// The only risk is during the item-reading inner loop (between two yield returns).
/// For the video use case (30 fps, capacity=150), this requires a 5+ second stall,
/// which is unrealistic. This trade-off is accepted.</para>
/// </remarks>
public sealed class AsyncMemoizer<T> : AsyncMemoizer, IAsyncMemoizer<T>
{
    private readonly ArrayPool<T> _pool;
    private readonly int _capacity; // logical capacity (user-requested), int.MaxValue = unbounded
    private readonly int _mask; // buffer.Length - 1 for bounded mode; int.MaxValue for unbounded
    private readonly IAsyncEnumerator<T> _source;

    private T[] _buffer;
    private OldBufferNode? _oldBuffersHead; // linked list of grown-out-of buffers (unbounded mode)
    private long _totalWritten; // absolute write position
    private volatile Exception? _completion; // null = running, ChannelClosedException = success, other = error

    // Seqlock-protected snapshot data (struct, zero allocation per write).
    // Protocol: version is even when consistent, odd when a write is in progress.
    // Writers: increment to odd, write struct, increment to even.
    // Readers: read version (spin if odd), copy struct, full barrier, re-read version — retry if changed.
    private SnapshotData _snapshotData;
    private long _version; // seqlock version: even = consistent, odd = write in progress

    // Shared notification signal for consumers.
    // All consumers await the same TCS instance, which is completed by WakeConsumers().
    // Race safety: EnsureNewDataSignal() may return a TCS that WakeConsumers() is about to
    // (or has already) completed. This is safe because consumers always recheck the seqlock
    // version after calling EnsureNewDataSignal() — if data arrived, they skip the await.
    // RunContinuationsAsynchronously ensures TrySetResult never runs consumer code inline
    // on the producer thread.
    private TaskCompletionSource? _newDataSignal;

    public Task ReadTask { get; }

    /// <summary>
    /// In the pull-based model there is no separate write task — consumers read directly
    /// from the shared buffer. This property aliases <see cref="ReadTask"/> for API compatibility.
    /// </summary>
    public Task WriteTask => ReadTask;

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
        _snapshotData = new SnapshotData(_buffer, _buffer.Length - 1, 0, 0);
        _version = 0; // even = consistent
        ReadTask = BackgroundTask.Run(() => Read(cancellationToken).SuppressCancellation(), cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        WakeConsumers();
        // Block until ReadTask finishes to ensure no concurrent buffer access.
        // This is a sync wait — acceptable here because Dispose is inherently synchronous
        // and the 5-second timeout prevents indefinite hangs.
        try {
            ReadTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch {
            // Best-effort — task may have faulted or been cancelled
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
        var (snapshot, _) = ReadSnapshot();
        var position = Math.Max(snapshot.StartIndex, snapshot.EndIndex - Math.Max(0, tailSize));
        await foreach (var item in ReplayFrom(position, cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public Task AddReplayTarget(ChannelWriter<T> channel, CancellationToken cancellationToken = default)
        => AddReplayTarget(channel, int.MaxValue, cancellationToken);

    public Task AddReplayTarget(
        ChannelWriter<T> channel,
        int tailSize,
        CancellationToken cancellationToken = default)
    {
        // Capture position synchronously so items written after this call are included.
        var (snapshot, _) = ReadSnapshot();
        var startPosition = Math.Max(snapshot.StartIndex, snapshot.EndIndex - Math.Max(0, tailSize));

        // Fire-and-forget: the background task pulls from the buffer and writes to the channel.
        // Callers that need to wait for completion should await channel.Reader.Completion.
        _ = Task.Run(async () => {
            try {
                await foreach (var item in ReplayFrom(startPosition, cancellationToken).ConfigureAwait(false)) {
                    if (!channel.TryWrite(item))
                        await channel.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
                channel.TryComplete();
            }
            catch (OperationCanceledException) {
                channel.TryComplete();
            }
            catch (Exception ex) {
                channel.TryComplete(ex);
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }

    // Private methods

    // Core pull-based replay loop. Consumers read directly from the shared buffer
    // using seqlock snapshots. Spin-waits briefly during burst production to batch
    // items and reduce TCS allocation/scheduling overhead.
    private async IAsyncEnumerable<T> ReplayFrom(
        long position,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var (data, version) = ReadSnapshot();

            // Bounded: if position fell behind the ring window, skip to StartIndex
            if (position < data.StartIndex)
                position = data.StartIndex;

            // Yield all available items directly from the shared buffer
            while (position < data.EndIndex) {
                yield return data.Buffer[(int)(position & data.Mask)];
                position++;
            }

            if (data.IsCompleted) {
                if (data.Completion is not ChannelClosedException)
                    ExceptionDispatchInfo.Capture(data.Completion!).Throw();
                yield break;
            }

            // Spin briefly to absorb burst production without TCS overhead.
            // During burst, the producer publishes every ~50ns; 20 spins cover ~3-6 items.
            for (var spin = 0; spin < 20; spin++) {
                Thread.SpinWait(1);
                if (Volatile.Read(ref _version) != version)
                    break;
            }
            if (Volatile.Read(ref _version) != version)
                continue;

            // No data after spinning — fall back to async TCS wait.
            // Missed-wakeup prevention: re-read version after installing TCS.
            // If data arrived between our last check and TCS creation, skip the await.
            var signal = EnsureNewDataSignal();
            var (_, recheckVersion) = ReadSnapshot();
            if (recheckVersion == version)
                await signal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

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
            WakeConsumers();
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
                // (Replay) may still be reading from it.
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

    // Seqlock read: returns a consistent SnapshotData struct copy and its version.
    // Zero allocation — SnapshotData is a readonly record struct (value type copy).
    // ARM64 safety: Thread.MemoryBarrier() after the struct copy ensures all fields
    // are fully read before we re-check the version. On x86, loads are already ordered,
    // but the barrier is required on ARM64 and other weakly-ordered architectures.
    private (SnapshotData Data, long Version) ReadSnapshot()
    {
        while (true) {
            var v1 = Volatile.Read(ref _version);       // acquire: see latest version
            if ((v1 & 1) != 0) {                        // odd = writer mid-write
                Thread.SpinWait(1);
                continue;
            }
            var data = _snapshotData;                   // struct copy (5 fields)
            Thread.MemoryBarrier();                     // full barrier: complete struct read before v2
            var v2 = Volatile.Read(ref _version);       // acquire: re-check version
            if (v1 == v2) return (data, v1);            // consistent if version unchanged
        }
    }

    // Seqlock write: publishes new snapshot data atomically (from the reader's perspective).
    // Single-writer only — called exclusively from ReadTask.
    // Protocol: odd version signals "write in progress", even signals "consistent".
    // Volatile.Write provides release semantics ensuring the struct write is visible
    // to readers before the version becomes even again.
    private void PublishSnapshotData(SnapshotData data)
    {
        Volatile.Write(ref _version, _version + 1);    // begin write (now odd)
        _snapshotData = data;                           // struct write
        Volatile.Write(ref _version, _version + 1);    // end write (now even, release)
        WakeConsumers();
    }

    // Completes the shared TCS to wake all consumers awaiting new data.
    // Uses Interlocked.Exchange to atomically take ownership of the TCS —
    // only one caller (producer or Dispose) will complete it.
    private void WakeConsumers()
        => Interlocked.Exchange(ref _newDataSignal, null)?.TrySetResult();

    // Returns a shared TCS for the caller to await. Multiple consumers share the same instance.
    // If no TCS exists, creates one with RunContinuationsAsynchronously to ensure TrySetResult
    // never runs consumer continuations inline on the producer thread.
    // The Volatile.Read fast-path is safe: if we read a non-null TCS that WakeConsumers() is
    // about to clear, we still hold a valid reference — TrySetResult will complete it, and
    // our version recheck after this call handles the "data already arrived" case.
    private TaskCompletionSource EnsureNewDataSignal()
    {
        var existing = Volatile.Read(ref _newDataSignal);
        if (existing != null) return existing;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return Interlocked.CompareExchange(ref _newDataSignal, tcs, null) ?? tcs;
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
