using ActualChat.IO;
using Microsoft.JSInterop;

namespace ActualChat.Kvas;

public class BatchingKvas : SafeAsyncDisposableBase, IKvas
{
    public record Options
    {
        public int ReaderBatchSize { get; init; } = 64;
        public IBatchProcessorWorkerPolicy ReaderWorkerPolicy { get; init; }
            = new BatchProcessorWorkerPolicy() { MaxWorkerCount = 4 };
        public Func<IThreadSafeLruCache<Symbol, byte[]?>> ReaderCacheFactory { get; init; }
            = () => new ThreadSafeLruCache<Symbol, byte[]?>(256);
        public int FlushBatchSize { get; init; } = 64;
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromSeconds(0.25);
        public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(3);
        public RetryDelaySeq FlushRetryDelays { get; init; } = new();
    }

    private ILogger? _log;

    protected IThreadSafeLruCache<Symbol, byte[]?> ReadCache { get; }
    protected BatchProcessor<string, byte[]?> Reader { get; }
    protected LazyWriter<(string Key, byte[]? Value)> Writer { get; }
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    public Options Settings { get; }
    public IServiceProvider Services { get; }
    public IBatchingKvasBackend Backend { get; init; } = null!; // Must be set by descendant!

    public BatchingKvas(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        ReadCache = settings.ReaderCacheFactory.Invoke();
        Reader = new BatchProcessor<string, byte[]?>() {
            BatchSize = settings.ReaderBatchSize,
            WorkerPolicy = settings.ReaderWorkerPolicy,
            Implementation = ReadBatch,
        };
        Writer = new LazyWriter<(string Key, byte[]? Value)>() {
            FlushDelay = settings.FlushDelay,
            FlushMaxItemCount = settings.FlushBatchSize,
            FlushRetryDelays = settings.FlushRetryDelays,
            DisposeTimeout = settings.DisposeTimeout,
            Implementation = WriteBatch,
            FlushErrorSeverityProvider = e =>
                e is JSDisconnectedException or ObjectDisposedException or OperationCanceledException
                    ? LogLevel.None
                    : LogLevel.Warning,
            Log = Log,
        };
    }

    protected override async Task DisposeAsync(bool disposing)
    {
        await Writer.DisposeAsync().ConfigureAwait(false);
        await Reader.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
    {
        if (ReadCache.TryGetValue(key, out var value))
            return ValueTask.FromResult(value)!;
        return Reader.Process(key, cancellationToken).ToValueTask();
    }

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
    {
        ReadCache[key] = value;
        Writer.Add((key, value));
        return Task.CompletedTask;
    }

    public async Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
    {
        foreach (var (key, value) in items)
            await Set(key, value, cancellationToken).ConfigureAwait(false);
    }

    public virtual Task Flush(CancellationToken cancellationToken = default)
        => Writer.Flush(cancellationToken);

    public virtual async Task Clear(CancellationToken cancellationToken = default)
    {
        await Flush(cancellationToken).ConfigureAwait(false);
        await Backend.Clear(cancellationToken).ConfigureAwait(false);
        ClearReadCache();
    }

    public void ClearReadCache()
        => ReadCache.Clear();

    // Private methods

    private async Task ReadBatch(List<BatchProcessor<string, byte[]?>.Item> batch, CancellationToken cancellationToken)
    {
        var results = await Backend
            .GetMany(batch.Select(i => i.Input).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < batch.Count; i++) {
            var batchItem = batch[i];
            batchItem.SetResult(results[i], cancellationToken);
        }
    }

    private Task WriteBatch(List<(string Key, byte[]? Value)> batch)
        => Backend.SetMany(batch, CancellationToken.None);
}
