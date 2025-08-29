namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    public static async IAsyncEnumerable<List<TSource>> Chunk<TSource>(
        this IAsyncEnumerable<TSource> source,
        int count,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var buffer = new List<TSource>(count);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Add(item);
            if (buffer.Count < count)
                continue;

            yield return buffer;

            buffer = new List<TSource>(count);
        }

        if (buffer.Count > 0)
            yield return buffer;
    }

    public static async IAsyncEnumerable<List<TSource>> ChunkWhile<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<List<TSource>, bool> predicate,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var buffer = new List<TSource>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Add(item);
            if (predicate(buffer))
                continue;

            yield return buffer;

            buffer = new List<TSource>();
        }

        if (buffer.Count > 0)
            yield return buffer;
    }

    public static async IAsyncEnumerable<MaybeHasNext<List<TSource>>> ChunkWhile<TSource>(
        this IAsyncEnumerable<MaybeHasNext<TSource>> source,
        Func<List<TSource>, bool> predicate,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var buffer = new List<TSource>();
        var hasNext = true;
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Add(item.Item);
            hasNext &= item.HasNext;
            if (predicate(buffer))
                continue;

            yield return new MaybeHasNext<List<TSource>>(buffer, hasNext);

            buffer = new List<TSource>();
        }

        if (buffer.Count > 0)
            yield return new MaybeHasNext<List<TSource>>(buffer, false);
    }
}
