using System.Collections.ObjectModel;

namespace ActualChat.Resilience;

/// <summary>
/// The configured set of <see cref="RateLimitRule"/>-s: charges an inbound call against every
/// identity dimension that has one, logs the dimension that tripped, and throws
/// <see cref="RateLimitExceededException"/>. Rejected calls are charged too - they were made.
/// </summary>
public class RateLimitPolicy(
    IReadOnlyDictionary<(RateLimitClass, RateLimitIdentityKind), RateLimitRule>? rules = null,
    ILogger? log = null)
{
    public static readonly RateLimitPolicy Unlimited = new();

    public IReadOnlyDictionary<(RateLimitClass, RateLimitIdentityKind), RateLimitRule> Rules { get; }
        = rules ?? ReadOnlyDictionary<(RateLimitClass, RateLimitIdentityKind), RateLimitRule>.Empty;
    protected ILogger? Log { get; } = log;

    public virtual bool IsCharged(RateLimitClass rateLimitClass, RateLimitIdentityKind kind)
        => Rules.ContainsKey((rateLimitClass, kind));

    public virtual ValueTask Check(
        string method,
        RateLimitClass rateLimitClass,
        ReadOnlySpan<RateLimitIdentity> identities,
        CancellationToken cancellationToken = default)
    {
        if (Rules.Count == 0 || identities.Length == 0)
            return default;

        List<(RateLimitRule Rule, RateLimitIdentity Identity)>? charges = null;
        foreach (var identity in identities)
            if (Rules.TryGetValue((rateLimitClass, identity.Kind), out var rule))
                (charges ??= new List<(RateLimitRule, RateLimitIdentity)>(identities.Length)).Add((rule, identity));
        if (charges is null)
            return default;

        // Auth goes to Redis, so its checks run concurrently to avoid a round trip per dimension
        return rateLimitClass is RateLimitClass.Auth && charges.Count > 1
            ? CheckConcurrently(method, rateLimitClass, charges, cancellationToken)
            : CheckSequentially(method, rateLimitClass, charges, cancellationToken);
    }

    // Private methods

    private async ValueTask CheckSequentially(
        string method,
        RateLimitClass rateLimitClass,
        List<(RateLimitRule Rule, RateLimitIdentity Identity)> charges,
        CancellationToken cancellationToken)
    {
        var exhaustedIndex = -1;
        var retryDelay = TimeSpan.Zero;
        for (var i = 0; i < charges.Count; i++) {
            var (rule, identity) = charges[i];
            if (await rule.Check(identity, cancellationToken).ConfigureAwait(false) is not { } delay)
                continue;
            if (delay <= retryDelay)
                continue;

            exhaustedIndex = i;
            retryDelay = delay;
        }
        if (exhaustedIndex >= 0)
            throw Exhausted(method, rateLimitClass, charges[exhaustedIndex], retryDelay);
    }

    private async ValueTask CheckConcurrently(
        string method,
        RateLimitClass rateLimitClass,
        List<(RateLimitRule Rule, RateLimitIdentity Identity)> charges,
        CancellationToken cancellationToken)
    {
        var checkTasks = new Task<TimeSpan?>[charges.Count];
        for (var i = 0; i < charges.Count; i++) {
            var (rule, identity) = charges[i];
            checkTasks[i] = rule.Check(identity, cancellationToken).AsTask();
        }
        var retryDelays = await Task.WhenAll(checkTasks).ConfigureAwait(false);

        var exhaustedIndex = -1;
        var retryDelay = TimeSpan.Zero;
        for (var i = 0; i < retryDelays.Length; i++) {
            if (retryDelays[i] is not { } delay || delay <= retryDelay)
                continue;

            exhaustedIndex = i;
            retryDelay = delay;
        }
        if (exhaustedIndex >= 0)
            throw Exhausted(method, rateLimitClass, charges[exhaustedIndex], retryDelay);
    }

    private Exception Exhausted(
        string method,
        RateLimitClass rateLimitClass,
        (RateLimitRule Rule, RateLimitIdentity Identity) charge,
        TimeSpan retryDelay)
    {
        // Which dimension tripped stays server-side: the exception carries the delay only
        Log?.LogWarning(
            "{Method}: {RateLimitClass} budget {Budget} exhausted for {IdentityKind} #{IdentityHash}, "
            + "retry after {RetryDelay}",
            method, rateLimitClass, charge.Rule.Budget, charge.Identity.Kind,
            charge.Identity.GetValueHash(), retryDelay);
        return StandardError.RateLimitExceeded(retryDelay);
    }
}
