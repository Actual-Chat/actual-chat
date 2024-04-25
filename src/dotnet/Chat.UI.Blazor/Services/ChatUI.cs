using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualLab.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized, IAsyncDisposable
{
    private readonly SharedResourcePool<Symbol, ISyncedState<ReadPosition>> _readPositionStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly IStoredState<ChatId> _selectedChatId;
    private readonly IMutableState<PlaceId> _selectedPlaceId;
    private readonly IStoredState<IImmutableDictionary<PlaceId, ChatId>> _selectedChatIds;
    private readonly IMutableState<ChatEntryId> _highlightedEntryId;
    private readonly ISyncedState<UserNavbarSettings> _navbarSettings;
    private ChatId _searchEnabledChatId;
    private readonly object _lock = new();
    private readonly TaskCompletionSource _whenActivePlaceRestored = TaskCompletionSourceExt.New();
    private List<ChatId>? _pendingSelectedChatIdChanges = new ();

    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory => Hub.ChatMarkupHubFactory;
    private IUserPresences UserPresences => Hub.UserPresences;
    private IAccounts Accounts => Hub.Accounts;
    private IContacts Contacts => Hub.Contacts;
    private IChats Chats => Hub.Chats;
    private IPlaces Places => Hub.Places;
    private IChatPositions ChatPositions => Hub.ChatPositions;
    private IMentions Mentions => Hub.Mentions;
    private DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatEditorUI ChatEditorUI => Hub.ChatEditorUI;
    private ChatListUI ChatListUI => Hub.ChatListUI;
    private History History => Hub.History;
    private SelectionUI SelectionUI => Hub.SelectionUI;
    private KeepAwakeUI KeepAwakeUI => Hub.KeepAwakeUI;
    private TuneUI TuneUI => Hub.TuneUI;
    private ModalUI ModalUI => Hub.ModalUI;
    private AutoNavigationUI AutoNavigationUI => Hub.AutoNavigationUI;
    private UICommander UICommander => Hub.UICommander();
    private UIEventHub UIEventHub => Hub.UIEventHub();
    private NavbarUI NavbarUI { get; }

    public IState<ChatId> SelectedChatId => _selectedChatId;
    public IState<PlaceId> SelectedPlaceId => _selectedPlaceId;
    public IState<IImmutableDictionary<PlaceId, ChatId>> SelectedChatIds => _selectedChatIds;
    public IState<ChatEntryId> HighlightedEntryId => _highlightedEntryId;
    public IState<UserNavbarSettings> NavbarSettings => _navbarSettings;
    public Task WhenLoaded => _selectedChatId.WhenRead;
    public Task WhenActivePlaceRestored => _whenActivePlaceRestored.Task;

    public ChatUI(ChatUIHub hub) : base(hub)
    {
        NavbarUI = Hub.Services.GetRequiredService<NavbarUI>();
        NavbarUI.SelectedGroupChanged += NavbarUIOnSelectedGroupChanged;

        var type = GetType();
        _selectedChatId = StateFactory.NewKvasStored<ChatId>(new(LocalSettings, nameof(SelectedChatId)) {
            Corrector = FixSelectedChatId,
        });
        _selectedPlaceId = StateFactory.NewMutable(
            PlaceId.None,
            StateCategories.Get(type, nameof(SelectedPlaceId)));
        _selectedChatIds = StateFactory.NewKvasStored<IImmutableDictionary<PlaceId, ChatId>>(new (LocalSettings, nameof(SelectedChatIds)) {
            InitialValue = ImmutableDictionary<PlaceId, ChatId>.Empty
        });
        _highlightedEntryId = StateFactory.NewMutable(
            ChatEntryId.None,
            StateCategories.Get(type, nameof(HighlightedEntryId)));
        _navbarSettings = StateFactory.NewKvasSynced<UserNavbarSettings>(
            new (AccountSettings, UserNavbarSettings.KvasKey) {
                InitialValue = new UserNavbarSettings(),
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(NavbarSettings)),
            });
        Hub.RegisterDisposable(_navbarSettings);

        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readPositionStates = new SharedResourcePool<Symbol, ISyncedState<ReadPosition>>(CreateReadPositionState);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod(MinCacheDuration = 300, InvalidationDelay = 0.1)]
    public virtual async Task<ChatInfo?> Get(ChatId chatId, CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug("Get: {ChatId}", chatId.Value);
        if (chatId.IsNone)
            return null;

        var contact = await Contacts.GetForChat(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return null;

        var chatNewsTask = Chats.GetNews(Session, chatId, cancellationToken);
        var lastMentionTask = Mentions.GetLastOwn(Session, chatId, cancellationToken);
        var readEntryLidTask = GetReadEntryLid(chatId, cancellationToken);
        var userSettingsTask = AccountSettings.GetUserChatSettings(chatId, cancellationToken);

        var news = await chatNewsTask.ConfigureAwait(false);
        var userSettings = await userSettingsTask.ConfigureAwait(false);
        var lastMention = await lastMentionTask.ConfigureAwait(false);
        var readEntryLid = await readEntryLidTask.ConfigureAwait(false);
        var unreadCount = ComputeUnreadCount(chatId, news, readEntryLid);

        var hasUnreadMentions = false;
        if (userSettings.NotificationMode is not ChatNotificationMode.Muted) {
            var lastMentionEntryId = lastMention?.EntryId.LocalId ?? 0;
            hasUnreadMentions = lastMentionEntryId > readEntryLid;
        }

        var navbarSettings = await NavbarSettings.Use(cancellationToken).ConfigureAwait(false);

        var lastTextEntryText = "";
        if (news.LastTextEntry is { } lastTextEntry) {
            if (lastTextEntry.IsStreaming)
                lastTextEntryText = Constants.Messages.RecordingSkeleton;
            else {
                var chatMarkupHub = ChatMarkupHubFactory[chatId];
                var markup = await chatMarkupHub
                    .GetMarkup(lastTextEntry, MarkupConsumer.ChatListItemText, cancellationToken)
                    .ConfigureAwait(false);
                lastTextEntryText = markup.ToReadableText(MarkupConsumer.ChatListItemText);
            }
        }

        var result = new ChatInfo(contact) {
            News = news,
            UserSettings = userSettings,
            LastMention = lastMention,
            ReadEntryLid = readEntryLid,
            UnreadCount = unreadCount,
            HasUnreadMentions = hasUnreadMentions,
            LastTextEntryText = lastTextEntryText,
            IsPinnedToNavbar = navbarSettings.PinnedChats.Contains(chatId),
        };
        return result;
    }

    [ComputeMethod]
    public virtual async Task<ChatState?> GetState(
        ChatId chatId,
        bool withPresence,
        CancellationToken cancellationToken = default)
    {
        if (chatId.IsNone)
            return null;

        if (withPresence) {
            // Recursive call to get a part of state that prob. changes less frequently
            var state = await GetState(chatId, false, cancellationToken).ConfigureAwait(false);
            if (state == null)
                return null;

            var account = state.Contact.Account;
            if (account == null)
                return state;

            var presence = await UserPresences.Get(account.Id, cancellationToken).ConfigureAwait(false);
            return state with { Presence = presence };
        }

        var chat = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;

        var isSelected = await IsSelected(chatId).ConfigureAwait(false);
        var audioState = await ChatAudioUI.GetState(chatId).ConfigureAwait(false);
        return new(chat, audioState) {
            IsSelected = isSelected,
        };
    }

    [ComputeMethod] // Manually & automatically invalidated
    public virtual async Task<long> GetReadEntryLid(ChatId chatId, CancellationToken cancellationToken)
    {
        // NOTE(AY): This method uses LeaseReadPositionState in a bit tricky way:
        // on one hand, it can't depend on it, coz it disposes the lease, which means
        // computed it maintains might end up being never updated.
        // On another hand, it makes sense to read the most up-to-date read position,
        // so it returns max(leased read position, fetched read position).

        DebugLog?.LogDebug("GetReadEntryLid: {ChatId}", chatId);

        var serverReadPosition = await ChatPositions
            .GetOwn(Session, chatId, ChatPositionKind.Read, cancellationToken)
            .ConfigureAwait(false);

        using var _ = ComputeContext.BeginIsolation();
        using var readPositionState = await LeaseReadPositionState(chatId, cancellationToken).ConfigureAwait(false);
        var readPosition = readPositionState.Value;
        return MathExt.Max(readPosition.EntryLid, serverReadPosition.EntryLid);
    }

    [ComputeMethod] // Synced
    public virtual Task<bool> IsSelected(ChatId chatId)
        => Task.FromResult(!chatId.IsNone && SelectedChatId.Value == chatId);

    [ComputeMethod] // Synced
    public virtual Task<bool> IsSearchEnabled(ChatId chatId)
        => Task.FromResult(!chatId.IsNone && _searchEnabledChatId == chatId);

    public void EnableSearch(ChatId chatId)
    {
        var old = _searchEnabledChatId;
        _searchEnabledChatId = chatId;
        using (ComputeContext.BeginInvalidation()) {
            if (!old.IsNone)
                _ = IsSearchEnabled(old);
            if (!chatId.IsNone)
                _ = IsSearchEnabled(chatId);
        }
    }

    [ComputeMethod]
    public virtual async Task<Trimmed<int>> GetUnreadCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatInfo = await Get(chatId, cancellationToken).ConfigureAwait(false);
        return chatInfo?.UnreadCount ?? new();
    }

    [ComputeMethod]
    public virtual async Task<bool> IsEmpty(ChatId chatId, CancellationToken cancellationToken)
    {
        Computed<Range<long>> cIdRange;
        using (ComputeContext.BeginIsolation()) {
            cIdRange = await Computed
                .Capture(() => Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        var idRange = cIdRange.Value;
        if (idRange.End - idRange.Start >= 100) {
            // Heuristics, it may produce false negatives - e.g. if the chat was cleaned up,
            // but it's still better than to scan a lot. Prob better to implement an actual check
            // on the server side for this (keep the cached false intact until any removal happens).
            return false;
        }

        var reader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text);
        await foreach (var entry in reader.Read(idRange, cancellationToken).ConfigureAwait(false))
            if (!entry.IsSystemEntry)
                return false;
        return true;
    }

    // SetXxx & Add/RemoveXxx

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

    public void SetNavbarPinState(ChatId chatId, bool mustPin)
    {
        if (chatId.IsNone)
            return;

        var pinnedChats = NavbarSettings.Value.PinnedChats;
        var isPinned = pinnedChats.Contains(chatId);
        if (isPinned == mustPin)
            return;

        var newPinnedChats = mustPin
            ? pinnedChats.Add(chatId, true)
            : pinnedChats.RemoveAll(chatId);
        SetNavbarPinnedChats(newPinnedChats);
    }

    public void SetNavbarPinnedChats(IReadOnlyCollection<ChatId> pinnedChats)
        => _navbarSettings.Value = _navbarSettings.Value with { PinnedChats = pinnedChats.ToApiArray() };

    public void SetNavbarPlacesOrder(IReadOnlyCollection<PlaceId> places)
        => _navbarSettings.Value = _navbarSettings.Value with { PlacesOrder = places.ToApiArray() };

    public void LeaveChat(Chat chat)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(false, "chat",
            m => _ = LeaveChatInternal(chat, false, m)));

    public void DeleteChat(Chat chat)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(true, "chat",
            m => _ = LeaveChatInternal(chat, true, m)));

    public void DeletePlace(PlaceId placeId, Func<Task> onBeforeExecuteCommand)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(true, "place",
            m => _ = LeavePlaceInternal(placeId, true, onBeforeExecuteCommand, m)));

    public void LeavePlace(PlaceId placeId)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(false, "place",
            m => _ = LeavePlaceInternal(placeId, false, () => Task.CompletedTask, m)));

    private async Task LeaveChatInternal(Chat chat, bool isDelete, Modal modal)
    {
        if (!isDelete) {
            var isOwner = chat.Rules.IsOwner();
            if (isOwner) {
                var authorId = chat.Rules.Author?.Id ?? AuthorId.None;
                var ownerIds = await Hub.Roles.ListOwnerIds(Session, chat.Id, default).ConfigureAwait(true); // Continue on Blazor context.
                var hasAnotherOwner = ownerIds.Any(c => c != authorId);
                if (!hasAnotherOwner) {
                    const string message =
                        "You can't leave this chat because you are its only owner. Please add another chat owner first.";
                    UICommander.ShowError(StandardError.Constraint(message));
                }
            }
        }
        var isSelectedChat = chat.Id.Equals(SelectedChatId.Value);
        var command = isDelete
            ? (ICommand)new Chats_Change(Session, chat.Id, null, Change.Remove<ChatDiff>())
            : new Authors_Leave(Session, chat.Id);
        var result = await UICommander.Run(command).ConfigureAwait(true); // Continue on Blazor context
        if (result.HasError)
            return;
        modal.Close();
        if (isSelectedChat && (isDelete || !chat.IsPublic))
            _ = History.NavigateTo(Links.Chats);
    }

    private async Task LeavePlaceInternal(PlaceId placeId, bool isDelete, Func<Task> onBeforeExecuteCommand, Modal modal)
    {
        var isSelectedPlace = placeId.Equals(SelectedPlaceId.Value)
            || (NavbarUI.IsPlaceSelected(out var selectedPlaceId) && placeId == selectedPlaceId);
        modal.Close();
        await onBeforeExecuteCommand().ConfigureAwait(true);
        var command = isDelete
            ? (ICommand)new Places_Delete(Session, placeId)
            : new Places_Leave(Session, placeId);
        var result = await UICommander.Run(command).ConfigureAwait(true);
        if (result.HasError)
            return;
        if (isSelectedPlace)
            NavbarUI.SelectGroup(NavbarGroupIds.Chats, true);
    }

    // Helpers

    // This method fixes provided ChatId w/ PeerChatId.FixOwnerId, which replaces
    // a guest UserId there with OwnAccount.Id.
    // It must be used mainly in Navbar, which renders independently from ChatPage content,
    // because ChatPage fixes SelectedChatId anyway for any of its nested components.
    public async ValueTask<ChatId> FixChatId(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // Trying to do as many checks as we can before resorting to Accounts.GetOwn access
        if (!chatId.IsPeerChat(out var peerChatId) || !peerChatId.HasSingleNonGuestUserId(out _))
            return chatId;

        var owner = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        chatId = peerChatId.FixOwnerId(owner.Id);
        return chatId;
    }

    public bool SelectChatOnNavigation(ChatId chatId)
    {
        var hasChanged = SelectChatInternal(chatId);
        if (!chatId.IsNone || hasChanged)
            _ = SelectNavbarGroup(chatId).SuppressExceptions();
        return hasChanged;
    }

    public void HighlightEntry(ChatEntryId entryId, bool navigate, bool updateUI = true)
    {
        if (navigate)
            _ = UIEventHub.Publish(new NavigateToChatEntryEvent(entryId, true));
        else lock (Lock) {
            if (_highlightedEntryId.Value == entryId)
                return;

            _highlightedEntryId.Value = entryId;
        }
        if (updateUI)
            _ = UICommander.RunNothing();
    }

    public async ValueTask<SyncedStateLease<ReadPosition>> LeaseReadPositionState(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _readPositionStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        try {
            var result = new SyncedStateLease<ReadPosition>(lease);
            await result.WhenFirstTimeRead.WaitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch {
            lease.Dispose();
            throw;
        }
    }


    public async ValueTask DisposeAsync()
    {
        await _readPositionStates.DisposeAsync().ConfigureAwait(false);
        _navbarSettings.Dispose();
    }

    // Private methods

    private bool SelectChatInternal(ChatId chatId)
    {
        lock (Lock) {
            if (_selectedChatId.Value == chatId)
                return false;

            _selectedChatId.Value = chatId;
            SaveSelectedChatIds(chatId);
        }
        // The rest is done by InvalidateSelectedChatDependencies
        return true;
    }

    private bool SelectPlaceInternal(PlaceId placeId)
    {
        lock (_lock) {
            if (_selectedPlaceId.Value == placeId)
                return false;

            _selectedPlaceId.Value = placeId;
        }
        // The rest is done by SynchronizeSelectedChatIdAndActivePlaceId
        return true;
    }

    private async Task SelectNavbarGroup(ChatId chatId)
    {
        if (NavbarUI.IsPinnedChatSelected(out var pinnedChatId) && chatId.Equals(pinnedChatId))
            return;

        var isChatsSelected = NavbarUI.IsGroupSelected(NavbarGroupIds.Chats);
        var isPlaceSelected = NavbarUI.IsPlaceSelected(out var selectedPlaceId);
        var isPeerChat = chatId.Kind == ChatKind.Peer;
        var isChatPlaceSelected = chatId.IsPlaceChat
            && isPlaceSelected
            && selectedPlaceId.Equals(chatId.PlaceId);
        if (!isChatsSelected && !(isPeerChat && isPlaceSelected) && !isChatPlaceSelected) {
            var navbarSettings = await NavbarSettings.Use().ConfigureAwait(false);
            if (navbarSettings.PinnedChats.Contains(chatId)) {
                Hub.NavbarUI.SelectGroup(chatId.GetNavbarGroupId(), false);
                return;
            }
        }

        var placeId = chatId.PlaceChatId.PlaceId;
        if (!placeId.IsNone) {
            var place = await Places.Get(Session, placeId, default).ConfigureAwait(true); // Continue on blazor context.
            var navbarGroupId = place != null ? placeId.GetNavbarGroupId() : NavbarGroupIds.Chats;
            NavbarUI.SelectGroup(navbarGroupId, false);
            return;
        }

        if (chatId.Kind == ChatKind.Peer &&
            !SelectedPlaceId.Value.IsNone &&
            OrdinalEquals(NavbarUI.SelectedGroupId, SelectedPlaceId.Value.GetNavbarGroupId())) {
            var listView = ChatListUI.ActiveChatListView.Value;
            // When peer chat is selected in url, we should keep selected place nav group if the chat belongs to
            // place chat list.
            if (listView != null && listView.PlaceId == SelectedPlaceId.Value) {
                var settings = await listView.GetSettings().ConfigureAwait(false);
                // Peer chat can be included in the chat list only within People and None filters.
                if (settings.Filter == ChatListFilter.People || settings.Filter == ChatListFilter.None) {
                    // Check if peer chat was shown for place chat list view
                    var chats = await ChatListUI.ListAllUnordered(SelectedPlaceId.Value).ConfigureAwait(false);
                    if (chats.ContainsKey(chatId))
                        return; // Keep selected group
                }
            }
        }

        NavbarUI.SelectGroup(NavbarGroupIds.Chats, false);
    }

    private async ValueTask<ChatId> FixSelectedChatId(ChatId chatId, CancellationToken cancellationToken = default)
    {
        chatId = await FixChatId(chatId, cancellationToken).ConfigureAwait(false);
        return chatId.IsNone ? Constants.Chat.AnnouncementsChatId : chatId;
    }

    // Not compute method!
    private static Trimmed<int> ComputeUnreadCount(ChatId chatId, ChatNews chatNews, long readEntryLid)
    {
        var unreadCount = 0;
        if ((readEntryLid > 0 || (chatId.IsPeerChat(out _) && readEntryLid == 0)) && !chatNews.IsNone) {
            // Otherwise the chat wasn't ever opened
            var lastId = chatNews.TextEntryIdRange.End - 1;
            unreadCount = (int)(lastId - readEntryLid).Clamp(0, ChatInfo.MaxUnreadCount);
        }
        return new Trimmed<int>(unreadCount, ChatInfo.MaxUnreadCount);
    }

    private Task<ISyncedState<ReadPosition>> CreateReadPositionState(Symbol chatId, CancellationToken cancellationToken)
    {
        var pChatId = new ChatId(chatId, ParseOrNone.Option);

        // Commander use here is intended: this "action" shouldn't be counted as user action
        var writeDebouncer = new Debouncer<ICommand>(
            TimeSpan.FromSeconds(1),
            command => Commander.Run(command, CancellationToken.None));

        return Task.FromResult(StateFactory.NewCustomSynced<ReadPosition>(
            new (
                // Reader
                async ct => {
                    if (pChatId.IsNone)
                        return ReadPosition.None;

                    var (entryLid, origin) = await ChatPositions.GetOwn(Session, pChatId, ChatPositionKind.Read, ct).ConfigureAwait(false);
                    return new ReadPosition(pChatId, entryLid, origin);
                },
                // Writer
                (position, ct) => {
                    if (pChatId.IsNone || position == null!)
                        return Task.CompletedTask;

                    if (position.ChatId != pChatId) {
                        Log.LogWarning(
                            $"{nameof(CreateReadPositionState)}.Write: expected ChatId={{ChatId}}, but received {{ActualChatId}}",
                            pChatId,
                            position.ChatId);
                        return Task.CompletedTask;
                    }

                    var command = new ChatPositions_Set(Session, pChatId, ChatPositionKind.Read, new ChatPosition(position.EntryLid, position.Origin));
                    writeDebouncer.Throttle(command);

                    var cReadEntryLid = Computed.GetExisting(() => GetReadEntryLid(pChatId, default));
                    // Conditions:
                    // - No computed -> nothing to invalidate
                    // - No value (error) -> invalidate
                    // - Value < current -> invalidate
                    if (cReadEntryLid?.IsConsistent() == true && (!cReadEntryLid.IsValue(out var entryLid) || entryLid < position.EntryLid))
                        cReadEntryLid.Invalidate();

                    return Task.CompletedTask;
                }) {
                InitialValue = ReadPosition.GetInitial(pChatId),
                UpdateDelayer = _readStateUpdateDelayer,
                Category = StateCategories.Get(GetType(), nameof(ChatPositions), "[*]"),
            }
        ));
    }

    private void SaveSelectedChatIds(ChatId chatId)
    {
        if (chatId.IsNone)
            return;
        if (chatId == SpecialChat.NoChatSelected.Id)
            return;

        // Is executing under _lock;
        if (_pendingSelectedChatIdChanges != null) {
            // Postpone _selectedChatIds update till _selectedChatIds is read.
            _pendingSelectedChatIdChanges.Add(chatId);
            return;
        }
        _selectedChatIds.Value = SetItem(_selectedChatIds.Value, chatId);
    }

    private static IImmutableDictionary<PlaceId, ChatId> SetItem(IImmutableDictionary<PlaceId, ChatId> selectedChatIds, ChatId chatId)
        => selectedChatIds.SetItem(chatId.PlaceId, chatId);

    private void NavbarUIOnSelectedGroupChanged(object? sender, NavbarGroupChangedEventArgs e)
    {
        var placeId = PlaceId.None;
        var isChats = OrdinalEquals(NavbarUI.SelectedGroupId, NavbarGroupIds.Chats)|| NavbarUI.IsPlaceSelected(out placeId);
        if (NavbarUI.IsPinnedChatSelected(out var pinnedChatId)) {
            isChats = true;
            placeId = pinnedChatId.PlaceId;
        }

        if (!isChats)
            return;

        SelectPlaceInternal(placeId);
        if (!pinnedChatId.IsNone)
            SelectChatInternal(pinnedChatId);

        if (!e.IsUserAction)
            return;

        _ = DisplayLastUsedChat();

        async Task DisplayLastUsedChat(CancellationToken cancellationToken = default)
        {
            try {
                var lastSelectedChatId = await GetChatIdToDisplay(cancellationToken)
                    .ConfigureAwait(true); // Continue on the Blazor Dispatcher

                if (lastSelectedChatId == ChatId.None)
                    DebugLog?.LogDebug("ResetSelectedChatOnPlaceChange");
                else
                    DebugLog?.LogDebug("Restoring SelectedChatOnPlace. ChatId: '{ChatId}'", lastSelectedChatId);

                SelectChatInternal(lastSelectedChatId);
                if (Hub.PanelsUI.IsWide()) {
                    // Do not navigate on narrow screen to prevent hiding panels
                    // Navigate to selected chat only after delay to make ChatLists update smoother.
                    await Task.Delay(500, default).ConfigureAwait(true); // Continue on the Blazor Dispatcher
                    if (SelectedChatId.Value == lastSelectedChatId) {
                        var mustReplace = History.LocalUrl.IsChat();
                        await History.NavigateTo(Links.Chat(lastSelectedChatId), mustReplace).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) {
                Log.LogError(ex, "DisplayLastUsedChat failed");
            }
        }

        async Task<ChatId> GetChatIdToDisplay(CancellationToken cancellationToken)
        {
            var selectedChatIds = SelectedChatIds.Value;
            if (!selectedChatIds.TryGetValue(placeId, out var lastSelectedChatId)) {
                var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
                if (contactIds.Count > 0)
                    lastSelectedChatId = contactIds[0].ChatId;
            }
            Chat? readChat = null;
            if (!lastSelectedChatId.IsNone)
                readChat = await Chats.Get(Session, lastSelectedChatId, cancellationToken)
                    .ConfigureAwait(false);
            if (readChat == null)
                lastSelectedChatId = !placeId.IsNone ? ChatId.None : Constants.Chat.AnnouncementsChatId;
            return lastSelectedChatId;
        }
    }
}
