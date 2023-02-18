namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    public HistoryChangeTracker TrackChanges(Action<HistoryItem> onChange)
        => new HistoryChangeTracker(this, onChange).Start();

    public CancellationTokenSource TrackChangesAndCancel(Func<HistoryItem, bool> whenPredicate)
    {
        var cts = new CancellationTokenSource();
        var tracker = new HistoryChangeTracker(this,
            item => {
                if (whenPredicate.Invoke(item) && !cts.IsCancellationRequested) {
                    try {
                        cts.Cancel();
                    }
                    catch {
                        // Intended
                    }
                }
            });
        cts.Token.Register(() => tracker.Dispose());
        tracker.Start();
        return cts;
    }
}
