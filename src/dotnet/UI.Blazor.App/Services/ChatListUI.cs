using ActualChat.Contacts;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatListUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    public static readonly int ActiveItemCountWhenLoading = 0;
    public static readonly int AllItemCountWhenLoading = 14;
    public static readonly TileStack<int> ChatTileStack = Constants.Chat.ChatTileStack;
    public static readonly int LoadLimit = ChatTileStack.Layers[1].TileSize; // 20
    public static readonly int HalfLoadLimit = LoadLimit / 2;
    public static readonly int TileSize = ChatTileStack.FirstLayer.TileSize;
    private static readonly TimeSpan MinNotificationInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeavyTaskCancellationDelay = TimeSpan.FromSeconds(5);

    private readonly MutableState<bool> _isSelectedChatUnlisted;
    private readonly ConcurrentDictionary<PlaceId, LazySlim<PlaceId, ChatListUI, PlaceChatListSettings>> _placeChatLists = new();

    private ComputedState<Trimmed<int>>? _unreadChatCount;

    private IContacts Contacts => Hub.Contacts;
    private IAuthors Authors => Hub.Authors;
    private IPlaces Places => Hub.Places;
    private AccountUI AccountUI => Hub.AccountUI;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private TuneUI TuneUI => Hub.TuneUI;
    private LoadingUI LoadingUI => Hub.LoadingUI;
    private UICommander UICommander => Hub.UICommander();
    private new ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

#pragma warning disable CA1721 // Confusing w/ GetUnreadChatCount
    public IState<Trimmed<int>> UnreadChatCount => _unreadChatCount!;
