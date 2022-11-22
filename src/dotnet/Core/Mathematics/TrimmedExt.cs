using System.Numerics;

namespace ActualChat.Mathematics;

public static class TrimmedExt
{
    public static Trimmed<T> Sum<T>(this IEnumerable<Trimmed<T>> values)
        where T : IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
    {
        var sum = default(Trimmed<T>);
        foreach (var value in values) {
            sum += value;
            if (sum.IsTrimmed)
                return sum;
        }
        return sum;
    }
}
