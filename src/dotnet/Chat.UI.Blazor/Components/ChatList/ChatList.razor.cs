using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatList : ComputedStateComponent<ChatList.Model>, IVirtualListDataSource<ChatListItemModel>, IDisposable
{
    public static readonly TileStack<int> ChatTileStack = Constants.Chat.ChatTileStack;
    public static readonly int LoadLimit = ChatTileStack.Layers[1].TileSize; // 20
    public static readonly int HalfLoadLimit = LoadLimit / 2;

    public async Task<VirtualListData<ChatListItemModel>> GetData(
        VirtualListDataQuery query,
        VirtualListData<ChatListItemModel> renderedData,
        CancellationToken cancellationToken)
    {
        var selectedChatId = ChatUI.SelectedChatId.Value;
        var selectedPlaceId = await ChatUI.SelectedPlaceId.Use(cancellationToken).ConfigureAwait(false);
        var selectedChatIndex = await ChatListUI.IndexOf(selectedPlaceId, selectedChatId, cancellationToken).ConfigureAwait(false);
        var chatCount = await ChatListUI.GetCount(selectedPlaceId, cancellationToken).ConfigureAwait(false);
        if (chatCount == 0)
            return VirtualListData<ChatListItemModel>.None;

        var firstItem = renderedData.FirstItem;
        var lastItem = renderedData.LastItem;
        var range = (!query.IsNone, firstItem != null) switch {
            // No query, no data -> initial load
            (false, false) => new Range<int>(0, LoadLimit),
            // No query, but there is old data + we're close to the end
            // KEEP THIS case, otherwise virtual list will grow indefinitely!
            (false, true) when Math.Abs(lastItem!.Position - chatCount) <= ChatTileStack.FirstLayer.TileSize
                => new Range<int>(
                    renderedData.GetNthItem(LoadLimit, true)?.Position ?? firstItem!.Position,
                    chatCount),
            // No query, but there is old data -> retaining it
            (false, true) => new Range<int>(firstItem!.Position, lastItem.Position),
            // Query is there, so data is irrelevant
            _ => query.KeyRange.ToIntRange().Move(query.MoveRange),
        };
        if (query.IsNone && renderedData.IsNone && !range.Contains(selectedChatIndex)) {
            // move range to the selected chat for the first render
            var scrollAnchorRange = new Range<int>(
                selectedChatIndex - HalfLoadLimit,
                selectedChatIndex + HalfLoadLimit);
            range = scrollAnchorRange.Overlaps(range)
                ? range.MinMaxWith(scrollAnchorRange)
                : scrollAnchorRange;
        }

        range = range
            .IntersectWith(new Range<int>(0, chatCount))
            .ExpandToTiles(ChatTileStack.FirstLayer);
        var indexTiles = ChatTileStack.FirstLayer.GetCoveringTiles(range);
        var resultItems = new List<ChatListItemModel>();
        foreach (var indexTile in indexTiles) {
            var tile = await ChatListUI.GetTile(selectedPlaceId, indexTile, cancellationToken).ConfigureAwait(false);
            if (tile.Items.Count <= 0)
                continue;

            resultItems.AddRange(tile.Items);
        }

        var scrollToKey = null as string;
        if (query.IsNone && renderedData.IsNone) {
            // scroll to the selected chat on first render
            var selectedItem = resultItems.FirstOrDefault(it => it.Chat.Id == selectedChatId);
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
        var result = new VirtualListData<ChatListItemModel>(resultTiles) {
            Index = renderedData.Index + 1,
            BeforeCount = range.Start,
            AfterCount = (chatCount - range.End).Clamp(0, chatCount),
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            ScrollToKey = scrollToKey,
        };

        // do not return new instance if data is the same to prevent re-renders
        return result.IsSimilarTo(renderedData)
            ? renderedData
            : result;
    }

    public void Dispose()
    { }
}
