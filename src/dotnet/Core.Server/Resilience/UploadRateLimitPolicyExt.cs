namespace ActualChat.Resilience;

public static class UploadRateLimitPolicyExt
{
    public static async Task CheckUpload(
        this RateLimitPolicy policy,
        RateLimitIdentityResolver identityResolver,
        string method,
        RateLimitSource source,
        long length,
        CancellationToken cancellationToken = default)
    {
        await Check(RateLimitClass.UploadCreation, 1).ConfigureAwait(false);
        var byteUnitCount = (int)((length + RateLimitBudgets.UploadByteUnit - 1)
            / RateLimitBudgets.UploadByteUnit);
        await Check(RateLimitClass.UploadBytes, byteUnitCount).ConfigureAwait(false);
        return;

        async Task Check(RateLimitClass rateLimitClass, int count)
        {
            if (count == 0)
                return;

            var identities = new RateLimitIdentity[RateLimitIdentityResolver.MaxIdentityCount];
            var identityCount = await identityResolver
                .Resolve(policy, rateLimitClass, source, identities, cancellationToken)
                .ConfigureAwait(false);
            for (var i = 0; i < count; i++)
                await policy
                    .Check(method, rateLimitClass, identities.AsSpan(0, identityCount), cancellationToken)
                    .ConfigureAwait(false);
        }
    }
}
