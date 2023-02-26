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
        using var cts = cancellationToken.CreateLinkedTokenSource();
        var computedTask = computed.When(predicate, cts.Token);
        var timeoutTask = timeout.Wait(cts.Token);
        await Task.WhenAny(timeoutTask, computedTask).ConfigureAwait(false);
        if (timeoutTask.IsCompleted)
            throw new TimeoutException();

        return await computedTask.ConfigureAwait(false);
    }
}
