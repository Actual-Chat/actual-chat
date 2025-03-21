namespace ActualChat.Chat.ML;

public class RateLimitedServiceKey(object originalServiceKey)
{
    public static RateLimitedServiceKey GetFor(object originalServiceKey)
        => new (originalServiceKey);

    public object OriginalServiceKey { get; } = originalServiceKey;

    protected bool Equals(RateLimitedServiceKey other)
        => Equals(OriginalServiceKey, other.OriginalServiceKey);

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((RateLimitedServiceKey)obj);
    }

    public override int GetHashCode()
        => HashCode.Combine(OriginalServiceKey, 297);
}
