using ActualChat.Sharding;

namespace ActualChat.Mui;

public sealed class MuiDb(IServiceProvider services) : IMuiDb
{
    private const int MaxCacheEntries = 1000;
    private const string SchemaCacheKey = "schema";

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<MuiDatabaseInfo[]>> _schemaCache = new();

    private IAccounts Accounts => field ??= services.GetRequiredService<IAccounts>();
    private IMuiDbBackend Backend => field ??= services.GetRequiredService<IMuiDbBackend>();

    public async Task<MuiDatabaseInfo[]> GetDatabases(Session session, CancellationToken cancellationToken)
    {
        await RequireAdmin(session, cancellationToken).ConfigureAwait(false);
        var now = Environment.TickCount64;
        if (_schemaCache.TryGetValue(SchemaCacheKey, out var entry) && entry.ExpiresAt > now && !entry.Task.IsFaulted)
            return await entry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        var task = Backend.GetDatabases(RandomShardRef.Value, CancellationToken.None);
        _schemaCache[SchemaCacheKey] = new(task, now + (long)TimeSpan.FromMinutes(10).TotalMilliseconds);
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MuiSqlResult> Query(
        Session session, MuiSqlQuery query, TimeSpan cacheFor, CancellationToken cancellationToken)
    {
        await RequireAdmin(session, cancellationToken).ConfigureAwait(false);
        if (cacheFor <= TimeSpan.Zero)
            return await Backend.RunQuery(query, cancellationToken).ConfigureAwait(false);

        var key = query.CacheKey ?? $"{query.Database}|{query.MaxRows}|{query.TimeoutMs}|{query.Sql}";
        var now = Environment.TickCount64;
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > now && !entry.Task.IsFaulted)
            return await entry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        // The task is shared between callers, so it must not depend on this caller's cancellation token
        var task = Backend.RunQuery(query, CancellationToken.None);
        var newEntry = new CacheEntry(task, now + (long)cacheFor.TotalMilliseconds);
        if (_cache.Count >= MaxCacheEntries)
            Prune(now);
        _cache[key] = newEntry;
        try {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted) {
            _cache.TryRemove(new KeyValuePair<string, CacheEntry>(key, newEntry));
            throw;
        }
    }

    public async Task ClearCache(Session session, CancellationToken cancellationToken)
    {
        await RequireAdmin(session, cancellationToken).ConfigureAwait(false);
        _cache.Clear();
        _schemaCache.Clear();
    }

    // Private methods

    private async Task RequireAdmin(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
    }

    private void Prune(long now)
    {
        foreach (var (key, entry) in _cache) {
            if (entry.ExpiresAt <= now || entry.Task.IsFaulted)
                _cache.TryRemove(key, out _);
        }
    }

    // Nested types

    private sealed record CacheEntry(Task<MuiSqlResult> Task, long ExpiresAt);
    private sealed record CacheEntry<T>(Task<T> Task, long ExpiresAt);
}
