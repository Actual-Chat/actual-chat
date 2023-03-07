namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    private readonly Action _processNextNavigationActionUnsafeCached;
    private readonly Queue<Action> _navigationQueue = new();
    private volatile Task<Unit> _whenNavigationCompleted;
    private CancellationTokenSource? _whenNavigationCompletedTimeoutCts;

    public Task WhenNavigationCompleted => _whenNavigationCompleted;

    [JSInvokable]
    public Task NavigateTo(string uri, bool mustAddHistoryItem = false)
    {
        lock (Lock) {
            var newUri = new LocalUrl(uri).Value;
            if (!mustAddHistoryItem && OrdinalEquals(newUri, _uri)) {
                DebugLog?.LogDebug("NavigateTo: skipped (same URI): {Uri}", uri);
                return Task.CompletedTask;
            }
            if (_locationChangeRegion.IsInside)
                throw StandardError.Internal("NavigateTo is invoked from LocationChange.");

            return EnqueueNavigation(() => {
                var eventArgs = new LocationChangedEventArgs(newUri, true);
                var newItem = NewItemUnsafe(newUri);
                LocationChange(eventArgs, newItem);
            });
        }
    }

    public ValueTask HardNavigateTo(LocalUrl url)
        => HardNavigateTo(url.ToAbsolute(UrlMapper));

    public async ValueTask HardNavigateTo(string url)
    {
        try {
            Log.LogInformation("HardNavigateTo: -> '{Url}'", url);
            await JS.EvalVoid($"window.location.assign({JsonFormatter.Format(url)})");
        }
        catch (Exception e) {
            Log.LogError(e, "HardNavigateTo failed");
            throw;
        }
    }

    // Internal and private methods

    private Task NavigateBack()
        => EnqueueNavigation(() => JS.EvalVoid("window.history.back()"));

    private Task ReplaceNavigationHistoryEntry(HistoryItem item)
        => EnqueueNavigation(() => Nav.NavigateTo(item.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = true,
                HistoryEntryState = ItemIdFormatter.Format(item.Id),
            }));

    private Task AddNavigationHistoryEntry(HistoryItem item)
        => EnqueueNavigation(() => Nav.NavigateTo(item.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = false,
                HistoryEntryState = ItemIdFormatter.Format(item.Id),
            }));

    /*
    private void ReplaceNavigationHistoryEntry(long itemId)
    {
        var sItemId = Hub.ItemIdFormatter.Format(itemId);
        Hub.JS.EvalVoid($"window.history.replaceState('{sItemId}', '')");
    }

    private void AddNavigationHistoryEntry(long itemId)
    {
        var sItemId = Hub.ItemIdFormatter.Format(itemId);
        Hub.JS.EvalVoid($"window.history.pushState('{sItemId}', '')");
    }
    */

    private Task EnqueueNavigation(Action action)
    {
        lock (Lock) {
            _navigationQueue.Enqueue(action);
            DebugLog?.LogDebug("EnqueueNavigation: {Count} action(s) in queue", _navigationQueue.Count);
            return ProcessNextNavigationUnsafe();
        }
    }

    private Task ProcessNextNavigationUnsafe()
    {
        if (_navigationQueue.Count != 0) {
            RestartWhenNavigationCompletedTimeoutUnsafe();
            var action = _navigationQueue.Dequeue();
            BackgroundTask.Run(
                () => Dispatcher.InvokeAsync(action),
                Log, "Queued navigation failed", CancellationToken.None);
        }
        else if (!_whenNavigationCompleted.IsCompleted) {
            DebugLog?.LogDebug("WhenNavigationCompleted: completed");
            _whenNavigationCompletedTimeoutCts.CancelAndDisposeSilently();
            _whenNavigationCompletedTimeoutCts = null;
            TaskSource.For(_whenNavigationCompleted).TrySetResult(default);
        }
        return _whenNavigationCompleted;
    }

    private void RestartWhenNavigationCompletedTimeoutUnsafe()
    {
        if (_whenNavigationCompleted.IsCompleted) {
            DebugLog?.LogDebug("WhenNavigationCompleted: renewed");
            _whenNavigationCompleted = TaskSource.New<Unit>(true).Task;
        }

        _whenNavigationCompletedTimeoutCts.CancelAndDisposeSilently();
        _whenNavigationCompletedTimeoutCts = new CancellationTokenSource();
        var cancellationToken = _whenNavigationCompletedTimeoutCts.Token;
        Clocks.CpuClock
            .Delay(MaxNavigationDuration, cancellationToken)
            .ContinueWith(task => {
                if (task.IsCanceled)
                    return;

                lock (Lock) {
                    Log.LogWarning("WhenNavigationCompleted: timed out");
                    _whenNavigationCompletedTimeoutCts.CancelAndDisposeSilently();
                    _whenNavigationCompletedTimeoutCts = null;
                    TaskSource.For(_whenNavigationCompleted).TrySetResult(default);
                }
            }, TaskScheduler.Default);
    }
}
