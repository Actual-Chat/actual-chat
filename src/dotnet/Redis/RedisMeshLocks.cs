using ActualChat.Mesh;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisMeshLocks<TContext> : RedisMeshLocks, IMeshLocks<TContext>
{
    public RedisMeshLocks(IServiceProvider services)
        : base(
            services.GetRequiredService<RedisDb<TContext>>(),
            services.Clocks().SystemClock,
            services.LogFor<RedisMeshLocks>())
    { }
    public RedisMeshLocks(RedisDb redisDb, IMomentClock? clock = null)
        : base(redisDb, DefaultKeyPrefix, clock) { }
    public RedisMeshLocks(RedisDb redisDb, string keyPrefix, IMomentClock? clock = null)
        : base(redisDb, keyPrefix, clock) { }
}

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
        // Ideally we want to use GT option with PEXPIRE, but it's available only since Redis 7.0
        """
        local key, value, expiresIn = KEYS[1], ARGV[1], ARGV[2]
        if redis.call('GET', key) ~= value then
            return -1
        end
        redis.call('PEXPIRE', key, expiresIn)
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
    private static readonly string RemoveKeysScript =
        """
        local pattern = ARGV[1]
        local count = 0;
        local cursor = "0";
        repeat
            local scanResult = redis.call("SCAN", cursor, "MATCH", pattern, "COUNT", 100);
        	local keys = scanResult[2];
        	for i = 1, #keys do
        		local key = keys[i];
        		redis.replicate_commands()
        		redis.call("DEL", key);
        		count = count + 1;
        	end;
        	cursor = scanResult[1];
        until cursor == "0";
        return count;
        """;

    public static readonly string DefaultKeyPrefix = "MeshLocks";

    private readonly Func<ChannelMessage, string> _changeMessageMapper;
    private readonly string _fullKeyPrefix;

    public RedisDb RedisDb { get; }

    public RedisMeshLocks(RedisDb redisDb, IMomentClock? clock = null, ILogger? log = null)
        : this(redisDb, DefaultKeyPrefix, clock, log) { }
    public RedisMeshLocks(RedisDb redisDb, string keyPrefix, IMomentClock? clock = null, ILogger? log = null)
        : base(clock, log)
    {
        if (!keyPrefix.IsNullOrEmpty())
            redisDb = redisDb.WithKeyPrefix(keyPrefix);
        RedisDb = redisDb;
        _fullKeyPrefix = RedisDb.FullKey("");
        _changeMessageMapper = m => {
            var key = (string?)m.Message ?? "";
            if (key.Length >= _fullKeyPrefix.Length)
                return key[_fullKeyPrefix.Length..];
            return "";
        };
    }

    public override string GetFullKey(string key)
        => RedisDb.FullKey(key);

    public override async Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default)
    {
        if (key.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(key));

        var failureCount = 0;
        while (true) {
            try {
                var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
                var storedValue = (string?)await database
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
        CancellationToken goneToken;
        while (true) {
            try {
                (var subscriber, goneToken) = await RedisDb.Subscriber
                    .GetTemporary(cancellationToken)
                    .ConfigureAwait(false);
                queue = await subscriber
                    .SubscribeAsync(channel)
                    .ConfigureAwait(false);
                break;
            }
            catch (RedisConnectionException) {
                // Intended
            }
            await Clock.Delay(RetryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
        }

        return new RedisSubscription<string>(queue, _changeMessageMapper, goneToken);
    }

    public override async Task<List<string>> ListKeys(string prefix, CancellationToken cancellationToken = default)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var r = await database
            .ExecuteAsync("KEYS", new object[] { (RedisKey)(prefix + "*") }, CommandFlags.DemandMaster)
            .ConfigureAwait(false);

        var keys = new List<string>(r.Length);
        for (var index = 0; index < r.Length; index++) {
            var key = (string?)r[index] ?? "";
            key = key.Length >= _fullKeyPrefix.Length ? key[_fullKeyPrefix.Length..] : "";
            keys.Add(key);
        }
        return keys;
    }

    public async Task<long> RemoveKeys(string pattern, CancellationToken cancellationToken = default)
    {
        // Must not auto-retry!
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var r = (long)await database
            .ScriptEvaluateAsync(RemoveKeysScript, [], [_fullKeyPrefix + pattern], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r;
    }

    public override IMeshLocks With(string? keyPrefix, MeshLockOptions? lockOptions)
    {
        if (!keyPrefix.IsNullOrEmpty())
            return new RedisMeshLocks(RedisDb, keyPrefix, Clock, Log) {
                LockOptions = lockOptions ?? LockOptions,
            };

        if (!ReferenceEquals(lockOptions, null) && lockOptions != LockOptions)
            return new RedisMeshLocks(RedisDb, "", Clock, Log) {
                LockOptions = lockOptions,
            };

        return this;
    }

    // Protected methods

    protected override async Task<bool> TryLock(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        var failureCount = 0;
        while (true) {
            try {
                var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
                var r = (long)await database
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
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var r = (long)await database
            .ScriptEvaluateAsync(TryRenewScript, [key], [value, (long)expiresIn.TotalMilliseconds], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }

    protected override async Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var r = (long)await database
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
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var r = (long)await database
            .ScriptEvaluateAsync(ForceReleaseScript, [key, ""], [mustNotify ? 1 : 0], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        return r >= 0;
    }
}
