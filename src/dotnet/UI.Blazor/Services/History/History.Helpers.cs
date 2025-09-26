namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    public HistoryChangeTracker TrackChanges(Action<HistoryItem> onChange)
        => new HistoryChangeTracker(this, onChange).Start();

    public Task When(Func<HistoryItem, bool> predicate, CancellationToken cancellationToken = default)
    {
        var tcs = AsyncTaskMethodBuilderExt.New();
        if (cancellationToken.IsCancellationRequested) {
            tcs.TrySetCanceled(cancellationToken);
            return tcs.Task;
        }
        if (predicate.Invoke(CurrentItem)) {
            tcs.TrySetResult();
            return tcs.Task;
        }

        var tracker = TrackChanges(item => {
            if (predicate.Invoke(item))
                tcs.TrySetResult();
        });
        var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _ = tcs.Task.ContinueWith(_ => {
            tracker.Dispose();
            registration.Dispose();
        }, TaskScheduler.Default);
        return tcs.Task;
    }
}
