namespace ActualChat.Comparison;

/// <summary>
/// Compares values by their serialized bytes - for types that compare by reference (<c>Chat</c>,
/// <c>Account</c>), where a value deserialized from a new RPC response never equals its predecessor.
/// </summary>
public sealed class SerializedEqualityComparer<T> : IEqualityComparer<T>
{
    public static readonly SerializedEqualityComparer<T> Instance = new();

    public bool Equals(T? x, T? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;

        using var xBuffer = ByteSerializer.Default.Write(x, typeof(T));
        using var yBuffer = ByteSerializer.Default.Write(y, typeof(T));
        return xBuffer.WrittenSpan.SequenceEqual(yBuffer.WrittenSpan);
    }

    public int GetHashCode(T obj)
    {
        using var buffer = ByteSerializer.Default.Write(obj, typeof(T));
        var hashCode = new HashCode();
        hashCode.AddBytes(buffer.WrittenSpan);
        return hashCode.ToHashCode();
    }
}
