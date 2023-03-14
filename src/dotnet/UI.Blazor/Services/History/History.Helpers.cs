namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    public HistoryChangeTracker TrackChanges(Action<HistoryItem> onChange)
        => new HistoryChangeTracker(this, onChange).Start();

    public Task When(Func<HistoryItem, bool> predicate, CancellationToken cancellationToken = default)
    {
        var ts = TaskSource.New<Unit>(true);
        if (cancellationToken.IsCancellationRequested) {
            ts.TrySetCanceled(cancellationToken);
            return ts.Task;
        }
        if (predicate.Invoke(CurrentItem)) {
            ts.TrySetResult(default);
            return ts.Task;
        }

        var cts = cancellationToken.CreateLinkedTokenSource();
        var tracker = new HistoryChangeTracker(this,
            item => {
                if (predicate.Invoke(item)) {
                    ts.TrySetResult(default);
                    cts.CancelAndDisposeSilently();
                }
            });
        cts.Token.Register(() => {
            ts.TrySetCanceled();
            tracker.Dispose();
        });
        tracker.Start();
        return ts.Task;
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
