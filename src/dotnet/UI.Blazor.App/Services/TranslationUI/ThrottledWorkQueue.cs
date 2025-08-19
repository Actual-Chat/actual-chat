using ActualLab.Diagnostics;

namespace ActualChat.UI.Blazor.App.Services;

public class ThrottledWorkQueue<TKey, TResult> : WorkerBase where TKey : notnull
{
    private readonly SemaphoreSlim _semaphore;
    private readonly WorkItems _workItems;
    private readonly Channel<WorkItem> _queuedItems = Channel.CreateUnbounded<WorkItem>(new () {
        SingleReader = true,
    });
    private readonly ConcurrentDictionary<TKey, WorkItem> _runningItems;
    private readonly Func<TKey, CancellationToken, Task<TResult>> _taskFactory;
    private readonly ILogger _log;
    private readonly IEqualityComparer<TKey>? _keyComparer;

    public ThrottledWorkQueue(int parallelismDegree, Func<TKey, CancellationToken, Task<TResult>> taskFactory, ILogger<ThrottledWorkQueue<TKey, TResult>> log, IEqualityComparer<TKey>? keyComparer = null, bool start = true)
    {
        _taskFactory = taskFactory;
        _log = log;
        _keyComparer = keyComparer;
        _semaphore = new SemaphoreSlim(parallelismDegree);
        _workItems = new WorkItems(keyComparer);
        _runningItems = new ConcurrentDictionary<TKey, WorkItem>(keyComparer);
        if (start)
            this.Start();
    }

    private ILogger? DebugLog => _log.IfEnabled(LogLevel.Debug, Constants.DebugMode.ThrottledWorkQueue);

    public async Task<TResult> Execute(TKey key, string consumerId, CancellationToken cancellationToken)
    {
        var workItem = await EnqueueInternal(key, consumerId, cancellationToken).ConfigureAwait(false);
        return await workItem.TaskCompletionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task Enqueue(TKey key, string consumerId, CancellationToken cancellationToken)
        => EnqueueInternal(key, consumerId, cancellationToken);

    public Task<TResult>? Get(TKey key)
    {
        var workItem = _workItems.Get(key);
 #pragma warning disable RCS1210
        return workItem?.TaskCompletionSource.Task;
 #pragma warning restore RCS1210
    }

    public void Dequeue(string consumerId, params IEnumerable<TKey> keys)
        => Dequeue(consumerId, false, keys);

    public void Dequeue(string consumerId, bool cancelRunning, params IEnumerable<TKey> keys)
    {
        foreach (var key in keys)
            if (_workItems.Remove(key, consumerId) is { } workItem) {
                DebugLog?.LogDebug("Dequeued work item #{Key}", key);
                if (cancelRunning) {
                    workItem.CancellationTokenSource.Cancel();
                    DebugLog?.LogDebug("Requested cancellation of work item #{Key}", key);
                }
            }
    }

    internal IReadOnlyList<(TKey Key, Task<TResult> Task)> ListAll()
        => ListRunning().Concat(ListQueued()).ToList();

    internal IReadOnlyList<(TKey Key, Task<TResult> Task)> ListRunning()
        => _runningItems.Values.Select(x => x.AsTuple()).ToList();

    internal IReadOnlyList<(TKey Key, Task<TResult> Task)> ListQueued()
        => _workItems.List().ExceptBy(_runningItems.Keys, x => x.Key, _keyComparer).ToList();

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(DispatchQueue),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, _log)
                .RetryForever(retryDelays, _log)
            ).RunIsolated(cancellationToken);

    }

    private async Task<WorkItem> EnqueueInternal(TKey key, string consumerId, CancellationToken cancellationToken)
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

    private async Task DispatchQueue(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            var workItem = await _queuedItems.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _ = BackgroundTask.Run(() => ProcessWorkItem(workItem),
                _log,
                $"Failed to start processing work item #{workItem.Key}",
                cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
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
