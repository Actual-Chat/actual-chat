namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    [JSInvokable]
    public void NavigateTo(string uri, bool mustAddHistoryItem = false)
    {
        lock (Lock) {
            var newUri = new LocalUrl(uri).Value;
            if (!mustAddHistoryItem && OrdinalEquals(newUri, _uri)) {
                DebugLog?.LogDebug("Navigate: skipped (same URI): {Uri}", uri);
                return;
            }
            if (_locationChangeRegion.IsInside)
                throw StandardError.Internal("Navigate is invoked from LocationChange.");

            LocationChange(NewItemUnsafe(newUri));
        }
    }

    public void NavigateTo(string uri, Action stateMutator)
    {
        lock (Lock) {
            var newUri = new LocalUrl(uri).Value;
            if (_locationChangeRegion.IsInside)
                throw StandardError.Internal("Navigate is invoked from LocationChange.");

            LocationChange(NewItemUnsafe(newUri), stateMutator);
        }
    }

    public ValueTask HardNavigateTo(LocalUrl url)
        => HardNavigateTo(url.ToAbsolute(UrlMapper));

    public async ValueTask HardNavigateTo(string url)
    {
        try {
            Log.LogInformation("HardRedirect: -> '{Url}'", url);
            await JS.EvalVoid($"window.location.assign({JsonFormatter.Format(url)})");
        }
        catch (Exception e) {
            Log.LogError(e, "HardRedirect failed");
            throw;
        }
    }

    // Private methods

    private void NavigateBack()
        => JS.EvalVoid("window.history.back()");

    private void ReplaceNavigationHistoryEntry(HistoryItem item)
        => Nav.NavigateTo(item.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = true,
                HistoryEntryState = ItemIdFormatter.Format(item.Id),
            });

    private void AddNavigationHistoryEntry(HistoryItem item)
        => Nav.NavigateTo(item.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = false,
                HistoryEntryState = ItemIdFormatter.Format(item.Id),
            });

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
}
