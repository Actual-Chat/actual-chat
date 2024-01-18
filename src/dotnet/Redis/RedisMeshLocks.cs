using ActualChat.Mesh;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisMeshLocks<TContext>(IServiceProvider services)
    : RedisMeshLocks(services.GetRequiredService<RedisDb<TContext>>(), services.Clocks().SystemClock),
        IMeshLocks<TContext>;

public class RedisMeshLocks : MeshLocksBase
{
    // Useful for debugging:
    // redis.log(redis.LOG_WARNING, string.format(cjson.encode(r)))
    private static readonly string TryLockScript =
        """
            local key, anyLockKey, value, expiresIn = KEYS[1], KEYS[2], ARGV[1], ARGV[2]
            local r = redis.call('SET', key, value, 'NX', 'PX', expiresIn)
            local rt = type(r)
            if (rt == 'boolean' and r) or (rt == 'table' and r['ok'] == 'OK') then
                redis.call('PUBLISH', key, '')
                redis.call('PUBLISH', anyLockKey, key)
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
            local key, anyLockKey, value = KEYS[1], KEYS[2], ARGV[1]
            if redis.call('GET', key) ~= value then
                return -2
            end
            if redis.call('DEL', key) <= 0 then
                return -1
            end
            redis.call('PUBLISH', key, '')
            redis.call('PUBLISH', anyLockKey, key)
            return 0
        """;
    private static readonly string ForceReleaseScript =
        """
            local key, anyLockKey, mustNotify = KEYS[1], KEYS[2], ARGV[1]
            if redis.call('DEL', key) <= 0 then
                return -1
            end
            if mustNotify ~= 0 then
                redis.call('PUBLISH', key, '')
                redis.call('PUBLISH', anyLockKey, key)
            end
            return 0
        """;

    private readonly Func<ChannelMessage, string> _changeMessageMapper;

    public RedisDb RedisDb { get; }
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.5, 10);

    public RedisMeshLocks(RedisDb redisDb, IMomentClock? clock = null) : base(clock)
    {
        RedisDb = redisDb.WithKeyPrefix("MeshLocks");
        var fullKeyPrefix = RedisDb.FullKey("");
        _changeMessageMapper = m => {
            var key = (string?)m.Message;
            if (key.IsNullOrEmpty())
                return "";
            if (key.OrdinalStartsWith(fullKeyPrefix))
                return key[fullKeyPrefix.Length..];
            return key;
        };
    }

    public override async Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default)
    {
        if (key.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(key));

        var failureCount = 0;
        while (true) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                var storedValue = (string?)await RedisDb.Database
                    .StringGetAsync((RedisKey)key, CommandFlags.DemandMaster)
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

    public override async Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default)
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

        return new RedisSubscription<string>(queue, _changeMessageMapper);
    }

    // Protected methods

    protected override async Task<bool> TryLock(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        var failureCount = 0;
        while (true) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                var r = (long)await RedisDb.Database
                    .ScriptEvaluateAsync(TryLockScript, [key, ""], [value, (long)expiresIn.TotalMilliseconds], CommandFlags.DemandMaster)
                    .ConfigureAwait(false);
                return r >= 0;
            }
            catch (RedisConnectionException) {
                // Intended
            }
            await Clock.Delay(RetryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task<bool> TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        cancellationToken.ThrowIfCancellationRequested();
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryRenewScript, [key], [value, (long)expiresIn.TotalMilliseconds], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }

    protected override async Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        cancellationToken.ThrowIfCancellationRequested();
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(TryReleaseScript, [key, ""], [value], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r switch {
            -2 => MeshLockReleaseResult.AcquiredBySomeoneElse,
            -1 => MeshLockReleaseResult.NotAcquired,
            0 => MeshLockReleaseResult.Released,
            _ => MeshLockReleaseResult.Unknown,
        };
    }

    protected override async Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        cancellationToken.ThrowIfCancellationRequested();
        var r = (long)await RedisDb.Database
            .ScriptEvaluateAsync(ForceReleaseScript, [key, ""], [mustNotify ? 1 : 0], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }
}
