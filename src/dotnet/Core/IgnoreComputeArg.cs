namespace ActualChat;

public class IgnoreComputeArg<T>(T value) : IEquatable<IgnoreComputeArg<T>>
{
    public T Value { get; } = value;

    /* Equality always says true, therefore, this argument will be excluded from compute input calculation */

    public override int GetHashCode()
        => 1;

    public bool Equals(IgnoreComputeArg<T>? other)
        => true;

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((IgnoreComputeArg<T>)obj);
    }

    public static bool operator ==(IgnoreComputeArg<T>? left, IgnoreComputeArg<T>? right)
        => Equals(left, right);

    public static bool operator !=(IgnoreComputeArg<T>? left, IgnoreComputeArg<T>? right)
        => !Equals(left, right);
}
