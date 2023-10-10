namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    public HistoryChangeTracker TrackChanges(Action<HistoryItem> onChange)
        => new HistoryChangeTracker(this, onChange).Start();

    public async Task When(Func<HistoryItem, bool> predicate, CancellationToken cancellationToken = default)
    {
        var tcs = TaskCompletionSourceExt.New<Unit>();
        if (cancellationToken.IsCancellationRequested) {
            tcs.TrySetCanceled(cancellationToken);
            await tcs.Task.ConfigureAwait(false);
            return;
        }
        if (predicate.Invoke(CurrentItem)) {
            tcs.TrySetResult(default);
            await tcs.Task.ConfigureAwait(false);
            return;
        }

        var cts = cancellationToken.CreateLinkedTokenSource();
        var tracker = new HistoryChangeTracker(this,
            item => {
                if (predicate.Invoke(item)) {
                    tcs.TrySetResult(default);
                    cts.CancelAndDisposeSilently();
                }
            });
        var registration = cts.Token.Register(() => {
            tcs.TrySetCanceled();
            tracker.Dispose();
        });
        tracker.Start();
        try {
            await tcs.Task.ConfigureAwait(false);
        }
        finally {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
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
