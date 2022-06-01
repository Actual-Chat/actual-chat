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

    public static (IAsyncEnumerable<TSource> Matched, IAsyncEnumerable<TSource> NotMatched) Split<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource,bool> splitPredicate,
        CancellationToken cancellationToken)
    {
        var matched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });
        var notMatched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });

        _ = Task.Run(async () => {
            Exception? error = null;
            try {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                    if (splitPredicate(item))
                        await matched.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                    else
                        await notMatched.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                error = e;
            }
            finally {
                matched.Writer.TryComplete(error);
                notMatched.Writer.TryComplete(error);
            }
        }, cancellationToken);

        return (matched.Reader.ReadAllAsync(cancellationToken), notMatched.Reader.ReadAllAsync(cancellationToken));
    }

    public static async IAsyncEnumerable<TSource> Delay<TSource>(
        this IAsyncEnumerable<TSource> source,
        TimeSpan eachItemDelay,
        IMomentClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in source.ConfigureAwait(false)) {
            await clock.Delay(eachItemDelay, cancellationToken).ConfigureAwait(false);

            yield return item;
        }
    }

    public static async IAsyncEnumerable<List<TSource>> Chunk<TSource>(
        this IAsyncEnumerable<TSource> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var buffer = new List<TSource>(count);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Add(item);
            if (buffer.Count != count)
                continue;

            yield return buffer;

            buffer = new List<TSource>(count);
        }

        if (buffer.Count > 0)
            yield return buffer;
    }

    // originally copied from there https://github.com/dotnet/reactive/blob/9f2a8090cea4bf931d4ac3ad071f4df147f4df50/Ix.NET/Source/System.Interactive.Async/System/Linq/Operators/Merge.cs#L20
    // fixed bugs and refactored later

    /// <summary>
    /// Merges elements from all of the specified async-enumerable sequences into a single async-enumerable sequence.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequences.</typeparam>
    /// <param name="source">Observable sequence.</param>
    /// <param name="otherSources">Observable sequences.</param>
    /// <returns>The async-enumerable sequence that merges the elements of the async-enumerable sequences.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="otherSources"/> is null.</exception>
    public static IAsyncEnumerable<TSource> Merge<TSource>(this IAsyncEnumerable<TSource> source, params IAsyncEnumerable<TSource>[] otherSources)
    {
        if (otherSources == null)
            throw new ArgumentNullException(nameof(otherSources));

        return Core(source, otherSources);

        static async IAsyncEnumerable<TSource> Core(
            IAsyncEnumerable<TSource> source,
            IAsyncEnumerable<TSource>[] sources,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var count = sources.Length + 1;

            var enumerators = new IAsyncEnumerator<TSource>?[count];
            var moveNextTasks = new ValueTask<bool>[count];
            var errors = null as List<Exception>;
            try {
                var sourceEnumerator = source.GetAsyncEnumerator(cancellationToken);
                enumerators[0] = sourceEnumerator;
                moveNextTasks[0] = sourceEnumerator.MoveNextAsync();

                for (var i = 1; i < count; i++) {
                    var enumerator = sources[i - 1].GetAsyncEnumerator(cancellationToken);
                    enumerators[i] = enumerator;
                    moveNextTasks[i] = enumerator.MoveNextAsync();
                }

                var completedTask = TaskExt.WhenAny(moveNextTasks);
                int active = count;
                while (active > 0) {
                    var index = await completedTask;
                    var enumerator = enumerators[index];
                    var moveNextTask = moveNextTasks[index];
                    bool moved = false;
                    try {
                        moved = await moveNextTask.ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        errors ??= new List<Exception>();
                        errors.Add(e);
                    }

                    if (!moved) {
                        enumerators[index] = null!; // NB: Avoids attempt at double dispose in finally if disposing fails.

                        if (enumerator != null)
                            await enumerator.DisposeAsync().ConfigureAwait(false);

                        active--;
                    }
                    else {
                        if (enumerator == null) continue;

                        var item = enumerator.Current;
                        completedTask.Replace(index, enumerator.MoveNextAsync());

                        yield return item;
                    }
                }
            }
            finally {
                for (var i = count - 1; i >= 0; i--) {
                    var enumerator = enumerators[i];

                    try {
                        if (enumerator != null)
                            await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        errors ??= new List<Exception>();
                        errors.Add(ex);
                    }
                }
            }
            if (errors != null)
                switch (errors.Count) {
                case 1:
                    throw errors[0];
                case > 1 when errors.All(e => e is OperationCanceledException):
                    throw errors[0];
                case > 1:
                    throw new AggregateException(errors);
                }
        }
    }

    public static async IAsyncEnumerable<List<TSource>> Buffer<TSource>(
        this IAsyncEnumerable<TSource> source,
        TimeSpan bufferDuration,
        IMomentClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new List<TSource>();
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);
        var moveNextTask = enumerator.MoveNextAsync();
        var delayTask = clock.Delay(bufferDuration, cancellationToken);
        while (true) {
            if (buffer.Count > 0)
                await Task.WhenAny(moveNextTask.AsTask(), delayTask).ConfigureAwait(false);
            else
                await moveNextTask.ConfigureAwait(false);

            if (moveNextTask.IsCompleted) {
                var hasNext = await moveNextTask;
                if (hasNext) {
                    buffer.Add(enumerator.Current);
                    moveNextTask = enumerator.MoveNextAsync();
                }
                else {
                    yield return buffer;
                    break;
                }
            }

            if (delayTask.IsCompleted) {
                await delayTask; // Will throw an exception on cancellation
                if (buffer.Count > 0) {
                    yield return buffer;

                    buffer = new List<TSource>();
                    delayTask = clock.Delay(bufferDuration, cancellationToken);
                }
            }
        }
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
