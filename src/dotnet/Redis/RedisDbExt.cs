using ActualLab.Redis;

namespace ActualChat.Redis;

public static class RedisDbExt
{
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
