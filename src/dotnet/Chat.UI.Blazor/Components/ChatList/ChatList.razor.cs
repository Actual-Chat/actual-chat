using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatList : ComputedStateComponent<ChatList.Model>, IVirtualListDataSource<ChatListItemModel>, IDisposable
{
    public static readonly TileStack<int> ChatTileStack = Constants.Chat.ChatTileStack;
    public static readonly int LoadLimit = ChatTileStack.Layers[1].TileSize; // 20
    public static readonly int HalfLoadLimit = LoadLimit / 2;

    public async Task<VirtualListData<ChatListItemModel>> GetData(
        ComputedState<VirtualListData<ChatListItemModel>> state,
        VirtualListDataQuery query,
        VirtualListData<ChatListItemModel> renderedData,
        CancellationToken cancellationToken)
    {
        var selectedChatId = ChatUI.SelectedChatId.Value;
        var selectedChatIndex = ChatListUI.IndexOf(selectedChatId);
        var chatCount = await ChatListUI.GetCount(Kind).ConfigureAwait(false);
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
        var tiles = new List<VirtualListTile<ChatListItemModel>>();
        foreach (var indexTile in indexTiles) {
            var tile = await ChatListUI.GetTile(indexTile, cancellationToken).ConfigureAwait(false);
            if (tile.Items.Count > 0)
                tiles.Add(tile);
        }

        var scrollToKey = null as string;
        if (query.IsNone && renderedData.IsNone) {
            // scroll to the selected chat on first render
            var selectedItem = tiles
                .SelectMany(t => t.Items)
                .FirstOrDefault(it => it.ChatInfo.Chat.Id == selectedChatId);
            if (selectedItem != null)
                scrollToKey = selectedItem.Key;
        }

        var hasVeryFirstItem = range.Start == 0;
        var hasVeryLastItem = range.End >= chatCount;

        return new VirtualListData<ChatListItemModel>(tiles) {
            Index = renderedData.Index + 1,
            BeforeCount = range.Start,
            AfterCount = (chatCount - range.End).Clamp(0, chatCount),
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            ScrollToKey = scrollToKey,
        };
    }

    public void Dispose()
    { }
}
