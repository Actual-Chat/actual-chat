using ActualChat.Channels.Internal;

namespace ActualChat.Channels;

public static class AsyncEnumerableExt
{
    public static IAsyncEnumerable<T> AsEnumerableOnce<T>(this IAsyncEnumerator<T> enumerator, bool suppressDispose)
        => new AsyncEnumerableOnce<T>(enumerator, suppressDispose);

    public static IAsyncEnumerable<T> Debounce<T>(this IAsyncEnumerable<T> source,
        TimeSpan minUpdateDelay,
        CancellationToken cancellationToken = default)
        => source.Debounce(minUpdateDelay, MomentClockSet.Default.CpuClock, cancellationToken);

    public static async IAsyncEnumerable<T> Debounce<T>(this IAsyncEnumerable<T> source,
        TimeSpan minUpdateDelay,
        IMomentClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var c = Channel.CreateBounded<T>(new BoundedChannelOptions(1) {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _ = source.CopyTo(c, ChannelCompletionMode.Full, cancellationToken);
        await foreach (var item in c.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            yield return item;
            await clock.Delay(minUpdateDelay, cancellationToken).ConfigureAwait(false);
        }
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

    public static async IAsyncEnumerable<T> Prepend<T>(
        this IAsyncEnumerator<T> enumerator,
        T value,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return value;

        while (await enumerator.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            yield return enumerator.Current;
    }

    /* This exists in Stl, though the impl. is different, so temp. keeping it here:

    public static IAsyncEnumerable<T> TrimOnCancellation<T>(this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
        => source.TrimOnCancellation(1, cancellationToken);

    public static IAsyncEnumerable<T> TrimOnCancellation<T>(this IAsyncEnumerable<T> source,
        int bufferSize,
        CancellationToken cancellationToken = default)
    {
        var c = Channel.CreateBounded<T>(new BoundedChannelOptions(bufferSize) {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        _ = source.CopyTo(c, ChannelCompletionMode.PropagateCompletion | ChannelCompletionMode.PropagateError, cancellationToken);
        return c.Reader.ReadAllAsync(cancellationToken);
    }
    */
}
