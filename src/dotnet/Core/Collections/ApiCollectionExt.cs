namespace ActualChat.Collections;

public static class ApiCollectionExt
{
    // ToApiList

    public static ApiArray<T> ToApiArray<T>(this T[] source, bool copy = false)
        => new(copy ? source.ToArray() : source);

    public static ApiArray<T> ToApiArray<T>(this IEnumerable<T> source)
        => new(source);

    // ToApiList

    public static ApiList<T> ToApiList<T>(this IEnumerable<T> source)
        => new(source);

    // ToApiMap

    public static ApiMap<TKey, TValue> ToApiMap<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source)
        where TKey : notnull
        => new(source);

    public static ApiMap<TKey, TValue> ToApiMap<TKey, TValue>(
        this IEnumerable<KeyValuePair<TKey, TValue>> source,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
        => new(source, comparer);

    public static ApiMap<TKey, TValue> ToApiMap<TKey, TValue>(this IDictionary<TKey, TValue> source)
        where TKey : notnull
        => new(source);

    public static ApiMap<TKey, TValue> ToApiMap<TKey, TValue>(
        this IDictionary<TKey, TValue> source,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
        => new(source, comparer);

    // ToApiSet

    public static ApiSet<T> ToApiSet<T>(this IEnumerable<T> source)
        => new(source);

    public static ApiSet<T> ToApiSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer)
        => new(source, comparer);
}
