namespace ActualChat;

public static partial class AsyncEnumerableExt
{
    public static AsyncMemoizer<T> Memoize<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
        => new(source, int.MaxValue, cancellationToken);

    public static AsyncMemoizer<T> Memoize<T>(
        this IAsyncEnumerable<T> source,
        int capacity,
        CancellationToken cancellationToken = default)
        => new(source, capacity, cancellationToken);
}
