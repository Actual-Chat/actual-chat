using ActualChat.Mesh;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisMeshLocks<TContext>(IServiceProvider services)
    : RedisMeshLocks(services.GetRequiredService<RedisDb<TContext>>(), services.Clocks().SystemClock);

public class RedisMeshLocks(RedisDb redisDb, IMomentClock? clock = null)
    : MeshLocksBase(clock)
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
            if redis.call('GET', @key) != @value then
                return -1
            end
            return 0
        """);
    private static readonly LuaScript TryReleaseScript = LuaScript.Prepare(
        """
            if redis.call('GET', @key) != @value then
                return -2
            end
            if redis.call('DEL', @key) <= 0 then
                return -1
            end
            redis.call('PUBLISH', @key, '')
            return 0
        """);
    private static readonly LuaScript ForceReleaseScript = LuaScript.Prepare(
        """
            if redis.call('DEL', @key) <= 0 then
                return -1
            end
            if @mustNotify != 0 then
                redis.call('PUBLISH', @key, '')
            end
            return 0
        """);

    private RedisDb RedisDb { get; } = redisDb.WithKeyPrefix("MeshLocks");

    public override async Task<MeshLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var storedValue = (string?)await RedisDb.Database
            .StringGetAsync((RedisKey)fullKey, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        if (storedValue == null)
            return null;

        var (value, holderId) = MeshLockHolder.ParseStoredValue(storedValue);
        return new MeshLockInfo(key, value, holderId);
    }

    public override async Task<Task> WhenChanged(Symbol key, CancellationToken cancellationToken = default)
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

    // Protected methods

    protected override async Task<bool> TryAcquire(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
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

    protected override async Task<bool> TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
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

    protected override async Task<MeshLockReleaseResult> TryRelease(Symbol key, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryReleaseScript, new {
                key = (RedisKey)fullKey,
                value = (RedisValue)value,
            }, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r switch {
            -2 => MeshLockReleaseResult.AcquiredBySomeoneElse,
            -1 => MeshLockReleaseResult.NotAcquired,
            _ => MeshLockReleaseResult.Released,
        };
    }

    protected override async Task<bool> ForceRelease(Symbol key, bool mustNotify, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = RedisDb.FullKey(key);
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(ForceReleaseScript, new {
                key = (RedisKey)fullKey,
                mustNotify = mustNotify ? 1 : 0,
            }, CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }
}
