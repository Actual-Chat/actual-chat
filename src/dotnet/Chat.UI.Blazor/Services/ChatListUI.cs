using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatListUI : ScopedWorkerBase, IComputeService, INotifyInitialized
{
    public static readonly int ActiveItemCountWhenLoading = 0;
    public static readonly int AllItemCountWhenLoading = 14;
    private static readonly TimeSpan MinNotificationInterval = TimeSpan.FromSeconds(5);

    // modified implicitly after returned by GetItems
    private readonly List<ChatId> _activeItems = new List<ChatId>().AddMany(default, ActiveItemCountWhenLoading);
    private readonly List<ChatId> _allItems = new List<ChatId>().AddMany(default, AllItemCountWhenLoading);
    private readonly IMutableState<bool> _isSelectedChatUnlisted;
    private readonly IStoredState<ChatListSettings> _settings;
    private readonly IMutableState<int> _loadLimit;

    private bool _isFirstLoad = true;
    private IComputedState<Trimmed<int>>? _unreadChatCount;

    private ChatHub ChatHub { get; }
    private IContacts Contacts => ChatHub.Contacts;
    private IAuthors Authors => ChatHub.Authors;
    private ActiveChatsUI ActiveChatsUI => ChatHub.ActiveChatsUI;
    private ChatUI ChatUI => ChatHub.ChatUI;
    private SearchUI SearchUI => ChatHub.SearchUI;
    private TuneUI TuneUI => ChatHub.TuneUI;
    private LoadingUI LoadingUI => ChatHub.LoadingUI;
    private AccountSettings AccountSettings => ChatHub.AccountSettings();
    private UICommander UICommander => ChatHub.UICommander();
    private new ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    public IMutableState<ChatListSettings> Settings => _settings;
#pragma warning disable CA1721 // Confusing w/ GetUnreadChatCount
    public IState<Trimmed<int>> UnreadChatCount => _unreadChatCount!;
#pragma warning restore CA1721
    public Task WhenLoaded => _settings.WhenRead;

    private Moment Now => Clocks.SystemClock.Now;

    public ChatListUI(ChatHub chatHub) : base(chatHub.Scope())
    {
        ChatHub = chatHub;

        var type = GetType();
        // var isClient = HostInfo.AppKind.IsClient();
        _loadLimit = StateFactory.NewMutable(Constants.Contacts.MinLoadLimit,
            StateCategories.Get(type, nameof(_loadLimit)));
        _isSelectedChatUnlisted = StateFactory.NewMutable(false,
            StateCategories.Get(type, nameof(_isSelectedChatUnlisted)));
        _settings = StateFactory.NewKvasStored<ChatListSettings>(
            new (AccountSettings, nameof(ChatListSettings)) {
                InitialValue = new(),
                Category = StateCategories.Get(type, nameof(Settings)),
            });
    }

    void INotifyInitialized.Initialized()
    {
        _unreadChatCount = StateFactory.NewComputed(
            new ComputedState<Trimmed<int>>.Options() {
                UpdateDelayer = FixedDelayer.Instant,
                TryComputeSynchronously = false,
                Category = StateCategories.Get(GetType(), nameof(UnreadChatCount)),
            },
            ComputeUnreadChatCount);
        Scope.RegisterDisposable(_unreadChatCount);
        this.Start();
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
    public virtual Task<ChatId> GetItem(ChatListKind listKind, int index)
    {
        var items = GetItems(listKind);
        lock (items)
            return Task.FromResult(index < 0 || index >= items.Count ? ChatId.None : items[index]);
    }

    // In fact, this is compute method, we just don't need one here, coz it routes the call further
    public Task<IReadOnlyList<ChatInfo>> List(ChatListKind listKind, CancellationToken cancellationToken = default)
        => listKind switch {
            ChatListKind.Active => ListActive(cancellationToken),
            ChatListKind.All => ListAll(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(listKind), listKind, null),
        };

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnreadChatCount(ChatListFilter filter, CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(filter, cancellationToken).ConfigureAwait(false);
        return chatById.Select(c => c.Value).UnreadChatCount();
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
    public virtual async Task<IReadOnlyList<ChatInfo>> ListAll(CancellationToken cancellationToken = default)
    {
        var settings = await Settings.Use(cancellationToken).ConfigureAwait(false);
        return await ListAll(settings, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> ListAll(
        ChatListSettings settings,
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(settings.Filter, cancellationToken).ConfigureAwait(false);
        var chats = chatById.Values.OrderBy(settings.Order);

        var searchPhrase = await SearchUI.GetSearchPhrase(cancellationToken).ConfigureAwait(false);
        chats = chats.FilterAndOrderBySearchPhrase(searchPhrase);
        return chats.ToList();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnordered(
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnorderedRaw(cancellationToken).ConfigureAwait(false);
        if (await _isSelectedChatUnlisted.Use(cancellationToken).ConfigureAwait(false)) {
            var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
            selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);
            var selectedChat = selectedChatId.IsNone ? null
                : await ChatUI.Get(selectedChatId, cancellationToken).ConfigureAwait(false);
            if (selectedChat != null)
                chatById = new Dictionary<ChatId, ChatInfo>(chatById) {
                    [selectedChat.Id] = selectedChat,
                };
        }
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnordered(
        ChatListFilter filter,
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnordered(cancellationToken).ConfigureAwait(false);
        return chatById.Values
            .Where(filter.Filter ?? (_ => true))
            .ToDictionary(c => c.Id, c => c);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnorderedRaw(CancellationToken cancellationToken)
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
            DebugLog?.LogDebug("-> ListAllUnorderedRaw");
            var startedAt = CpuTimestamp.Now;
            var contactIds = await Contacts.ListIds(Session, cancellationToken).ConfigureAwait(false);
            var loadLimit = _loadLimit.Value; // It is explicitly invalidated in IncreaseLoadLimit
            if (contactIds.Count > loadLimit) {
                contactIds = contactIds[..loadLimit];
                _ = IncreaseLoadLimit();
            }

            var contacts = (await contactIds
                .Select(contactId => ChatUI.Get(contactId.ChatId, cancellationToken))
                .CollectResults(256)
                .ConfigureAwait(false)
                ).Select(x => x.ValueOrDefault)
                .SkipNullItems()
                .ToDictionary(c => c.Id);
            DebugLog?.LogDebug("<- ListAllUnorderedRaw ({Count} contacts, {Duration})", contacts.Count, startedAt.Elapsed.ToShortString());
            return contacts;
        }
        finally {
            LoadingUI.MarkChatListLoaded();
        }
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsSelectedChatUnlistedInternal(CancellationToken cancellationToken)
    {
        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);

        var chatById = await ListAllUnorderedRaw(cancellationToken).ConfigureAwait(false);
        return !chatById.ContainsKey(selectedChatId);
    }

    // Private methods

    private List<ChatId> GetItems(ChatListKind listKind)
        => listKind switch {
            ChatListKind.Active => _activeItems,
            ChatListKind.All => _allItems,
            _ => throw new ArgumentOutOfRangeException(nameof(listKind)),
        };

    private async Task IncreaseLoadLimit()
    {
        await Task.Delay(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
        if (_loadLimit.Value == int.MaxValue)
            return;

        _loadLimit.Value = int.MaxValue;
        _ = UICommander.RunNothing(); // No UI update delays in near term
        using (Computed.Invalidate())
            _ = ListAllUnorderedRaw(default);
    }

    private async Task<Trimmed<int>> ComputeUnreadChatCount(
        IComputedState<Trimmed<int>> state,
        CancellationToken cancellationToken)
    {
        var chatById = await ListAllUnordered(cancellationToken).ConfigureAwait(false);
        var count = chatById.Values.Where(c => c.UnmutedUnreadCount > 0).UnreadChatCount();
        return count;
    }
}
