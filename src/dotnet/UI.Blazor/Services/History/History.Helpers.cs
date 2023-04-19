namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    public HistoryChangeTracker TrackChanges(Action<HistoryItem> onChange)
        => new HistoryChangeTracker(this, onChange).Start();

    public Task When(Func<HistoryItem, bool> predicate, CancellationToken cancellationToken = default)
    {
        var tcs = TaskCompletionSourceExt.New<Unit>();
        if (cancellationToken.IsCancellationRequested) {
            tcs.TrySetCanceled(cancellationToken);
            return tcs.Task;
        }
        if (predicate.Invoke(CurrentItem)) {
            tcs.TrySetResult(default);
            return tcs.Task;
        }

        var cts = cancellationToken.CreateLinkedTokenSource();
        var tracker = new HistoryChangeTracker(this,
            item => {
                if (predicate.Invoke(item)) {
                    tcs.TrySetResult(default);
                    cts.CancelAndDisposeSilently();
                }
            });
        cts.Token.Register(() => {
            tcs.TrySetCanceled();
            tracker.Dispose();
        });
        tracker.Start();
        return tcs.Task;
    }

    public void CancelWhen(CancellationTokenSource cancellationTokenSource, Func<HistoryItem, bool> predicate)
    {
        if (predicate.Invoke(CurrentItem)) {
            cancellationTokenSource.Cancel();
            return;
        }

        var tracker = new HistoryChangeTracker(this,
            item => {
                if (predicate.Invoke(item) && !cancellationTokenSource.IsCancellationRequested) {
                    try {
                        cancellationTokenSource.Cancel();
                    }
                    catch {
                        // Intended
                    }
                }
            });
        cancellationTokenSource.Token.Register(() => tracker.Dispose());
        tracker.Start();
    }
}
