using ActualChat.Contacts;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages chat list filtering, sorting, pinning, and unread count tracking.
/// </summary>
public partial class ChatListUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    public static readonly TileLayer<int> IndexTiles = Constants.Chat.ChatListIndexTiles;
    public static readonly int TileSize = IndexTiles.TileSize;
    public static readonly int LoadLimit = TileSize * 8; // 40
    public static readonly int HalfLoadLimit = LoadLimit / 2;
    private static readonly TimeSpan MinNotificationInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeavyTaskCancellationDelay = TimeSpan.FromSeconds(5);

    private readonly MutableState<bool> _isSelectedChatUnlisted;
    private readonly ConcurrentDictionary<Option<PlaceId>, LazySlim<Option<PlaceId>, ChatListUI, PlaceChatListSettings>>
        _placeChatLists = new();

    private ComputedState<Trimmed<int>>? _unreadChatCount;
    private ComputedState<ChatInfo?>? _notesChat;
    private (string ListKey, ChatId ChatId)? _scrollAnchor;

    private IContacts Contacts => Hub.Contacts;
    private IAuthors Authors => Hub.Authors;
    private IPlaces Places => Hub.Places;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private SearchUI SearchUI => Hub.SearchUI;
    private NotificationsPanelUI NotificationsPanelUI => Hub.NotificationsPanelUI;
    private NotificationsUI NotificationsUI => Hub.NotificationsUI;
    private LoadingUI LoadingUI => Hub.LoadingUI;
    private new ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

#pragma warning disable CA1721 // Confusing w/ GetUnreadChatCount
    public IState<Trimmed<int>> UnreadChatCount => _unreadChatCount!;
