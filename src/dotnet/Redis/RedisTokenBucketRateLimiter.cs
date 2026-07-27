using ActualChat.Resilience;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

/// <summary>
/// Redis-backed token bucket limiter: permits refill continuously, and a single call may spend
/// more than one of them, so it can bound throughput rather than just call count.
/// </summary>
public sealed class RedisTokenBucketRateLimiter(
    RedisDb redisDb,
    string keyPrefix,
    TokenBucketBudget defaultBudget
) : IRateLimiter<string, TokenBucketBudget>
{
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

    private RedisDb RedisDb { get; } = redisDb.WithKeyPrefix(keyPrefix);
    private TokenBucketBudget DefaultBudget { get; } = defaultBudget;

    public ValueTask<TimeSpan?> Check(
        string key,
        TokenBucketBudget? budget,
        CancellationToken cancellationToken = default)
        => Check(key, 1, budget, cancellationToken);

    // Unlike the shared contract, a call here may cost more than one permit - an LLM call spends
    // as many as it consumes tokens
    public ValueTask<TimeSpan?> Check(
        string key,
        int permitCount,
        TokenBucketBudget? budget = null,
        CancellationToken cancellationToken = default)
        => CheckImpl(key, permitCount, budget ?? DefaultBudget, cancellationToken);

    public async Task Acquire(string key, int permitCount, CancellationToken cancellationToken = default)
    {
        while (true) {
            if (await Check(key, permitCount, null, cancellationToken).ConfigureAwait(false) is not { } retryDelay)
                return;

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteKey(string key, CancellationToken cancellationToken = default)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await database.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    ValueTask<TimeSpan?> IRateLimiter<string>.Check(
        string key,
        object? budget,
        CancellationToken cancellationToken)
        => Check(key, (TokenBucketBudget?)budget, cancellationToken);

    // Private methods

    private async ValueTask<TimeSpan?> CheckImpl(
        string key,
        int permitCount,
        TokenBucketBudget budget,
        CancellationToken cancellationToken)
    {
        var database = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var result = await database.ScriptEvaluateAsync(
                TokenBucketScript,
                [key],
                [(long)budget.ReplenishmentPeriod.TotalSeconds, budget.TokenLimit, permitCount])
            .ConfigureAwait(false);
        var values = (long[])result!;
        if (values[0] == 1)
            return null;

        var tokens = values[1];
        return budget.ReplenishmentPeriod.MultiplyBy((permitCount - tokens) / (double)budget.TokenLimit);
    }
}
