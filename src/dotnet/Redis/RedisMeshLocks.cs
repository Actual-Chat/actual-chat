using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisMeshLocks<TContext> : RedisMeshLocks
{
    public RedisMeshLocks(IServiceProvider services)
        : base(services, services.GetRequiredService<RedisDb<TContext>>())
    { }

    public RedisMeshLocks(IServiceProvider services, string keyPrefix)
        : base(services, services.GetRequiredService<RedisDb<TContext>>(), keyPrefix)
    { }
}

public class RedisMeshLocks : MeshLocksBase
{
    // Useful for debugging:
    // redis.log(redis.LOG_WARNING, string.format(cjson.encode(r)))
    private static readonly string TryLockScript =
        """
        local key, anyLockKey, value, expiresIn = KEYS[1], KEYS[2], ARGV[1], ARGV[2]
        local rSet = redis.call('SET', key, value, 'NX', 'PX', expiresIn)
        local rt = type(rSet)
        if (rt == 'boolean' and rSet) or (rt == 'table' and rSet['ok'] == 'OK') then
            redis.call('PUBLISH', key, '')
            redis.call('PUBLISH', anyLockKey, key)
            return 0
        end
        return -1
        """;
    private static readonly string TryRenewScript =
        // SET key again if missing - there is no other lock holder
        // Useful for debugging:
        // redis.call('ECHO', 'TryRenewScript: ' .. string.format(cjson.encode(result)))
        """
        local key, anyLockKey, value, expiresIn = KEYS[1], KEYS[2], ARGV[1], ARGV[2]
        local rGet = redis.call('GET', key)

        if rGet == false then
            return -2
        end
        if rGet ~= value then
            return -1
        end
        local rExpire = redis.call('PEXPIRE', key, expiresIn, 'GT')
        if rExpire == 0 then
            return -3
        end
        return 0
        """;
    private static readonly string TryReleaseScript =
        """
        local key, anyLockKey, value = KEYS[1], KEYS[2], ARGV[1]
        if redis.call('GET', key) ~= value then
            return -1
        end
        if redis.call('DEL', key) <= 0 then
            return -2
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
            local rScan = redis.call("SCAN", cursor, "MATCH", pattern, "COUNT", 100);
        	local keys = rScan[2];
        	for i = 1, #keys do
        		local key = keys[i];
        		redis.replicate_commands()
        		redis.call("DEL", key);
        		count = count + 1;
        	end;
        	cursor = rScan[1];
        until cursor == "0";
        return count;
        """;

    private readonly Func<ChannelMessage, string> _changeMessageMapper;
    private readonly string _fullKeyPrefix;

    private RedisDb RedisDb { get; }

    public RedisMeshLocks(IServiceProvider services, RedisDb redisDb)
        : this(services, redisDb, DefaultKeyPrefix) { }
    public RedisMeshLocks(IServiceProvider services, RedisDb redisDb, string keyPrefix = "")
        : base(services)
    {
        RedisDb = redisDb.WithKeyPrefix(keyPrefix);
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
                var id = (string?)await database
                    .StringGetAsync((RedisKey)key, CommandFlags.DemandMaster)
                    .ConfigureAwait(false);
                return id == null ? null
                    : new MeshLockInfo(key, id);
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

    public override Task<List<string>> ListKeys(string prefix, CancellationToken cancellationToken = default)
        => RedisDb.ScanKeys(prefix + "*", cancellationToken).ToListAsync(cancellationToken).AsTask();

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
        if (keyPrefix.IsNullOrEmpty() && ReferenceEquals(lockOptions, null))
            return this;

        return new RedisMeshLocks(Services, RedisDb, keyPrefix ?? "") {
            LockOptions = lockOptions ?? LockOptions,
        };
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
                if (r != 0)
                    DebugLog?.LogDebug("TryLock: {Key} = {Value} -> {Result}", $"{_fullKeyPrefix}{key}", value, r);
                return r >= 0;
            }
            catch (RedisConnectionException) {
                // Intended
            }
            await Clock.Delay(RetryDelays[++failureCount], cancellationToken).ConfigureAwait(false);
        }
    }

    protected override bool TryRenewBlocking(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
    {
        // Synchronous path for the dedicated renewal thread — no ThreadPool dependency
        var valueTask = RedisDb.Database.Get(cancellationToken);
        var database = valueTask.IsCompletedSuccessfully
            ? valueTask.Result
            : valueTask.AsTask().GetAwaiter().GetResult();
        var expiresInMs = (long)expiresIn.TotalMilliseconds;
        var r = (long)database.ScriptEvaluate(TryRenewScript, [(RedisKey)key, (RedisKey)""], [(RedisValue)value, expiresInMs], CommandFlags.DemandMaster);
        if (r != 0)
            DebugLog?.LogDebug("TryRenewBlocking: {Key} = {Value} -> {Result} @ {ExpiresIn}", $"{_fullKeyPrefix}{key}", value, r, expiresInMs);
        return r >= 0;
    }

    protected override async Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var r = (long)await database
            .ScriptEvaluateAsync(TryReleaseScript, [key, ""], [value], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        switch (r) {
        case 0:
            return MeshLockReleaseResult.Released;
        case -1:
            DebugLog?.LogDebug("TryRelease: {Key} = {Value} -> {Result}", $"{_fullKeyPrefix}{key}", value, r);
            return MeshLockReleaseResult.AcquiredBySomeoneElse;
        default:
            // Technically, we should never land here, coz -2 = something is off with Redis atomicity for LUA scripts
            Log?.LogError("TryRelease: {Key} = {Value} -> {Result}", $"{_fullKeyPrefix}{key}", value, r);
            return MeshLockReleaseResult.UnknownError;
        }
    }

    protected override async Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken)
    {
        // Must not auto-retry!
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var r = (long)await database
            .ScriptEvaluateAsync(ForceReleaseScript, [key, ""], [mustNotify ? 1 : 0], CommandFlags.DemandMaster)
            .ConfigureAwait(false);
        if (r != 0)
            DebugLog?.LogDebug("ForceRelease: {Key} -> {Result}", $"{_fullKeyPrefix}{key}", r);
        return r >= 0;
    }
}
