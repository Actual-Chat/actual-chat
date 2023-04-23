using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatListUI : WorkerBase, IHasServices, IComputeService, INotifyInitialized
{
    private readonly List<ChatId> _activeItems = new();
    private readonly List<ChatId> _allItems = new();
    private volatile bool _isLoaded;

    private ChatUI? _chatUI;
    private ActiveChatsUI? _activeChatsUI;

    private Session Session { get; }
    private IContacts Contacts { get; }
    private ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    private ActiveChatsUI ActiveChatsUI => _activeChatsUI ??= Services.GetRequiredService<ActiveChatsUI>();
    private SearchUI SearchUI { get; }
    private AccountSettings AccountSettings { get; }
    private IStateFactory StateFactory { get; }
    private HostInfo HostInfo { get; }

    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    private IMutableState<bool> _isSelectedChatUnlisted;
    private IMutableState<int> _loadLimit;

    public IServiceProvider Services { get; }
    public IStoredState<ChatListSettings> Settings { get; }

    public ChatListUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        Session = services.GetRequiredService<Session>();
        Contacts = services.GetRequiredService<IContacts>();
        SearchUI = services.GetRequiredService<SearchUI>();
        AccountSettings = services.AccountSettings();
        StateFactory = services.StateFactory();
        HostInfo = services.GetRequiredService<HostInfo>();

        var type = GetType();
        _loadLimit = StateFactory.NewMutable(HostInfo.AppKind.IsClient() ? 16 : int.MaxValue,
            StateCategories.Get(type, nameof(_loadLimit)));
        _isSelectedChatUnlisted = StateFactory.NewMutable(false,
            StateCategories.Get(type, nameof(_isSelectedChatUnlisted)));
        Settings = StateFactory.NewKvasStored<ChatListSettings>(
            new (AccountSettings, nameof(ChatListSettings)) {
                InitialValue = new(),
                Category = StateCategories.Get(type, nameof(Settings)),
            });
    }

    void INotifyInitialized.Initialized()
        => this.Start();

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
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken);
        var chats = (await activeChats
                .OrderByDescending(c => c.Recency)
                .Select(c => ChatUI.Get(c.ChatId, cancellationToken))
                .Collect())
            .SkipNullItems();

        var searchPhrase = await SearchUI.GetSearchPhrase(cancellationToken);
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

        var searchPhrase = await SearchUI.GetSearchPhrase(cancellationToken);
        chats = chats.FilterAndOrderBySearchPhrase(searchPhrase);
        return chats.ToList();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnordered(
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnorderedRaw(cancellationToken).ConfigureAwait(false);
        if (await _isSelectedChatUnlisted.Use(cancellationToken)) {
            var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken);
            selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken);
            var selectedChat = selectedChatId.IsNone ? null
                : await ChatUI.Get(selectedChatId, cancellationToken);
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
        if (!_isLoaded) {
            _isLoaded = true;
            if (HostInfo.AppKind.IsClient()) {
                // First chat list load can be delayed if left panel is invisible
                var panelsUI = Services.GetRequiredService<PanelsUI>();
                if (panelsUI.IsWide() || !panelsUI.Left.IsVisible.Value)
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        var startedAt = CpuTimestamp.Now;
        DebugLog?.LogDebug("-> ListAllUnorderedRaw");
        var contactIds = await Contacts.ListIds(Session, cancellationToken).ConfigureAwait(false);
        var loadLimit = _loadLimit.Value; // It is explicitly invalidated in BumpUpLoadLimit
        if (contactIds.Length > loadLimit) {
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

    [ComputeMethod]
    protected virtual async Task<bool> IsSelectedChatUnlistedInternal(CancellationToken cancellationToken)
    {
        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken);
        selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken);

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
}
