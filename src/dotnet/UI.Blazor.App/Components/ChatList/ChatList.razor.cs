using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public partial class ChatList : IVirtualListDataSource<ChatListItemModel>, IDisposable
{
    // Shared between the UI thread, which reports item visibility and sets the parameters, and the
    // compute path that runs GetData - hence the Volatile accesses to all four below.
    private VirtualListItemVisibility? _visibility;
    private IReadOnlyList<ChatListItemModel> _items = [];
    private ChatId? _topVisibleChatId;
    private ChatId? _restoreChatId;

    public void Dispose()
        // The search overlay replaces this list rather than hiding it, so the scroll position goes away
        // with the component - ChatListUI holds the chat it was on until the list is re-created.
        => ChatListUI.SetScrollAnchor(_lastKey ?? "", Volatile.Read(ref _topVisibleChatId));

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
        var visibility = Volatile.Read(ref _visibility);
        var visibleIndices = visibility?.VisibleKeys.Select(int.Parse).ToList() ?? [];
        var isFirstRender = (firstItem is null || visibleIndices.Count == 0) && query.IsNone;
        var hasQuery = !query.IsNone;
        var minVisibleIndex = visibleIndices.DefaultIfEmpty(firstItem?.Position ?? 0).Min();
        var maxVisibleIndex = visibleIndices.DefaultIfEmpty(lastItem?.Position ?? 0).Max();
        if (!isFirstRender)
            Volatile.Write(ref _restoreChatId, null); // The list has a position of its own again

        var restoreChatId = Volatile.Read(ref _restoreChatId);
        var restoreIndexTask = restoreChatId is null
            ? Task.FromResult(-1)
            : ChatListUI.IndexOf(placeId, restoreChatId, chatListSettings, cancellationToken);
        var restoreIndex = await restoreIndexTask.ConfigureAwait(false);
        // A restored list loads around the chat it's going back to rather than around the selected one,
        // and around index 0 when that chat is gone - so it lands on the top, not somewhere arbitrary.
        var initialIndex = restoreChatId is null ? chatIndex : Math.Max(restoreIndex, 0);
        var range = (hasQuery, isFirstRender) switch {
            // No query, no data -> initial load
            (false, true) => new Range<int>(
                initialIndex - ChatListUI.LoadLimit,
                initialIndex + ChatListUI.LoadLimit),
            // No query, but there is old data -> retaining visual position
            (false, false) => new Range<int>(
                minVisibleIndex - (ChatListUI.TileSize * 2),
                maxVisibleIndex + (ChatListUI.TileSize * 2)),
            // Query is there, so data is irrelevant
            _ => query.KeyRange.ToIntRange().Move(query.MoveRange),
        };

        // Fit to existing chat count
        range = range
            .IntersectWith(new Range<int>(0, chatCount))
            .ExpandToTiles(ChatListUI.ChatTiles);
        // Expand and fit again if too small
        if (range.Size() < ChatListUI.LoadLimit)
            range = range.Expand(ChatListUI.TileSize)
                .IntersectWith(new Range<int>(0, chatCount))
                .ExpandToTiles(ChatListUI.ChatTiles);
        var indexTiles = ChatListUI.ChatTiles.GetCoveringTiles(range);
        var resultItems = new List<ChatListItemModel>();
        foreach (var indexTile in indexTiles) {
            var tile = await ChatListUI
                .GetTile(placeId, indexTile, chatListSettings, cancellationToken)
                .ConfigureAwait(false);
            if (tile.Items.Count != 0)
                resultItems.AddRange(tile.Items);
        }

        var scrollToKey = null as string;
        var mustScrollToKeyInTheMiddle = false;
        if (isFirstRender) {
            if (restoreChatId is not null) {
                // Landing on the chat rather than on the pixel offset it had: the list is a fresh set
                // of elements, and the chat is what the user was actually looking at.
                scrollToKey = resultItems.FirstOrDefault(it => it.Chat.Id == restoreChatId)?.Key;
            }
            else {
                // scroll to the selected chat on very first render
                var selectedItem = resultItems.FirstOrDefault(it => it.Chat.Id == chatId);
                if (selectedItem != null) {
                    scrollToKey = selectedItem.Key;
                    mustScrollToKeyInTheMiddle = true;
                }
            }
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
            ScrollToKeyInTheMiddle = mustScrollToKeyInTheMiddle ? true : null,
        };

        // Return the old data if the new one is identical (to prevent re-renders)
        var data = result.IsSimilarTo(renderedData) ? renderedData : result;
        Volatile.Write(ref _items, data.Items);
        return data;
    }

    // Private methods

    private void OnItemVisibilityChanged(VirtualListItemVisibility visibility)
    {
        // GetData anchors the next load window on what the user can actually see - reusing the rendered
        // range instead would grow it on every recompute.
        Volatile.Write(ref _visibility, visibility);
        // The retraction a disposed list reports must not erase what Dispose is about to hand over.
        if (visibility.IsEmpty)
            return;

        var topPosition = visibility.VisibleKeys.Select(int.Parse).Min();
        var topItem = Volatile.Read(ref _items).FirstOrDefault(x => x.Position == topPosition);
        if (topItem != null)
            Volatile.Write(ref _topVisibleChatId, topItem.Chat.Id);
    }
}
