namespace ActualChat.Channels;

public static class AsyncEnumerableExt
{
    public static IAsyncEnumerable<T> Buffer<T>(
        this IAsyncEnumerable<T> source,
        int bufferSize,
        CancellationToken cancellationToken = default)
    {
        var buffer = Channel.CreateBounded<T>(new BoundedChannelOptions(bufferSize) {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        _ = source.CopyTo(buffer, ChannelCompletionMode.CompleteAndPropagateError, cancellationToken);
        return buffer.Reader.ReadAllAsync(cancellationToken);
    }

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
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception e) {
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
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception e) {
            return Result.New<T>(default!, e);
        }
    }
}
