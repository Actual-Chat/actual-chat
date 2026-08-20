using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public partial class ChatList : IVirtualListDataSource<ChatListItemModel>
{
    private volatile VirtualListItemVisibility? _visibility;

    public async Task<VirtualListData<ChatListItemModel>> GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatListItemModel> renderedData,
        CancellationToken cancellationToken)
    {
        var placeId = PlaceId;
        var usePlaceChatListSettings = UsePlaceChatListSettings;

        ChatListSettings chatListSettings;
        Task<int> chatIndexTask;
        ChatId? chatId;
        if (usePlaceChatListSettings) {
            var placeChatListSettings = ChatListUI.GetPlaceChatListSettings(placeId);
            chatListSettings = await placeChatListSettings.Get(cancellationToken).ConfigureAwait(false);
            chatId = ChatUI.SelectedChatId.Value;
            chatIndexTask = chatId is not null
                ? ChatListUI.IndexOf(placeId, chatId, chatListSettings, cancellationToken)
                : Task.FromResult(-1);
        }
        else {
            chatListSettings = Settings
                ?? new ChatListSettings { Order = ChatListOrder.ByAlphabet, FilterId = ChatListFilter.Groups.Id };
            chatId = null;
            chatIndexTask = Task.FromResult(-1);
        }

        var chatCountTask = ChatListUI.GetCount(placeId, chatListSettings, cancellationToken);
        var separatorIndexesTask = ChatListUI.GetSeparatorIndexes(placeId, chatListSettings, cancellationToken);
        var chatIndex = await chatIndexTask.ConfigureAwait(false);
        var chatCount = await chatCountTask.ConfigureAwait(false);

        DebugLog?.LogDebug(
            "GetData: {PlaceId}/{UsePlaceChatListSettings}/{ChatId} (#{ChatIndex}/{ChatCount})",
            placeId, usePlaceChatListSettings, chatId, chatIndex, chatCount);

        var firstItem = renderedData.FirstItem;
        var lastItem = renderedData.LastItem;
        var visibleIndices = _visibility?.VisibleKeys.Select(int.Parse).ToList() ?? [];
        var isFirstRender = (firstItem is null || visibleIndices.Count == 0) && query.IsNone;
        var hasQuery = !query.IsNone;
        var minVisibleIndex = visibleIndices.DefaultIfEmpty(firstItem?.Position ?? 0).Min();
        var maxVisibleIndex = visibleIndices.DefaultIfEmpty(lastItem?.Position ?? 0).Max();
        var range = (hasQuery, isFirstRender) switch {
            // No query, no data -> initial load
            (false, true) => new Range<int>(
                chatIndex - ChatListUI.LoadLimit,
                chatIndex + ChatListUI.LoadLimit),
            // No query, but there is old data -> retaining visual position
            (false, false) => new Range<int>(minVisibleIndex - (ChatListUI.TileSize * 2), maxVisibleIndex + (ChatListUI.TileSize * 2)),
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
            var tile = await ChatListUI.GetTile(placeId, indexTile, chatListSettings, cancellationToken).ConfigureAwait(false);
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

        // Console.WriteLine(Computed.Current.DebugDump());
        var firstItemPosition = resultItems.FirstOrDefault()?.Position ?? 0;
        var lastItemPosition = resultItems.LastOrDefault()?.Position ?? chatCount;
        var result = new VirtualListData<ChatListItemModel>(resultItems) {
            Index = renderedData.Index + 1,
            BeforeCount = firstItemPosition,
            AfterCount = (chatCount - lastItemPosition - 1).Clamp(0, chatCount),
            SeparatorIndexes = await separatorIndexesTask.ConfigureAwait(false),
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            ScrollToKey = scrollToKey,
            ScrollToKeyInTheMiddle = scrollToKey is not null ? true : null,
        };

        // Return the old data if the new one is identical (to prevent re-renders)
        return result.IsSimilarTo(renderedData)
            ? renderedData
            : result;
    }

    // Private methods

    private void OnItemVisibilityChanged(VirtualListItemVisibility visibility)
        // The only consumer is GetData below, which anchors the next load window on what the user
        // can actually see - reusing the rendered range instead would grow it on every recompute.
        => _visibility = visibility;
}
