using Stl.OS;

namespace ActualChat.Kvas;

public class KvasForBackend : IKvas
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
        public RetryDelaySeq FlushRetryDelays { get; init; } = new();
    }

    protected IKvasBackend Backend { get; }
    protected IThreadSafeLruCache<Symbol, string?> ReadCache { get; }
    protected BatchProcessor<Symbol, string?> Reader { get; }
    protected LazyWriter<(Symbol Key, string? Value)> Writer { get; }
    protected ILogger Log { get; }

    public KvasForBackend(Options options, IKvasBackend backend, ILogger<KvasForBackend>? log = null)
    {
        Log = log ?? NullLogger<KvasForBackend>.Instance;
        Backend = backend;
        ReadCache = options.ReadCacheFactory.Invoke();
        Reader = new BatchProcessor<Symbol, string?>() {
            ConcurrencyLevel = options.ReadBatchConcurrencyLevel,
            MaxBatchSize = options.ReadBatchMaxSize,
            BatchingDelayTaskFactory = options.ReadBatchDelayTaskFactory,
            Implementation = BatchRead,
        };
        Writer = new LazyWriter<(Symbol Key, string? Value)>() {
            FlushDelay = options.FlushDelay,
            FlushLimit = options.FlushMaxItemCount,
            FlushRetryDelays = options.FlushRetryDelays,
            Implementation = BatchWrite,
            Log = Log,
        };
    }

    public ValueTask<string?> Get(Symbol key, CancellationToken cancellationToken = default)
    {
        if (ReadCache.TryGetValue(key, out var value))
            return ValueTask.FromResult(value);
        return Reader.Process(key, cancellationToken).ToValueTask();
    }

    public void Set(Symbol key, string? value)
    {
        ReadCache[key] = value;
        Writer.Add((key, value));
    }

    public Task Flush(CancellationToken cancellationToken = default)
        => Writer.Flush().WaitAsync(cancellationToken);

    // Private methods

    private async Task BatchRead(List<BatchItem<Symbol, string?>> batch, CancellationToken cancellationToken)
    {
        var results = await Backend
            .GetMany(batch.Select(i => i.Input).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < batch.Count; i++) {
            var batchItem = batch[i];
            batchItem.SetResult(results[i], cancellationToken);
        }
    }

    private Task BatchWrite(List<(Symbol Key, string? Value)> batch)
        => Backend.SetMany(batch, CancellationToken.None);
}