#pragma warning restore CA1721

    public IState<ChatInfo?> NotesChat => _notesChat!;

    private Moment CpuNow => Clocks.CpuClock.Now;

    public ChatListUI(AppUIHub hub) : base(hub)
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
        _notesChat = StateFactory.NewComputed(GetNotes);
        Hub.RegisterDisposable(_notesChat);
        this.Start();
    }

    public PlaceChatListSettings GetPlaceChatListSettings(PlaceId? placeId)
        => _placeChatLists.GetOrAdd(placeId is not null ? Option.Some(placeId) : Option<PlaceId>.None,
            static (placeId1, self) => new PlaceChatListSettings(placeId1.ValueOrDefault, self.Hub, true),
            this);

    [ComputeMethod]
    public virtual async Task<int> GetCount(
        PlaceId? placeId, ChatListSettings chatListSettings, CancellationToken cancellationToken)
    {
        var chatById = await ListUnorderedForDisplay(placeId, chatListSettings, cancellationToken)
            .ConfigureAwait(false);
        return chatById.Count;
    }

    [ComputeMethod]
    public virtual async Task<int> IndexOf(
        PlaceId? placeId, ChatId chatId, ChatListSettings chatListSettings, CancellationToken cancellationToken)
    {
        var items = await List(placeId, chatListSettings, cancellationToken).ConfigureAwait(false);
        return items.FirstIndexOf(x => x.Id == chatId);
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnreadChatCount(
        PlaceId? placeId, ChatListFilter filter, CancellationToken cancellationToken = default)
    {
        var chatById = await ListUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        return await ComputeGatedUnreadChatCount(chatById, false, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnmutedUnreadChatCount(
        PlaceId? placeId, CancellationToken cancellationToken = default)
    {
        var filter = placeId is null ? ChatListFilter.None : ChatListFilter.Groups;
        return await GetUnmutedUnreadChatCount(placeId, filter, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod(InvalidationDelay = 0.6)]
    public virtual async Task<Trimmed<int>> GetUnmutedUnreadChatCount(
        PlaceId? placeId, ChatListFilter filter, CancellationToken cancellationToken = default)
    {
        var chatById = await ListUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        return await ComputeGatedUnreadChatCount(chatById, true, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> ListActive(CancellationToken cancellationToken = default)
    {
        await ActiveChatsUI.WhenReady.ConfigureAwait(true); // No need for .ConfigureAwait(false) here

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
        PlaceId? placeId,
        ChatListSettings settings,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug("-> List({PlaceId}, {Settings})", placeId, settings);
        var chatById = await ListUnorderedForDisplay(placeId, settings, cancellationToken).ConfigureAwait(false);
        var filter = settings.GetFilter();
        // AcrossPlace filters are exactly the notifications panel's, but the Mentions tab neither
        // surfaces reactions nor sorts by them - see ListUnorderedForDisplay.
        var isNotificationsPanel = filter.AcrossPlace && filter != ChatListFilter.UnreadMentions;
        var reactedAt = isNotificationsPanel
            ? await GetReactedAt(chatById.Keys, cancellationToken).ConfigureAwait(false)
            : null;
        var preOrder = isNotificationsPanel ? ChatListPreOrder.ReactionsFirst : ChatListPreOrder.ChatList;
        DebugLog?.LogDebug(
            "<- List({PlaceId}, {Settings}): {Count} items",
            placeId, settings, chatById.Count);
        return chatById.Values.OrderBy(settings.Order, preOrder, reactedAt).ToList();
    }

    public virtual Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListPeopleOnly(
        CancellationToken cancellationToken = default)
        => ListUnordered(null, ChatListFilter.People, cancellationToken);

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListMembersOnly(
        PlaceId placeId, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("-> ListMembersOnly({PlaceId})", placeId);
        var startedAt = CpuTimestamp.Now;
        var placeUsers = await Places.ListUserIds(Session, placeId, cancellationToken).ConfigureAwait(false);
        var owner = await AccountUI.OwnAccount.Use(cancellationToken).ConfigureAwait(false);

        var chatIds = placeUsers
            .Where(userId => userId != owner.Id)
            .Select(userId => PeerChatId.New(owner.Id, userId));
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
        PlaceId? placeId,
        CancellationToken cancellationToken = default)
    {
        using var gracefulCts = cancellationToken.CreateDelayedTokenSource(HeavyTaskCancellationDelay);
        var cancellationToken2 = gracefulCts.Token;
        var chatById = await ListUnorderedRaw(placeId, cancellationToken2).ConfigureAwait(false);
        chatById = await AddUnlistedSelectedChat(chatById, cancellationToken2).ConfigureAwait(false);
        return chatById;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnordered(
        PlaceId? placeId,
        ChatListFilter filter,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<ChatId, ChatInfo> chatById;
        if (filter.AcrossPlace)
            chatById = await ListAllUnordered(cancellationToken).ConfigureAwait(false);
        else if (placeId is not null && filter == ChatListFilter.People)
            chatById = await ListMembersOnly(placeId, cancellationToken).ConfigureAwait(false);
        else
            chatById = await ListUnordered(placeId, cancellationToken).ConfigureAwait(false);
        return chatById.Values
            .Where(filter.Invoke)
            .ToDictionary(c => c.Id, c => c);
    }

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnorderedForDisplay(
        PlaceId? placeId,
        ChatListSettings settings,
        CancellationToken cancellationToken = default)
    {
        // Same as ListUnordered(placeId, filter), but also keeps chats that were read recently: a read chat
        // lingers in the notifications panel for the user-configured retention window, then drops out on its own.
        var filter = settings.GetFilter();
        var chatById = await ListUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        if (!filter.AcrossPlace)
            return chatById;

        var expiring = await NotificationsPanelUI.GetExpiring(filter.Id, cancellationToken).ConfigureAwait(false);
        // The Mentions tab means own-mentions and nothing else.
        var reactedChatIds = filter == ChatListFilter.UnreadMentions
            ? ApiArray<ChatId>.Empty
            : await NotificationsUI.ListReactedChatIds(cancellationToken).ConfigureAwait(false);
        if (expiring.Count == 0 && reactedChatIds.Count == 0)
            return chatById;

        // The ChatInfo each one had on its way out, so this needs no lookup over every chat.
        var result = new Dictionary<ChatId, ChatInfo>(chatById);
        foreach (var (chatId, chatInfo) in expiring)
            result.TryAdd(chatId, chatInfo);
        foreach (var chatId in reactedChatIds) {
            if (result.ContainsKey(chatId))
                continue;

            // Only chats the filter rejected land here - the ones it accepts are already in chatById.
            // A reaction relaxes just the ChatInfoFilter (unread) dimension: a chat the Filter (kind)
            // dimension rejects - e.g. a group chat on the People tab - must stay out.
            var chatInfo = await ChatUI.Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chatInfo is not null && !filter.Invoke(chatInfo) && (filter.Filter?.Invoke(chatInfo.Chat) ?? true))
                result.Add(chatId, chatInfo);
        }

        return result;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<int>> GetSeparatorIndexes(
        PlaceId? placeId, ChatListSettings chatListSettings, CancellationToken cancellationToken)
    {
        // Same rule GetTile applies per item, but over the whole ordering rather than a tile: the
        // separator's height has to be modelled for items nowhere near the loaded window.
        var chatInfos = await List(placeId, chatListSettings, cancellationToken).ConfigureAwait(false);
        var result = new List<int>();
        for (var i = 0; i < chatInfos.Count - 1; i++) {
            var chatInfo = chatInfos[i];
            var nextChatInfo = chatInfos[i + 1];
            if (chatInfo != null && nextChatInfo != null && chatInfo.Contact.IsPinned && !nextChatInfo.Contact.IsPinned)
                result.Add(i);
        }

        return result;
    }

    [ComputeMethod]
    public virtual async Task<VirtualListTile<ChatListItemModel>> GetTile(
        PlaceId? placeId, Tile<int> indexTile, ChatListSettings chatListSettings, CancellationToken cancellationToken)
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
                    ? chatInfos.GetOrDefault(indexTile.Start + i + 1)
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
        var contact = await Contacts.GetForChat(Session, chatId, default).Require().ConfigureAwait(false);
        if (contact.IsPinned == mustPin)
            return;

        var changedContact = contact with { IsPinned = mustPin };
        var change = contact.HasVersion()
            ? new Change<Contact>() { Update = changedContact }
            : new Change<Contact>() { Create = changedContact };
        var command = new Contacts_Change {
            Session = Session,
            Id = contact.Id,
            ExpectedVersion = contact.Version,
            Change = change,
        };
        _ = TuneUI.Play(Tune.PinUnpinChat);
        await UICommander.Run(command).ConfigureAwait(false);
    }

    public void SetScrollAnchor(string listKey, ChatId? topVisibleChatId)
    {
        // Only the search overlay is worth restoring from: every other reason a chat list is re-created -
        // another navbar group, place, or filter - is a new context, where landing on the previous list's
        // chat would read as a random jump.
        var isSearchOpen = SearchUI.IsSearchModeOn.Value || SearchUI.IsShowRecentOn.Value;
        _scrollAnchor = isSearchOpen && topVisibleChatId is { } chatId
            ? (listKey, chatId)
            : null;
    }

    public ChatId? PullScrollAnchor(string listKey)
    {
        var scrollAnchor = _scrollAnchor;
        _scrollAnchor = null;
        return scrollAnchor is { } anchor && anchor.ListKey == listKey
            ? anchor.ChatId
            : null;
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListAllUnorderedRaw(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ChatId, ChatInfo>();
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var extendedPlaceIds = new List<PlaceId?> { null };
        extendedPlaceIds.AddRange(placeIds);

        using var gracefulCts = cancellationToken.CreateDelayedTokenSource(HeavyTaskCancellationDelay);
        var cancellationToken2 = gracefulCts.Token;
        foreach (var placeId in extendedPlaceIds) {
            var chatById = await ListUnorderedRaw(placeId, cancellationToken2).ConfigureAwait(false);
            result.AddRange(placeId is null ? chatById : chatById.Where(c => c.Key.Kind != ChatKind.Peer));
        }

        return result;
    }

    [ComputeMethod]
    protected virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnorderedRaw(
        PlaceId? placeId,
        CancellationToken cancellationToken)
    {
        // NOTE:
        // The code below must be kept in sync with AppNonScopedServiceStarter.PreloadContacts,
        // otherwise you're going to slow down the app startup!

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
            placeId, chatById.Count, contactIds.Length, startedAt.Elapsed.ToShortString());
        return chatById;
    }

    [ComputeMethod]
    protected virtual async Task<bool> IsSelectedChatUnlistedInternal(CancellationToken cancellationToken)
    {
        var placeId = await ChatUI.SelectedPlaceId.Use(cancellationToken).ConfigureAwait(false);
        if (placeId is not null)
            return false;

        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);
        if (selectedChatId is null)
            return false;
        if (selectedChatId.IsThread())
            return false;

        using var gracefulCts = cancellationToken.CreateDelayedTokenSource(HeavyTaskCancellationDelay);
        var cancellationToken2 = gracefulCts.Token;
        var chatById = await ListUnorderedRaw(null, cancellationToken2).ConfigureAwait(false);
        return !chatById.ContainsKey(selectedChatId);
    }

    // Private methods

    private async Task<IReadOnlyDictionary<ChatId, Moment>?> GetReactedAt(
        IEnumerable<ChatId> chatIds, CancellationToken cancellationToken)
    {
        var result = (Dictionary<ChatId, Moment>?)null;
        foreach (var chatId in chatIds) {
            var state = await NotificationsUI.GetReactionState(chatId, cancellationToken).ConfigureAwait(false);
            if (state.Emoji is null)
                continue;

            result ??= new Dictionary<ChatId, Moment>();
            result.Add(chatId, state.SentAt);
        }

        return result;
    }

    private async Task<ChatInfo?> GetNotes(CancellationToken cancellationToken = default)
    {
        var chatById = await ListUnorderedRaw(null, cancellationToken).ConfigureAwait(false);
        return chatById.Values.FirstOrDefault(c => c.Chat.SystemTag == Constants.Chat.SystemTags.Notes);
    }

    private async Task<IReadOnlyDictionary<ChatId, ChatInfo>> AddUnlistedSelectedChat(
        IReadOnlyDictionary<ChatId, ChatInfo> chatById, CancellationToken cancellationToken)
    {
        if (!await _isSelectedChatUnlisted.Use(cancellationToken).ConfigureAwait(false))
            return chatById;

        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (selectedChatId?.Kind == ChatKind.Place)
            return chatById;

        selectedChatId = await ChatUI.FixChatId(selectedChatId, cancellationToken).ConfigureAwait(false);
        var selectedChat = selectedChatId is null
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
        return await ComputeGatedUnreadChatCount(chatById, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Trimmed<int>> ComputeGatedUnreadChatCount(
        IReadOnlyDictionary<ChatId, ChatInfo> chatById,
        bool mustBeUnmuted,
        CancellationToken cancellationToken)
    {
        // Only the selected chat's badge can be gated off (IsReadingTail short-circuits on
        // IsSelected), so fold the raw per-chat counts and override that single chat - a
        // per-chat GetUnreadState fan-out would re-register O(N) dependencies per recompute.
        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        var gatedChatId = selectedChatId?.GetThreadOutermostParentOrSelf() ?? default;
        var count = 0;
        foreach (var (chatId, chatInfo) in chatById) {
            bool hasUnread;
            if (chatId == gatedChatId) {
                var unreadState = await ChatUI.GetUnreadState(chatId, cancellationToken).ConfigureAwait(false);
                hasUnread = mustBeUnmuted ? unreadState.HasUnmutedUnread : unreadState.Count > 0;
            }
            else
                hasUnread = mustBeUnmuted
                    ? chatInfo.UnmutedUnreadCount > 0 && chatInfo.UnreadCount > 0
                    : chatInfo.UnreadCount > 0;
            if (hasUnread)
                count++;
        }
        return new Trimmed<int>(
            Math.Min(count, ChatInfoExt.MaxUnreadChatCount),
            ChatInfoExt.MaxUnreadChatCount);
    }
}
