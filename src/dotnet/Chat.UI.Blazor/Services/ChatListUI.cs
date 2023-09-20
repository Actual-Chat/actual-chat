using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatListUI : WorkerBase, IHasServices, IComputeService, INotifyInitialized
{
    public static readonly int ActiveItemCountWhenLoading = 2;
    public static readonly int AllItemCountWhenLoading = 14;

    private readonly List<ChatId> _activeItems = new List<ChatId>().AddMany(default, ActiveItemCountWhenLoading);
    private readonly List<ChatId> _allItems = new List<ChatId>().AddMany(default, AllItemCountWhenLoading);
    private readonly IMutableState<bool> _isSelectedChatUnlisted;
    private readonly IStoredState<ChatListSettings> _settings;
    // Delayed load-related
    private readonly IMutableState<int> _loadLimit;

    private ChatUI? _chatUI;
    private ActiveChatsUI? _activeChatsUI;
    private bool _isFirstLoad = true;

    private IContacts? _contacts;
    private SearchUI? _searchUI;
    private LoadingUI? _loadingUI;

    private Session Session { get; }
    private IContacts Contacts => _contacts ??= Services.GetRequiredService<IContacts>();
    private ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    private ActiveChatsUI ActiveChatsUI => _activeChatsUI ??= Services.GetRequiredService<ActiveChatsUI>();

    private SearchUI SearchUI => _searchUI ??= Services.GetRequiredService<SearchUI>();
    private LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    private AccountSettings AccountSettings { get; }
    private IStateFactory StateFactory { get; }
    private HostInfo HostInfo { get; }

    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    public IServiceProvider Services { get; }
    public IMutableState<ChatListSettings> Settings => _settings;
    public Task WhenLoaded => _settings.WhenRead;
    public IState<Trimmed<int>> UnreadChatCount { get; private set; } = null!;

    public ChatListUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        Session = services.Session();
        AccountSettings = services.AccountSettings();
        StateFactory = services.StateFactory();
        HostInfo = services.GetRequiredService<HostInfo>();

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
        UnreadChatCount = StateFactory.NewComputed(
            new ComputedState<Trimmed<int>>.Options() {
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(UnreadChatCount)),
            },
            ComputeUnreadChatsCount);
        this.Start();
    }

    public int GetCountWhenLoading(ChatListKind listKind)
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
            return Task.FromResult(index >= items.Count ? ChatId.None : items[index]);
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
                .OrderByDescending(c => c.Recency)
                .Select(c => ChatUI.Get(c.ChatId, cancellationToken))
                .Collect()
                .ConfigureAwait(true))
            .SkipNullItems();

        var searchPhrase = await SearchUI.GetSearchPhrase(cancellationToken).ConfigureAwait(false);
        chats = chats.FilterBySearchPhrase(searchPhrase);
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

            var contacts = await contactIds
                .Select(contactId => ChatUI.Get(contactId.ChatId, cancellationToken))
                .Collect()
                .ConfigureAwait(false);
            var result = contacts.SkipNullItems().ToDictionary(c => c.Id);
            DebugLog?.LogDebug("<- ListAllUnorderedRaw ({IdsLength} contacts, {Duration})", contacts.Length, startedAt.Elapsed.ToShortString());
            return result;
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
        _ = Services.UICommander().RunNothing(); // No UI update delays in near term
        _loadLimit.Value = int.MaxValue;
        using (Computed.Invalidate())
            _ = ListAllUnorderedRaw(default);
    }

    private async Task<Trimmed<int>> ComputeUnreadChatsCount(
        IComputedState<Trimmed<int>> state,
        CancellationToken cancellationToken)
    {
        var chatById = await ListAllUnordered(cancellationToken).ConfigureAwait(false);
        var count = chatById.Values.Where(c => c.UnmutedUnreadCount > 0).UnreadChatCount();
        return count;
    }
}