#pragma warning restore CA1721

    private Moment CpuNow => Clocks.CpuClock.Now;

    public ChatListUI(ChatUIHub hub) : base(hub)
    {
        var type = GetType();
        _isSelectedChatUnlisted = StateFactory.NewMutable(false,
            StateCategories.Get(type, nameof(_isSelectedChatUnlisted)));
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

    public PlaceChatListSettings GetPlaceChatListSettings(PlaceId placeId)
        => _placeChatLists.GetOrAdd(placeId,
            static (placeId1, self) => new PlaceChatListSettings(placeId1, self.Hub), this);

    [ComputeMethod]
    public virtual async Task<int> GetCount(PlaceId placeId, ChatListSettings chatListSettings, CancellationToken cancellationToken)
    {
        var chatById = await ListUnordered(placeId, chatListSettings.Filter, cancellationToken).ConfigureAwait(false);
        return chatById.Count;
    }

    [ComputeMethod]
    public virtual async Task<int> IndexOf(PlaceId placeId, ChatId chatId, ChatListSettings chatListSettings, CancellationToken cancellationToken)
    {
        var items = await List(placeId, chatListSettings, cancellationToken).ConfigureAwait(false);
        return items.FirstIndexOf(x => x.Id == chatId);
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnreadChatCount(PlaceId placeId, ChatListFilter filter, CancellationToken cancellationToken = default)
    {
        var chatById = await ListUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        return chatById.Select(c => c.Value).UnreadChatCount();
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnmutedUnreadChatCount(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var filter = placeId == PlaceId.None ? ChatListFilter.None : ChatListFilter.Groups;
        var chatById = await ListUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        return chatById.Select(c => c.Value).UnmutedUnreadChatCount();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> ListActive(CancellationToken cancellationToken = default)
    {
        await ActiveChatsUI.WhenLoaded.ConfigureAwait(true); // No need for .ConfigureAwait(false) here

        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        var chats = (await activeChats
            .Select(c => ChatUI.Get(c.ChatId, cancellationToken))
            .CollectResults(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(true)
            ).Select(x => x.ValueOrDefault)
            .SkipNullItems();
        return chats.ToList();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> List(
        PlaceId placeId,
        ChatListSettings settings,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug("-> List({PlaceId}, {Settings})", placeId, settings);
        var chatById = await ListUnordered(placeId, settings.Filter, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug(
            "<- List({PlaceId}, {Settings}): {Count} items",
            placeId, settings, chatById.Count);
        return chatById.Values.OrderBy(settings.Order, ChatListPreOrder.ChatList).ToList();
    }

    public virtual Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListPeopleOnly(
        CancellationToken cancellationToken = default)
        => ListUnordered(PlaceId.None, ChatListFilter.People, cancellationToken);

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListMembersOnly(
        PlaceId placeId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        DebugLog?.LogDebug("-> ListMembersOnly({PlaceId})", placeId);
        var startedAt = CpuTimestamp.Now;
        var placeUsers = await Places.ListUserIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        var owner = await AccountUI.OwnAccount.Use(cancellationToken).ConfigureAwait(false);
        var chatIds = placeUsers
            .Where(userId => userId != owner.Id)
            .Select(userId => (ChatId)new PeerChatId(owner.Id, userId));

        var chatResults = await chatIds
            .Select(chatId => ChatUI.Get(chatId, cancellationToken))
            .CollectResults(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        var chatById = chatResults.Select(x => x.ValueOrDefault)
            .SkipNullItems()
            .ToDictionary(c => c.Id);
        DebugLog?.LogDebug(
            "<- ListMembersOnly({PlaceId}): {Count} items, {Duration}",
            placeId, chatById.Count, startedAt.Elapsed.ToShortString());
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnordered(
        CancellationToken cancellationToken = default)
    {
        var chatById = await ListAllUnorderedRaw(cancellationToken).ConfigureAwait(false);
        chatById = await AddUnlistedSelectedChat(chatById, cancellationToken).ConfigureAwait(false);
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnordered(
        PlaceId placeId,
        CancellationToken cancellationToken = default)
    {
        using var gracefulCts = cancellationToken.CreateDelayedTokenSource(HeavyTaskCancellationDelay);
        cancellationToken = gracefulCts.Token;
        var chatById = await ListUnorderedRaw(placeId, cancellationToken).ConfigureAwait(false);
        chatById = await AddUnlistedSelectedChat(chatById, cancellationToken).ConfigureAwait(false);
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnordered(
        PlaceId placeId,
        ChatListFilter filter,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<ChatId, ChatInfo> chatById;
        if (!placeId.IsNone && filter == ChatListFilter.People)
            chatById = await ListMembersOnly(placeId, cancellationToken).ConfigureAwait(false);
        else
            chatById = await ListUnordered(placeId, cancellationToken).ConfigureAwait(false);
        return chatById.Values
            .Where(filter.Filter ?? (_ => true))
            .ToDictionary(c => c.Id, c => c);
    }

    [ComputeMethod]
    public virtual async Task<VirtualListTile<ChatListItemModel>> GetTile(
        PlaceId placeId, Tile<int> indexTile, ChatListSettings chatListSettings, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("GetTile: -> {PlaceId}, {Indexes}, {Settings}", placeId, indexTile, chatListSettings);
        var longRange = indexTile.Range.AsLongRange();
        var chatInfos = await List(placeId, chatListSettings, cancellationToken).ConfigureAwait(false);
        var chatInfoTile = chatInfos
            .Take(indexTile.Start..indexTile.End)
            .SkipNullItems()
            .ToList();

        var result = new List<ChatListItemModel>();
        for (var i = 0; i < chatInfoTile.Count; i++) {
            var chatInfo = chatInfoTile[i];
            var isLastItemInBlock = false;
            if (chatInfo.Contact.IsPinned) {
                var nextChatState = i == chatInfoTile.Count - 1
                    ? chatInfos.GetOrDefault(i + 1)
                    : chatInfoTile[i + 1];
                if (nextChatState != null)
                    isLastItemInBlock = !nextChatState.Contact.IsPinned;
            }
            var isFirstItem = i == 0 && indexTile.Start == 0;
            result.Add(new ChatListItemModel(indexTile.Start + i, chatInfo.Chat, isLastItemInBlock, isFirstItem));
        }
        DebugLog?.LogDebug("GetTile: <- {PlaceId}, {Indexes}", placeId, indexTile);
        return new VirtualListTile<ChatListItemModel>(longRange, result);
    }

    public ValueTask Pin(ChatId chatId) => SetPinState(chatId, true);
    public ValueTask Unpin(ChatId chatId) => SetPinState(chatId, false);
    public async ValueTask SetPinState(ChatId chatId, bool mustPin)
    {
        if (chatId.IsNone)
            return;

        var contact = await Contacts.GetForChat(Session, chatId, default).Require().ConfigureAwait(false);
        if (contact.IsPinned == mustPin)
            return;

        var changedContact = contact with { IsPinned = mustPin };
        var change = contact.IsStored()
            ? new Change<Contact>() { Update = changedContact }
            : new Change<Contact>() { Create = changedContact };
        var command = new Contacts_Change(Session, contact.Id, contact.Version, change);
        _ = TuneUI.Play(Tune.PinUnpinChat);
        await UICommander.Run(command).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnorderedRaw(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ChatId, ChatInfo>();
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var extendedPlaceIds = new List<PlaceId> { PlaceId.None };
        extendedPlaceIds.AddRange(placeIds);

        using var gracefulCts = cancellationToken.CreateDelayedTokenSource(HeavyTaskCancellationDelay);
        cancellationToken = gracefulCts.Token;
        foreach (var placeId in extendedPlaceIds) {
            var chatById = await ListUnorderedRaw(placeId, cancellationToken).ConfigureAwait(false);
            result.AddRange(placeId.IsNone ? chatById : chatById.Where(c => c.Key.PeerChatId.IsNone));
        }
        return result;
    }

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnorderedRaw(
        PlaceId placeId, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("-> ListUnorderedRaw({PlaceId})", placeId);
        var startedAt = CpuTimestamp.Now;
        var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        var chatResults = await contactIds
            .Select(x => ChatUI.Get(x.ChatId, cancellationToken))
            .CollectResults(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        var chatById = chatResults
            .Select(x => x.ValueOrDefault)
            .SkipNullItems()
            .ToDictionary(c => c.Id);
        LoadingUI.MarkChatListLoaded();

        DebugLog?.LogDebug(
            "<- ListUnorderedRaw({PlaceId}): {Count} items ({IdCount} IDs), {Duration})",
            placeId, chatById.Count, contactIds.Count, startedAt.Elapsed.ToShortString());
        return chatById;
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsSelectedChatUnlistedInternal(CancellationToken cancellationToken)
    {
        var placeId = await ChatUI.SelectedPlaceId.Use(cancellationToken).ConfigureAwait(false);
        if (!placeId.IsNone)
            return false;

        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);

        using var gracefulCts = cancellationToken.CreateDelayedTokenSource(HeavyTaskCancellationDelay);
        cancellationToken = gracefulCts.Token;
        var chatById = await ListUnorderedRaw(PlaceId.None, cancellationToken).ConfigureAwait(false);
        return !chatById.ContainsKey(selectedChatId);
    }

    // Private methods

    private async Task<IReadOnlyDictionary<ChatId, ChatInfo>> AddUnlistedSelectedChat(
        IReadOnlyDictionary<ChatId, ChatInfo> chatById, CancellationToken cancellationToken)
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
        var chatById = await ListAllUnordered(cancellationToken).ConfigureAwait(false);
        var count = chatById.Values.UnmutedUnreadChatCount();
        return count;
    }
}
