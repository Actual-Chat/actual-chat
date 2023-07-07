namespace ActualChat;

public static class CancellationTokenExt
{
    private static CancellationTokenSource CancelledTokenSource { get; }

    public static CancellationToken Cancelled { get; }

    static CancellationTokenExt()
    {
        CancelledTokenSource = new CancellationTokenSource();
        CancelledTokenSource.Cancel();
        Cancelled = CancelledTokenSource.Token;
    }

    public static async Task TimeoutBlock(
        this CancellationToken cancellationToken,
        Timeout timeout,
        Func<CancellationToken, Task> block)
    {
        var cts = cancellationToken.LinkWith(cancellationToken);
        try {
            var blockTask = block.Invoke(cts.Token);
            var timeoutTask = timeout.Wait(cts.Token);
            await Task.WhenAny(timeoutTask, blockTask).ConfigureAwait(false);
            if (timeoutTask.IsCompleted && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException();

            await blockTask.ConfigureAwait(false);
        }
        finally {
            cts.Dispose();
        }
    }

    public static async Task<T> TimeoutBlock<T>(
        this CancellationToken cancellationToken,
        Timeout timeout,
        Func<CancellationToken, Task<T>> block)
    {
        var cts = cancellationToken.LinkWith(cancellationToken);
        try {
            var blockTask = block.Invoke(cts.Token);
            var timeoutTask = timeout.Wait(cts.Token);
            await Task.WhenAny(timeoutTask, blockTask).ConfigureAwait(false);
            if (timeoutTask.IsCompleted && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException();

            return await blockTask.ConfigureAwait(false);
        }
        finally {
            cts.Dispose();
        }
    }

    public static async Task<T> TimeoutBlock<T>(
        this CancellationToken cancellationToken,
        Timeout timeout,
        T timeoutResult,
        Func<CancellationToken, Task<T>> block)
    {
        var cts = cancellationToken.LinkWith(cancellationToken);
        try {
            var blockTask = block.Invoke(cts.Token);
            var timeoutTask = timeout.Wait(cts.Token);
            await Task.WhenAny(timeoutTask, blockTask).ConfigureAwait(false);
            if (timeoutTask.IsCompleted && !cancellationToken.IsCancellationRequested)
                return timeoutResult;

            return await blockTask.ConfigureAwait(false);
        }
        finally {
            cts.Dispose();
        }
    }
}
