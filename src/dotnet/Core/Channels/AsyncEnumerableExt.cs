namespace ActualChat.Channels;

public static class AsyncEnumerableExt
{
    public static AsyncMemoizer<T> Memoize<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
        => new(source, cancellationToken);

    public static async ValueTask<Option<T>> TryReadAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        await foreach (var value in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            return value;
        return Option<T>.None;
    }

    public static async ValueTask<Option<T>> TryReadAsync<T>(
        this IAsyncEnumerator<T> source,
        CancellationToken cancellationToken = default)
        => await source.MoveNextAsync(cancellationToken).ConfigureAwait(false)
            ? source.Current
            : Option<T>.None;

    public static async ValueTask<Result<T>> ReadResultAsync<T>(
        this IAsyncEnumerator<T> source,
        CancellationToken cancellationToken = default)
    {
        try {
            if (!await source.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                return ChannelExt.GetChannelClosedResult<T>();
            return source.Current;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            return Result.New<T>(default!, e);
        }
    }

    public static async ValueTask<Result<T>> ReadResultAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        try {
            await foreach (var value in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                return value;
            return ChannelExt.GetChannelClosedResult<T>();
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            return Result.New<T>(default!, e);
        }
    }
}
