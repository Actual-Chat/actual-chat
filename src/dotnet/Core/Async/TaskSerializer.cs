namespace ActualChat;

public sealed class TaskSerializer
{
    private readonly object _lock = new();
    private CancellationTokenSource _abortCts = new();
    private volatile Task _whenCompleted = Task.CompletedTask;

    // ReSharper disable once InconsistentlySynchronizedField
    public Task WhenCompleted => _whenCompleted;

    public Task Abort()
        => EnqueueImpl(null, true);

    public Task Enqueue(Func<CancellationToken, Task> taskFactory, bool mustAbortPreviousTasks = false)
        => EnqueueImpl(taskFactory, mustAbortPreviousTasks);
    public Task<Result<T>> Enqueue<T>(Func<CancellationToken, Task<T>> taskFactory, bool mustAbortPreviousTasks = false)
        => EnqueueImpl(taskFactory, mustAbortPreviousTasks);

    // Private methods

    private Task<Result<Unit>> EnqueueImpl(Func<CancellationToken, Task>? taskFactory, bool mustAbortPreviousTasks)
    {
        lock (_lock) {
            var whenCompleted = _whenCompleted;
            if (mustAbortPreviousTasks) {
                _abortCts.CancelAndDisposeSilently();
                _abortCts = new();
            }
            var nextTask = CompleteAndProcess(whenCompleted, taskFactory, _abortCts.Token);
            _whenCompleted = nextTask;
            return nextTask;
        }
    }

    private Task<Result<T>> EnqueueImpl<T>(Func<CancellationToken, Task<T>>? taskFactory, bool mustAbortPreviousTasks)
    {
        lock (_lock) {
            var whenCompleted = _whenCompleted;
            if (mustAbortPreviousTasks) {
                _abortCts.CancelAndDisposeSilently();
                _abortCts = new();
            }
            var nextTask = CompleteAndProcess(whenCompleted, taskFactory, _abortCts.Token);
            _whenCompleted = nextTask;
            return nextTask;
        }
    }

    private static async Task<Result<Unit>> CompleteAndProcess(
        Task whenCompleted,
        Func<CancellationToken, Task>? taskFactory,
        CancellationToken cancellationToken)
    {
        await whenCompleted.ConfigureAwait(false);
        if (taskFactory == null)
            return default;

        try {
            await taskFactory.Invoke(cancellationToken).ConfigureAwait(false);
            return default;
        }
        catch (Exception e) {
            return Result.Error<Unit>(e);
        }
    }

    private static async Task<Result<T>> CompleteAndProcess<T>(
        Task whenCompleted,
        Func<CancellationToken, Task<T>>? taskFactory,
        CancellationToken cancellationToken)
    {
        await whenCompleted.ConfigureAwait(false);
        if (taskFactory == null)
            return default!;

        try {
            return await taskFactory.Invoke(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            return Result.Error<T>(e);
        }
    }
}
