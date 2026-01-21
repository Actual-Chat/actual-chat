using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisSlidingWindowRateLimiter(RedisDb redisDb, RedisSlidingWindowRateLimiter.Options options, IServiceProvider? services = null)
{
    public static RedisSlidingWindowRateLimiter Create<TContext>(Options options, IServiceProvider services)
    {
        var redisDb = services.GetRequiredService<RedisDb<TContext>>();
        return new RedisSlidingWindowRateLimiter(redisDb, options, services);
    }

    private RedisDb RedisDb { get; } = redisDb;
    private ILogger Log => field ??= services?.LogFor(GetType()) ?? NullLogger.Instance;

    // Lua Script (Sliding Window):
    private const string SlidingWindowScript =
        """
        local key = KEYS[1]

        local windowInMs = tonumber(ARGV[1])
        local window = windowInMs * 1000
        local limit = tonumber(ARGV[2])

        local arrayNow = redis.call('TIME')
        local nowSeconds = tonumber(arrayNow[1])
        local nowMicroseconds = tonumber(arrayNow[2])
        -- TIME returns a seconds and a microseconds component. Combine them here.
        local now = nowSeconds * 1000000 + nowMicroseconds

        --redis.log(redis.LOG_WARNING, "script, now=" .. now)

        -- Remove entries outside the current window
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
        local count = redis.call('ZCARD', key)

        -- ensure now is unique
        if count > 0 then
            local sLatestRequestTimestamp = redis.call('ZRANGE', key, 0, 0, 'REV')
            --redis.log(redis.LOG_WARNING, "script, sLatestRequestTimestamp=" .. sLatestRequestTimestamp[1])
            local latestRequestTimestamp = tonumber(sLatestRequestTimestamp[1])
            if now < latestRequestTimestamp then
                now = latestRequestTimestamp + 1
            end
        end

        if count < limit then
            redis.call('ZADD', key, now, now)
            redis.call('PEXPIRE', key, windowInMs)
            return { 1, 0 } -- Allowed
        else
            local sOldestRequestTimestamp = redis.call('ZRANGE', key, 0, 0)
            local oldestRequestTimestamp = tonumber(sOldestRequestTimestamp[1])
            local replyAfterMs = math.max(0, (oldestRequestTimestamp + window - now) / 1000)
            return { -1, replyAfterMs } -- Denied
        end
        """;

    public async Task<bool> IsRequestAllowedAsync(CancellationToken cancellationToken = default)
    {
        var (isAcquired, _) = await TryAcquire(cancellationToken).ConfigureAwait(false);
        return isAcquired;
    }

    public async Task Acquire(CancellationToken cancellationToken = default)
    {
        while (true) {
            var result = await TryAcquire(cancellationToken).ConfigureAwait(false);
            if (result.IsAcquired)
                return;

            await Task.Delay(result.RetryAfter, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<AcquireResult> TryAcquire(CancellationToken cancellationToken = default)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var result = await database.ScriptEvaluateAsync(
                SlidingWindowScript,
                new RedisKey[] { options.Key },
                new RedisValue[] { (long)options.Window.TotalMilliseconds, options.Limit }
            )
            .ConfigureAwait(false);
        var iResult = (long[])result!;
        var isAllowed = iResult[0];
        var isAcquired = isAllowed == 1;
        if (isAcquired)
            return AcquireResult.Success;

        var retryAfterMs = iResult[1];
        return new AcquireResult(false, TimeSpan.FromMilliseconds(retryAfterMs));
    }

    public record AcquireResult(bool IsAcquired, TimeSpan RetryAfter)
    {
        public static readonly AcquireResult Success = new (true, TimeSpan.Zero);
    }

    public record Options(string Key, int Limit, TimeSpan Window);
}
