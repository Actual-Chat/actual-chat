namespace ActualChat.Comparison;

/// <summary>
/// Compares <see cref="ApiMap{TKey,TValue}"/>s entry by entry - it derives from
/// <see cref="Dictionary{TKey,TValue}"/>, so a rebuilt one is never equal to its predecessor.
/// </summary>
public sealed class ApiMapComparer<TKey, TValue> : IEqualityComparer<ApiMap<TKey, TValue>>
    where TKey : notnull
{
    public static readonly ApiMapComparer<TKey, TValue> Instance = new();

    public bool Equals(ApiMap<TKey, TValue>? x, ApiMap<TKey, TValue>? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null || x.Count != y.Count)
            return false;

        foreach (var (key, value) in x)
            if (!y.TryGetValue(key, out var otherValue)
                || !EqualityComparer<TValue>.Default.Equals(value, otherValue))
                return false;
        return true;
    }

    public int GetHashCode(ApiMap<TKey, TValue> obj)
    {
        // XOR rather than HashCode.Add in a loop: enumeration order must not change the result
        var result = obj.Count;
        foreach (var (key, value) in obj)
            result ^= HashCode.Combine(key, value);
        return result;
    }
}
