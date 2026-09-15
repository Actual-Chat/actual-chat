namespace ActualChat.Resilience;

/// <summary>
/// Overrides the <see cref="RateLimitClass"/> <c>HttpRateLimitMiddleware</c> charges a controller action to;
/// without it GET/HEAD are <see cref="RateLimitClass.HttpRead"/> and everything else
/// <see cref="RateLimitClass.Command"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RateLimitClassAttribute(RateLimitClass rateLimitClass) : Attribute
{
    public RateLimitClass RateLimitClass { get; } = rateLimitClass;
}
