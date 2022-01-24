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

    public static (IAsyncEnumerable<TSource>, IAsyncEnumerable<TSource>) Split<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource,bool> splitPredicate,
        CancellationToken cancellationToken)
    {
        var matched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
        });
        var notMatched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
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

    /// <summary>
    /// Merges elements from all of the specified async-enumerable sequences into a single async-enumerable sequence.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequences.</typeparam>
    /// <param name="source">Observable sequence.</param>
    /// <param name="sources">Observable sequences.</param>
    /// <returns>The async-enumerable sequence that merges the elements of the async-enumerable sequences.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is null.</exception>
    public static IAsyncEnumerable<TSource> Merge<TSource>(this IAsyncEnumerable<TSource> source, params IAsyncEnumerable<TSource>[] sources)
    {
        if (sources == null)
            throw new ArgumentNullException(nameof(sources));

        return Core(source, sources);

        static async IAsyncEnumerable<TSource> Core(
            IAsyncEnumerable<TSource> source,
            IAsyncEnumerable<TSource>[] sources,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var count = sources.Length + 1;

            var enumerators = new IAsyncEnumerator<TSource>[count];
            var moveNextTasks = new ValueTask<bool>[count];

            try {
                IAsyncEnumerator<TSource> sourceEnumerator = source.GetAsyncEnumerator(cancellationToken);
                enumerators[0] = sourceEnumerator;
                moveNextTasks[0] = sourceEnumerator.MoveNextAsync();

                for (var i = 1; i < count; i++) {
                    IAsyncEnumerator<TSource> enumerator = sources[i - 1].GetAsyncEnumerator(cancellationToken);
                    enumerators[i] = enumerator;

                    // REVIEW: This follows the lead of the original implementation where we kick off MoveNextAsync
                    //         operations immediately. An alternative would be to do this in a separate stage, thus
                    //         preventing concurrency across MoveNextAsync and GetAsyncEnumerator calls and avoiding
                    //         any MoveNextAsync calls before all enumerators are acquired (or an exception has
                    //         occurred doing so).

                    moveNextTasks[i] = enumerator.MoveNextAsync();
                }

                var whenAny = TaskExt.WhenAny(moveNextTasks);
                int active = count;
                while (active > 0) {
                    int index = await whenAny;

                    IAsyncEnumerator<TSource> enumerator = enumerators[index];
                    ValueTask<bool> moveNextTask = moveNextTasks[index];

                    if (!await moveNextTask.ConfigureAwait(false)) {
                        //
                        // Replace the task in our array by a completed task to make finally logic easier. Note that
                        // the WhenAnyValueTask object has a reference to our array (i.e. no copy is made), so this
                        // gets rid of any resources the original task may have held onto. However, we *don't* call
                        // whenAny.Replace to set this value, because it'd attach an awaiter to the already completed
                        // task, causing spurious wake-ups when awaiting whenAny.
                        //

                        moveNextTasks[index] = new ValueTask<bool>();

                        enumerators[index] = null!; // NB: Avoids attempt at double dispose in finally if disposing fails.
                        await enumerator.DisposeAsync().ConfigureAwait(false);

                        active--;
                    }
                    else
                    {
                        TSource item = enumerator.Current;

                        //
                        // Replace the task using whenAny.Replace, which will write it to the moveNextTasks array, and
                        // will start awaiting the task. Note we don't have to write to moveNextTasks ourselves because
                        // the whenAny object has a reference to it (i.e. no copy is made).
                        //

                        whenAny.Replace(index, enumerator.MoveNextAsync());

                        yield return item;
                    }
                }
            }
            finally
            {
                var errors = default(List<Exception>);

                for (var i = count - 1; i >= 0; i--) {
                    ValueTask<bool> moveNextTask = moveNextTasks[i];
                    IAsyncEnumerator<TSource> enumerator = enumerators[i];

                    try {
                        try {
                            //
                            // Await the task to ensure outstanding work is completed prior to performing a dispose
                            // operation. Note that we don't have to do anything special for tasks belonging to
                            // enumerators that have finished; we swapped in a placeholder completed task.
                            //

                            // REVIEW: This adds an additional continuation to all of the pending tasks (note that
                            //         whenAny also has registered one). The whenAny object will be collectible
                            //         after all of these complete. Alternatively, we could drain via whenAny, by
                            //         awaiting it until the active count drops to 0. This saves on attaching the
                            //         additional continuations, but we need to decide on order of dispose. Right
                            //         now, we dispose in opposite order of acquiring the enumerators, with the
                            //         exception of enumerators that were disposed eagerly upon early completion.
                            //         Should we care about the dispose order at all?

                            _ = await moveNextTask.ConfigureAwait(false);
                        }
                        finally
                        {
                            if (enumerator != null)
                            {
                                await enumerator.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (errors == null)
                            errors = new List<Exception>();

                        errors.Add(ex);
                    }
                }

                // NB: If we had any errors during cleaning (and awaiting pending operations), we throw these exceptions
                //     instead of the original exception that may have led to running the finally block. This is similar
                //     to throwing from any finally block (except that we catch all exceptions to ensure cleanup of all
                //     concurrent sequences being merged).

                if (errors != null)
 #pragma warning disable MA0072
                    throw new AggregateException(errors);
 #pragma warning restore MA0072
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
