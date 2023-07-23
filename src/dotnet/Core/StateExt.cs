namespace ActualChat;

public static class StateExt
{
    public static async Task<Computed<T>> When<T>(
        this IState<T> state,
        Func<T, bool> predicate,
        Timeout timeout,
        CancellationToken cancellationToken = default)
    {
        var cts = cancellationToken.CreateLinkedTokenSource();
        try {
            var whenTask = state.When(predicate, cts.Token);
            var timeoutTask = timeout.Wait(cts.Token);
            await Task.WhenAny(timeoutTask, whenTask).ConfigureAwait(false);
            if (timeoutTask.IsCompleted && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException();

            return await whenTask.ConfigureAwait(false);
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }
}
