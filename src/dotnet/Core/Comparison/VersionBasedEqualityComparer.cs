using Stl.Versioning;

namespace ActualChat.Comparison;

public class VersionBasedEqualityComparer<T, TKey> : IEqualityComparer<T>
    where T : IHasId<TKey>, IHasVersion<long>
{
#pragma warning disable MA0018
    public static IEqualityComparer<T> Instance { get; } = new VersionBasedEqualityComparer<T, TKey>();
#pragma warning restore MA0018

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
