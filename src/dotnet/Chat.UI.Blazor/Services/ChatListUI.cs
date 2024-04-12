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

    // modified implicitly after returned by GetItems
    private readonly List<ChatId> _activeItems = new List<ChatId>().AddMany(default, ActiveItemCountWhenLoading);
    private readonly List<ChatId> _allItems = new List<ChatId>().AddMany(default, AllItemCountWhenLoading);
    private readonly IMutableState<bool> _isSelectedChatUnlisted;
    private readonly IMutableState<int> _loadLimit;
    private readonly IMutableState<ChatListView?> _activeChatListView;

    private bool _isFirstLoad = true;
    private IComputedState<Trimmed<int>>? _unreadChatCount;

    private IContacts Contacts => Hub.Contacts;
    private IAuthors Authors => Hub.Authors;
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

    public IState<ChatListView?> ActiveChatListView => _activeChatListView;

    public ChatListUI(ChatUIHub hub) : base(hub)
    {
        var type = GetType();
        _loadLimit = StateFactory.NewMutable(Constants.Contacts.MinLoadLimit,
            StateCategories.Get(type, nameof(_loadLimit)));
        _isSelectedChatUnlisted = StateFactory.NewMutable(false,
            StateCategories.Get(type, nameof(_isSelectedChatUnlisted)));
        _activeChatListView = StateFactory.NewMutable((ChatListView?)null,
            StateCategories.Get(type, nameof(ActiveChatListView)));
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

    public ChatListView ActivateChatList(PlaceId placeId)
    {
        if (_activeChatListView.Value == null || _activeChatListView.Value.PlaceId != placeId)
            _activeChatListView.Value = new ChatListView(placeId, CreateSettingsState(placeId));
        return _activeChatListView.Value;
    }

#pragma warning disable CA1822 // Can be static
    public int GetCountWhenLoading(ChatListKind listKind)
#pragma warning restore CA1822
        => listKind switch {
            ChatListKind.All => AllItemCountWhenLoading,
            ChatListKind.Active => ActiveItemCountWhenLoading,
            _ => throw new ArgumentOutOfRangeException(nameof(listKind), listKind, null)
        };

    [ComputeMethod]
    public virtual Task<int> GetCount(ChatListKind listKind)
    {
        var items = GetItems(listKind);
        lock (items)
            return Task.FromResult(items.Count);
    }

    [ComputeMethod]
    public virtual Task<(bool, ChatId)> GetItem(ChatListKind listKind, int index)
    {
        var items = GetItems(listKind);
        lock (items) {
            var indexIsValid = index >= 0 && index < items.Count;
            var chatId = indexIsValid ? items[index] : ChatId.None;
            return Task.FromResult((indexIsValid, chatId));
        }
    }

    // In fact, this is compute method, we just don't need one here, coz it routes the call further
    public Task<IReadOnlyList<ChatInfo>> List(ChatListKind listKind, CancellationToken cancellationToken = default)
        => listKind switch {
            ChatListKind.Active => ListActive(cancellationToken),
            ChatListKind.All => ListAll(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(listKind), listKind, null),
        };

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnreadChatCount(PlaceId placeId, ChatListFilter filter, CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        return chatById.Select(c => c.Value).UnreadChatCount();
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnmutedUnreadChatCount(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(placeId, ChatListFilter.None, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken = default)
    {
        var listView = await ActiveChatListView.Use(cancellationToken).ConfigureAwait(false);
        if (listView == null)
            return ImmutableList<ChatInfo>.Empty;

        var settings = await listView.GetSettings(cancellationToken).ConfigureAwait(false);
        return await ListAll(listView.PlaceId, settings, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> ListAll(
        PlaceId placeId,
        ChatListSettings settings,
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(placeId, settings.Filter, cancellationToken).ConfigureAwait(false);
        var chats = chatById.Values.OrderBy(settings.Order, ChatListPreOrder.ChatList);

        var searchPhrase = await SearchUI.GetSearchPhrase(cancellationToken).ConfigureAwait(false);
        chats = chats.FilterAndOrderBySearchPhrase(searchPhrase);
        return chats.ToList();
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

    private async Task<IReadOnlyDictionary<ChatId, ChatInfo>> AddUnlistedSelectedChat(IReadOnlyDictionary<ChatId, ChatInfo> chatById, CancellationToken cancellationToken)
    {
        if (await _isSelectedChatUnlisted.Use(cancellationToken).ConfigureAwait(false)) {
            var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
            if (!selectedChatId.IsPlaceChat) {
                selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);
                var selectedChat = selectedChatId.IsNone
                    ? null
                    : await ChatUI.Get(selectedChatId, cancellationToken).ConfigureAwait(false);
                if (selectedChat != null)
                    chatById = new Dictionary<ChatId, ChatInfo>(chatById) {
                        [selectedChat.Id] = selectedChat,
                    };
            }
        }
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnordered(
        PlaceId placeId,
        ChatListFilter filter,
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(placeId, cancellationToken).ConfigureAwait(false);
        return chatById.Values
            .Where(filter.Filter ?? (_ => true))
            .ToDictionary(c => c.Id, c => c);
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

            await PseudoListAllUnorderedRawDependency().ConfigureAwait(false);
            var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
            var loadLimit = _loadLimit.Value; // It is explicitly invalidated in IncreaseLoadLimit
            if (contactIds.Count > loadLimit) {
                contactIds = contactIds[..loadLimit];
                _ = IncreaseLoadLimit(placeId);
            }

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
    protected virtual Task<Unit> PseudoListAllUnorderedRawDependency()
        => ActualLab.Async.TaskExt.UnitTask;

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

    private List<ChatId> GetItems(ChatListKind listKind)
        => listKind switch {
            ChatListKind.Active => _activeItems,
            ChatListKind.All => _allItems,
            _ => throw new ArgumentOutOfRangeException(nameof(listKind)),
        };

    private async Task IncreaseLoadLimit(PlaceId placeId)
    {
        await Task.Delay(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
        if (_loadLimit.Value == int.MaxValue)
            return;

        _loadLimit.Value = int.MaxValue;
        _ = UICommander.RunNothing(); // No UI update delays in near term
        using (Computed.Invalidate())
            _ = ListAllUnorderedRaw(placeId, default);
    }

    private async Task<Trimmed<int>> ComputeUnreadChatCount(
        IComputedState<Trimmed<int>> state,
        CancellationToken cancellationToken)
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
