using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

/// <summary>
/// A thin wrapper around a set of regular Redis keys sharing a common prefix
/// (applied via <see cref="RedisDb.WithKeyPrefix"/>) that stores MessagePack-serialized values.
/// Unlike <see cref="RedisMultiHashMap{TValue}"/>, each entry is an individual Redis key
/// and therefore can carry its own TTL.
/// </summary>
public sealed class RedisScope<TValue>(RedisDb redisDb, string? keyPrefix = null, ILogger? log = null)
{
    private RedisDb RedisDb { get; } = redisDb.WithKeyPrefix(keyPrefix ?? "");
    private ILogger? Log { get; } = log;

    public RedisSerializer Serializer { get; init; } = RedisSerializer.Default;
    public TimeSpan? DefaultTtl { get; init; }

    public async Task Set(string key, TValue value, TimeSpan? ttl = null)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var effectiveTtl = ttl ?? DefaultTtl;
        await db.StringSetAsync(key, Serializer.Write(value), effectiveTtl, When.Always).ConfigureAwait(false);
    }

    public async Task<TValue?> Get(string key)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var raw = await db.StringGetAsync(key).ConfigureAwait(false);
        return Serializer.Read<TValue>(raw);
    }

    public async Task<bool> Remove(string key)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        return await db.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    public IAsyncEnumerable<string> ListKeys(
        string keyPattern = "*",
        CancellationToken cancellationToken = default)
        => RedisDb.ScanKeys(keyPattern, cancellationToken);
}
