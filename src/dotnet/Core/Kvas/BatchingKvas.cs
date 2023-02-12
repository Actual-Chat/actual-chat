using ActualChat.IO;
using Microsoft.JSInterop;

namespace ActualChat.Kvas;

public class BatchingKvas : IKvas, IAsyncDisposable
{
    public record Options
    {
        public Func<IThreadSafeLruCache<Symbol, string?>> ReadCacheFactory { get; init; } =
            () => new ThreadSafeLruCache<Symbol, string?>(1024);
        public Func<CancellationToken, Task>? ReadBatchDelayTaskFactory { get; init; } = null;
        public int ReadBatchConcurrencyLevel { get; init; } = 4;
        public int ReadBatchMaxSize { get; init; } = 64;
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromMilliseconds(100);
        public int FlushMaxItemCount { get; init; } = 64;
        public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(3);
        public RetryDelaySeq FlushRetryDelays { get; init; } = new();
    }

    protected Options Settings { get; }
    protected IBatchingKvasBackend Backend { get; }
    protected IThreadSafeLruCache<Symbol, string?> ReadCache { get; }
    protected BatchProcessor<string, string?> Reader { get; }
    protected LazyWriter<(string Key, string? Value)> Writer { get; }
    protected ILogger Log { get; }

    public BatchingKvas(Options options, IBatchingKvasBackend backend, ILogger<BatchingKvas>? log = null)
    {
        Settings = options;
        Log = log ?? NullLogger<BatchingKvas>.Instance;
        Backend = backend;
        ReadCache = options.ReadCacheFactory.Invoke();
        Reader = new BatchProcessor<string, string?>() {
            ConcurrencyLevel = options.ReadBatchConcurrencyLevel,
            MaxBatchSize = options.ReadBatchMaxSize,
            BatchingDelayTaskFactory = options.ReadBatchDelayTaskFactory,
            Implementation = BatchRead,
        };
        Writer = new LazyWriter<(string Key, string? Value)>() {
            FlushDelay = options.FlushDelay,
            FlushMaxItemCount = options.FlushMaxItemCount,
            FlushRetryDelays = options.FlushRetryDelays,
            DisposeTimeout = options.DisposeTimeout,
            Implementation = BatchWrite,
            FlushErrorSeverityProvider = e =>
                e is JSDisconnectedException or ObjectDisposedException or OperationCanceledException
                    ? LogLevel.None
                    : LogLevel.Warning,
            Log = Log,
        };
    }

    public virtual ValueTask DisposeAsync()
        => Writer.DisposeAsync();

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
    {
        if (ReadCache.TryGetValue(key, out var value))
            return ValueTask.FromResult(value)!;
        return Reader.Process(key, cancellationToken).ToValueTask();
    }

    public Task Set(string key, string? value, CancellationToken cancellationToken = default)
    {
        ReadCache[key] = value;
        Writer.Add((key, value));
        return Task.CompletedTask;
    }

    public async Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default)
    {
        foreach (var (key, value) in items)
            await Set(key, value, cancellationToken).ConfigureAwait(false);
    }

    public Task Flush(CancellationToken cancellationToken = default)
        => Writer.Flush(cancellationToken);

    public void ClearReadCache()
        => ReadCache.Clear();

    // Private methods

    private async Task BatchRead(List<BatchItem<string, string?>> batch, CancellationToken cancellationToken)
    {
        var results = await Backend
            .GetMany(batch.Select(i => i.Input).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < batch.Count; i++) {
            var batchItem = batch[i];
            batchItem.SetResult(results[i], cancellationToken);
        }
    }

    private Task BatchWrite(List<(string Key, string? Value)> batch)
        => Backend.SetMany(batch, CancellationToken.None);
}
