using ActualLab.Redis;

namespace ActualChat.Redis;

/// <summary>
/// A thin wrapper around a set of Redis Hashes sharing a common prefix
/// (applied via <see cref="RedisDb.WithKeyPrefix"/>) that stores MessagePack-serialized values.
/// Each entry is stored as a field within a hash keyed by <paramref name="hashKey"/>.
/// Supports per-field TTL via HEXPIRE (<see cref="DefaultFieldTtl"/> / fieldTtl arg)
/// and whole-hash TTL via PEXPIRE (<see cref="HashTtl"/>), refreshed on each mutation.
/// </summary>
public sealed class RedisMultiHashMap<TValue>(RedisDb redisDb, string? keyPrefix = null, ILogger? log = null)
{
    private RedisDb RedisDb { get; } = redisDb.WithKeyPrefix(keyPrefix ?? "");
    private ILogger? Log { get; } = log;

    public RedisSerializer Serializer { get; init; } = RedisSerializer.Default;
    public TimeSpan? DefaultFieldTtl { get; init; }
    public TimeSpan? HashTtl { get; init; }

    public async Task<bool> Set(string hashKey, string field, TValue value, TimeSpan? fieldTtl = null)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var isAdded = await db.HashSetAsync(hashKey, field, Serializer.Write(value)).ConfigureAwait(false);

        await Touch(hashKey, field, fieldTtl).ConfigureAwait(false);

        return isAdded;
    }

    public async Task Touch(string hashKey, string? field = null, TimeSpan? fieldTtl = null)
    {
        var effectiveFieldTtl = fieldTtl ?? DefaultFieldTtl;
        var db = await RedisDb.Database.Get().ConfigureAwait(false);

        var fieldTtlTask = (field is not null && effectiveFieldTtl is { } fTtl)
            ? db.HashFieldExpireAsync(hashKey, [field], fTtl)
            : null;
        var hashTtlTask = HashTtl is { } hTtl
            ? db.KeyExpireAsync(hashKey, hTtl)
            : null;

        if (fieldTtlTask != null && hashTtlTask != null)
            await Task.WhenAll(fieldTtlTask, hashTtlTask).ConfigureAwait(false);
        else if (fieldTtlTask != null)
            await fieldTtlTask.ConfigureAwait(false);
        else if (hashTtlTask != null)
            await hashTtlTask.ConfigureAwait(false);
    }

    public async Task<TValue?> Get(string hashKey, string field)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var raw = await db.HashGetAsync(hashKey, field).ConfigureAwait(false);
        return Serializer.Read<TValue>(raw);
    }

    public async Task<bool> Remove(string hashKey, string field)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var deleted = await db.HashDeleteAsync(hashKey, field).ConfigureAwait(false);
        if (HashTtl is { } hTtl)
            await db.KeyExpireAsync(hashKey, hTtl).ConfigureAwait(false);
        return deleted;
    }

    public async Task<Dictionary<string, TValue?>> GetHashMap(string hashKey)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var entries = await db.HashGetAllAsync(hashKey).ConfigureAwait(false);
        var result = new Dictionary<string, TValue?>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries) {
            var field = entry.Name.ToString();
            try {
                result[field] = Serializer.Read<TValue>(entry.Value);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                result[field] = default;
                Log?.LogWarning(e, "Failed to deserialize {HashKey}[{Field}] value", RedisDb.FullKey(hashKey), field);
            }
        }
        return result;
    }

    public async Task RemoveHashMap(string hashKey)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        await db.KeyDeleteAsync(hashKey).ConfigureAwait(false);
    }
}
