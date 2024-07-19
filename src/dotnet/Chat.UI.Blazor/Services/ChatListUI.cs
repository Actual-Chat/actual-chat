using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatListUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    public static readonly int ActiveItemCountWhenLoading = 0;
    public static readonly int AllItemCountWhenLoading = 14;
    private static readonly TimeSpan MinNotificationInterval = TimeSpan.FromSeconds(5);

    private readonly MutableState<bool> _isSelectedChatUnlisted;
    private readonly ConcurrentDictionary<PlaceId, ChatListView> _chatListViews = new ();

    private bool _isFirstLoad = true;
    private ComputedState<Trimmed<int>>? _unreadChatCount;

    private IContacts Contacts => Hub.Contacts;
    private IAuthors Authors => Hub.Authors;
    private IPlaces Places => Hub.Places;
    private AccountUI AccountUI => Hub.AccountUI;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private SearchUI SearchUI => Hub.SearchUI;
    private TuneUI TuneUI => Hub.TuneUI;
    private LoadingUI LoadingUI => Hub.LoadingUI;
    private UICommander UICommander => Hub.UICommander();
    private new ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

#pragma warning disable CA1721 // Confusing w/ GetUnreadChatCount
    public IState<Trimmed<int>> UnreadChatCount => _unreadChatCount!;
#pragma warning restore CA1721

    private Moment CpuNow => Clocks.CpuClock.Now;

    // public IState<ChatListView?> ActiveChatListView => _activeChatListView;

    public ChatListUI(ChatUIHub hub) : base(hub)
    {
        var type = GetType();
        _isSelectedChatUnlisted = StateFactory.NewMutable(false,
            StateCategories.Get(type, nameof(_isSelectedChatUnlisted)));
        // _activeChatListView = StateFactory.NewMutable((ChatListView?)null,
        //     StateCategories.Get(type, nameof(ActiveChatListView)));
    }

    void INotifyInitialized.Initialized()
    {
        _unreadChatCount = StateFactory.NewComputed(
            new ComputedState<Trimmed<int>>.Options() {
                UpdateDelayer = FixedDelayer.NextTick,
                TryComputeSynchronously = false,
                Category = StateCategories.Get(GetType(), nameof(UnreadChatCount)),
            },
            ComputeUnreadChatCount);
        Hub.RegisterDisposable(_unreadChatCount);
        this.Start();
    }

    public ChatListView GetChatListView()
        => GetChatListView(ChatUI.SelectedPlaceId.Value);

    public ChatListView GetChatListView(PlaceId placeId)
        => _chatListViews.GetOrAdd(placeId, pid => new ChatListView(placeId, CreateSettingsState(placeId)));

#pragma warning disable CA1822 // Can be static
    public int GetCountWhenLoading(ChatListKind listKind)
