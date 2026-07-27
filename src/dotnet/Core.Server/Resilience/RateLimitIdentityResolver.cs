namespace ActualChat.Resilience;

/// <summary>
/// Fills the identity dimensions a <see cref="RateLimitPolicy"/> charges - session, user id and IP.
/// <see cref="RateLimitIdentityKind.Target"/> is caller-supplied: it can't be derived from the call.
/// </summary>
public sealed class RateLimitIdentityResolver(IServiceProvider services)
{
    public const int MaxIdentityCount = 4;

    // Resolved on first use, so it's required only in hosts whose policy charges UserId
    private RateLimitUserIdResolver UserIdResolver
        => field ??= services.GetRequiredService<RateLimitUserIdResolver>();

    public async ValueTask<int> Resolve(
        RateLimitPolicy policy,
        RateLimitClass rateLimitClass,
        RateLimitSource source,
        RateLimitIdentity[] buffer,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        var session = source.Session.IsValid() ? source.Session : null;
        if (session is not null && policy.IsCharged(rateLimitClass, RateLimitIdentityKind.Session))
            buffer[count++] = new RateLimitIdentity(RateLimitIdentityKind.Session, session.Id);
        if (policy.IsCharged(rateLimitClass, RateLimitIdentityKind.IP)
            && RateLimitIdentity.ForIP(source.IPAddress) is { } ipIdentity)
            buffer[count++] = ipIdentity;
        if (session is not null && policy.IsCharged(rateLimitClass, RateLimitIdentityKind.UserId)) {
            var userId = await UserIdResolver.Invoke(session, cancellationToken).ConfigureAwait(false);
            if (userId is { } id && !id.Value.IsNullOrEmpty())
                buffer[count++] = new RateLimitIdentity(RateLimitIdentityKind.UserId, id.Value);
        }
        return count;
    }
}
