namespace ActualChat;

public readonly record struct Timeout(
    IMomentClock Clock,
    TimeSpan Duration)
{
    public override string ToString()
        => $"{GetType().Name}({Duration.ToShortString()})";

    public Task Delay(CancellationToken cancellationToken = default)
        => Clock.Delay(Duration, cancellationToken);

    public async Task ApplyTo(
        Func<CancellationToken, Task> taskFactory,
        CancellationToken cancellationToken = default)
    {
        var cts = cancellationToken.LinkWith(cancellationToken);
        try {
            var task = taskFactory.Invoke(cts.Token);
            var timeoutTask = Delay(cts.Token);
            await Task.WhenAny(timeoutTask, task).ConfigureAwait(false);
            if (task.IsCompleted) {
                if (timeoutTask.IsCompletedSuccessfully)
                    throw new TimeoutException();

                await task.ConfigureAwait(false);
                return;
            }

            // timeoutTask is completed or cancelled via cancellationToken
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    public async Task<T> ApplyTo<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        CancellationToken cancellationToken = default)
    {
        var cts = cancellationToken.LinkWith(cancellationToken);
        try {
            var task = taskFactory.Invoke(cts.Token);
            var timeoutTask = Delay(cts.Token);
            await Task.WhenAny(timeoutTask, task).ConfigureAwait(false);
            if (task.IsCompleted) {
                if (timeoutTask.IsCompletedSuccessfully)
                    throw new TimeoutException();

                return await task.ConfigureAwait(false);
            }

            // timeoutTask is completed or cancelled via cancellationToken
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }
}
