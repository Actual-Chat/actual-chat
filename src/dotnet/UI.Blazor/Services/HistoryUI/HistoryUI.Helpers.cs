namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    private int _nextItemId;

    private HistoryItem CurrentItemUnsafe
        => GetItemById(_history[_position]) ?? GetDefaultItem(_history[_position], Uri);

    private HistoryItem? BackItemUnsafe
        => _position <= 1 ? null : GetItemByPosition(_position - 1);

    private int NextItemId()
        => Interlocked.Increment(ref _nextItemId);

    private HistoryItem NewItem(string uri)
    {
        var currentItem = CurrentItemUnsafe;
        return new (NextItemId(), currentItem.Id, uri, currentItem.States);
    }

    private HistoryItem? GetItemByPosition(int position)
        => position < 0 || position >= _history.Count
            ? null
            : GetItemById(_history[position]);

    private HistoryItem? GetItemById(int id)
        => _itemById.GetValueOrDefault(id);

    private HistoryItem GetDefaultItem(int id, string uri)
        => _defaultItem with {
            Id = NextItemId(),
            Uri = uri,
        };
}
