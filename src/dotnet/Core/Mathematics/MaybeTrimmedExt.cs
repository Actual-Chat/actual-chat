using System.Numerics;

namespace ActualChat.Mathematics;

public static class MaybeTrimmedExt
{
    public static MaybeTrimmed<T> Sum<T>(this IEnumerable<MaybeTrimmed<T>> values)
        where T : IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
    {
        var sum = default(MaybeTrimmed<T>);
        foreach (var value in values) {
            sum += value;
            if (sum.IsTrimmed)
                return sum;
        }
        return sum;
    }
}
