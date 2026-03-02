using ActualLab.Diagnostics;
using ActualLab.Internal;

namespace ActualChat.Concurrency;

public sealed class ConcurrentProcessor<TKey, TResult> : WorkerBase
    where TKey : notnull
{
    private readonly Func<TKey, CancellationToken, Task<TResult>> _processor;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly ConcurrentDictionary<TKey, Item> _queue;
    private readonly Channel<Item> _channel;
    private readonly ChannelWriter<Item> _writer;
    private volatile int _enqueueCount;
    private volatile int _processedCount;

    private ILogger? Log { get; }
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, CoreConstants.DebugMode.ConcurrentProcessor);

    public int EnqueueCount => _enqueueCount;
    public int ProcessedCount => _processedCount; // Processed or removed
    public int QueueSize => _queue.Count;
    public IEnumerable<Item> Queue => _queue.Values;

    public ConcurrentProcessor(
        int concurrencyLevel,
        Func<TKey, CancellationToken, Task<TResult>> processor,
        IEqualityComparer<TKey>? keyComparer = null,
        ILogger? log = null,
        bool mustStart = true)
    {
        Log = log;
        _processor = processor;
        _concurrencyGate = new SemaphoreSlim(concurrencyLevel);
        _queue = new ConcurrentDictionary<TKey, Item>(keyComparer);
        _channel = Channel.CreateUnbounded<Item>(ChannelExt.UnboundedFanInOptions);
        _writer = _channel.Writer;
        StopToken.Register(() => _channel.Writer.Complete());
        if (mustStart)
            this.Start();
    }

    public Item? Get(TKey key)
        => _queue.GetValueOrDefault(key);

    public Item Enqueue(TKey key)
    {
        if (StopToken.IsCancellationRequested)
            throw Errors.AlreadyDisposed(GetType());

        var item = new Item(this, key);
        var addedItem = _queue.GetOrAdd(key, item);
        if (!ReferenceEquals(addedItem, item))
            return addedItem;

        Interlocked.Increment(ref _enqueueCount);
        try {
            if (!_writer.TryWrite(item)) {
                // If we're here, OnRun loop is ended
                Remove(item, true);
                return item;
            }

            DebugLog?.LogDebug("Enqueued item #{Key}", key);
            return item;
        }
        catch (Exception e) {
            Log?.LogError(e, "Failed to enqueue item #{Key}", key);
            Remove(item, true);
            throw;
        }
    }

    public bool Remove(Item item, bool cancelRunning)
    {
        if (!_queue.TryRemove(item.Key, item))
            return false;

        // Remove wasn't called for this item yet
        Interlocked.Increment(ref _processedCount);
        item.Remove(cancelRunning);
        return true;
    }

    public bool Remove(TKey key, bool cancelRunning)
    {
        if (!_queue.TryRemove(key, out var item))
            return false;

        // Remove wasn't called for this item yet
        Interlocked.Increment(ref _processedCount);
        item.Remove(cancelRunning);
        return true;
    }

    public void RemoveMany(bool cancelRunning, params ReadOnlySpan<TKey> keys)
    {
        foreach (var key in keys)
            Remove(key, cancelRunning);
    }

    public void RemoveMany(bool cancelRunning, params ReadOnlySpan<Item> items)
    {
        foreach (var item in items)
            Remove(item, cancelRunning);
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // The channel is completed on StopToken cancellation, see .ctor. - that's why we pass CancellationToken.None
        var readAllTask = _channel.Reader.ReadAllAsync(CancellationToken.None);
        await foreach (var item in readAllTask.ConfigureAwait(false))
            _ = item.Process();
    }

    // Nested types

    public sealed class Item
    {
        private readonly AsyncTaskMethodBuilder<TResult> _resultSource;
        private readonly CancellationTokenSource _stopTokenSource;
        private object Lock => _stopTokenSource;

        public readonly ConcurrentProcessor<TKey, TResult> Owner;
        public readonly TKey Key;
        public readonly CancellationToken StopToken;
        public Task<TResult> ResultTask => _resultSource.Task;
        public Task<TResult>? ProcessTask { get; private set; }
        public bool IsStarted => ProcessTask != null;

        public Item(ConcurrentProcessor<TKey, TResult> owner, TKey key)
        {
            Owner = owner;
            Key = key;
            _resultSource = AsyncTaskMethodBuilderExt.New<TResult>();
            _stopTokenSource = Owner.StopToken.CreateLinkedTokenSource();
            StopToken = _stopTokenSource.Token;
        }

        internal bool Remove(bool cancelRunning)
        {
            var debugLog = Owner.DebugLog;
            lock (Lock) {
                if (ProcessTask != null) {
                    if (cancelRunning) {
                        _stopTokenSource.CancelAndDisposeSilently();
                        debugLog?.LogDebug("Cancelled already running item #{Key}", Key);
                    }
                    else
                        debugLog?.LogDebug("Too late to remove already running item #{Key}", Key);
                    return false;
                }

                // Not started yet
                _stopTokenSource.CancelAndDisposeSilently();
                ProcessTask = Task.FromCanceled<TResult>(StopToken);
                _resultSource.TrySetCanceled(StopToken);
                debugLog?.LogDebug("Removed item #{Key}", Key);
                return true;
            }
        }

        internal async Task Process()
        {
            var cancellationToken = StopToken;
            var key = Key;
            var concurrencyGate = Owner._concurrencyGate;
            var debugLog = Owner.DebugLog;
            var alreadyRemoved = false;

            try {
                await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                Owner.Remove(this, true);
                return;
            }

            try {
                var startedAt = CpuTimestamp.Now;
                lock (Lock) {
                    alreadyRemoved = ProcessTask != null;
                    if (alreadyRemoved)
                        return;

                    debugLog?.LogDebug("Processing item #{Key}", key);
                    ProcessTask ??= Task.Run(() => Owner._processor.Invoke(key, cancellationToken), cancellationToken);
                }
                var result = await ProcessTask.ConfigureAwait(false);
                _resultSource.TrySetResult(result);
                debugLog?.LogDebug("Processed item #{Key} in {Elapsed}", key, startedAt.Elapsed.ToShortString());
            }
            catch (Exception e) {
                if (e.IsCancellationOf(cancellationToken)) {
                    _resultSource.TrySetCanceled(cancellationToken);
                    debugLog?.LogDebug("Item #{Key} cancelled", key);
                }
                else {
                    _resultSource.TrySetException(e);
                    Owner.Log?.LogError(e, "Item #{Key} failed", key);
                }
            }
            finally {
                if (alreadyRemoved)
                    debugLog?.LogDebug("Skipping already removed item #{Key}", key);
                else if (Owner._queue.TryRemove(key, this))
                    Interlocked.Increment(ref Owner._processedCount);
                concurrencyGate.Release();
                _stopTokenSource.CancelAndDisposeSilently();
            }
        }
    }
}
