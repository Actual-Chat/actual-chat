namespace ActualChat.Resilience;

/// <summary>
/// Type-erased <see cref="IRateLimiter{TKey,TBudget}"/>, for collections of differently-typed
/// limiters. A null <c>Check</c> result means "allowed", any other one is the retry delay.
/// </summary>
public interface IRateLimiter<in TKey>
{
    ValueTask<TimeSpan?> Check(TKey key, object? budget, CancellationToken cancellationToken = default);
}

/// <summary>
/// Charges one call against the limit of <c>key</c>. A null budget means the limiter's own default.
/// Implementations must charge atomically: two concurrent calls must not both pass the last permit.
/// </summary>
public interface IRateLimiter<in TKey, in TBudget> : IRateLimiter<TKey>
    where TBudget : class
{
    ValueTask<TimeSpan?> Check(TKey key, TBudget? budget, CancellationToken cancellationToken = default);
}

public static class RateLimiterExt
{
    // The budget parameter is required on both interfaces, so a one-argument call can't be
    // ambiguous between the typed and the erased overload - it binds here instead
    public static ValueTask<TimeSpan?> Check<TKey>(
        this IRateLimiter<TKey> limiter,
        TKey key,
        CancellationToken cancellationToken = default)
        => limiter.Check(key, null, cancellationToken);
}
