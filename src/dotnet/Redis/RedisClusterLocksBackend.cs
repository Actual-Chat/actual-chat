using ActualChat.ServiceMesh;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public sealed class RedisClusterLocksBackend(RedisDb redisDb, IMomentClock? clock = null)
    : ClusterLocksBackend(clock)
{
    private static readonly LuaScript TryAcquireScript = LuaScript.Prepare(
        """
            if redis.call('SET', @key, @value, 'NX', 'PX', @expiresIn) != 'OK' then
                return -1
            else
            redis.call('PUBLISH', @key, '')
            return 0
        """);
    private static readonly LuaScript TryRenewScript = LuaScript.Prepare(
        """
            if redis.call('GET', @key) != @value then
                return -1
            end
            redis.call('PEXPIRE', @key, @expiresIn, 'GT')
            redis.call('PUBLISH', @key, '')
            return 0
        """);
    private static readonly LuaScript TryReleaseScript = LuaScript.Prepare(
        """
            if redis.call('GET', @key) != @value then
                return -1
            end
            if redis.call('DEL', @key) == 1 then
                redis.call('PUBLISH', @key, '')
            end
            return 0
        """);

    private RedisDb RedisDb { get; } = redisDb;

    public override async Task<ClusterLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var storedValue = (string?)await RedisDb.Database
            .StringGetAsync((RedisKey)fullKey, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        if (storedValue == null)
            return null;

        var (value, holderId) = ClusterLockHolder.ParseStoredValue(storedValue);
        return new ClusterLockInfo(key, value, holderId);
    }

    // Private methods

    public override async Task<bool> TryAcquire(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryAcquireScript, new {
                key = (RedisKey)fullKey,
                value = (RedisValue)value,
                expiresIn = (long)expiresIn.TotalMilliseconds,
            }, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }

    public override async Task<bool> TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryRenewScript, new {
                key = (RedisKey)fullKey,
                value = (RedisValue)value,
                expiresIn = (long)expiresIn.TotalMilliseconds,
            }, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }

    public override async Task<bool> TryRelease(Symbol key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryReleaseScript, new {
                key = (RedisKey)fullKey,
                value = (RedisValue)value,
            }, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }

    public override async Task<Task> WhenChanged(Symbol key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var channel = new RedisChannel(fullKey, RedisChannel.PatternMode.Literal);
        var whenChangedSource = new TaskCompletionSource();
        Action<RedisChannel, RedisValue> handler = (_, _) => whenChangedSource.TrySetResult();

        var subscriber = RedisDb.Redis.GetSubscriber();
        await subscriber.SubscribeAsync(channel, handler, CommandFlags.DemandMaster).ConfigureAwait(false);
        return Complete();

        async Task Complete() {
            try {
                await whenChangedSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally {
                await subscriber.UnsubscribeAsync(channel, handler, CommandFlags.DemandMaster).ConfigureAwait(false);
            }
        }
    }
}
