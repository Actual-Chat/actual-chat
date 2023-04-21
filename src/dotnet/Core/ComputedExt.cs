namespace ActualChat;

public static class ComputedExt
{
    public static async Task<Computed<T>> When<T>(
        this ValueTask<Computed<T>> computedTask,
        Func<T, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        var computed = await computedTask.ConfigureAwait(false);
        return await computed.When(predicate, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Computed<T>> When<T>(
        this ValueTask<Computed<T>> computedTask,
        Func<T, bool> predicate,
        Timeout timeout,
        CancellationToken cancellationToken = default)
    {
        var computed = await computedTask.ConfigureAwait(false);
        return await computed.When(predicate, timeout, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Computed<T>> When<T>(
        this Computed<T> computed,
        Func<T, bool> predicate,
        Timeout timeout,
        CancellationToken cancellationToken = default)
    {
        var cts = cancellationToken.CreateLinkedTokenSource();
        try {
            var computedTask = computed.When(predicate, cts.Token);
            var timeoutTask = timeout.Wait(cts.Token);
            await Task.WhenAny(timeoutTask, computedTask).ConfigureAwait(false);
            if (timeoutTask.IsCompleted)
                throw new TimeoutException();

            return await computedTask.ConfigureAwait(false);
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    public static async IAsyncEnumerable<Computed<T>> Until<T>(
        this IAsyncEnumerable<Computed<T>> changes,
        Task task,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (task.IsCompleted) {
            await task.ConfigureAwait(false); // will throw exception in case of failed task
            yield break;
        }

        var enumerator = changes.GetAsyncEnumerator(cancellationToken);
        var hasNextChangeTask = enumerator.MoveNextAsync();
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAny(task, hasNextChangeTask.AsTask()).ConfigureAwait(false);

            if (task.IsCompleted) {
                await task.ConfigureAwait(false); // will throw exception in case of failed task
                yield break;
            }

            yield return enumerator.Current;
            hasNextChangeTask = enumerator.MoveNextAsync();
        }
    }
}
