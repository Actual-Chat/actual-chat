namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    public static readonly TimeSpan MaxNavigationDuration = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan DefaultWhenNavigationCompletedTimeout = TimeSpan.FromSeconds(5);

    public Task WhenNavigationCompleted(CancellationToken cancellationToken = default)
        => NavigationQueue.WhenAllEntriesCompleted(cancellationToken);

    public async Task WhenNavigationCompletedOrTimeout(TimeSpan timeout = default)
    {
        if (timeout == default)
            timeout = DefaultWhenNavigationCompletedTimeout;
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;
        try {
            await WhenNavigationCompleted(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested) {
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogDebug(e, $"{WhenNavigationCompletedOrTimeout} timed out");
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    [JSInvokable]
    public async Task NavigateTo(string url, bool mustReplace = false, bool force = false, bool addInFront = false)
    {
        Dispatcher.AssertAccess();
        await WhenNavigationCompletedOrTimeout().ConfigureAwait(true);

        var localUrl = new LocalUrl(url);
        var fixedUriLogLevel = LogLevel.Warning;
        if (localUrl.IsChat(out var chatId, out var query, out var hash)
            && !hash.IsNullOrEmpty()
            && query.IsNullOrEmpty()
            && NumberExt.TryParsePositiveLong(hash, out var entryId)) {
            // NOTE(DF): converts message links of old format to new format
            localUrl = Links.Chat(chatId, entryId);
            fixedUriLogLevel = LogLevel.Information;
        }
        var fixedUri = localUrl.Value;
        if (!OrdinalEquals(url, fixedUri)) {
            Log.Log(fixedUriLogLevel, "NavigateTo: {Uri} is fixed to {FixedUri}", url, fixedUri);
            url = fixedUri;
        }

        var title = $"NavigateTo: {(mustReplace ? "*>" : "->")} {url}{(force ? " + force" : "")}";
        var entry = NavigationQueue.Enqueue(addInFront, title, () => {
            if (!force && OrdinalEquals(url, _url)) {
                DebugLog?.LogDebug("{Entry} - skipped (same URI + no force option)", title);
                return null;
            }

            DebugLog?.LogDebug("{Entry}", title);
            var itemId = mustReplace ? _currentItem.Id : NewItemId();
            Nav.NavigateTo(url, new NavigationOptions() {
                ForceLoad = force,
                ReplaceHistoryEntry = mustReplace,
                HistoryEntryState = ItemIdFormatter.Format(itemId),
            });
            return itemId;
        });
        await entry.WhenCompleted.ConfigureAwait(false);
    }

    public async Task<bool> TryStepBack()
    {
        Dispatcher.AssertAccess();
        var currentItem = _currentItem;
        if (!currentItem.HasBackSteps)
            return false;

        var backItem = GetItemById(currentItem.BackItemId);
        if (backItem != null) {
            // Do back if there is History back step
            await NavigateBack().ConfigureAwait(false);
            return true;
        }

        // There is no back item but there are back steps, so let's generating back item
        if (backItem == null) {
            backItem = currentItem.GenerateBackItem();
            if (backItem == null)
                return false; // No way to step back: can't neither get nor generate the back step
            backItem = backItem with { Id = NewItemId() }; // Change Id to prevent endless loop
        }

        // Back item is either found or generated
        RegisterItem(backItem);
        RegisterCurrentItem(currentItem with { BackItemId = backItem.Id });
        Nav.NavigateTo(backItem.Url, new NavigationOptions() {
            ForceLoad = false,
            HistoryEntryState = ItemIdFormatter.Format(backItem.Id),
            ReplaceHistoryEntry = true,
        });
        return true;
    }

    public ValueTask ForceReload(string reason, string url, bool mustReplace = true)
    {
        Log.LogWarning("ForceReload ({Reason}): {Url} (mustReplace = {MustReplace})", reason, url, mustReplace);
        // return JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.History.forceReload", url, mustReplace, NewItemId());
        var method = mustReplace ? "replace" : "assign";
        return JS.InvokeVoidAsync($"window.location.{method}", url);
    }

    // Internal and private methods

    private Task NavigateBack(bool addInFront = false)
    {
        DebugLog?.LogDebug("NavigateBack: {AddInFront}", addInFront);
        return NavigationQueue.Enqueue(addInFront,
                "NavigateBack",
                () => {
                    _ = JS.InvokeVoidAsync("window.history.back");
                    return 0; // "Fits" any itemId
                })
            .WhenCompleted;
    }

    private Task AddHistoryEntry(HistoryItem item, bool addInFront = false)
        => NavigationQueue.Enqueue(addInFront,  $"AddHistoryEntry: {item}", () => {
            Nav.NavigateTo(item.Url,
                new NavigationOptions {
                    ForceLoad = false,
                    ReplaceHistoryEntry = false,
                    HistoryEntryState = ItemIdFormatter.Format(item.Id),
                });
            return item.Id;
        }).WhenCompleted;

    private Task ReplaceHistoryEntry(HistoryItem item, bool addInFront = false)
        => NavigationQueue.Enqueue(addInFront, $"ReplaceHistoryEntry: {item}", () => {
            Nav.NavigateTo(item.Url,
                new NavigationOptions {
                    ForceLoad = false,
                    ReplaceHistoryEntry = true,
                    HistoryEntryState = ItemIdFormatter.Format(item.Id),
                });
            return item.Id;
        }).WhenCompleted;

    /*
    private void AddNavigationHistoryEntry(long itemId)
    {
        var sItemId = Hub.ItemIdFormatter.Format(itemId);
        Hub.JS.EvalVoid($"window.history.pushState('{sItemId}', '')");
    }

    private void ReplaceNavigationHistoryEntry(long itemId)
    {
        var sItemId = Hub.ItemIdFormatter.Format(itemId);
        Hub.JS.EvalVoid($"window.history.replaceState('{sItemId}', '')");
    }
    */
}
