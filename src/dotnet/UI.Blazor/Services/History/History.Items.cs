using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    private long _nextItemId;
    private volatile string _uri;
    private volatile HistoryItem _currentItem;
    private volatile HistoryItem _defaultItem;
    private readonly LruCache<long, HistoryItem> _itemById = new(MaxItemCount);

    private HistoryItem NewItemUnsafe(string? uri = null)
        => new(this, _currentItem.Id, uri ?? _uri, _currentItem.States) { Id = NewItemId() };

    private HistoryItem? GetItemByIdUnsafe(long id)
        => _itemById.GetValueOrDefault(id);

    private long NewItemId()
        => Interlocked.Increment(ref _nextItemId);

    // History manipulation

    private HistoryItem RegisterItem(HistoryItem item)
    {
        if (item.Id == 0)
            throw StandardError.Internal("RegisterItem: item.Id == 0");

        _itemById[item.Id] = item;
        return item;
    }

    private HistoryItem RegisterCurrentItem(HistoryItem item)
    {
        RegisterItem(item);
        _currentItem = item;
        return item;
    }

    private void AddItem(ref HistoryItem item)
    {
        if (item.BackItemId != _currentItem.Id)
            throw StandardError.Internal("AddHistoryItem: item.BackItemId != CurrentItem.Id");

        RegisterCurrentItem(item);
    }

    private void ReplaceItem(ref HistoryItem item)
    {
        if (item.Id != _currentItem.Id)
            throw StandardError.Internal("ReplaceHistoryItem: item.Id != CurrentItem.Id");

        RegisterCurrentItem(item);
    }

    private bool ReplaceItem(
        ref HistoryItem item,
        [NotNullWhen(true)] out HistoryItem? backItem)
    {
        if (item.Id == 0)
            throw StandardError.Internal("ReplaceHistoryItemWithGeneratedBackItem: item.Id == 0");

        backItem = GetItemByIdUnsafe(item.BackItemId);
        if (backItem != null) { // Already has back item
            ReplaceItem(ref item);
            return false;
        }

        backItem = item.GenerateBackItem();
        if (backItem == null) { // No back item implied
            ReplaceItem(ref item);
            return false;
        }

        // We can generate back item
        backItem = RegisterItem(backItem with {
            Id = item.Id, // We don't want to have an extra replaceState call, so we keep its Id
            BackItemId = 0,
        });
        item = RegisterCurrentItem(item with {
            Id = NewItemId(), // And for this one we'll do pushState
            BackItemId = backItem.Id,
            OnNavigation = new NavigationAction("Jump from generated back item", NavigationAction.NoAction),
        });
        return true;
    }
}
