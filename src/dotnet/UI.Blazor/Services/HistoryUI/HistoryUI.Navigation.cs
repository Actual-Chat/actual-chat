namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    public void NavigateTo(string uri)
    {
        lock (Lock) {
            var newUri = new LocalUrl(uri).Value;
            if (OrdinalEquals(newUri, _uri)) {
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

    // Private methods

    private void NavigateBack()
        => Hub.JS.EvalVoid("window.history.back()");

    private void ReplaceNavigationHistoryEntry(HistoryItem item)
        => Hub.Nav.NavigateTo(item.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = true,
                HistoryEntryState = Hub.ItemIdFormatter.Format(item.Id),
            });

    private void AddNavigationHistoryEntry(HistoryItem item)
        => Hub.Nav.NavigateTo(item.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = false,
                HistoryEntryState = Hub.ItemIdFormatter.Format(item.Id),
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
