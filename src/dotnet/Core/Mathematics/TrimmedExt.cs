using System.Numerics;

namespace ActualChat.Mathematics;

public static class TrimmedExt
{
    public static Trimmed<T> Sum<T>(this IEnumerable<Trimmed<T>> values)
        where T : struct, IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
    {
        var sum = default(Trimmed<T>);
        foreach (var value in values) {
            sum += value;
            if (sum.IsTrimmed)
                return sum;
        }
        return sum;
    }

    public static string FormatK(this Trimmed<int> trimmed)
    {
        if (!trimmed.IsTrimmed)
            return trimmed.Format();

        var (alias, denominator) = trimmed.Value switch {
            >= 1_000 => ("K", 1_000),
            _ => ("", 1),
        };
        var limit = (int)Math.Floor((double)trimmed.Value / denominator);
        return new Trimmed<int>(trimmed.Value, limit).Format($"{alias}+");
    }
}
