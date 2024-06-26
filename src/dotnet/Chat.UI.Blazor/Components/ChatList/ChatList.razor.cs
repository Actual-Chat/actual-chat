using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public partial class ChatList : ComputedStateComponent<ChatList.Model>, IVirtualListDataSource<ChatListItemModel>, IDisposable
{
    public static readonly TileStack<int> ChatTileStack = Constants.Chat.ChatTileStack;

    public async Task<VirtualListData<ChatListItemModel>> GetData(
        ComputedState<VirtualListData<ChatListItemModel>> state,
        VirtualListDataQuery query,
        VirtualListData<ChatListItemModel> renderedData,
        CancellationToken cancellationToken)
    {
        var chatCount = await ChatListUI.GetCount(Kind).ConfigureAwait(false);
        var range = query.IsNone
            ? new Range<int>(0, 20)
            : query.KeyRange.ToIntRange().Expand(new Range<int>(query.ExpandStartBy, query.ExpandEndBy));
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
