namespace ActualChat;

public static class WaitExt
{
    public static Task WaitAsync(this ManualResetEventSlim mres, CancellationToken cancellationToken = default)
        => mres.IsSet ? Task.CompletedTask : mres.WaitHandle.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);

    public static Task WaitAsync(this ManualResetEventSlim mres, TimeSpan timeout, CancellationToken cancellationToken = default)
        => mres.IsSet ? Task.CompletedTask : mres.WaitHandle.WaitAsync(timeout, cancellationToken);

    public static Task WaitAsync(this WaitHandle handle, CancellationToken cancellationToken = default)
        => WaitAsync(handle, Timeout.InfiniteTimeSpan, cancellationToken);

    public static Task WaitAsync(this WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.AttachedToParent |
            TaskCreationOptions.RunContinuationsAsynchronously);

        var registration = ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) => {
            var localTcs = (TaskCompletionSource<bool>)state!;
            if (timedOut) {
#pragma warning disable MA0040
                localTcs.TrySetCanceled();
            }
            else {
                localTcs.TrySetResult(true);
            }
        }, tcs, timeout, executeOnlyOnce: true);
        _ = tcs.Task.ContinueWith((_, state)
            => ((RegisteredWaitHandle)state!)!.Unregister(waitObject: null), registration, TaskScheduler.Default);
        return (cancellationToken == default) ? tcs.Task : tcs.Task.WaitAsync(cancellationToken);
    }

    // helps to not allocate a Task, if we got the default CancellationToken
    private static readonly Task TaskNever = new TaskCompletionSource<bool>().Task;

    public static Task WaitAsync(this CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled) {
            return TaskNever;
        }
        if (cancellationToken.IsCancellationRequested) {
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.AttachedToParent
            | TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationTokenRegistration = default;
        cancellationTokenRegistration = cancellationToken.Register(s => {
            ((TaskCompletionSource<bool>)s!).TrySetResult(true);
            cancellationTokenRegistration.Dispose();
        }, tcs, useSynchronizationContext: false);
        return tcs.Task;
    }
}
