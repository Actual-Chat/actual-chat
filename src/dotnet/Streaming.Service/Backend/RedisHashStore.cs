using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

/// <summary>
/// A thin wrapper around a Redis Hash that stores MemoryPack-serialized values
/// keyed by <c>{prefix}:{chatId}</c>.
/// Each mutation refreshes the key's TTL so the hash self-expires when the chat becomes inactive.
/// </summary>
internal sealed class RedisHashStore<TValue>(
    RedisDb<StreamingContext> redisDb,
    string keyPrefix,
    TimeSpan ttl,
    ILogger log)
{
    /// <summary>Sets or overwrites a field in the hash and refreshes the TTL.</summary>
    public async Task<bool> SetField(ChatId chatId, string field, TValue value)
    {
        try {
            var db = await redisDb.Database.Get().ConfigureAwait(false);
            var key = GetKey(chatId);
            var bytes = MemoryPackSerializer.Serialize(value);
            await db.HashSetAsync(key, field, bytes).ConfigureAwait(false);
            await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            log.LogWarning(e, "Redis SetField failed for {KeyPrefix}:{ChatId}/{Field}", keyPrefix, chatId, field);
            return false;
        }
    }

    /// <summary>Removes a single field from the hash and refreshes the TTL.</summary>
    public async Task<bool> RemoveField(ChatId chatId, string field)
    {
        try {
            var db = await redisDb.Database.Get().ConfigureAwait(false);
            var key = GetKey(chatId);
            var deleted = await db.HashDeleteAsync(key, field).ConfigureAwait(false);
            await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
            return deleted;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            log.LogWarning(e, "Redis RemoveField failed for {KeyPrefix}:{ChatId}/{Field}", keyPrefix, chatId, field);
            return false;
        }
    }

    /// <summary>Returns all fields and their deserialized values. Skips entries that fail to deserialize.</summary>
    public async Task<Dictionary<string, TValue>> GetAll(ChatId chatId)
    {
        var db = await redisDb.Database.Get().ConfigureAwait(false);
        var key = GetKey(chatId);
        var entries = await db.HashGetAllAsync(key).ConfigureAwait(false);
        var result = new Dictionary<string, TValue>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries) {
            var field = entry.Name.ToString();
            try {
                var value = MemoryPackSerializer.Deserialize<TValue>((byte[])entry.Value!);
                if (value != null)
                    result[field] = value;
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                log.LogWarning(e, "Redis deserialization failed for {KeyPrefix}:{ChatId}/{Field}, skipping stale entry",
                    keyPrefix, chatId, field);
            }
        }
        return result;
    }

    /// <summary>Deletes the entire hash key for a chat.</summary>
    public async Task DeleteKey(ChatId chatId)
    {
        try {
            var db = await redisDb.Database.Get().ConfigureAwait(false);
            await db.KeyDeleteAsync(GetKey(chatId)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            log.LogWarning(e, "Redis DeleteKey failed for {KeyPrefix}:{ChatId}", keyPrefix, chatId);
        }
    }

    private string GetKey(ChatId chatId) => $"{keyPrefix}:{chatId}";
}
