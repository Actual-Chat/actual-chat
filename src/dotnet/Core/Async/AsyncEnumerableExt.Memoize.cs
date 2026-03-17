using System.Buffers;

namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    public static AsyncMemoizer<T> Memoize<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
        => new (source, int.MaxValue, ArrayPool<T>.Shared, cancellationToken);

    public static AsyncMemoizer<T> Memoize<T>(
        this IAsyncEnumerable<T> source,
        int capacity,
        CancellationToken cancellationToken = default)
        => new (source, capacity, ArrayPool<T>.Shared, cancellationToken);

    public static AsyncMemoizer<T> Memoize<T>(
        this IAsyncEnumerable<T> source,
        ArrayPool<T> pool,
        CancellationToken cancellationToken = default)
        => new (source, int.MaxValue, pool, cancellationToken);

    public static AsyncMemoizer<T> Memoize<T>(
        this IAsyncEnumerable<T> source,
        int capacity,
        ArrayPool<T> pool,
        CancellationToken cancellationToken = default)
        => new (source, capacity, pool, cancellationToken);
}
