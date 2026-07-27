namespace ActualChat.Comparison;

/// <summary>
/// Compares <see cref="ApiArray{T}"/>s item by item - <see cref="ApiArray{T}.Equals(ApiArray{T})"/>
/// compares the backing arrays by reference, so a rebuilt one is never equal to its predecessor.
/// </summary>
public sealed class ApiArrayComparer<T> : IEqualityComparer<ApiArray<T>>
{
    public static readonly ApiArrayComparer<T> Instance = new();

    public bool Equals(ApiArray<T> x, ApiArray<T> y)
        => x.Items.AsSpan().SequenceEqual(y.Items.AsSpan());

    public int GetHashCode(ApiArray<T> obj)
    {
        var hashCode = new HashCode();
        hashCode.Add(obj.Count);
        foreach (var item in obj)
            hashCode.Add(item);
        return hashCode.ToHashCode();
    }
}
