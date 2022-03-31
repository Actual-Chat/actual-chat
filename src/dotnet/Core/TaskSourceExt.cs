namespace ActualChat;

public static class TaskSourceExt
{
    public static void TrySetFromTaskWhenCompleted<T>(this TaskSource<T> target, Task<T> source, CancellationToken cancellationToken = default)
        => _ = source.ContinueWith(s => {
            target.TrySetFromTask(s, cancellationToken);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public static void TrySetFromTaskWhenCompleted(this TaskSource<Unit> target, Task source, CancellationToken cancellationToken = default)
        => _ = source.ContinueWith(_ => {
            if (source.IsCanceled)
                target.SetCanceled(cancellationToken.IsCancellationRequested ? cancellationToken : CancellationToken.None);
            else if (source.Exception != null)
                target.SetException(source.Exception);
            else
                target.SetResult(default);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
