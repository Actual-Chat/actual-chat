using ActualLab.Versioning;

namespace ActualChat.Comparison;

/// <summary>
/// Compares entities by both Id and Version for equality.
/// </summary>
public sealed class VersionEqualityComparer<T, TKey> : IEqualityComparer<T>
    where T : IHasId<TKey>, IHasVersion<long>
{
    public static readonly VersionEqualityComparer<T, TKey> Instance = new();

    public bool Equals(T? x, T? y)
    {
        if (x == null)
            return y == null;
        if (y == null)
            return false;
        return x.Version == y.Version && EqualityComparer<TKey>.Default.Equals(x.Id, y.Id);
    }

    public int GetHashCode(T obj)
        => HashCode.Combine(obj.Id, obj.Version);
}
