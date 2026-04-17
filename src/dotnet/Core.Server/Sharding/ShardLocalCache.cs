using ActualLab.Rpc;

namespace ActualChat.Sharding;

/// <summary>
/// A shard-ownership-aware in-memory cache that maps keys to values within a sharded service.
/// Embeds <see cref="ShardOwner.RequireShardOwnership{T}"/> into access methods,
/// so compute methods automatically get shard dependency, and stale entries from
/// previous ownership epochs are evicted on access or by a periodic sweep.
/// </summary>
public sealed class ShardLocalCache<TKey, TValue>(
    ShardOwner shardOwner,
    Func<TKey, ShardOwnership, TValue> factory,
    Action<TKey, TValue>? disposer = null)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

    public ValueTask<TValue> GetOrAdd(TKey key, bool addDependency, CancellationToken cancellationToken)
    {
        var ownershipTask = shardOwner.RequireShardOwnership(key, addDependency, cancellationToken);
        return ownershipTask.IsCompletedSuccessfully
            ? new ValueTask<TValue>(GetOrAddImpl(key, ownershipTask.Result))
            : CompleteAsync(this, key, ownershipTask);

        static async ValueTask<TValue> CompleteAsync(
            ShardLocalCache<TKey, TValue> self,
            TKey key,
            ValueTask<ShardOwnership> ownershipTask)
        {
            var shardOwnership = await ownershipTask.ConfigureAwait(false);
            return self.GetOrAddImpl(key, shardOwnership);
        }
    }

    public TValue GetOrAdd(TKey key, ShardOwnership shardOwnership)
    {
        if (!ReferenceEquals(shardOwnership.ShardOwner, shardOwner))
            throw new ArgumentOutOfRangeException(nameof(shardOwnership), "ShardOwner mismatch.");
        if (shardOwnership.LockToken.IsCancellationRequested)
            throw new RpcRerouteException("Shard ownership is already lost.");

        return GetOrAddImpl(key, shardOwnership);
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_entries.TryGetValue(key, out var entry) || entry.Ownership.LockToken.IsCancellationRequested) {
            value = default;
            return false;
        }

        value = entry.Value;
        return true;
    }

    public bool TryRemove(TKey key, out TValue? value)
    {
        if (!_entries.TryRemove(key, out var entry)) {
            value = default;
            return false;
        }

        value = entry.Value;
        Dispose(key, entry.Value);
        return true;
    }

    public async Task Maintain(TimeSpan cleanupPeriod, CancellationToken cancellationToken)
    {
        while (true) {
            await Task.Delay(cleanupPeriod, cancellationToken).SilentAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return;

            ClearStale();
        }
    }

    public void ClearStale()
    {
        foreach (var (key, entry) in _entries)
            if (entry.Ownership.LockToken.IsCancellationRequested)
                Remove(key, entry);
    }

    public void Clear()
    {
        foreach (var (key, entry) in _entries)
            Remove(key, entry);
    }

    // Private methods

    private TValue GetOrAddImpl(TKey key, ShardOwnership shardOwnership)
    {
        // CAS retry loop: concurrent callers may race here, and only one write to _entries
        // must win per (key, ownership). We use TryAdd / TryUpdate (optimistic) so that
        // the dictionary slot is updated only if it still matches what we observed.
        // A losing CAS means another thread produced a winning entry — we dispose our
        // unused value and retry from the top, where we'll either (a) see the winner with
        // matching ownership and return its value, or (b) see a still-stale entry and try
        // to replace it again.
        while (true) {
            if (_entries.TryGetValue(key, out var entry)) {
                if (ReferenceEquals(entry.Ownership, shardOwnership))
                    return entry.Value;

                // Stale — try to replace atomically
                var newValue = factory.Invoke(key, shardOwnership);
                var newEntry = new Entry(newValue, shardOwnership);
                if (_entries.TryUpdate(key, newEntry, entry)) {
                    Dispose(key, entry.Value);
                    return newValue;
                }
                // Lost the race — discard freshly-made value and retry
                Dispose(key, newValue);
                continue;
            }

            var addedValue = factory.Invoke(key, shardOwnership);
            var addedEntry = new Entry(addedValue, shardOwnership);
            if (_entries.TryAdd(key, addedEntry))
                return addedValue;

            // Lost the race — discard and retry
            Dispose(key, addedValue);
        }
    }

    private void Remove(TKey key, Entry entry)
    {
        if (_entries.TryRemove(key, entry))
            Dispose(key, entry.Value);
    }

    private void Dispose(TKey key, TValue value)
    {
        if (disposer is not null)
            disposer.Invoke(key, value);
        else if (value is IAsyncDisposable ad)
            _ = ad.DisposeAsync();
        else if (value is IDisposable d)
            d.Dispose();
    }

    // Nested types

    private sealed record Entry(TValue Value, ShardOwnership Ownership);
}
