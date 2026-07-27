using ActualChat.Resilience;
using ActualLab.Redis;

namespace ActualChat.Redis;

/// <summary>
/// Builds the <see cref="RateLimitPolicy"/> of an API host: commands are limited in-process, because
/// a node-local bound is enough to stop a runaway client, everything else through Redis, which is
/// authoritative across nodes.
/// </summary>
public static class RedisRateLimitPolicy
{
    private const string KeyPrefix = "RateLimit";

    public static RateLimitPolicy New(RedisDb redisDb, RateLimitBudgets budgets, IServiceProvider services)
    {
        var rules = new Dictionary<(RateLimitClass, RateLimitIdentityKind), RateLimitRule>();
        foreach (var (key, budget) in budgets.Items) {
            if (budget.Limit <= 0)
                continue;

            var (rateLimitClass, kind) = key;
            rules.Add(key, rateLimitClass is RateLimitClass.Command
                ? RateLimitRule.New(new LocalRateLimiter<string>(budget), budget, GetValue)
                : RateLimitRule.New(
                    new RedisSlidingWindowRateLimiter(redisDb, $"{KeyPrefix}.{rateLimitClass}.{kind}", budget),
                    budget,
                    GetValueHash));
        }
        return new RateLimitPolicy(rules, services.LogFor<RateLimitPolicy>());
    }

    // Private methods

    private static string GetValue(RateLimitIdentity identity)
        => identity.Value;

    private static string GetValueHash(RateLimitIdentity identity)
        => identity.GetValueHash();
}
