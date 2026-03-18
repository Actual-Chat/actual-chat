using ActualLab.Redis;
using MemoryPack;
using StackExchange.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming;

internal sealed class RedisLiveStateStore<TValue>(
    RedisDb<StreamingContext> redisDb,
    string keyPrefix,
    TimeSpan ttl,
    ILogger log)
{
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

    public async Task<Dictionary<string, TValue>> GetAll(ChatId chatId)
    {
        var db = await redisDb.Database.Get().ConfigureAwait(false);
        var key = GetKey(chatId);
        var entries = await db.HashGetAllAsync(key).ConfigureAwait(false);
        var result = new Dictionary<string, TValue>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries) {
            var field = entry.Name.ToString();
            var value = MemoryPackSerializer.Deserialize<TValue>((byte[])entry.Value!);
            if (value != null)
                result[field] = value;
        }
        return result;
    }

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
