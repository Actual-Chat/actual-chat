using ActualChat.Mesh;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisMeshLocks<TContext>(IServiceProvider services)
    : RedisMeshLocks(services.GetRequiredService<RedisDb<TContext>>(), services.Clocks().SystemClock),
        IMeshLocks<TContext>;

public class RedisMeshLocks(RedisDb redisDb, IMomentClock? clock = null)
    : MeshLocksBase(clock)
{
    // Useful for debugging:
    // redis.log(redis.LOG_WARNING, string.format(cjson.encode(r)))
    private static readonly string TryLockScript =
        """
            local key, value, expiresIn = KEYS[1], ARGV[1], ARGV[2]
            local r = redis.call('SET', key, value, 'NX', 'PX', expiresIn)
            local rt = type(r)
            if (rt == 'boolean' and r) or (rt == 'table' and r['ok'] == 'OK') then
                redis.call('PUBLISH', key, '+')
                return 0
            end
            return -1
        """;
    private static readonly string TryRenewScript =
        """
            local key, value, expiresIn = KEYS[1], ARGV[1], ARGV[2]
            if redis.call('GET', key) ~= value then
                return -1
            end
            redis.call('PEXPIRE', key, expiresIn, 'GT')
            if redis.call('GET', key) ~= value then
                return -1
            end
            return 0
        """;
    private static readonly string TryReleaseScript =
        """
            local key, value = KEYS[1], ARGV[1]
            if redis.call('GET', key) ~= value then
                return -2
            end
            if redis.call('DEL', key) <= 0 then
                return -1
            end
            redis.call('PUBLISH', key, '-')
            return 0
        """;
    private static readonly string ForceReleaseScript =
        """
            local key, mustNotify = KEYS[1], ARGV[1]
            if redis.call('DEL', key) <= 0 then
                return -1
            end
            if mustNotify ~= 0 then
                redis.call('PUBLISH', key, '-')
            end
            return 0
        """;

    public RedisDb RedisDb { get; } = redisDb.WithKeyPrefix("MeshLocks");
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.5, 10);

    public override async Task<MeshLockInfo?> GetInfo(Symbol key, CancellationToken cancellationToken = default)
    {
        var failureCount = 0;
        while (true) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                var storedValue = (string?)await RedisDb.Database
                    .StringGetAsync((RedisKey)key.Value, CommandFlags.DemandMaster)
                    .ConfigureAwait(false);
                if (storedValue == null)
                    return null;

                var (value, holderId) = MeshLockHolder.ParseStoredValue(storedValue);
                return new MeshLockInfo(key, value, holderId);
            }
            catch (RedisConnectionException) {
                // Intended
            }
            await Clock.Delay(RetryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task<Task> WhenChanged(Symbol key, CancellationToken cancellationToken = default)
    {
        var channel = new RedisChannel(RedisDb.FullKey(key), RedisChannel.PatternMode.Literal);
        var failureCount = 0;
        ChannelMessageQueue queue;
        while (true) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                queue = await RedisDb.Redis
                    .GetSubscriber()
                    .SubscribeAsync(channel)
                    .ConfigureAwait(false);
                break;
            }
            catch (RedisConnectionException) {
                // Intended
            }
            await Clock.Delay(RetryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
        }
        return ReadOneAndUnsubscribe();

        async Task ReadOneAndUnsubscribe()
        {
            try {
                await queue.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally {
                _ = queue.UnsubscribeAsync(CommandFlags.FireAndForget);
            }
        }
    }

    // Protected methods

    protected override async Task<bool> TryLock(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        var failureCount = 0;
        while (true) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                var r = (long)await RedisDb.Database
                    .ScriptEvaluateAsync(TryLockScript, [key.Value], [value, (long)expiresIn.TotalMilliseconds], CommandFlags.DemandMaster)
                    .ConfigureAwait(false);
                return r >= 0;
            }
            catch (RedisConnectionException) {
                // Intended
            }
            await Clock.Delay(RetryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task<bool> TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        cancellationToken.ThrowIfCancellationRequested();
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryRenewScript, [key.Value], [value, (long)expiresIn.TotalMilliseconds], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }

    protected override async Task<MeshLockReleaseResult> TryRelease(Symbol key, string value, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        cancellationToken.ThrowIfCancellationRequested();
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryReleaseScript, [key.Value], [value], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r switch {
            -2 => MeshLockReleaseResult.AcquiredBySomeoneElse,
            -1 => MeshLockReleaseResult.NotAcquired,
            0 => MeshLockReleaseResult.Released,
            _ => MeshLockReleaseResult.Unknown,
        };
    }

    protected override async Task<bool> ForceRelease(Symbol key, bool mustNotify, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        cancellationToken.ThrowIfCancellationRequested();
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(ForceReleaseScript, [key.Value], [mustNotify ? 1 : 0], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }
}
