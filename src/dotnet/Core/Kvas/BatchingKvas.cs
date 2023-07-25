using ActualChat.IO;
using Microsoft.JSInterop;

namespace ActualChat.Kvas;

public class BatchingKvas : IKvas, IAsyncDisposable
{
    public record Options
    {
        public Func<IThreadSafeLruCache<Symbol, byte[]?>> ReadCacheFactory { get; init; } =
            () => new ThreadSafeLruCache<Symbol, byte[]?>(256);
        public Func<CancellationToken, Task>? ReadBatchDelayTaskFactory { get; init; } = null;
        public int ReadBatchConcurrencyLevel { get; init; } = 4;
        public int ReadBatchMaxSize { get; init; } = 64;
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromSeconds(0.25);
        public int FlushMaxItemCount { get; init; } = 64;
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
        ReadCache = settings.ReadCacheFactory.Invoke();
        Reader = new BatchProcessor<string, byte[]?>() {
            ConcurrencyLevel = settings.ReadBatchConcurrencyLevel,
            MaxBatchSize = settings.ReadBatchMaxSize,
            BatchingDelayTaskFactory = settings.ReadBatchDelayTaskFactory,
            Implementation = BatchRead,
        };
        Writer = new LazyWriter<(string Key, byte[]? Value)>() {
            FlushDelay = settings.FlushDelay,
            FlushMaxItemCount = settings.FlushMaxItemCount,
            FlushRetryDelays = settings.FlushRetryDelays,
            DisposeTimeout = settings.DisposeTimeout,
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

    private async Task BatchRead(List<BatchItem<string, byte[]?>> batch, CancellationToken cancellationToken)
    {
        var results = await Backend
            .GetMany(batch.Select(i => i.Input).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < batch.Count; i++) {
            var batchItem = batch[i];
            batchItem.SetResult(results[i], cancellationToken);
        }
    }

    private Task BatchWrite(List<(string Key, byte[]? Value)> batch)
        => Backend.SetMany(batch, CancellationToken.None);
}
