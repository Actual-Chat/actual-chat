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
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var computed = await computedTask.ConfigureAwait(false);
        return await computed.When(predicate, timeout, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Computed<T>> When<T>(
        this Computed<T> computed,
        Func<T, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(timeout);
        try {
            return await computed.When(predicate, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            throw new TimeoutException();
        }
    }
}
