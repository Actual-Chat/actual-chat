using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisTokenBucketRateLimiter(RedisDb redisDb, RedisTokenBucketRateLimiter.Options options, IServiceProvider? services = null)
{
    public static RedisTokenBucketRateLimiter Create<TContext>(Options options, IServiceProvider services)
    {
        var redisDb = services.GetRequiredService<RedisDb<TContext>>();
        return new RedisTokenBucketRateLimiter(redisDb, options, services);
    }

    private RedisDb RedisDb { get; } = redisDb;
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services?.LogFor(GetType()) ?? NullLogger.Instance;

    // Lua Script (Token Bucket):
    private const string TokenBucketScript =
        """
        local key = KEYS[1]
        local window = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        local permitCount = tonumber(ARGV[3])

        -- Get current time in seconds from Redis
        local now = tonumber(redis.call('TIME')[1])

        -- Gets current token number
        local last_refill = tonumber(redis.call('HGET', key, 'last_refill')) or now
        local tokens = tonumber(redis.call('HGET', key, 'tokens')) or limit

        -- Gets how many tokens have to be refilled for elapsed time
        local elapsed_time = now - last_refill
        local refill_rate = limit / window
        local new_tokens = math.min(limit, tokens + elapsed_time * refill_rate)

        -- Checks if there are enough tokens to approve the request
        if new_tokens >= permitCount then
            new_tokens = new_tokens - permitCount
            redis.call('HSET', key, 'tokens', new_tokens)
            redis.call('HSET', key, 'last_refill', now)
            redis.call('EXPIRE', key, window)
            return { 1, 0 } -- Allowed
        else
            return { 0, new_tokens } -- Denied
        end
        """;

    public async Task<bool> IsRequestAllowedAsync(int permitCount, CancellationToken cancellationToken = default)
    {
        var (isAcquired, _) = await TryAcquire(permitCount, cancellationToken).ConfigureAwait(false);
        return isAcquired;
    }

    public async Task Acquire(int permitCount, CancellationToken cancellationToken = default)
    {
        while (true) {
            var result = await TryAcquire(permitCount, cancellationToken).ConfigureAwait(false);
            if (result.IsAcquired)
                return;

            await Task.Delay(result.RetryAfter, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<AcquireResult> TryAcquire(int permitCount, CancellationToken cancellationToken = default)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var result = await database.ScriptEvaluateAsync(
                TokenBucketScript,
                new RedisKey[] { options.Key },
                new RedisValue[] {
                    (long)options.ReplenishmentPeriod.TotalSeconds,
                    options.TokenLimit,
                    permitCount
                }
            )
            .ConfigureAwait(false);
        var iResult = (long[])result!;
        var isAllowed = iResult[0];
        var isAcquired = isAllowed == 1;
        if (isAcquired)
            return AcquireResult.Success;

        var tokens = iResult[1];
        var retryAfter = options.ReplenishmentPeriod.MultiplyBy((permitCount - tokens) / (double)options.TokenLimit);
        return new AcquireResult(false, TimeSpan.FromMilliseconds(retryAfter.TotalMilliseconds));
    }

    public record AcquireResult(bool IsAcquired, TimeSpan RetryAfter)
    {
        public static readonly AcquireResult Success = new (true, TimeSpan.Zero);
    }

    public record Options(string Key, int TokenLimit, TimeSpan ReplenishmentPeriod);
}
