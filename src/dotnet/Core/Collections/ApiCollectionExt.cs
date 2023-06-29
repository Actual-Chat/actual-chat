using System.Buffers;
using Microsoft.Toolkit.HighPerformance.Buffers;

namespace ActualChat.Collections;

public static class ApiCollectionExt
{
    // ToApiList

    public static ApiArray<T> ToApiArray<T>(this T[] source, bool copy = false)
        => new(copy ? source.ToArray() : source);

    public static ApiArray<T> ToApiArray<T>(this IEnumerable<T> source)
        => new(source);

    public static async Task<ApiArray<TSource>> ToApiArrayAsync<TSource>(
        this IAsyncEnumerable<TSource> source,
        CancellationToken cancellationToken = default)
    {
        var buffer = ArrayBuffer<TSource>.Lease(false);
        try {
            await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                buffer.Add(item);
            return buffer.Count == 0 ? default : new ApiArray<TSource>(buffer.ToArray());
        }
        finally {
            buffer.Release();
        }
    }

    // ToApiList

    public static ApiList<T> ToApiList<T>(this IEnumerable<T> source)
        => new(source);

    public static async Task<ApiList<TSource>> ToApiListAsync<TSource>(
        this IAsyncEnumerable<TSource> source,
        CancellationToken cancellationToken = default)
    {
        var list = new ApiList<TSource>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            list.Add(item);
        return list;
    }

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
