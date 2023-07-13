using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    public Task WhenNavigationCompleted(CancellationToken cancellationToken = default)
        => NavigationQueue.WhenAllEntriesCompleted(cancellationToken);

    [JSInvokable]
    public Task NavigateTo(string uri, bool mustReplace = false, bool force = false, bool addInFront = false)
    {
        var fixedUri = new LocalUrl(uri).Value;
        if (!OrdinalEquals(uri, fixedUri)) {
            Log.LogWarning("NavigateTo: {Uri} is fixed to {FixedUri}", uri, fixedUri);
            uri = fixedUri;
        }

        var title = $"NavigateTo: {(mustReplace ? "*>" : "->")} {uri}{(force ? " + force" : "")}";
        var entry = NavigationQueue.Enqueue(addInFront, title, () => {
            if (!force && OrdinalEquals(uri, _uri)) {
                DebugLog?.LogDebug("{Entry} - skipped (same URI + no force option)", title);
                return null;
            }

            var itemId = mustReplace ? _currentItem.Id : NewItemId();
            Nav.NavigateTo(uri, new NavigationOptions() {
                ForceLoad = force,
                ReplaceHistoryEntry = mustReplace,
                HistoryEntryState = ItemIdFormatter.Format(itemId),
            });
            return itemId;
        });
        return entry.WhenCompleted;
    }

    public void ForceReload(string url, bool mustReplace = true)
    {
        Log.LogInformation("ForceReload: {Url} (mustReplace = {MustReplace})", url, mustReplace);
        _ = JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.History.forceReload", url, mustReplace, NewItemId());
    }

    // Internal and private methods

    private Task NavigateBack(bool addInFront = false)
        => NavigationQueue.Enqueue(addInFront, "NavigateBack", () => {
            _ = JS.EvalVoid("window.history.back()");
            return 0; // "Fits" any itemId
        }).WhenCompleted;

    private Task AddHistoryEntry(HistoryItem item, bool addInFront = false)
        => NavigationQueue.Enqueue(addInFront,  $"AddHistoryEntry: {item}", () => {
            Nav.NavigateTo(item.Uri,
                new NavigationOptions {
                    ForceLoad = false,
                    ReplaceHistoryEntry = false,
                    HistoryEntryState = ItemIdFormatter.Format(item.Id),
                });
            return item.Id;
        }).WhenCompleted;

    private Task ReplaceHistoryEntry(HistoryItem item, bool addInFront = false)
        => NavigationQueue.Enqueue(addInFront, $"ReplaceHistoryEntry: {item}", () => {
            Nav.NavigateTo(item.Uri,
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
