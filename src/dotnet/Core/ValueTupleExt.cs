namespace ActualChat;

public static class ValueTupleExt
{
    public static T[] ToArray<T>(this (T, T) source)
        => new [] { source.Item1, source.Item2 };
    public static T[] ToArray<T>(this (T, T, T) source)
        => new [] { source.Item1, source.Item2, source.Item3 };

    public static T OtherThan<T>(this (T, T) source, T value)
        where T : IEquatable<T>
    {
        var (i1, i2) = source;
        var eq1 = EqualityComparer<T>.Default.Equals(i1, value);
        var eq2 = EqualityComparer<T>.Default.Equals(i2, value);
        return eq1
            ? (eq2 ? throw new ArgumentOutOfRangeException(nameof(source)) : i2)
            : (eq2 ? i1 : throw new ArgumentOutOfRangeException(nameof(source)));
    }

    public static T OtherThanOrDefault<T>(this (T, T) source, T value)
        where T : IEquatable<T>
    {
        var (i1, i2) = source;
        var eq1 = EqualityComparer<T>.Default.Equals(i1, value);
        var eq2 = EqualityComparer<T>.Default.Equals(i2, value);
        return eq1
            ? (eq2 ? default! : i2)
            : (eq2 ? i1 : default!);
    }

    public static (T, T) Sort<T>(this (T, T) source)
        where T : IComparable<T>
    {
        var (i1, i2) = source;
        return Comparer<T>.Default.Compare(i1, i2) <= 0 ? source : (i2, i1);
    }

    public static (T, T, T) Sort<T>(this (T, T, T) source)
        where T : IComparable<T>
    {
        var (i1, i2, i3) = source;
        // Bubble sort
        (i1, i2) = (i1, i2).Sort();
        (i2, i3) = (i2, i3).Sort();
        (i1, i2) = (i1, i2).Sort();
        return (i1, i2, i3);
    }
}
