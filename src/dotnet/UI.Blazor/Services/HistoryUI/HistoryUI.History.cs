namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    private HistoryItem _defaultItem;
    private readonly List<int> _history = new();
    private readonly LruCache<int, HistoryItem> _itemById = new(MaxItemCount);
    private int _position = -1;

    // Navigation

    private void NavigateBack()
        => Hub.JS.EvalVoid("window.history.back()");

    private void ReplaceNavigationHistoryEntry(int position)
        => Hub.Nav.NavigateTo(Hub.Nav.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = true,
                HistoryEntryState = Hub.PositionFormatter.Format(position),
            });

    private void AddNavigationHistoryEntry(int position)
        => Hub.Nav.NavigateTo(Hub.Nav.Uri,
            new NavigationOptions {
                ForceLoad = false,
                ReplaceHistoryEntry = false,
                HistoryEntryState = Hub.PositionFormatter.Format(position),
            });

    /*
    private void ReplaceNavigationHistoryEntry(int position)
    {
        var sPosition = Hub.PositionFormatter.Format(position);
        Hub.JS.EvalVoid($"window.history.replaceState('{sPosition}', '')");
    }

    private void AddNavigationHistoryEntry(int position)
    {
        var sPosition = Hub.PositionFormatter.Format(position);
        Hub.JS.EvalVoid($"window.history.pushState('{sPosition}', '')");
    }
    */

    // History manipulation

    private HistoryItem FindValidBackItem(HistoryItem item)
    {
        var currentItem = item;
        var seenIds = new HashSet<int>();
        while (true) {
            currentItem = GetItemById(currentItem.PrevId);
            if (currentItem == null)
                return DefaultItem();

            var id = currentItem.Id;
            if (seenIds.Contains(id)) {
                Log.LogError("Cycle in history item dependency graph, repeating item: {Item}", currentItem);
                return DefaultItem();
            }

            seenIds.Add(id);
            if (!OrdinalEquals(item.Uri, currentItem.Uri))
                return DefaultItem();

            if (item.CompareBackStepCount(currentItem) > 0)
                return currentItem;
        }

        HistoryItem DefaultItem()
            => GetDefaultItem(NextItemId(), item.Uri);
    }

    private void AddHistoryItem(HistoryItem item) => AddOrReplaceHistoryItem(item, false);
    private void ReplaceHistoryItem(HistoryItem item) => AddOrReplaceHistoryItem(item, true);
    private void AddOrReplaceHistoryItem(HistoryItem item, bool mustReplace)
    {
        if (mustReplace)
            _history[_position] = item.Id;
        else {
            ExpandHistory(++_position);
            _history[_position] = item.Id;
            TrimHistory(); // When item is added, future history is wiped out
        }
        _itemById[item.Id] = item;
 #pragma warning disable CA2254
        DebugLog?.LogDebug(
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            $"{(mustReplace ? "Replace" : "Add")}HistoryItem: [{{Items}}]",
            _history.ToDelimitedString(", "));
 #pragma warning restore CA2254
    }

    private void MovePosition(int position)
    {
        ExpandHistory(position);
        _position = position;
    }

    // Low-level helpers

    private void TrimHistory()
        => TrimHistory(_position);

    private void TrimHistory(int position)
    {
        var lastPosition = _history.Count - 1;
        if (lastPosition == 0)
            return; // Nothing to trim

        position.Clamp(0, lastPosition);
        if (position < lastPosition)
            _history.RemoveRange(position + 1, lastPosition - position);
    }

    private void ExpandHistory(int position)
    {
        var lastItem = _history.Count > 0 ? _history[^1] : default;
        while (position >= _history.Count)
            _history.Add(lastItem);
    }
}
