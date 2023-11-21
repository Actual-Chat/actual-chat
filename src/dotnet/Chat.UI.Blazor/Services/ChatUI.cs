using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI : ScopedWorkerBase<ChatUIHub>, IComputeService, INotifyInitialized
{
    private readonly SharedResourcePool<Symbol, ISyncedState<ReadPosition>> _readPositionStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly IStoredState<ChatId> _selectedChatId;
    private readonly IMutableState<PlaceId> _selectedPlaceId;
    private readonly IStoredState<IImmutableDictionary<PlaceId, ChatId>> _selectedChatIds;
    private readonly IMutableState<ChatEntryId> _highlightedEntryId;
    private ChatId _searchEnabledChatId;
    private readonly object _lock = new();
    private readonly TaskCompletionSource _whenActivePlaceRestored = TaskCompletionSourceExt.New();
    private List<ChatId>? _pendingSelectedChatIdChanges = new ();

    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory => Hub.ChatMarkupHubFactory;
    private IUserPresences UserPresences => Hub.UserPresences;
    private IAccounts Accounts => Hub.Accounts;
    private IContacts Contacts => Hub.Contacts;
    private IChats Chats => Hub.Chats;
    private IChatPositions ChatPositions => Hub.ChatPositions;
    private IMentions Mentions => Hub.Mentions;
    private TimeZoneConverter TimeZoneConverter => Hub.TimeZoneConverter;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatEditorUI ChatEditorUI => Hub.ChatEditorUI;
    private SelectionUI SelectionUI => Hub.SelectionUI;
    private KeepAwakeUI KeepAwakeUI => Hub.KeepAwakeUI;
    private TuneUI TuneUI => Hub.TuneUI;
    private AutoNavigationUI AutoNavigationUI => Hub.AutoNavigationUI;
    private UICommander UICommander => Hub.UICommander();
    private UIEventHub UIEventHub => Hub.UIEventHub();
    private NavbarUI NavbarUI { get; }

    public IState<ChatId> SelectedChatId => _selectedChatId;
    public IState<PlaceId> SelectedPlaceId => _selectedPlaceId;
    public IState<IImmutableDictionary<PlaceId, ChatId>> SelectedChatIds => _selectedChatIds;
    public IState<ChatEntryId> HighlightedEntryId => _highlightedEntryId;
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
        _selectedPlaceId = Services.StateFactory().NewMutable(
            PlaceId.None,
            StateCategories.Get(type, nameof(SelectedPlaceId)));
        _selectedChatIds = StateFactory.NewKvasStored<IImmutableDictionary<PlaceId, ChatId>>(new (LocalSettings, nameof(SelectedChatIds)) {
            InitialValue = ImmutableDictionary<PlaceId, ChatId>.Empty
        });
        _highlightedEntryId = StateFactory.NewMutable(
            ChatEntryId.None,
            StateCategories.Get(type, nameof(HighlightedEntryId)));

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
        var unreadCount = ComputeUnreadCount(news, readEntryLid);

        var hasUnreadMentions = false;
        if (userSettings.NotificationMode is not ChatNotificationMode.Muted) {
            var lastMentionEntryId = lastMention?.EntryId.LocalId ?? 0;
            hasUnreadMentions = lastMentionEntryId > readEntryLid;
        }

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

        using var _ = Computed.SuspendDependencyCapture();
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
        using (Computed.Invalidate()) {
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
        using (Computed.SuspendDependencyCapture()) {
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

    public bool SelectChat(ChatId chatId)
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

    public bool SelectPlace(PlaceId placeId)
    {
        lock (_lock) {
            if (_selectedPlaceId.Value == placeId)
                return false;

            _selectedPlaceId.Value = placeId;
        }
        // The rest is done by SynchronizeSelectedChatIdAndActivePlaceId
        return true;
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

    // Private methods

    private async ValueTask<ChatId> FixSelectedChatId(ChatId chatId, CancellationToken cancellationToken = default)
    {
        chatId = await FixChatId(chatId, cancellationToken).ConfigureAwait(false);
        return chatId.IsNone ? Constants.Chat.AnnouncementsChatId : chatId;
    }

    // Not compute method!
    private static Trimmed<int> ComputeUnreadCount(ChatNews chatNews, long readEntryLid)
    {
        var unreadCount = 0;
        if (readEntryLid > 0 && !chatNews.IsNone) {
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
        // Is executing under _lock;
        if (_pendingSelectedChatIdChanges != null) {
            // Postpone _selectedChatIds update till _selectedChatIds is read.
            _pendingSelectedChatIdChanges.Add(chatId);
            return;
        }
        _selectedChatIds.Value = SetItem(_selectedChatIds.Value, chatId);
    }

    private IImmutableDictionary<PlaceId, ChatId> SetItem(IImmutableDictionary<PlaceId, ChatId> selectedChatIds, ChatId chatId)
    {
        chatId.IsPlaceChat(out var placeChatId);
        return selectedChatIds.SetItem(placeChatId.PlaceId, chatId);
    }

    private void NavbarUIOnSelectedGroupChanged(object? sender, EventArgs e)
    {
        var placeId = PlaceId.None;
        if (NavbarUI.SelectedGroupId.OrdinalStartsWith(NavbarGroupIds.PlacePrefix)) {
            var sPlaceId = NavbarUI.SelectedGroupId.Substring(NavbarGroupIds.PlacePrefix.Length);
            placeId = new PlaceId(sPlaceId, AssumeValid.Option);
        }
        SelectPlace(placeId);
    }
}
