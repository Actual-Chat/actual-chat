namespace ActualChat.Collections;

public abstract class FlushingKeyValueStore : WorkerBase
{
    protected readonly ConcurrentDictionary<HashedString, string?> WriteCache = new();

    public ILogger? Log { get; init; }
    public RandomTimeSpan FlushPeriod { get; init; } = TimeSpan.FromSeconds(1).ToRandom(0.1);
    public RetryDelaySeq FlushRetryDelays { get; init; } = new(0.25, 1);

    public ValueTask<string?> Get(HashedString key, CancellationToken cancellationToken = default)
        => WriteCache.TryGetValue(key, out var value)
            ? ValueTask.FromResult((string?)value)
            : StorageGet(key, cancellationToken);

    public void Set(HashedString key, string? value)
        => WriteCache.AddOrUpdate(key, static (_, v) => v, static (_, _, v) => v, value);

    public ValueTask Clear()
    {
        WriteCache.Clear();
        return StorageClear();
    }

    public virtual async Task Flush(CancellationToken cancellationToken = default)
    {
        var itemCount = 0;
        var sw = new Stopwatch();
        while (true) {
            // Likely it's faster to enumerate concurrent dictionary this way
            var batch = WriteCache.Take(32).ToList();
            if (batch.Count == 0)
                break;

            sw.Start();
            foreach (var (key, value) in batch) {
                await StorageSet(key, value, cancellationToken).ConfigureAwait(false);
                WriteCache.TryRemove(new KeyValuePair<HashedString, string>(key, value));
            }
            sw.Stop();
            itemCount += batch.Count;
        }
        if (itemCount > 0)
            Log?.LogInformation("Flushed {ItemCount} item(s) in {Duration}", itemCount, sw.Elapsed.ToShortString());
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        try {
            await new AsyncChain(nameof(Flush), Flush)
                .LogError(Log)
                .RetryForever(FlushRetryDelays, Log)
                .AppendDelay(FlushPeriod)
                .CycleForever()
                .PrependDelay(FlushPeriod)
                .RunIsolated(cancellationToken)
                .ConfigureAwait(false);
        }
        finally {
            await Flush(CancellationToken.None).ConfigureAwait(false);
        }
    }

    protected abstract ValueTask<string?> StorageGet(HashedString key, CancellationToken cancellationToken);
    protected abstract ValueTask StorageSet(HashedString key, string? value, CancellationToken cancellationToken);
    protected abstract ValueTask StorageClear();
}
