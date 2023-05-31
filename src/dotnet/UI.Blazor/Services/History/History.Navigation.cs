using Cysharp.Text;

namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    private readonly Action _processNextNavigationActionUnsafeCached;
    private readonly Queue<Action> _navigationQueue = new();
    private volatile TaskCompletionSource<Unit> _whenNavigationCompletedSource;
    private CancellationTokenSource? _whenNavigationCompletedTimeoutCts;

    // ReSharper disable once InconsistentlySynchronizedField
    public Task WhenNavigationCompleted => _whenNavigationCompletedSource.Task;

    [JSInvokable]
    public Task NavigateTo(string uri, bool mustReplace = false, bool force = false)
    {
        var fixedUri = new LocalUrl(uri).Value;
        if (!OrdinalEquals(uri, fixedUri)) {
            Log.LogWarning("NavigateTo: {Uri} is fixed to {FixedUri}", uri, fixedUri);
            uri = fixedUri;
        }
        lock (Lock) {
            if (_locationChangeRegion.IsInside)
                throw StandardError.Internal("NavigateTo is invoked from LocationChange.");

            return EnqueueNavigation(() => {
                if (!force && OrdinalEquals(uri, _uri)) {
                    DebugLog?.LogDebug("NavigateTo: {Uri} - skipped (same URI + no force option)", uri);
                    lock (Lock)
                        ProcessNextNavigationUnsafe();
                    return;
                }

                if (DebugLog != null) {
                    using var sb = ZString.CreateStringBuilder(true);
                    sb.Append("NavigateTo: {Uri}, ");
                    if (mustReplace || force) {
                        sb.Append("options: ");
                        if (mustReplace)
                            sb.Append("mustReplace, ");
                        if (force)
                            sb.Append("force, ");
                    }
                    sb.Remove(sb.Length - 2, 2); // Removing trailing ", "
                    var message = sb.ToString();
                    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                    DebugLog?.LogDebug(message, uri);
                }
                Nav.NavigateTo(uri, new NavigationOptions() {
                    ForceLoad = false,
                    ReplaceHistoryEntry = mustReplace,
                    HistoryEntryState = mustReplace ? ItemIdFormatter.Format(_currentItem.Id) : null,
                });
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

    public async Task<bool> WhenNavigatedTo(LocalUrl localUrl, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(0.5));
        await When(_ => LocalUrl.Value.OrdinalStartsWith(localUrl), cts.Token)
            .SuppressCancellationAwait();
        await WhenNavigationCompleted;
        return LocalUrl.Value.OrdinalStartsWith(localUrl);
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
        else if (!_whenNavigationCompletedSource.Task.IsCompleted) {
            DebugLog?.LogDebug("WhenNavigationCompleted: completed");
            _whenNavigationCompletedTimeoutCts.CancelAndDisposeSilently();
            _whenNavigationCompletedTimeoutCts = null;
            _whenNavigationCompletedSource.TrySetResult(default);
        }
        return _whenNavigationCompletedSource.Task;
    }

    private void RestartWhenNavigationCompletedTimeoutUnsafe()
    {
        if (_whenNavigationCompletedSource.Task.IsCompleted) {
            DebugLog?.LogDebug("WhenNavigationCompleted: renewed");
            _whenNavigationCompletedSource = TaskCompletionSourceExt.New<Unit>();
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
                    _whenNavigationCompletedSource.TrySetResult(default);
                }
            }, TaskScheduler.Default);
    }
}
