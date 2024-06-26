using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatList : ComputedStateComponent<ChatList.Model>, IVirtualListDataSource<ChatListItemModel>, IDisposable
{
    public static readonly TileStack<int> ChatTileStack = Constants.Chat.ChatTileStack;
    public static readonly int LoadLimit = ChatTileStack.Layers[1].TileSize; // 20

    public async Task<VirtualListData<ChatListItemModel>> GetData(
        ComputedState<VirtualListData<ChatListItemModel>> state,
        VirtualListDataQuery query,
        VirtualListData<ChatListItemModel> renderedData,
        CancellationToken cancellationToken)
    {
        var chatCount = await ChatListUI.GetCount(Kind).ConfigureAwait(false);
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
            _ => query.KeyRange.ToIntRange().Expand(new Range<int>(query.ExpandStartBy, query.ExpandEndBy)),
        };
        range = range
            .IntersectWith(new Range<int>(0, chatCount))
            .ExpandToTiles(ChatTileStack.FirstLayer);
        var indexTiles = ChatTileStack.FirstLayer.GetCoveringTiles(range);
        var tiles = new List<VirtualListTile<ChatListItemModel>>();
        foreach (var indexTile in indexTiles) {
            var tile = await ChatListUI.GetTile(indexTile, cancellationToken).ConfigureAwait(false);
            tiles.Add(tile);
        }

        var hasVeryFirstItem = range.Start == 0;
        var hasVeryLastItem = range.End >= chatCount;

        return new VirtualListData<ChatListItemModel>(tiles) {
            Index = renderedData.Index + 1,
            HasVeryFirstItem = hasVeryFirstItem,
            HasVeryLastItem = hasVeryLastItem,
            RequestedStartExpansion = query.IsNone
                ? null
                : query.ExpandStartBy,
            RequestedEndExpansion = query.IsNone
                ? null
                : query.ExpandEndBy,
        };
    }

    public void Dispose()
    { }
}
