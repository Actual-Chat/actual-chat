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
    private volatile Snapshot _snapshot;
    private volatile Exception? _completion; // null = running, ChannelClosedException = success, other = error

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
            var arraySize = (int)Bits.GreaterOrEqualPowerOf2((ulong)Math.Max(2, capacity + 1));
            _buffer = _pool.Rent(arraySize);
            _mask = _buffer.Length - 1; // ring buffer mask
        }
        _source = source.GetAsyncEnumerator(cancellationToken);
        _newTargets = Channel.CreateBounded<(ChannelWriter<T>, long)>(
            new BoundedChannelOptions(CoreConstants.AsyncMemoizer.TargetQueueSize) {
                SingleReader = true,
            });
        _snapshot = new Snapshot(_buffer, _buffer.Length - 1, 0, 0);
        WriteTask = BackgroundTask.Run(() => Write(cancellationToken), cancellationToken);
        ReadTask = BackgroundTask.Run(() => Read(cancellationToken), cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        _newTargets.Writer.TryComplete();
        var clearOnReturn = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        _pool.Return(_buffer, clearOnReturn);
        for (var node = _oldBuffersHead; node != null; node = node.Next)
            _pool.Return(node.Buffer, clearOnReturn);
    }

    public IAsyncEnumerable<T> Replay(CancellationToken cancellationToken = default)
        => Replay(int.MaxValue, cancellationToken);

    public async IAsyncEnumerable<T> Replay(
        int tailSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // AY: SingleWriter should be false!
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });
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
        var snapshot = _snapshot;
        var fromIndex = Math.Max(snapshot.StartIndex, snapshot.EndIndex - Math.Max(0, tailSize));
        var isCompleteCopy = await snapshot.CopyTo(channel, fromIndex, cancellationToken).ConfigureAwait(false);
        if (!isCompleteCopy)
            return;

        var copiedUpTo = snapshot.EndIndex;
        while (await _newTargets.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        while (_newTargets.Writer.TryWrite((channel, copiedUpTo)))
            return;

        if (!WriteTask.IsCompleted)
            await WriteTask.SuppressCancellationAwait(false);
        snapshot = _snapshot;
        await snapshot.CopyTo(channel, copiedUpTo, cancellationToken).ConfigureAwait(false);
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
            await _source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void WriteItem(T item)
    {
        var writePos = _totalWritten;

        if (IsUnbounded) {
            // Growing mode: ensure capacity and append
            if (writePos >= _buffer.Length) {
                var newBuffer = _pool.Rent(_buffer.Length * 2);
                Array.Copy(_buffer, newBuffer, _buffer.Length);
                _oldBuffersHead = new OldBufferNode(_buffer, _oldBuffersHead);
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

        // Snapshot always gets buffer.Length - 1 as mask (works for both modes)
        var newSnapshot = new Snapshot(_buffer, _buffer.Length - 1, startIndex, _totalWritten);
        var oldSnapshot = Interlocked.Exchange(ref _snapshot, newSnapshot);
        oldSnapshot.MarkOutdated();
    }

    private void Complete(Exception completion)
    {
        _completion = completion;
        var startIndex = IsUnbounded
            ? 0
            : _totalWritten > _capacity
                ? _totalWritten - _capacity
                : 0;
        var finalSnapshot = new Snapshot(_buffer, _buffer.Length - 1, startIndex, _totalWritten, completion);
        var old = Interlocked.Exchange(ref _snapshot, finalSnapshot);
        old.MarkOutdated();
    }

    private async Task Write(CancellationToken cancellationToken)
    {
        var closedTargets = new HashSet<ChannelWriter<T>>();
        var snapshot = await SwitchToNewSnapshot(null).ConfigureAwait(false);
        var newTargetsReadTask = _newTargets.Reader.ReadOrNone(cancellationToken);
        while (newTargetsReadTask != null || !snapshot.IsCompleted) {
            if (newTargetsReadTask != null)
                await Task.WhenAny(newTargetsReadTask, snapshot.WhenOutdated).ConfigureAwait(false);
            else
                await snapshot.WhenOutdated.ConfigureAwait(false);

            if (snapshot.WhenOutdated.IsCompleted)
                // No need to await for WhenOutdatedTask - it never fails or gets cancelled
                snapshot = await SwitchToNewSnapshot(snapshot).ConfigureAwait(false);

            if (newTargetsReadTask is not { IsCompleted: true })
                continue;

            var newTargetReads = await newTargetsReadTask.ConfigureAwait(false);
            if (!newTargetReads.IsSome(out var newTarget)) {
                newTargetsReadTask = null;
                continue;
            }

            var success = await snapshot
                .CopyTo(newTarget.Target, newTarget.CopiedUpTo, cancellationToken)
                .ConfigureAwait(false);
            if (success)
                _targets.Add(newTarget.Target);
            newTargetsReadTask = _newTargets.Reader.ReadOrNone(cancellationToken);
        }
        return;

        async Task<Snapshot> SwitchToNewSnapshot(Snapshot? oldSnapshot)
        {
            var newSnapshot = _snapshot;
            var skipUpTo = Math.Max(oldSnapshot?.EndIndex ?? 0, newSnapshot.StartIndex);
            if (newSnapshot == oldSnapshot)
                return newSnapshot;
            foreach (var target in _targets) {
                try {
                    for (var i = skipUpTo; i < newSnapshot.EndIndex; i++) {
                        var item = newSnapshot.Buffer[(int)(i & newSnapshot.Mask)];
                        if (!target.TryWrite(item))
                            await target.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                    }
                    if (newSnapshot.Completion != null) {
                        var error = newSnapshot.Completion;
                        if (error is ChannelClosedException)
                            target.TryComplete();
                        else
                            target.TryComplete(error);
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
            return newSnapshot;
        }
    }

    // Nested types

    private sealed class Snapshot(
        T[] buffer,
        int mask,
        long startIndex,
        long endIndex,
        Exception? completion = null)
    {
        private readonly AsyncTaskMethodBuilder _whenOutdatedSource = AsyncTaskMethodBuilderExt.New();

        public readonly T[] Buffer = buffer;
        public readonly int Mask = mask;
        public readonly long StartIndex = startIndex; // absolute index of oldest item
        public readonly long EndIndex = endIndex;   // absolute index past newest item
        public readonly Exception? Completion = completion;
        public Task WhenOutdated => _whenOutdatedSource.Task;
        public bool IsCompleted => Completion != null;

        public void MarkOutdated()
            => _whenOutdatedSource.TrySetResult();

        public async ValueTask<bool> CopyTo(
            ChannelWriter<T> channel,
            long fromIndex,
            CancellationToken cancellationToken)
        {
            try {
                var start = Math.Max(fromIndex, StartIndex);
                for (var i = start; i < EndIndex; i++) {
                    var item = Buffer[(int)(i & Mask)];
                    if (!channel.TryWrite(item))
                        await channel.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
                if (Completion != null) {
                    if (Completion is ChannelClosedException)
                        channel.TryComplete();
                    else
                        channel.TryComplete(Completion);
                }
                return true;
            }
            catch (ChannelClosedException) {
                return false;
            }
        }
    }

    private sealed class OldBufferNode(T[] buffer, OldBufferNode? next)
    {
        public readonly T[] Buffer = buffer;
        public readonly OldBufferNode? Next = next;
    }
}
