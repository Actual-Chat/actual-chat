using ActualLab.Diagnostics;

namespace ActualChat.Concurrency;

public class ConstrainedWorkQueue<TKey, TResult> : WorkerBase where TKey : notnull
{
    private readonly SemaphoreSlim _semaphore;
    private readonly WorkItems _workItems;
    private readonly Channel<WorkItem> _queuedItems = Channel.CreateUnbounded<WorkItem>(ChannelExt.UnboundedFanInOptions);
    private readonly ConcurrentDictionary<TKey, WorkItem> _runningItems;
    private readonly Func<TKey, CancellationToken, Task<TResult>> _taskFactory;
    private readonly IEqualityComparer<TKey>? _keyComparer;
    private readonly ILogger _log;
    private ILogger? DebugLog => _log.IfEnabled(LogLevel.Debug, CoreConstants.DebugMode.ConstrainedWorkQueue);

    public ConstrainedWorkQueue(int concurrencyLevel,
        Func<TKey, CancellationToken, Task<TResult>> taskFactory,
        ILogger log,
        IEqualityComparer<TKey>? keyComparer = null,
        bool mustStart = true)
    {
        _taskFactory = taskFactory;
        _log = log;
        _keyComparer = keyComparer;
        _semaphore = new SemaphoreSlim(concurrencyLevel);
        _workItems = new WorkItems(keyComparer);
        _runningItems = new ConcurrentDictionary<TKey, WorkItem>(keyComparer);
        if (mustStart)
            this.Start();
    }

    public Task<TResult>? Get(TKey key)
    {
        var workItem = _workItems.Get(key);
#pragma warning disable RCS1210
        return workItem?.TaskCompletionSource.Task;
#pragma warning restore RCS1210
    }

    public async Task<TResult> Execute(TKey key, string consumerId, CancellationToken cancellationToken)
    {
        var workItem = await AddInternal(key, consumerId, cancellationToken).ConfigureAwait(false);
        return await workItem.TaskCompletionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task Add(TKey key, string consumerId, CancellationToken cancellationToken)
        => AddInternal(key, consumerId, cancellationToken);

    public void Remove(string consumerId, params IEnumerable<TKey> keys)
        => Remove(consumerId, false, keys);

    public void Remove(string consumerId, bool cancelIfRunning, params IEnumerable<TKey> keys)
    {
        foreach (var key in keys)
            if (_workItems.Remove(key, consumerId) is { } workItem) {
                DebugLog?.LogDebug("Dequeued work item #{Key}", key);
                if (cancelIfRunning) {
                    workItem.CancellationTokenSource.Cancel();
                    DebugLog?.LogDebug("Requested cancellation of work item #{Key}", key);
                }
            }
    }

    public IReadOnlyList<(TKey Key, Task<TResult> Task)> ListAll()
        => ListRunning().Concat(ListQueued()).ToList();

    public IReadOnlyList<(TKey Key, Task<TResult> Task)> ListRunning()
        => _runningItems.Values.Select(x => x.AsTuple()).ToList();

    public IReadOnlyList<(TKey Key, Task<TResult> Task)> ListQueued()
        => _workItems.List().ExceptBy(_runningItems.Keys, x => x.Key, _keyComparer).ToList();

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        while (true) {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            var workItem = await _queuedItems.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
 #pragma warning disable CA2025
            _ = BackgroundTask.Run(() => ProcessWorkItem(workItem),
                _log,
                $"Failed to start processing work item #{workItem.Key}",
                cancellationToken);
 #pragma warning restore CA2025
        }
        return;

        async Task ProcessWorkItem(WorkItem workItem)
        {
            using var processCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, workItem.CancellationTokenSource.Token);
            _runningItems.TryAdd(workItem.Key, workItem);
            var processCancellationToken = processCts.Token;
            try {
                var sw = Stopwatch.StartNew();
                DebugLog?.LogDebug("Processing work item #{Key}", workItem.Key);
                var result = await _taskFactory(workItem.Key, processCancellationToken).ConfigureAwait(false);
                DebugLog?.LogDebug("Finished processing work item #{Key}, {TimeSpent}", workItem.Key, sw.Elapsed);
                workItem.TaskCompletionSource.TrySetResult(result);
            }
            catch (Exception e) {
                if (e.IsCancellationOf(processCancellationToken))
                    _log.LogDebug("Work item #{Key} cancelled", workItem.Key);
                else
                    _log.LogError(e, "Work item #{Key} failed", workItem.Key);
                workItem.TaskCompletionSource.TrySetException(e);
            }
            finally {
                _workItems.Remove(workItem.Key);
                _semaphore.Release();
                _runningItems.Remove(workItem.Key, out _);
                workItem.DisposeSilently();
            }
        }
    }

    private async Task<WorkItem> AddInternal(TKey key, string consumerId, CancellationToken cancellationToken)
    {
        if (_workItems.Add(key, consumerId, out var workItem))
            try {
                await _queuedItems.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
                DebugLog?.LogDebug("Enqueued work item #{Key}", key);
            }
            catch (Exception e) {
                _log.LogError(e, "Failed to enqueue work item #{Key}", key);
                _workItems.Remove(key, consumerId);
                throw;
            }
        return workItem;
    }

    private class WorkItems(IEqualityComparer<TKey>? keyComparer = null)
    {
        private readonly Lock _lock = new ();
        private readonly Dictionary<TKey, WorkItem> _items = new(keyComparer);

        public bool Add(TKey key, string consumerId, out WorkItem workItem)
        {
            lock (_lock) {
                workItem = _items.GetOrAdd(key, static key1 => new WorkItem(key1));
                return workItem.AddConsumer(consumerId);
            }
        }

        public WorkItem? Remove(TKey key)
        {
            lock (_lock) {
                _items.Remove(key, out var workItem);
                return workItem;
            }
        }

        public WorkItem? Remove(TKey key, string consumerId)
        {
            lock (_lock) {
                if (!_items.TryGetValue(key, out var workItem))
                    return workItem;

                workItem.RemoveConsumer(consumerId);
                if (workItem.IsEmpty)
                    _items.Remove(key);
                return workItem;
            }
        }

        public WorkItem? Get(TKey key)
        {
            lock (_lock)
                return _items.GetValueOrDefault(key);
        }

        internal IReadOnlyList<(TKey Key, Task<TResult> Task)> List()
        {
            lock (_lock)
                return _items.Values.Select(x => x.AsTuple()).ToList();
        }
    }

    private class WorkItem(TKey key) : IDisposable
    {
        private readonly HashSet<string> _consumers = new (StringComparer.Ordinal);
        public TKey Key => key;
        public TaskCompletionSource<TResult> TaskCompletionSource { get; } = new ();
        public CancellationTokenSource CancellationTokenSource { get; } = new();
        public bool IsEmpty => _consumers.Count == 0;

        public void Dispose()
            => CancellationTokenSource.Dispose();

        public bool AddConsumer(string consumerId)
            => _consumers.Add(consumerId);

        public bool RemoveConsumer(string consumerId)
            => _consumers.Remove(consumerId);

        public (TKey Key, Task<TResult> Task) AsTuple()
            => (Key, TaskCompletionSource.Task);
    }
}
