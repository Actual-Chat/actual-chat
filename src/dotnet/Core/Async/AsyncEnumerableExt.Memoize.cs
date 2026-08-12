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

    public static FoldingAsyncMemoizer<T, TState> MemoizeFolding<T, TState>(
        this IAsyncEnumerable<T> source,
        TState seed,
        Func<TState, T, TState> folder,
        Func<TState, T>? toItem = null,
        CancellationToken cancellationToken = default)
        => new(source, seed, folder, toItem, int.MaxValue, cancellationToken);
}
