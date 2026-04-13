namespace ActualChat.Sharding;

/// <summary>
/// A shard-ownership-aware in-memory cache that maps keys to values within a sharded service.
/// Embeds <see cref="ShardOwner.RequireShardOwnership{T}"/> into access methods,
/// so compute methods automatically get shard dependency, and stale entries from
/// previous ownership epochs are evicted on access or by a periodic sweep.
/// </summary>
public sealed class ShardLocalCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();
    private readonly ShardOwner _shardOwner;
    private readonly Func<TKey, ShardOwnership, TValue> _factory;
    private readonly Action<TKey, TValue>? _onEvict;
    private readonly TimeSpan _evictionInterval;

    public ShardLocalCache(
        ShardOwner shardOwner,
        Func<TKey, ShardOwnership, TValue> factory,
        Action<TKey, TValue>? onEvict = null,
        TimeSpan? evictionInterval = null)
    {
        _shardOwner = shardOwner;
        _factory = factory;
        _onEvict = onEvict;
        _evictionInterval = evictionInterval ?? TimeSpan.FromSeconds(30);
    }

    public ValueTask<TValue> GetOrAdd(TKey key, bool addDependency, CancellationToken cancellationToken)
    {
        var ownership = _shardOwner.RequireShardOwnership(key, addDependency, cancellationToken);
        return ownership.IsCompleted
            ? GetOrAdd(key, ownership.Result)
            : CompleteAsync(key, ownership);

        async ValueTask<TValue> CompleteAsync(TKey k, ValueTask<ShardOwnership> ownershipTask)
        {
            var o = await ownershipTask.ConfigureAwait(false);
            return GetOrAdd(k, o).Result;
        }
    }

    public TValue? GetLocal(TKey key)
        => _entries.TryGetValue(key, out var entry) ? entry.Value : default;

    public bool TryRemove(TKey key, out TValue? value)
    {
        if (_entries.TryRemove(key, out var entry)) {
            value = entry.Value;
            EvictValue(key, entry.Value);
            return true;
        }
        value = default;
        return false;
    }

    public void Clear()
    {
        foreach (var (key, entry) in _entries)
            if (_entries.TryRemove(KeyValuePair.Create(key, entry)))
                EvictValue(key, entry.Value);
    }

    public async Task RunEvictionLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(_evictionInterval, cancellationToken).SilentAwait(false);
            if (cancellationToken.IsCancellationRequested)
                break;

            foreach (var (key, entry) in _entries)
                if (entry.Ownership.LockToken.IsCancellationRequested)
                    EvictEntry(key, entry);
        }
    }

    // Private methods

    private ValueTask<TValue> GetOrAdd(TKey key, ShardOwnership ownership)
    {
        if (_entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Ownership, ownership))
            return new(entry.Value);

        // Stale or missing — evict old entry if present, create new
        if (entry != null)
            EvictEntry(key, entry);

        var value = _factory(key, ownership);
        _entries[key] = new Entry(value, ownership);
        return new(value);
    }

    private void EvictEntry(TKey key, Entry entry)
    {
        if (!_entries.TryRemove(KeyValuePair.Create(key, entry)))
            return; // Already replaced by a fresh entry or removed by another thread

        EvictValue(key, entry.Value);
    }

    private void EvictValue(TKey key, TValue value)
    {
        _onEvict?.Invoke(key, value);
        if (value is IAsyncDisposable ad)
            _ = ad.DisposeAsync();
        else if (value is IDisposable d)
            d.Dispose();
    }

    // Nested types

    private sealed record Entry(TValue Value, ShardOwnership Ownership);
}
