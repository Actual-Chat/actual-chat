namespace ActualChat.Resilience;

/// <summary>
/// One configured rate limit: a limiter, the budget it charges against, and how a
/// <see cref="RateLimitIdentity"/> becomes its key. Built once at startup.
/// </summary>
public abstract class RateLimitRule
{
    public static RateLimitRule New<TKey, TBudget>(
        IRateLimiter<TKey, TBudget> limiter,
        TBudget budget,
        Func<RateLimitIdentity, TKey> keyBuilder)
        where TBudget : class
        => new RateLimitRule<TKey>(limiter, budget, keyBuilder);

    public abstract object Budget { get; }
    public abstract ValueTask<TimeSpan?> Check(RateLimitIdentity identity, CancellationToken cancellationToken);
}

public sealed class RateLimitRule<TKey>(
    IRateLimiter<TKey> limiter,
    object budget,
    Func<RateLimitIdentity, TKey> keyBuilder
) : RateLimitRule
{
    public override object Budget { get; } = budget;

    public override ValueTask<TimeSpan?> Check(RateLimitIdentity identity, CancellationToken cancellationToken)
        => limiter.Check(keyBuilder.Invoke(identity), Budget, cancellationToken);
}
