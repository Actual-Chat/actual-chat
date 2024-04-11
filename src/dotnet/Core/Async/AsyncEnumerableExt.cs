using ActualChat.Internal;

namespace ActualChat;

#pragma warning disable CA1849 // Task.Result synchronously blocks

public static class AsyncEnumerableExt
{
    public static IAsyncEnumerable<T> AsEnumerableOnce<T>(this IAsyncEnumerator<T> enumerator, bool suppressDispose)
        => new AsyncEnumerableOnce<T>(enumerator, suppressDispose);

    public static IAsyncEnumerable<T> Throttle<T>(this IAsyncEnumerable<T> source,
        TimeSpan minInterval,
        CancellationToken cancellationToken = default)
        => source.Throttle(minInterval, MomentClockSet.Default.CpuClock, cancellationToken);

    public static async IAsyncEnumerable<T> Throttle<T>(this IAsyncEnumerable<T> source,
        TimeSpan minInterval,
        IMomentClock clock,
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
            return Option<IAsyncEnumerable<T>>.Some(source.WithUsedEnumerator(enumerator, hasCurrent));
        }
        catch (TimeoutException) {
            return Option<IAsyncEnumerable<T>>.None;
        }
    }

    public static async Task<Option<IAsyncEnumerable<T>>> IsNonEmpty<T>(
        this IAsyncEnumerable<T> source,
        IMomentClock clock,
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
            return Option<IAsyncEnumerable<T>>.Some(source.WithUsedEnumerator(enumerator, hasCurrent));
        }
        catch (TimeoutException) {
            return Option<IAsyncEnumerable<T>>.None;
        }
    }

    public static async IAsyncEnumerable<T> TakeWhile<T>(
        this IAsyncEnumerable<T> source,
        Task whileTask,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (whileTask.IsCompleted)
            yield break;

        // ReSharper disable once NotDisposedResource
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);

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

    public static IAsyncEnumerable<T> WithUsedEnumerator<T>(
        this IAsyncEnumerable<T> source,
        IAsyncEnumerator<T> usedEnumerator,
        bool hasCurrent)
        => new AsyncEnumerableWithUsedEnumerator<T>(source, usedEnumerator, hasCurrent);

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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return value;

        while (await enumerator.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            yield return enumerator.Current;
    }

    public static (IAsyncEnumerable<TSource> Matched, IAsyncEnumerable<TSource> NotMatched) Split<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource,bool> splitPredicate,
        CancellationToken cancellationToken = default)
        => source.Split((_, s) => splitPredicate(s), cancellationToken);

    public static (IAsyncEnumerable<TSource> Matched, IAsyncEnumerable<TSource> NotMatched) Split<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<int,TSource,bool> splitPredicate,
        CancellationToken cancellationToken = default)
    {
        var matched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });
        var notMatched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });

        _ = BackgroundTask.Run(async () => {
            Exception? error = null;
            try {
                var i = 0;
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                    if (splitPredicate(i++, item))
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

    public static (Task<TSource> HeadTask, IAsyncEnumerable<TSource> Tail) SplitHead<TSource>(
        this IAsyncEnumerable<TSource> source,
        CancellationToken cancellationToken = default)
    {
        var headSource = TaskCompletionSourceExt.New<TSource>();
        var notMatched = Channel.CreateUnbounded<TSource>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
        });

        _ = BackgroundTask.Run(async () => {
            Exception? error = null;
            try {
                bool isHead = true;
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                    if (isHead) {
                        isHead = false;
                        headSource.SetResult(item);
                    }
                    else
                        await notMatched.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                error = e;
            }
            finally {
                if (error != null)
                    headSource.TrySetException(error);
                else
                    headSource.TrySetCanceled();
                notMatched.Writer.TryComplete(error);
            }
        }, cancellationToken);

        return (headSource.Task, notMatched.Reader.ReadAllAsync(cancellationToken));
    }

    public static async IAsyncEnumerable<List<TSource>> Chunk<TSource>(
        this IAsyncEnumerable<TSource> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var buffer = new List<TSource>(count);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Add(item);
            if (buffer.Count < count)
                continue;

            yield return buffer;
            buffer = new List<TSource>(count);
        }

        if (buffer.Count > 0)
            yield return buffer;
    }

    public static async IAsyncEnumerable<List<TSource>> ChunkWhile<TSource>(
        this IAsyncEnumerable<TSource> source,
        Func<List<TSource>, bool> predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var buffer = new List<TSource>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Add(item);
            if (predicate(buffer))
                continue;

            yield return buffer;
            buffer = new List<TSource>();
        }

        if (buffer.Count > 0)
            yield return buffer;
    }

    public static async IAsyncEnumerable<MaybeHasNext<List<TSource>>> ChunkWhile<TSource>(
        this IAsyncEnumerable<MaybeHasNext<TSource>> source,
        Func<List<TSource>, bool> predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var buffer = new List<TSource>();
        var hasNext = true;
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Add(item.Item);
            hasNext &= item.HasNext;
            if (predicate(buffer))
                continue;

            yield return new MaybeHasNext<List<TSource>>(buffer, hasNext);

            buffer = new List<TSource>();
        }

        if (buffer.Count > 0)
            yield return new MaybeHasNext<List<TSource>>(buffer, false);
    }

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

    // Originally copied from there https://github.com/dotnet/reactive/blob/9f2a8090cea4bf931d4ac3ad071f4df147f4df50/Ix.NET/Source/System.Interactive.Async/System/Linq/Operators/Merge.cs#L20
    // fixed bugs and refactored later

    /// <summary>
    /// Merges elements from all of the specified async-enumerable sequences into a single async-enumerable sequence.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequences.</typeparam>
    /// <param name="source">Observable sequence.</param>
    /// <param name="sources">Observable sequences.</param>
    /// <returns>The async-enumerable sequence that merges the elements of the async-enumerable sequences.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sources"/> is null.</exception>
    public static IAsyncEnumerable<TSource> Merge<TSource>(this IAsyncEnumerable<TSource> source, params IAsyncEnumerable<TSource>[] sources)
        => source.Merge(sources, CancellationToken.None);

    public static async IAsyncEnumerable<TSource> Merge<TSource>(
        this IAsyncEnumerable<TSource> source,
        IAsyncEnumerable<TSource>[] sources,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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

    /// <summary>
    /// Merges elements from all of the specified async-enumerable sequences into a single async-enumerable sequence.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequences.</typeparam>
    /// <param name="left">Observable sequence.</param>
    /// <param name="right">Observable sequence.</param>
    /// <returns>The async-enumerable sequence that merges the elements of the async-enumerable sequences.</returns>
    public static IAsyncEnumerable<TSource> Merge<TSource>(this IAsyncEnumerable<TSource> left, IAsyncEnumerable<TSource> right)
        => left.Merge(right, CancellationToken.None);

    public static async IAsyncEnumerable<TSource> Merge<TSource>(
        this IAsyncEnumerable<TSource> left,
        IAsyncEnumerable<TSource> right,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumeratorLeft = left.GetAsyncEnumerator(cancellationToken);
        var enumeratorRight = right.GetAsyncEnumerator(cancellationToken);
        var errors = null as List<Exception>;
        var falseTask = ActualLab.Async.TaskExt.FalseTask;
        var moveNextLeftTask = falseTask;
        var moveNextRightTask = falseTask;
        try {
            var hasLeft = true;
            var hasRight = true;
            do {
                ValueTask<bool> moveNextLeftValueTask = default;
                ValueTask<bool> moveNextRightValueTask = default;
                if (hasLeft && ReferenceEquals(moveNextLeftTask, falseTask))
                    moveNextLeftValueTask = enumeratorLeft.MoveNextAsync();
                if (hasRight && ReferenceEquals(moveNextRightTask, falseTask))
                    moveNextRightValueTask = enumeratorRight.MoveNextAsync();
                if (hasLeft) {
                    if (moveNextLeftValueTask.IsCompletedSuccessfully)
                        hasLeft = moveNextLeftValueTask.Result;
                    else if (ReferenceEquals(moveNextLeftTask, falseTask))
                        moveNextLeftTask = moveNextLeftValueTask.AsTask();
                }

                if (hasRight) {
                    if (moveNextRightValueTask.IsCompletedSuccessfully)
                        hasRight = moveNextRightValueTask.Result;
                    else if (ReferenceEquals(moveNextRightTask, falseTask))
                        moveNextRightTask = moveNextRightValueTask.AsTask();
                }

                var hasLeftTask = !ReferenceEquals(moveNextLeftTask, falseTask);
                var hasRightTask = !ReferenceEquals(moveNextRightTask, falseTask);
                if (hasLeftTask && hasRightTask) {
                    var readyTask = await Task.WhenAny(moveNextLeftTask, moveNextRightTask).ConfigureAwait(false);
                    if (ReferenceEquals(readyTask, moveNextLeftTask))
                        try {
                            hasLeft = readyTask.Result;
                            moveNextLeftTask = falseTask;
                        }
                        catch (Exception e) {
                            errors ??= new List<Exception>();
                            errors.Add(e);
                        }
                    else
                        try {
                            hasRight = readyTask.Result;
                            moveNextRightTask = falseTask;
                        }
                        catch (Exception e) {
                            errors ??= new List<Exception>();
                            errors.Add(e);
                        }
                }
                else if (hasLeftTask)
                    try {
                        if (moveNextLeftTask.IsCompleted) {
                            hasLeft = moveNextLeftTask.Result;
                            moveNextLeftTask = falseTask;
                        }
                    }
                    catch (Exception e) {
                        errors ??= new List<Exception>();
                        errors.Add(e);
                    }
                else if (hasRightTask)
                    try {
                        if (moveNextRightTask.IsCompleted) {
                            hasRight = moveNextRightTask.Result;
                            moveNextRightTask = falseTask;
                        }
                    }
                    catch (Exception e) {
                        errors ??= new List<Exception>();
                        errors.Add(e);
                    }

                if (!hasLeft)
                    await enumeratorLeft.DisposeSilentlyAsync().ConfigureAwait(false);
                else if (ReferenceEquals(moveNextLeftTask, falseTask)) {
                    var item = enumeratorLeft.Current;
                    yield return item;
                }
                if (!hasRight)
                    await enumeratorRight.DisposeSilentlyAsync().ConfigureAwait(false);
                else if (ReferenceEquals(moveNextRightTask, falseTask)) {
                    var item = enumeratorRight.Current;
                    yield return item;
                }
            } while (hasLeft || hasRight);
        }
        finally {
            try {
                await enumeratorLeft.DisposeSilentlyAsync().ConfigureAwait(false);
                await enumeratorRight.DisposeSilentlyAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                errors ??= new List<Exception>();
                errors.Add(ex);
            }
        }
        if (errors == null)
            yield break;

        switch (errors.Count) {
        case 1:
            throw errors[0];
        case > 1 when errors.All(e => e is OperationCanceledException):
            throw errors[0];
        case > 1:
            var exception = new AggregateException(errors).Flatten();
            if (exception.InnerExceptions.All(e => e is OperationCanceledException))
                throw exception.InnerExceptions[0];

            throw exception;
        }
    }

    public static async IAsyncEnumerable<TSource> Buffer<TSource>(
        this IAsyncEnumerable<TSource> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var buffer = new Queue<TSource>(count);
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            buffer.Enqueue(item);
            while (buffer.Count >= count) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return buffer.Dequeue();
            }
        }

        while (buffer.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return buffer.Dequeue();
        }
    }

    public static async IAsyncEnumerable<List<TSource>> Buffer<TSource>(
        this IAsyncEnumerable<TSource> source,
        TimeSpan bufferDuration,
        IMomentClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        bufferDuration = bufferDuration.Positive();
        var buffer = new List<TSource>();
        // ReSharper disable once NotDisposedResource
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
#pragma warning disable MA0004
                var hasNext = await moveNextTask.ConfigureAwait(false);
#pragma warning restore MA0004
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
#pragma warning disable MA0004
                await delayTask.ConfigureAwait(false); // Will throw an exception on cancellation
#pragma warning restore MA0004
                if (buffer.Count > 0) {
                    yield return buffer;

                    buffer = new List<TSource>();
                    delayTask = clock.Delay(bufferDuration, cancellationToken);
                }
            }
        }
    }

    public static async IAsyncEnumerable<TSource> Prepend<TSource>(this IAsyncEnumerable<TSource> source, Task<TSource> elementTask)
    {
        if (source == null)
            throw StandardError.Constraint(nameof(Prepend), "source is null.");

        yield return await elementTask.ConfigureAwait(false);

        await foreach (var item in source.ConfigureAwait(false))
            yield return item;
    }

    public static IAsyncEnumerable<TSource> AsAsyncEnumerable<TSource>(this IList<TSource> source)
    {
        if (source == null)
            throw StandardError.Constraint(nameof(AsAsyncEnumerable), "source is null.");

        return source.Count == 0
            ? AsyncEnumerable.Empty<TSource>()
            : new AsyncEnumerableAdapter<TSource>(source);
    }

    private readonly struct AsyncEnumerableAdapter<T> : IAsyncEnumerable<T>
    {
        private readonly IList<T> _source;

        public AsyncEnumerableAdapter(IList<T> source)
            => _source = source;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
            => new AsyncEnumeratorAdapter<T>(_source);
    }

    private sealed class AsyncEnumeratorAdapter<T>(IList<T> source) : IAsyncEnumerator<T>
    {
        private int _index = -1;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;

        public ValueTask<bool> MoveNextAsync()
            => new (++_index < source.Count);

        public T Current => source[_index];
    }

    private record struct MoveNextResult(Task<bool> MOveNextTask, int Index);

    /* This exists in ActualLab.Core, though the impl. is different, so temp. keeping it here:

    public static IAsyncEnumerable<T> TrimOnCancellation<T>(this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
        => source.SuppressCancellation(1, cancellationToken);

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
