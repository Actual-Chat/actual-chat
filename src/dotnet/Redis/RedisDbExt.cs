using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public static class RedisDbExt
{
    public static async Task<TValue?> Get<TValue>(
        this RedisDb redisDb,
        string key,
        CancellationToken cancellationToken = default)
    {
        var database = await redisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var value = await database.StringGetAsync(key).ConfigureAwait(false);
        return RedisSerializer.Default.Read<TValue>(value);
    }

    public static async Task Set<TValue>(
        this RedisDb redisDb,
        string key,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        var database = await redisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await database.StringSetAsync(key, RedisSerializer.Default.Write(value)).ConfigureAwait(false);
    }

    public static async Task<bool> TrySetOnce(
        this RedisDb redisDb,
        string key,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        // A fixed-window throttle: true only for the first caller within the window
        var database = await redisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        return await database.StringSetAsync(key, "1", window, When.NotExists).ConfigureAwait(false);
    }

    public static async IAsyncEnumerable<string> ScanKeys(
        this RedisDb redisDb,
        string keyPattern = "*",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Uses SCAN rather than KEYS: KEYS blocks the server and, since StackExchange.Redis 3.0,
        // is rejected client-side unless AllowAdmin is on - which we don't want to enable.
        // SCAN may yield the same key more than once, hence the dedup via `keys`.
        var (multiplexer, _) = await redisDb.Connector.GetMultiplexer(cancellationToken).ConfigureAwait(false);
        var database = await redisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var fullPrefix = redisDb.FullKey("");
        var fullPattern = fullPrefix + keyPattern;
        var keys = new HashSet<string>();

        foreach (var endpoint in multiplexer.GetEndPoints()) {
            var server = multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica)
                continue;

            await foreach (var fullKey in server.KeysAsync(database.Database, fullPattern)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)) {
                var key = (string?)fullKey ?? "";
                key = key.Length >= fullPrefix.Length ? key[fullPrefix.Length..] : "";
                if (keys.Add(key))
                    yield return key;
            }
        }
    }
}
