using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public partial class ChatList : ComputedStateComponent<ChatList.Model>, IVirtualListDataSource<ChatListItemModel>, IDisposable
{
    private volatile VirtualListItemVisibility? _visibility;

    public async Task<VirtualListData<ChatListItemModel>> GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatListItemModel> renderedData,
        CancellationToken cancellationToken)
    {
        var chatId = ChatUI.SelectedChatId.Value;
        var placeId = await ChatUI.SelectedPlaceId.Use(cancellationToken).ConfigureAwait(false);
        var chatIndexTask = ChatListUI.IndexOf(placeId, chatId, cancellationToken);
        var chatCountTask = ChatListUI.GetCount(placeId, cancellationToken);
        var chatIndex = await chatIndexTask.ConfigureAwait(false);
        var chatCount = await chatCountTask.ConfigureAwait(false);
        DebugLog?.LogDebug(
            "GetData: {PlaceId}/{ChatId} (#{ChatIndex}/{ChatCount})",
            placeId, chatId, chatIndex, chatCount);
        if (chatCount == 0)
            return VirtualListData<ChatListItemModel>.None; // TODO(AY): This leaves the list in "loading" state, AK please fix this

        var firstItem = renderedData.FirstItem;
        var lastItem = renderedData.LastItem;
        var isFirstRender = firstItem == null && query.IsNone;
        var hasQuery = !query.IsNone;
        var visibleIndices = _visibility?.VisibleKeys.Select(int.Parse).ToList() ?? [];
        var minVisibleIndex = visibleIndices.DefaultIfEmpty(firstItem?.Position ?? 0).Min();
        var maxVisibleIndex = visibleIndices.DefaultIfEmpty(lastItem?.Position ?? 0).Max();
        var range = (hasQuery, isFirstRender) switch {
            // No query, no data -> initial load
            (false, true) => new Range<int>(
                chatIndex - ChatListUI.HalfLoadLimit,
                chatIndex + ChatListUI.HalfLoadLimit),
            // No query, but there is old data -> retaining visual position
            (false, false) => new Range<int>(minVisibleIndex - ChatListUI.TileSize, maxVisibleIndex + ChatListUI.TileSize),
            // Query is there, so data is irrelevant
            _ => query.KeyRange.ToIntRange().Move(query.MoveRange),
        };

        // Fit to existing chat count
        var indexTileLayer = ChatListUI.ChatTileStack.FirstLayer;
        range = range
            .IntersectWith(new Range<int>(0, chatCount))
            .ExpandToTiles(indexTileLayer);
        // Expand and fit again if too small
        if (range.Size() < ChatListUI.LoadLimit)
            range = range.Expand(ChatListUI.TileSize)
                .IntersectWith(new Range<int>(0, chatCount))
                .ExpandToTiles(indexTileLayer);
        var indexTiles = indexTileLayer.GetCoveringTiles(range);
        var resultItems = new List<ChatListItemModel>();
        foreach (var indexTile in indexTiles) {
            var tile = await ChatListUI.GetTile(placeId, indexTile, cancellationToken).ConfigureAwait(false);
            if (tile.Items.Count != 0)
                resultItems.AddRange(tile.Items);
        }

        var scrollToKey = null as string;
        if (isFirstRender) {
            // scroll to the selected chat on very first render
            var selectedItem = resultItems.FirstOrDefault(it => it.Chat.Id == chatId);
            if (selectedItem != null)
                scrollToKey = selectedItem.Key;
        }

        var hasVeryFirstItem = range.Start == 0;
        var hasVeryLastItem = range.End >= chatCount;

        // use single tile as multiple tiles don't provide benefits for randomly changed list
        var resultTile = new VirtualListTile<ChatListItemModel>("0", resultItems);
        var resultTiles = new List<VirtualListTile<ChatListItemModel>>();
        if (resultItems.Count > 0)
            resultTiles.Add(resultTile);

        // Console.WriteLine(Computed.Current.DebugDump());
        var firstItemPosition = resultItems.FirstOrDefault()?.Position ?? 0;
        var lastItemPosition = resultItems.LastOrDefault()?.Position ?? chatCount;
        var result = new VirtualListData<ChatListItemModel>(resultTiles) {
            Index = renderedData.Index + 1,
            BeforeCount = firstItemPosition,
            AfterCount = (chatCount - lastItemPosition - 1).Clamp(0, chatCount),
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            ScrollToKey = scrollToKey,
        };

        // Return the old data if the new one is identical (to prevent re-renders)
        return result.IsSimilarTo(renderedData)
            ? renderedData
            : result;
    }

    public void Dispose()
    { }

    // Private methods

    private void OnItemVisibilityChanged(VirtualListItemVisibility visibility)
        => _visibility = visibility;
}
