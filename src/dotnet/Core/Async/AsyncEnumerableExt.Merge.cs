namespace ActualChat;

public static partial class AsyncEnumerableExt
{
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
    public static IAsyncEnumerable<TSource> Merge<TSource>(
        this IAsyncEnumerable<TSource> source,
        params IAsyncEnumerable<TSource>[] sources)
        => source.Merge(sources, CancellationToken.None);

    public static async IAsyncEnumerable<TSource> Merge<TSource>(
        this IAsyncEnumerable<TSource> source,
        IAsyncEnumerable<TSource>[] sources,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
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
    public static IAsyncEnumerable<TSource> Merge<TSource>(
        this IAsyncEnumerable<TSource> left,
        IAsyncEnumerable<TSource> right)
        => left.Merge(right, CancellationToken.None);

    public static async IAsyncEnumerable<TSource> Merge<TSource>(
        this IAsyncEnumerable<TSource> left,
        IAsyncEnumerable<TSource> right,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
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
}
