using ActualChat.Resilience;
using ActualLab.Redis;

namespace ActualChat.Redis;

/// <summary>
/// Redis-backed sliding window limiter: each permitted call stores one ZSET member,
/// and every check is a single atomic script evaluation.
/// </summary>
public sealed class RedisSlidingWindowRateLimiter(
    RedisDb redisDb,
    string keyPrefix,
    SlidingWindowBudget defaultBudget
) : IRateLimiter<string, SlidingWindowBudget>
{
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

        -- Remove entries outside the current window
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
        local count = redis.call('ZCARD', key)

        -- ensure now is unique
        if count > 0 then
            local sLatestRequestTimestamp = redis.call('ZRANGE', key, 0, 0, 'REV')
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

    private RedisDb RedisDb { get; } = redisDb.WithKeyPrefix(keyPrefix);
    private SlidingWindowBudget DefaultBudget { get; } = defaultBudget;

    ValueTask<TimeSpan?> IRateLimiter<string>.Check(
        string key,
        object? budget,
        CancellationToken cancellationToken)
        => Check(key, (SlidingWindowBudget?)budget, cancellationToken);

    public ValueTask<TimeSpan?> Check(
        string key,
        SlidingWindowBudget? budget,
        CancellationToken cancellationToken = default)
        => CheckImpl(key, budget ?? DefaultBudget, cancellationToken);

    public async Task DeleteKey(string key, CancellationToken cancellationToken = default)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await database.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask<TimeSpan?> CheckImpl(
        string key,
        SlidingWindowBudget budget,
        CancellationToken cancellationToken)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var result = await database
            .ScriptEvaluateAsync(
                SlidingWindowScript,
                [key],
                [(long)budget.Window.TotalMilliseconds, budget.Limit]
            ).ConfigureAwait(false);
        var values = (long[])result!;
        return values[0] == 1
            ? null
            : TimeSpan.FromMilliseconds(values[1]);
    }
}