#pragma warning restore CA1822
        => listKind switch {
            ChatListKind.All => AllItemCountWhenLoading,
            ChatListKind.Active => ActiveItemCountWhenLoading,
            _ => throw new ArgumentOutOfRangeException(nameof(listKind), listKind, null)
        };

    [ComputeMethod]
    public virtual async Task<int> GetCount(PlaceId placeId, CancellationToken cancellationToken)
    {
        var listView = GetChatListView(placeId);
        var settings = await listView.GetSettings(cancellationToken).ConfigureAwait(false);
        var chatById = await ListAllUnordered(placeId, settings.Filter, cancellationToken).ConfigureAwait(false);
        return chatById.Count;
    }

    [ComputeMethod]
    public virtual async Task<int> IndexOf(PlaceId placeId, ChatId chatId, CancellationToken cancellationToken)
    {
        var listView = GetChatListView(placeId);
        var settings = await listView.GetSettings(cancellationToken).ConfigureAwait(false);
        var items = await ListAll(placeId, settings, cancellationToken).ConfigureAwait(false);
        var index = -1;
        for (int i = 0; i < items.Count; i++) {
            var item = items[i];
            if (item.Id != chatId)
                continue;

            index = i;
            break;
        }
        return index;
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnreadChatCount(PlaceId placeId, ChatListFilter filter, CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        return chatById.Select(c => c.Value).UnreadChatCount();
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnmutedUnreadChatCount(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var filter = placeId == PlaceId.None ? ChatListFilter.None : ChatListFilter.Groups;
        var chatById = await ListAllUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        return chatById.Select(c => c.Value).UnmutedUnreadChatCount();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> ListActive(CancellationToken cancellationToken = default)
    {
        await ActiveChatsUI.WhenLoaded.ConfigureAwait(true); // No need for .ConfigureAwait(false) here

        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        var chats = (await activeChats
            .Select(c => ChatUI.Get(c.ChatId, cancellationToken))
            .CollectResults()
            .ConfigureAwait(true)
            ).Select(x => x.ValueOrDefault)
            .SkipNullItems();
        return chats.ToList();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> ListAll(
        PlaceId placeId,
        ChatListSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (await SearchUI.IsSearchModeOn(cancellationToken).ConfigureAwait(false)) {
            var searchResults = await SearchUI.GetContactSearchResults().ConfigureAwait(false);
            var foundChats = await searchResults.Select(x => ChatUI.Get(x.SearchResult.ContactId.ChatId, cancellationToken))
                .Collect()
                .ConfigureAwait(false);
            return foundChats.SkipNullItems().ToList();
        }
        var chatById = await ListAllUnordered(placeId, settings.Filter, cancellationToken).ConfigureAwait(false);
        return chatById.Values.OrderBy(settings.Order, ChatListPreOrder.ChatList).ToList();
    }

    public virtual Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListPeopleContacts(
        CancellationToken cancellationToken = default)
        => ListAllUnordered(PlaceId.None, ChatListFilter.People, cancellationToken);

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnordered(
        PlaceId placeId,
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnorderedRaw(placeId, cancellationToken).ConfigureAwait(false);
        chatById = await AddUnlistedSelectedChat(chatById, cancellationToken).ConfigureAwait(false);
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListOverallUnordered(
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListOverallUnorderedRaw(cancellationToken).ConfigureAwait(false);
        chatById = await AddUnlistedSelectedChat(chatById, cancellationToken).ConfigureAwait(false);
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnordered(
        PlaceId placeId,
        ChatListFilter filter,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<ChatId, ChatInfo> chatById;
        if (!placeId.IsNone && filter == ChatListFilter.People)
            chatById = await ListPlaceMembers(placeId, cancellationToken).ConfigureAwait(false);
        else
            chatById = await ListAllUnordered(placeId, cancellationToken).ConfigureAwait(false);
        return chatById.Values
            .Where(filter.Filter ?? (_ => true))
            .ToDictionary(c => c.Id, c => c);
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListPlaceMembers(PlaceId placeId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        DebugLog?.LogDebug("-> ListPlaceMembers (PlaceId={PlaceId})", placeId);
        var startedAt = CpuTimestamp.Now;

        var placeUsers = await Places.ListUserIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        var owner = await AccountUI.OwnAccount.Use(cancellationToken).ConfigureAwait(false);
        var chatIds = placeUsers
            .Where(userId => userId != owner.Id)
            .Select(userId => (ChatId)new PeerChatId(owner.Id, userId));

        var chatInfos = (await chatIds
                .Select(chatId => ChatUI.Get(chatId, cancellationToken))
                .CollectResults(256)
                .ConfigureAwait(false)
            ).Select(x => x.ValueOrDefault)
            .SkipNullItems()
            .ToDictionary(c => c.Id);

        DebugLog?.LogDebug("<- ListPlaceMembers (PlaceId={PlaceId}, {Count} contacts, {Duration})",
            placeId, chatInfos.Count, startedAt.Elapsed.ToShortString());

        return chatInfos;
    }

    [ComputeMethod]
    public virtual async Task<VirtualListTile<ChatListItemModel>> GetTile(PlaceId placeId, Tile<int> indexTile, CancellationToken cancellationToken)
    {
        var longRange = indexTile.Range.AsLongRange();
        var listView = GetChatListView(placeId);
        var settings = await listView.GetSettings(cancellationToken).ConfigureAwait(false);
        var chatInfos = await ListAll(placeId, settings, cancellationToken).ConfigureAwait(false);
        var chatInfoTile = chatInfos
            .Take(indexTile.Start..indexTile.End)
            .SkipNullItems()
            .ToList();

        var result = new List<ChatListItemModel>();
        for (var i = 0; i < chatInfoTile.Count; i++) {
            var chatInfo = chatInfoTile[i];
            var isLastItemInBlock = false;
            if (chatInfo.Contact.IsPinned && i < chatInfoTile.Count - 1) {
                // tile range is larger than pinned chat limit
                var nextChatState = chatInfoTile[i + 1];
                isLastItemInBlock = !nextChatState.Contact.IsPinned;
            }
            var isFirstItem = i == 0 && indexTile.Start == 0;
            result.Add(new ChatListItemModel(indexTile.Start + i, chatInfo.Chat, isLastItemInBlock, isFirstItem));
        }

        return new VirtualListTile<ChatListItemModel>(longRange, result);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListOverallUnorderedRaw(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ChatId, ChatInfo>();
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var extendedPlaceIds = new List<PlaceId> { PlaceId.None };
        extendedPlaceIds.AddRange(placeIds);
        foreach (var placeId in extendedPlaceIds) {
            var chatById = await ListAllUnorderedRaw(placeId, cancellationToken).ConfigureAwait(false);
            result.AddRange(placeId.IsNone ? chatById : chatById.Where(c => c.Key.PeerChatId.IsNone));
        }
        return result;
    }

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnorderedRaw(PlaceId placeId, CancellationToken cancellationToken)
    {
        if (_isFirstLoad) {
            _isFirstLoad = false;
#if false
            if (HostInfo.AppKind.IsClient()) {
                // The operations we do here are somehow CPU-intensive,
                // so we allow other tasks to run on the first load
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
#endif
        }
        try {
            DebugLog?.LogDebug("-> ListAllUnorderedRaw (PlaceId={PlaceId})", placeId);
            var startedAt = CpuTimestamp.Now;

            var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
            var contacts = (await contactIds
                    .Select(contactId => ChatUI.Get(contactId.ChatId, cancellationToken))
                    .CollectResults(256)
                    .ConfigureAwait(false)
                ).Select(x => x.ValueOrDefault)
                .SkipNullItems()
                .ToDictionary(c => c.Id);
            DebugLog?.LogDebug("<- ListAllUnorderedRaw (PlaceId={PlaceId}, {Count} contacts, {Duration})",
                placeId, contacts.Count, startedAt.Elapsed.ToShortString());
            return contacts;
        }
        finally {
            LoadingUI.MarkChatListLoaded();
        }
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsSelectedChatUnlistedInternal(CancellationToken cancellationToken)
    {
        var placeId = await ChatUI.SelectedPlaceId.Use(cancellationToken).ConfigureAwait(false);
        if (!placeId.IsNone)
            return false;

        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);
        var chatById = await ListAllUnorderedRaw(PlaceId.None, cancellationToken).ConfigureAwait(false);
        return !chatById.ContainsKey(selectedChatId);
    }

    // Private methods

    private async Task<IReadOnlyDictionary<ChatId, ChatInfo>> AddUnlistedSelectedChat(IReadOnlyDictionary<ChatId, ChatInfo> chatById, CancellationToken cancellationToken)
    {
        if (!await _isSelectedChatUnlisted.Use(cancellationToken).ConfigureAwait(false))
            return chatById;

        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (selectedChatId.IsPlaceChat)
            return chatById;

        selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);
        var selectedChat = selectedChatId.IsNone
            ? null
            : await ChatUI.Get(selectedChatId, cancellationToken).ConfigureAwait(false);
        if (selectedChat != null)
            chatById = new Dictionary<ChatId, ChatInfo>(chatById) {
                [selectedChat.Id] = selectedChat,
            };
        return chatById;
    }

    private async Task<Trimmed<int>> ComputeUnreadChatCount(CancellationToken cancellationToken)
    {
        var chatById = await ListOverallUnordered(cancellationToken).ConfigureAwait(false);
        var count = chatById.Values.UnmutedUnreadChatCount();
        return count;
    }

    private IStoredState<ChatListSettings> CreateSettingsState(PlaceId placeId)
    {
        var type = GetType();
        var key = ChatListSettings.GetKvasKey(placeId);
        var settings = StateFactory.NewKvasStored<ChatListSettings>(
            new (LocalSettings, key) {
                InitialValue = new(),
                Category = StateCategories.Get(type, key),
            });
        return settings;
    }
}
