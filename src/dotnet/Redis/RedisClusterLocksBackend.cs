using ActualChat.ServiceMesh;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public sealed class RedisClusterLocksBackend(RedisDb redisDb, IMomentClock? clock = null)
    : ClusterLocksBackend(clock)
{
    private static readonly LuaScript TryQueryScript = LuaScript.Prepare(
        """
            local v = redis.call('GET', @key);
            local e = redis.call('PEXPIRETIME', @key);
            if e >= 0 then
                local time = redis.call('TIME')
                local now = math.floor(time[1] * 1000 + time[2] / 1000)
                e = e - now
            end
            return { v, e }
        """);
    private static readonly LuaScript TryAcquireScript = LuaScript.Prepare(
        """
            if redis.call('SET', @key, @value, 'NX', 'PX', @expiresIn) == 'OK' then
                return -3
            else
            local e = redis.call('PEXPIRETIME', @key)
            if e >= 0 then
                local time = redis.call('TIME')
                local now = math.floor(time[1] * 1000 + time[2] / 1000)
                e = e - now
            end
        """);
    private static readonly LuaScript TryRenewScript = LuaScript.Prepare(
        """
            if redis.call('GET', @key) != @value then
                return -1
            end
            redis.call('PEXPIRE', @key, @expiresIn, 'GT')
            return 0
        """);
    private static readonly LuaScript TryReleaseScript = LuaScript.Prepare(
        """
            if redis.call('GET', @key) != @value then
                return -1
            end
            return redis.call('DEL', @key)
        """);

    private RedisDb RedisDb { get; } = redisDb;

    public override async Task<ClusterLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var r = await RedisDb.Database
            .ScriptEvaluateAsync(TryQueryScript, new { key = (RedisKey)RedisDb.FullKey(key) }, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        var v = (string?)r[0];
        if (v == null)
            return null;

        var e = (long)r[1];
        if (e == -2)
            return null; // Key doesn't exist

        var expiresAt = e switch {
            -1 => Moment.MaxValue, // Key exists, but doesn't expire
            _ => Clock.Now + TimeSpan.FromMilliseconds(e),
        };
        var (value, holderId) = ClusterLockHolder.ParseStoredValue(v);
        return new ClusterLockInfo(key, value, holderId, expiresAt);
    }

    // Private methods

    public override async Task<Moment?> TryAcquire(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
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
        var result = r switch {
            -3 => (Moment?)null, // Someone else has the lock
            -2 => null, // Somehow key doesn't exist
            -1 => Moment.MaxValue, // Key exists, but doesn't expire
            _ => Clock.Now + TimeSpan.FromMilliseconds(r),
        };
        if (result.HasValue)
            await RedisDb.Database
                .PublishAsync(RedisChannel.Literal(fullKey), RedisValue.EmptyString, CommandFlags.DemandMaster)
                .ConfigureAwait(false);
        return result;
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
        if (r < 0)
            return false;

        await RedisDb.Database
            .PublishAsync(RedisChannel.Literal(fullKey), RedisValue.EmptyString, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return true;
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
        if (r < 0)
            return false; // Someone else has the lock
        if (r == 0)
            return true; // Key wasn't there

        // Key was removed
        await RedisDb.Database
            .PublishAsync(RedisChannel.Literal(fullKey), RedisValue.EmptyString, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return true;
    }

    public override async Task WhenChanged(Symbol key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var channel = new RedisChannel(fullKey, RedisChannel.PatternMode.Literal);
        var whenChangedSource = new TaskCompletionSource();
        Action<RedisChannel, RedisValue> handler = (_, _) => whenChangedSource.TrySetResult();

        var subscriber = RedisDb.Redis.GetSubscriber();
        await subscriber.SubscribeAsync(channel, handler, CommandFlags.DemandMaster).ConfigureAwait(false);
        try {
            await whenChangedSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            await subscriber.UnsubscribeAsync(channel, handler, CommandFlags.DemandMaster).ConfigureAwait(false);
        }
    }
}
