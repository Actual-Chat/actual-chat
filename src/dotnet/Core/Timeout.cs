namespace ActualChat;

public readonly record struct Timeout(
    IMomentClock Clock,
    TimeSpan Duration)
{
    public override string ToString()
        => $"{GetType().Name}({Duration.ToShortString()})";

    public Task Delay(CancellationToken cancellationToken = default)
        => Clock.Delay(Duration, cancellationToken);

    public Task ApplyTo(
        Func<CancellationToken, Task> taskFactory,
        CancellationToken cancellationToken = default)
        => ApplyTo(taskFactory, true, cancellationToken);
    public async Task ApplyTo(
        Func<CancellationToken, Task> taskFactory,
        bool throwOnTimeout,
        CancellationToken cancellationToken = default)
    {
        var cts = cancellationToken.LinkWith(cancellationToken);
        try {
            var task = taskFactory.Invoke(cts.Token);
            var timeoutTask = Delay(cts.Token);
            await Task.WhenAny(timeoutTask, task).ConfigureAwait(false);
            if (task.IsCompleted) {
                await task.ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (throwOnTimeout)
                throw new TimeoutException();
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    public Task<T> ApplyTo<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        CancellationToken cancellationToken = default)
        => ApplyTo(taskFactory, true, cancellationToken);
    public async Task<T> ApplyTo<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        bool throwOnTimeout,
        CancellationToken cancellationToken = default)
    {
        var cts = cancellationToken.LinkWith(cancellationToken);
        try {
            var task = taskFactory.Invoke(cts.Token);
            var timeoutTask = Delay(cts.Token);
            await Task.WhenAny(timeoutTask, task).ConfigureAwait(false);
            if (task.IsCompleted)
                return await task.ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return throwOnTimeout
                ? throw new TimeoutException()
                : default!;
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }
}
