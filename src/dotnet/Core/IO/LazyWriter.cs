using Stl.Internal;

namespace ActualChat.IO;

public class LazyWriter<T> : IAsyncDisposable
{
    private readonly List<T> _buffer = new();
    private long _itemIndex;
    private long _flushedItemIndex;
    private Task _flushTask = Task.CompletedTask;
    private CancellationTokenSource? _cancelFlushDelayCts;
    private bool _isDisposed;
    private object Lock => _buffer;

    public int FlushMaxItemCount { get; init; } = 64;
    public TimeSpan FlushDelay { get; init; } = TimeSpan.FromMilliseconds(1);
    public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public RetryDelaySeq FlushRetryDelays { get; init; } = new();
    public Func<List<T>, Task> Implementation { get; init; } = _ => Task.CompletedTask;
    public IMomentClock Clock { get; init; } = MomentClockSet.Default.CpuClock;
    public ILogger Log { get; init; } = NullLogger.Instance;

    public async ValueTask DisposeAsync()
    {
        lock (Lock) {
            if (_isDisposed)
                return;
            _isDisposed = true;
            UnsafeEndFlushDelay();
        }
        try {
            using var disposeDelayCts = new CancellationTokenSource(DisposeTimeout);
            await Flush(disposeDelayCts.Token).ConfigureAwait(false);
        }
        catch {
            // Intended
        }
    }

    public void Add(T item)
    {
        lock (Lock) {
            if (_isDisposed)
                throw Errors.AlreadyDisposedOrDisposing();
            _buffer.Add(item);
            _itemIndex++;
            if (_buffer.Count < FlushMaxItemCount) {
                UnsafeDelayedFlush(FlushDelay);
                return;
            }
        }
        Flush();
    }

    public Task Flush(CancellationToken cancellationToken = default)
    {
        lock (Lock) // Lock is needed here b/c of _itemIndex access
            return Flush(_itemIndex, cancellationToken);
    }

    // Private methods

    private async Task Flush(long expectedFlushedItemIndex, CancellationToken cancellationToken)
    {
        while (true) {
            Task flushTask;
            lock (Lock) {
                if (expectedFlushedItemIndex <= _flushedItemIndex)
                    return;
                flushTask = UnsafeDelayedFlush(TimeSpan.Zero).WaitAsync(cancellationToken);
            }
            await flushTask.ConfigureAwait(false);
        }
    }

    private Task UnsafeDelayedFlush(TimeSpan delay)
    {
        // This method has to be called from inside lock (Lock) block!

        if (!_flushTask.IsCompleted) {
            if (delay <= TimeSpan.Zero && _cancelFlushDelayCts != null)
                UnsafeEndFlushDelay();
            return _flushTask;
        }
        if (_buffer.Count == 0)
            return _flushTask;

        var delayTask = Task.CompletedTask;
        if (delay > TimeSpan.Zero && !_isDisposed) {
            UnsafeEndFlushDelay();
            _cancelFlushDelayCts = new CancellationTokenSource();
            delayTask = Clock.Delay(FlushDelay, _cancelFlushDelayCts.Token);
        }
        return _flushTask = DelayedFlush(delayTask);
    }

    private async Task DelayedFlush(Task delayTask)
    {
        if (!delayTask.IsCompleted)
            try {
                await delayTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Intended
            }

        var flushBuffer = new List<T>();
        var failedTryCount = 0;
        while (true) {
            try {
                long flushedItemIndex;
                lock (Lock) {
                    if (_isDisposed)
                        return;
                    UnsafeEndFlushDelay();
                    flushBuffer.AddRange(_buffer);
                    _buffer.Clear();
                    flushedItemIndex = _itemIndex;
                }
                await Implementation.Invoke(flushBuffer).ConfigureAwait(false);
                lock (Lock) {
                    _flushedItemIndex = flushedItemIndex;
                }
                return;
            }
            catch (Exception e) {
                failedTryCount++;
                var retryDelay = FlushRetryDelays[failedTryCount];
                Log.LogError(e,
                    "Error #{ErrorCount} while flushing a batch of {ItemCount} items, will retry in {RetryDelay}",
                    failedTryCount, flushBuffer.Count, retryDelay.ToShortString());
                await Clock.Delay(retryDelay).ConfigureAwait(false);
            }
        }
    }

    private void UnsafeEndFlushDelay()
    {
        _cancelFlushDelayCts.CancelAndDisposeSilently(); // Instant flush in this case
        _cancelFlushDelayCts = null;
    }
}
