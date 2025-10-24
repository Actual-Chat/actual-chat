using ActualChat.Internal;
using ActualLab.Diagnostics;

namespace ActualChat;

#pragma warning disable CA1849 // Task.Result synchronously blocks

public static partial class AsyncEnumerableExt
{
    // PrependOne

    public static async IAsyncEnumerable<T> PrependOne<T>(
        this IAsyncEnumerator<T> enumerator,
        T value,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return value;
        while (await enumerator.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            yield return enumerator.Current;
    }

    public static async IAsyncEnumerable<TSource> PrependOne<TSource>(
        this IAsyncEnumerable<TSource> source,
        Task<TSource> firstElementTask)
    {
        yield return await firstElementTask.ConfigureAwait(false);
        await foreach (var item in source.ConfigureAwait(false))
            yield return item;
    }

    // TakeWhile

    public static async IAsyncEnumerable<T> TakeWhile<T>(
        this IAsyncEnumerable<T> source,
        Task whileTask,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (whileTask.IsCompleted)
            yield break;

        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        try {
            var hasNextTask = enumerator.MoveNextAsync();
            while (true) {
                if (!hasNextTask.IsCompleted)
                    await Task.WhenAny(whileTask, hasNextTask.AsTask()).ConfigureAwait(false);

                if (whileTask.IsCompleted || !await hasNextTask.ConfigureAwait(false))
                    yield break;

                yield return enumerator.Current;
                hasNextTask = enumerator.MoveNextAsync();
            }
        }
        finally {
            await enumerator.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }

    // AdjacentDistinct

    public static async IAsyncEnumerable<T> AdjacentDistinct<T>(
        this IAsyncEnumerable<T> source,
        IEqualityComparer<T>? comparer = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        T prev = default!;
        var isFirst = true;
        await foreach (var item in source.ConfigureAwait(false)) {
            if (isFirst) {
                isFirst = false;
                yield return item;
            }
            else if (!comparer.Equals(prev, item))
                yield return item;

            prev = item;
        }
    }

    // Throttle

    public static IAsyncEnumerable<T> Throttle<T>(this IAsyncEnumerable<T> source,
        TimeSpan minInterval,
        CancellationToken cancellationToken = default)
        => source.Throttle(minInterval, MomentClockSet.Default.CpuClock, cancellationToken);

    public static async IAsyncEnumerable<T> Throttle<T>(this IAsyncEnumerable<T> source,
        TimeSpan minInterval,
        MomentClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var c = Channel.CreateBounded<T>(new BoundedChannelOptions(1) {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _ = source.CopyTo(c, ChannelCopyMode.CopyAllSilently, cancellationToken);
        await foreach (var item in c.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            yield return item;
            if (minInterval > TimeSpan.Zero)
                await clock.Delay(minInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // Memoize

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

    // IsNonEmpty

    public static async Task<Option<IAsyncEnumerable<T>>> IsNonEmpty<T>(
        this IAsyncEnumerable<T> source,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // ReSharper disable once PossibleMultipleEnumeration
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        try {
            var hasCurrent = await enumerator
                .MoveNextAsync().AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            // ReSharper disable once PossibleMultipleEnumeration
            return Option.Some(source.WithUsedEnumerator(enumerator, hasCurrent));
        }
        catch (TimeoutException) {
            return Option<IAsyncEnumerable<T>>.None;
        }
    }

    public static async Task<Option<IAsyncEnumerable<T>>> IsNonEmpty<T>(
        this IAsyncEnumerable<T> source,
        MomentClock clock,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        // ReSharper disable once PossibleMultipleEnumeration
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        try {
            var hasCurrent = await enumerator
                .MoveNextAsync().AsTask()
                .WaitAsync(clock, timeout, cancellationToken)
                .ConfigureAwait(false);
            // ReSharper disable once PossibleMultipleEnumeration
            return Option.Some(source.WithUsedEnumerator(enumerator, hasCurrent));
        }
        catch (TimeoutException) {
            return Option<IAsyncEnumerable<T>>.None;
        }
    }

    // TryReadAsync

    public static async ValueTask<Option<T>> TryReadAsync<T>(
        this IAsyncEnumerator<T> source,
        CancellationToken cancellationToken = default)
        => await source.MoveNextAsync(cancellationToken).ConfigureAwait(false)
            ? source.Current
            : Option<T>.None;

    // ReadResultAsync

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

    // ToMaybeHasNextSequence

    public static async IAsyncEnumerable<MaybeHasNext<TSource>> ToMaybeHasNextSequence<TSource>(
        this IAsyncEnumerable<TSource> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);
        var hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
        if (!hasNext)
            yield break;

        do {
            var item = enumerator.Current;
            hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            yield return new MaybeHasNext<TSource>(item, hasNext);
        } while (hasNext);
    }

    // SuppressXxx

    public static async IAsyncEnumerable<T> SuppressException<T, TException>(
        this IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    where TException : Exception
    {
        // ReSharper disable once NotDisposedResource
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);

        while (true) {
            bool hasMore;
            T item = default!;
            try {
                hasMore = await enumerator.MoveNextAsync(cancellationToken).ConfigureAwait(false);
                if (hasMore)
                    item = enumerator.Current;
            }
            catch (TException) {
                yield break;
            }
            if (hasMore)
                yield return item;
            else
                yield break;
        }
    }

    // WithActivity

    public static async IAsyncEnumerable<T> WithActivity<T>(
        this IAsyncEnumerable<T> source,
        Activity? activity,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);

        while (true) {
            bool hasMore;
            T item = default!;
            try {
                hasMore = await enumerator.MoveNextAsync(cancellationToken).ConfigureAwait(false);
                if (hasMore)
                    item = enumerator.Current;
            }
            catch (Exception e) {
                activity?.Finalize(e, cancellationToken);
                yield break;
            }
            if (hasMore)
                yield return item;
            else {
                activity?.SetStatus(ActivityStatusCode.Ok);
                yield break;
            }
        }
    }

    // Enumerator-related

    public static IAsyncEnumerable<T> AsEnumerableOnce<T>(this IAsyncEnumerator<T> enumerator, bool suppressDispose)
        => new AsyncEnumerableOnce<T>(enumerator, suppressDispose);

    public static IAsyncEnumerable<T> WithUsedEnumerator<T>(
        this IAsyncEnumerable<T> source,
        IAsyncEnumerator<T> usedEnumerator,
        bool hasCurrent)
        => new AsyncEnumerableWithUsedEnumerator<T>(source, usedEnumerator, hasCurrent);
}
