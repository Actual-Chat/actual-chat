using ActualChat.Internal;
using ActualLab.Diagnostics;

namespace ActualChat;

/// <summary>
/// Extension methods for <see cref="IAsyncEnumerable{T}"/> and <see cref="IAsyncEnumerator{T}"/>.
/// </summary>
#pragma warning disable CA1849 // Task.Result synchronously blocks
public static partial class AsyncEnumerableExt
{
    // PrependOne

    public static async IAsyncEnumerable<T> PrependOne<T>(
        this IAsyncEnumerator<T> enumerator,
        T value,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = cancellationToken; // Cancellation is handled by the enumerator itself
        yield return value;
        while (await enumerator.MoveNextAsync().ConfigureAwait(false))
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

    public static async IAsyncEnumerable<T> AdjacentDistinctBy<T, TKey>(
        this IAsyncEnumerable<T> source,
        Func<T, TKey> selector,
        IEqualityComparer<TKey>? comparer = null)
    {
        comparer ??= EqualityComparer<TKey>.Default;
        T prev = default!;
        var isFirst = true;
        await foreach (var item in source.ConfigureAwait(false)) {
            if (isFirst) {
                isFirst = false;
                yield return item;
            }
            else if (!comparer.Equals(selector(prev), selector(item)))
                yield return item;

            prev = item;
        }
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
    {
        _ = cancellationToken; // Cancellation is handled by the enumerator itself
        return await source.MoveNextAsync().ConfigureAwait(false)
            ? source.Current
            : Option<T>.None;
    }

    public static async ValueTask<Option<T>> TryReadAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        await foreach (var value in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            return value;
        return Option<T>.None;
    }

    // ReadResultAsync

    public static async ValueTask<Result<T>> ReadResultAsync<T>(
        this IAsyncEnumerator<T> source,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken; // Cancellation is handled by the enumerator itself
        try {
            if (!await source.MoveNextAsync().ConfigureAwait(false))
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

    public static async IAsyncEnumerable<T> SuppressExceptions<T>(
        this IAsyncEnumerable<T> source,
        Func<Exception, bool> exceptionFilter,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ReSharper disable once NotDisposedResource
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);

        while (true) {
            bool hasMore;
            T item = default!;
            try {
                hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
                if (hasMore)
                    item = enumerator.Current;
            }
            catch (Exception e) when (exceptionFilter.Invoke(e)) {
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
                hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
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
