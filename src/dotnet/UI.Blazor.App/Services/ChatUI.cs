using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.Localization;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;
using MathExt = ActualLab.Mathematics.MathExt;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages chat selection, read positions, and chat state in the UI.
/// </summary>
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized, IAsyncDisposable
{
    private readonly SharedResourcePool<ChatId, SyncedState<ReadPosition>> _readPositionStates;
    private readonly SharedResourcePool<ChatId, MutableState<ReadPosition>> _viewPositionStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly StoredState<ChatId?> _selectedChatId;
    private readonly MutableState<ChatViewItemVisibility> _itemVisibility;
    private readonly MutableState<PlaceId?> _selectedPlaceId;
    private readonly StoredState<string> _selectedNavbarGroupId;
    private readonly StoredState<IImmutableDictionary<string, ChatId>> _selectedChatIds;
    private readonly MutableState<ChatEntryId?> _highlightedEntryId;
    private readonly MutableState<IImmutableSet<ConversationId>> _conversationExpansionOverrides;
    private readonly MutableState<IImmutableSet<ConversationId>> _autoExpandedConversations;
    // Holds the selected chat's lids only - ClearAutoExpansionState drops it on every chat change
    private LidRangeSet? _witnessedLids;
    private readonly ConcurrentDictionary<ConversationId, Unit> _suppressedAutoExpansions = new();
    private int _autoExpansionEpoch;
    private ChatId? _searchEnabledChatId;
    private List<ChatId>? _pendingSelectedChatIds = new();

    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory => Hub.ChatMarkupHubFactory;
    private IUserPresences UserPresences => Hub.UserPresences;
    private IAccounts Accounts => Hub.Accounts;
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;
    private UserActivityUI UserActivityUI => Hub.UserActivityUI;
    private NotificationsUI NotificationsUI => Hub.NotificationsUI;
    private IAvatars Avatars => Hub.Avatars;
    private IAuthors Authors => Hub.Authors;
    private IContacts Contacts => Hub.Contacts;
    private IChats Chats => Hub.Chats;
    private IChatThreads ChatThreads => Hub.ChatThreads;
    private IConversations Conversations => Hub.Conversations;
    private IPlaces Places => Hub.Places;
    private IChatPositions ChatPositions => Hub.ChatPositions;
    private IMentions Mentions => Hub.Mentions;
    private ActiveChatsUI ActiveChatsUI => Hub.ActiveChatsUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatEditorUI ChatEditorUI => Hub.ChatEditorUI;
    private ChatListUI ChatListUI => Hub.ChatListUI;
    private LocationUI LocationUI => Hub.LocationUI;
    private SelectionUI SelectionUI => Hub.SelectionUI;
    private KeepAwakeUI KeepAwakeUI => Hub.KeepAwakeUI;
    private LanguageUI LanguageUI => Hub.LanguageUI;
    private AutoNavigationUI AutoNavigationUI => Hub.AutoNavigationUI;
    private NavbarUI NavbarUI { get; }

    public IState<ChatId?> SelectedChatId => _selectedChatId;
    public IState<PlaceId?> SelectedPlaceId => _selectedPlaceId;
    public IState<IImmutableDictionary<string, ChatId>> SelectedChatIds => _selectedChatIds;
    public IState<ChatEntryId?> HighlightedEntryId => _highlightedEntryId;
    public IState<IImmutableSet<ConversationId>> ConversationExpansionOverrides => _conversationExpansionOverrides;
    public IState<IImmutableSet<ConversationId>> AutoExpandedConversations => _autoExpandedConversations;
    public Task WhenReady => _selectedChatId.WhenRead;
    public IState<ChatViewItemVisibility> ItemVisibility => _itemVisibility;

    public static event Action<(ChatId, long)> OnReadPositionUpdated = _ => { };

    public ChatUI(AppUIHub hub) : base(hub)
    {
        NavbarUI = Hub.Services.GetRequiredService<NavbarUI>();
        NavbarUI.SelectedGroupChanged += NavbarUIOnSelectedGroupChanged;

        var type = GetType();
        _selectedChatId = StateFactory.NewKvasStored<ChatId?>(
            new(LocalSettings, nameof(SelectedChatId)) {
                Corrector = FixSelectedChatId,
            });
        _selectedPlaceId = StateFactory.NewMutable(
            (PlaceId?)null,
            StateCategories.Get(type, nameof(SelectedPlaceId)));
        _selectedNavbarGroupId = StateFactory.NewKvasStored<string>(
            new (LocalSettings, "SelectedNavbarGroupId") {
                InitialValue = "",
            });
        _selectedChatIds = StateFactory.NewKvasStored<IImmutableDictionary<string, ChatId>>(
            new (LocalSettings, nameof(SelectedChatIds)) {
                InitialValue = ImmutableDictionary<string, ChatId>.Empty,
            });
        _highlightedEntryId = StateFactory.NewMutable(
            (ChatEntryId?)null,
            StateCategories.Get(type, nameof(HighlightedEntryId)));
        _conversationExpansionOverrides = StateFactory.NewMutable(
            (IImmutableSet<ConversationId>)ImmutableHashSet<ConversationId>.Empty,
            StateCategories.Get(type, nameof(ConversationExpansionOverrides)));
        _autoExpandedConversations = StateFactory.NewMutable(
            (IImmutableSet<ConversationId>)ImmutableHashSet<ConversationId>.Empty,
            StateCategories.Get(type, nameof(AutoExpandedConversations)));
        _itemVisibility = StateFactory.NewMutable(
            ChatViewItemVisibility.Empty,
            StateCategories.Get(type, nameof(ItemVisibility)));
        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readPositionStates = new SharedResourcePool<ChatId, SyncedState<ReadPosition>>(CreateReadPositionState);
        _viewPositionStates = new SharedResourcePool<ChatId, MutableState<ReadPosition>>(CreateViewPositionState);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod(MinCacheDuration = 300)]
    public virtual async Task<ChatInfo?> Get(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // DebugLog?.LogDebug("Get({ChatId})", chatId.Value);
        if (_readPositionStates.IsDisposed)
            return null;

        var contact = await Contacts.GetForChat(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return null;

        try {
            var chatNewsTask = Chats.GetNews(Session, chatId, cancellationToken);
            var lastMentionTask = Mentions.GetLastOwn(Session, chatId, cancellationToken);
            var readEntryLidTask = GetReadEntryLid(chatId, cancellationToken);
            var chatUserSettingsTask = UserSettingsUI.ChatUserSettings(chatId).Get(cancellationToken);

            var news = await chatNewsTask.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            var chatUserSettings = await chatUserSettingsTask.ConfigureAwait(false);
            var lastMention = await lastMentionTask.ConfigureAwait(false);
            var readEntryLid = await readEntryLidTask.ConfigureAwait(false);
            var unreadCount = ComputeUnreadCount(chatId, news, readEntryLid);

            var hasUnreadMentions = false;
            if (lastMention is { } mention && chatUserSettings.NotificationMode is not ChatNotificationMode.Muted)
                hasUnreadMentions = mention.EntryId.LocalId > readEntryLid;

            var navbarSettings = await NavbarUI.Settings.Use(cancellationToken).ConfigureAwait(false);

            var result = new ChatInfo(contact) {
                News = news,
                ChatUserSettings = chatUserSettings,
                LastMention = lastMention,
                ReadEntryLid = readEntryLid,
                UnreadCount = unreadCount,
                HasUnreadMentions = hasUnreadMentions,
                IsPinnedToNavbar = navbarSettings.PinnedChats.Contains(chatId),
            };
            return result;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // A disposed pool means this ChatUI's circuit is gone, so its computed graph is dead:
            // fail terminally instead of logging + rethrowing on every recompute.
            if (_readPositionStates.IsDisposed)
                return null;

            Log.LogError(e, "Get({ChatId}) failed", chatId.Value);
            throw;
        }
    }

    [ComputeMethod(MinCacheDuration = 300)]
    public virtual async Task<ChatPreview> GetPreview(ChatId chatId, CancellationToken cancellationToken = default)
    {
        // Deliberately not a part of Get(): this is per-visible-row work, and keeping it out
        // also stops a preview change from invalidating the whole chat list.
        var news = await Chats.GetNews(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (news?.LastTextEntry is not { } lastTextEntry)
            return ChatPreview.None;
        if (lastTextEntry.IsContentStreaming)
            return new ChatPreview { Text = Constants.Messages.RecordingSkeleton };

        if (lastTextEntry.IsThreadStart) {
            var threadChatId = lastTextEntry.ChatId.CreateThreadId(lastTextEntry.LocalId);
            var threadChatTask = Chats.Get(Session, threadChatId, cancellationToken);
            var threadCreatorTask = ChatThreads.GetThreadCreator(Session, threadChatId, cancellationToken);
            var threadChat = await threadChatTask.ConfigureAwait(false);
            var threadCreator = await threadCreatorTask.ConfigureAwait(false);
            return new ChatPreview {
                Text = threadChat is not null ? L.ChatList_Thread_Format(threadChat.Title) : "",
                Thread = threadChat,
                ThreadCreator = threadCreator,
            };
        }

        if (lastTextEntry is { HasLocation: true, LocationId: { } locationId }) {
            var isOneTime = await LocationUI.IsOneTime(chatId, locationId, cancellationToken).ConfigureAwait(false);
            return new ChatPreview { Text = isOneTime ? L.ChatList_SentLocation : L.ChatList_SharedLiveLocation };
        }

        var emoji = Emojis.TryGetByIdOrSymbol(lastTextEntry.Content.Trim());
        if (emoji is not null)
            return new ChatPreview { Text = emoji.Symbol };

        var chatMarkupHub = ChatMarkupHubFactory[chatId];
        var markup = await chatMarkupHub
            .GetMarkup(lastTextEntry, MarkupConsumer.ChatListItemText, cancellationToken)
            .ConfigureAwait(false);
        return new ChatPreview { Text = markup.ToReadableText(MarkupConsumer.ChatListItemText) };
    }

    [ComputeMethod]
    public virtual async Task<ChatState?> GetState(
        ChatId chatId,
        bool withPresence,
        CancellationToken cancellationToken = default)
    {
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

    // Consolidated: the local lease usually already holds the position the server is catching up to,
    // so the invalidation that follows our own ChatPositions_Set recomputes to the same lid.
    [ComputeMethod(ConsolidationDelay = 0.2)] // Manually & automatically invalidated
    public virtual async Task<long> GetReadEntryLid(ChatId chatId, CancellationToken cancellationToken)
    {
        // Notes chat should always appear as fully read
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat?.HasSingleAuthor == true)
            return long.MaxValue;

        // NOTE(AY): This method uses LeaseReadPositionState in a bit tricky way:
        // on the one hand, it can't depend on it, coz it disposes the lease, which means
        // computed it maintains might end up being never updated.
        // On the other hand, it makes sense to read the most up-to-date read position,
        // so it returns max(leased read position, fetched read position).

        // DebugLog?.LogDebug("GetReadEntryLid: {ChatId}", chatId);

        var serverReadPosition = await ChatPositions
            .GetOwn(Session, chatId, ChatPositionKind.Read, cancellationToken)
            .ConfigureAwait(false);

        using var _ = Computed.BeginIsolation();
        using var lease = await LeaseReadPositionState(chatId, cancellationToken).ConfigureAwait(false);
        var readPosition = lease.Resource.Value;
        return MathExt.Max(readPosition.EntryLid, serverReadPosition.EntryLid);
    }

    [ComputeMethod] // Synced
    public virtual Task<bool> IsSelected(ChatId chatId)
        => Task.FromResult(SelectedChatId.Value?.GetThreadOutermostParentOrSelf() == chatId.GetThreadOutermostParentOrSelf());

    [ComputeMethod] // Synced
    public virtual Task<bool> IsSearchEnabled(ChatId chatId)
        => Task.FromResult(_searchEnabledChatId == chatId);

    public void EnableSearch(ChatId? chatId)
    {
        var oldChatId = _searchEnabledChatId;
        if (oldChatId == chatId)
            return;

        _searchEnabledChatId = chatId;
        using (Invalidation.Begin()) {
            if (oldChatId is not null)
                _ = IsSearchEnabled(oldChatId);
            if (chatId is not null)
                _ = IsSearchEnabled(chatId);
        }
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<Trimmed<int>> GetUnreadCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var unreadState = await GetUnreadState(chatId, cancellationToken).ConfigureAwait(false);
        return unreadState.Count;
    }

    [ComputeMethod(ConsolidationDelay = 0.3)]
    public virtual async Task<ChatUnreadState> GetUnreadState(ChatId chatId, CancellationToken cancellationToken)
    {
        // Gated and consolidated because the raw count blinks: an incoming message raises it, then the
        // read position catches up ~1.6s later and drops it back. ChatInfo can carry neither - it holds
        // a referentially compared ChatEntry, and depending on ItemVisibility there would make every
        // scroll invalidate the whole row.
        var chatInfo = await Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chatInfo == null)
            return default;

        var isReadingTail = await IsReadingTail(chatId, cancellationToken).ConfigureAwait(false);
        if (isReadingTail)
            return default;

        var reactionState = await NotificationsUI.GetReactionState(chatId, cancellationToken).ConfigureAwait(false);
        return new ChatUnreadState(
            chatInfo.UnreadCount,
            chatInfo.HasUnreadOwnMention,
            chatInfo.UnmutedUnreadCount > 0 && chatInfo.UnreadCount > 0,
            reactionState.Emoji);
    }

    // Consolidated so streaming-expansion flaps of the end anchor (pushed out, then restored by the
    // sticky-edge scroll-back) don't propagate: the wrapper serves the old value during the window
    // and recomputes once after it - only a genuine pin/unpin changes the result.
    [ComputeMethod(ConsolidationDelay = 0.25)]
    public virtual async Task<bool> IsReadingTail(ChatId chatId, CancellationToken cancellationToken)
    {
        // Must stay in sync with what actually advances the read position (ChatView) - suppressing
        // the unread badge for a chat whose read position isn't moving would hide real unread messages.
        if (!await IsSelected(chatId).ConfigureAwait(false))
            return false;

        var itemVisibility = await ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        if (itemVisibility.ChatId != chatId || !itemVisibility.IsPinnedToEnd)
            return false;

        var lastPresentAt = await UserActivityUI.LastPresentAt.Use(cancellationToken).ConfigureAwait(false);
        var readingUntil = lastPresentAt + Constants.Chat.ReadingGracePeriod;
        var now = Clocks.CpuClock.Now;
        if (readingUntil <= now)
            return false;

        Computed.GetCurrent().Invalidate(readingUntil - now);
        return true;
    }

    // The non-reactive half of IsReadingTail, for the JS-driven callbacks that can't await:
    // the document is visible and the user interacted recently enough to still be at the screen.
    public bool IsUserPresent()
        => UserActivityUI.LastPresentAt.Value + Constants.Chat.ReadingGracePeriod > Clocks.CpuClock.Now;

    [ComputeMethod(ConsolidationDelay = 0.3)]
    public virtual async Task<bool> IsUnreadByOthers(
        ChatEntryId entryId,
        AuthorId ownAuthorId,
        CancellationToken cancellationToken)
    {
        // Consolidated: every message in the chat rewrites GetReadPositionsStat, but the answer for one
        // entry changes at most once - when someone else's read position passes it. It stays false for
        // messages older than the tracking start, which the stat can't judge.
        var readPositionsStat = await Chats
            .GetReadPositionsStat(Session, entryId.ChatId, cancellationToken)
            .ConfigureAwait(false);
        return readPositionsStat.CanCalculateHasReadByAnotherAuthor(entryId)
            && !readPositionsStat.HasReadByAnotherAuthor(entryId, ownAuthorId);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsEmpty(ChatId chatId, CancellationToken cancellationToken)
    {
        Computed<Range<long>> cIdRange;
        using (Computed.BeginIsolation()) {
            cIdRange = await Computed
                .Capture(() => Chats.GetIdRange(Session, chatId, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        if (cIdRange.HasError)
            return false; // It's fine for this method

        var idRange = cIdRange.Value;
        if (idRange.End - idRange.Start >= 100) {
            // Heuristics, it may produce false negatives - e.g. if the chat was cleaned up,
            // but it's still better than to scan a lot. Prob better to implement an actual check
            // on the server side for this (keep the cached false intact until any removal happens).
            return false;
        }

        var reader = Chats.NewEntryReader(Session, chatId);
        await foreach (var entry in reader.Read(idRange, cancellationToken).ConfigureAwait(false))
            if (!entry.IsSystemEntry)
                return false;
        return true;
    }

    // SetXxx & Add/RemoveXxx

    public void SetItemVisibility(ChatViewItemVisibility itemVisibility)
        => _itemVisibility.Value = itemVisibility;

    public void ResetItemVisibility(ChatId chatId)
    {
        // Chat views overlap during navigation, so a disposing view must not clear its successor's
        // visibility - only the view that published the current value may retract it.
        lock (Lock) {
            if (_itemVisibility.Value.ChatId == chatId)
                _itemVisibility.Value = ChatViewItemVisibility.Empty;
        }
    }

    public void LeaveChat(Chat.Chat chat)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(false, LeaveChatConfirmationModal.TargetKind.Chat,
            m => _ = DeleteOrLeaveChatInternal(chat, false, m)));

    public void DeleteChat(Chat.Chat chat)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(true, LeaveChatConfirmationModal.TargetKind.Chat,
            m => _ = DeleteOrLeaveChatInternal(chat, true, m)));

    public void DeleteThread(Chat.Chat chat)
    {
        if (!chat.Id.IsThread())
            throw new ArgumentOutOfRangeException(nameof(chat), "Given chat should be a thread");

        _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(true,
            LeaveChatConfirmationModal.TargetKind.Thread,
            m => _ = DeleteOrLeaveChatInternal(chat, true, m)));
    }

    public void DeletePlace(PlaceId placeId, Func<Task> onBeforeExecuteCommand)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(true, LeaveChatConfirmationModal.TargetKind.Place,
            m => _ = DeleteOrLeavePlaceInternal(placeId, true, onBeforeExecuteCommand, m)));

    public void LeavePlace(PlaceId placeId)
        => _ = ModalUI.Show(new LeaveChatConfirmationModal.Model(false, LeaveChatConfirmationModal.TargetKind.Place,
            m => _ = DeleteOrLeavePlaceInternal(placeId, false, () => Task.CompletedTask, m)));

    public void ArchiveChat(Chat.Chat chat)
    {
        var warning = L.Chat_ArchiveWarning_Format(chat.Title);
        _ = ModalUI.Show(new ConfirmModal.Model(true,
            warning,
            () => _ = ArchiveChatInternal(chat.Id)) {
            Title = L.Chat_ArchiveTitle,
            ConfirmButtonText = L.Chat_Archive
        });
    }

    public async Task JoinPlace(PlaceId placeId) {
        var avatars = await Avatars.ListOwnAvatarIds(Session, default).ConfigureAwait(false); // Continue on Blazor context.
        var hasMultipleAvatars = avatars.Count > 1;

        if (!hasMultipleAvatars) {
            var command = new Places_Join { Session = Session, PlaceId = placeId };
            await UICommander.Run(command).ConfigureAwait(false);
            return;
        }

        await ModalUI.Show(new AvatarSelectModal.Model(null, false, JoinWithAvatar)).ConfigureAwait(false);

        async Task JoinWithAvatar(AvatarFull avatar) {
            var command = new Places_Join { Session = Session, PlaceId = placeId, AvatarId = avatar.Id };
            await UICommander.Run(command).ConfigureAwait(false);
        }
    }

    public void ToggleExpandConversation(ConversationId conversationId)
    {
        // A closed live-block overlay intercepts its own toggle: collapsing it is "dismiss the
        // frozen view", not an expansion override on the overlay's render id.
        if (Hub.LiveBlockUI.TryCollapseOverlay(conversationId))
            return;

        // ResetReveal takes LiveBlockUI's lock, so it stays outside this one - a ChatUI -> LiveBlockUI
        // lock edge would invert the one TryCollapseOverlay acquires in the other direction.
        lock (Lock) {
            var isAutoExpanded = SuppressAutoExpansion(conversationId);
            var overrides = _conversationExpansionOverrides.Value;
            var isOverridden = overrides.Contains(conversationId);
            // Membership here flips a conversation's expansion away from its IsExpandedByDefault, so the
            // toggle works regardless of which default the conversation carries. Dropping the auto entry
            // is the whole collapse only while nothing else expands it - the latched default is written
            // once by whoever sees the conversation first, so it can land as expanded after the rule
            // recorded the auto-expansion, and flipping then would re-expand what just collapsed.
            var isExpandedWithoutAuto =
                _knownConversationDefaultExpanded.GetValueOrDefault(conversationId) ^ isOverridden;
            if (!isAutoExpanded || isExpandedWithoutAuto)
                _conversationExpansionOverrides.Value = isOverridden
                    ? overrides.Remove(conversationId)
                    : overrides.Add(conversationId);
        }
        Hub.LiveBlockUI.ResetReveal(conversationId.ChatId);
    }

    public bool IsConversationExpanded(Conversation conversation)
    {
        // Read the latched default, never the record's: IsExpandedByDefault flips when a summary lands,
        // and a consumer still reading the record would disagree with everything keyed off the cache for
        // the rest of the session. Seeds it too, so whichever consumer sees the conversation first wins.
        var isExpandedByDefault = _knownConversationDefaultExpanded.GetOrAdd(
            conversation.Id,
            static (_, c) => c.IsExpandedByDefault,
            conversation);
        return (isExpandedByDefault ^ _conversationExpansionOverrides.Value.Contains(conversation.Id))
            || _autoExpandedConversations.Value.Contains(conversation.Id);
    }

    // This method fixes provided ChatId w/ PeerChatId.FixOwnerId, which replaces
    // a guest UserId there with OwnAccount.Id.
    // It must be used mainly in Navbar, which renders independently of ChatPage content,
    // because ChatPage fixes SelectedChatId anyway for any of its nested components.
    public async ValueTask<ChatId?> FixChatId(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        // Trying to do as many checks as we can before resorting to Accounts.GetOwn access
        if (chatId is not PeerChatId peerChatId || !peerChatId.HasSingleNonGuestUserId(out _))
            return chatId;

        var owner = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        chatId = peerChatId.FixOwnerId(owner.Id);
        return chatId;
    }

    public bool SelectChatOnNavigation(ChatId? chatId)
    {
        var hasChanged = SelectChatInternal(chatId);
        if (chatId is not null || hasChanged)
            _ = SelectNavbarGroup(chatId).SuppressExceptions();
        return hasChanged;
    }

    public void HighlightEntry(ChatEntryId? entryId, bool navigate, bool updateUI = true)
    {
        if (navigate) {
            if (entryId is null)
                throw StandardError.Constraint("Not null entry should be specified for navigate request.");
            _ = UIEventHub.Publish(new NavigateToChatEntryEvent(entryId, true));
        }
        else lock (Lock) {
            if (_highlightedEntryId.Value == entryId)
                return;

            _highlightedEntryId.Value = entryId;
        }
        if (updateUI)
            _ = UICommander.RunNothing();
    }

    public async ValueTask<SharedResourcePool<ChatId, SyncedState<ReadPosition>>.Lease> LeaseReadPositionState(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        ChatSwitchTracer.Mark("ChatUI.LeaseReadPositionState: Rent -> in", chatId);
        var lease = await _readPositionStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        try {
            var state = lease.Resource;
            ChatSwitchTracer.Mark("ChatUI.LeaseReadPositionState: Rent <- out",
                state.WhenFirstTimeRead.IsCompleted ? "already read (pool hit)" : "first read pending");
            await state.WhenFirstTimeRead.WaitAsync(cancellationToken).ConfigureAwait(false);
            ChatSwitchTracer.Mark("ChatUI.LeaseReadPositionState: WhenFirstTimeRead awaited");
            InvokeReadPositionUpdated(state);
            state.Updated += (s, stateEventKind) => {
                if (stateEventKind == StateEventKind.Updated)
                    InvokeReadPositionUpdated(s);
            };
            return lease;
        }
        catch {
            lease.Dispose();
            throw;
        }

        static void InvokeReadPositionUpdated(State state) {
            var value = ((IState<ReadPosition>)state).Value;
            OnReadPositionUpdated.Invoke((value.ChatId, value.EntryLid));
        }
    }

    public async ValueTask<SharedResourcePool<ChatId, MutableState<ReadPosition>>.Lease> LeaseViewPositionState(
        ChatId chatId,
        CancellationToken cancellationToken)
        => await _viewPositionStates.Rent(chatId, cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        await _readPositionStates.DisposeAsync().ConfigureAwait(false);
        await _viewPositionStates.DisposeAsync().ConfigureAwait(false);
    }

    // Protected/internal methods

    internal void EnsureConversationCollapsed(ConversationId conversationId, bool isExpandedByDefault)
    {
        SuppressAutoExpansion(conversationId);
        // The caller's flag is the template's close-time value; the effective state is computed from the
        // latched one, and keying the override off the wrong one leaves the block expanded.
        var latched = _knownConversationDefaultExpanded.GetOrAdd(conversationId, isExpandedByDefault);
        var overrides = _conversationExpansionOverrides.Value;
        _conversationExpansionOverrides.Value = latched
            ? overrides.Add(conversationId)
            : overrides.Remove(conversationId);
    }

    internal bool SuppressAutoExpansion(ConversationId conversationId)
    {
        // Returns whether an auto-expansion was dropped - for the toggle, that removal IS the collapse.
        // Called on its own for ids whose IsExpandedByDefault isn't knowable: a frozen block's render id
        // has no conversation behind it once materialized, so normalizing its override would expand it.
        _suppressedAutoExpansions[conversationId] = default;
        var autoExpanded = _autoExpandedConversations.Value;
        var isAutoExpanded = autoExpanded.Contains(conversationId);
        if (isAutoExpanded)
            _autoExpandedConversations.Value = autoExpanded.Remove(conversationId);
        return isAutoExpanded;
    }

    internal static List<ConversationId> GetNewAutoExpansions(
        ChatId chatId,
        IEnumerable<Range<long>> conversationLidRanges,
        IImmutableSet<ConversationId> defaultExpanded,
        IImmutableSet<ConversationId> overrides,
        IImmutableSet<ConversationId> autoExpanded,
        Func<ConversationId, bool> isSuppressed,
        LidRangeSet witnessedLids,
        ConversationId? liveBlockId,
        ConversationId? materializedBlockId)
    {
        // A conversation that appeared (or grew) over rows the user has actually seen this visit must
        // not swallow them in place; it auto-expands until the user leaves the chat. Live/materialized
        // block ids are excluded - the live overlay machinery owns their expansion. An id carrying a
        // manual override is excluded too: suppression dies with the visit but the override doesn't,
        // so without this an earlier visit's deliberate collapse would be undone by later range growth.
        var result = new List<ConversationId>();
        foreach (var range in conversationLidRanges) {
            var conversationId = ConversationId.New(chatId, range.Start);
            if (conversationId == liveBlockId || conversationId == materializedBlockId)
                continue;
            if (autoExpanded.Contains(conversationId)
                || isSuppressed(conversationId)
                || overrides.Contains(conversationId))
                continue;

            var isExpanded = defaultExpanded.Contains(conversationId) ^ overrides.Contains(conversationId);
            if (isExpanded)
                continue;

            if (witnessedLids.Intersects(range))
                result.Add(conversationId);
        }
        return result;
    }

    // Private methods

    private bool SelectChatInternal(ChatId? chatId)
    {
        var selectedChatId = _selectedChatId;
        lock (Lock) {
            if (selectedChatId.Value == chatId)
                return false;

            if (chatId is not null) {
                if (_pendingSelectedChatIds == null)
                    // Postpone _selectedChatIds update till _selectedChatIds is read.
                    _selectedChatIds.Value = _selectedChatIds.Value
                        .SetItem((chatId as PlaceChatId)?.PlaceId.Value ?? "", chatId);
                else
                    _pendingSelectedChatIds.Add(chatId);
            }
            ClearAutoExpansionState();
            selectedChatId.Value = chatId; // "Resumes" InvalidateSelectedChatDependencies, which does the rest
            return true;
        }
    }

    private void ClearAutoExpansionState()
    {
        // Bumped first, and before the wipes: an in-flight build snapshots the epoch at its start, so
        // moving it here is what tells that build its results belong to a visit that's already over -
        // including the leave-and-return-to-the-same-chat case, where a chat-id check still matches.
        Interlocked.Increment(ref _autoExpansionEpoch);
        // Released, not just written under Lock: the auto-expansion pass reads this field lock-free.
        Volatile.Write(ref _witnessedLids, null);
        _suppressedAutoExpansions.Clear();
        if (_autoExpandedConversations.Value.Count != 0)
            _autoExpandedConversations.Value = ImmutableHashSet<ConversationId>.Empty;
    }

    private bool SelectPlaceInternal(PlaceId? placeId)
    {
        var selectedPlaceId = _selectedPlaceId;
        lock (Lock) {
            if (selectedPlaceId.Value == placeId)
                return false;

            selectedPlaceId.Value = placeId; // "Resumes" SynchronizeSelectedChatIdAndActivePlaceId, which does the rest
            return true;
        }
    }

    private async Task SelectNavbarGroup(ChatId? chatId)
    {
        if (chatId is null) {
            NavbarUI.SelectGroup(NavbarGroupIds.Chats, false);
            return;
        }

        if (NavbarUI.IsPinnedChatSelected(out var pinnedChatId) && chatId.Equals(pinnedChatId))
            return;

        if (NavbarUI.IsGroupSelected(NavbarGroupIds.Unread))
            return; // Keep the Unread group so "Back" returns to the unread panel

        var isChatsSelected = NavbarUI.IsGroupSelected(NavbarGroupIds.Chats);
        var isPlaceSelected = NavbarUI.IsPlaceSelected(out var navbarSelectedPlaceId);
        var isPeerChat = chatId.Kind == ChatKind.Peer;
        var placeId = (chatId.GetThreadOutermostParentOrSelf() as PlaceChatId)?.PlaceId;
        var isChatPlaceSelected = placeId is not null
            && isPlaceSelected
            && Equals(navbarSelectedPlaceId, placeId);
        if (!isChatsSelected && !(isPeerChat && isPlaceSelected) && !isChatPlaceSelected) {
            var navbarSettings = await NavbarUI.Settings.Use().ConfigureAwait(false);
            if (navbarSettings.PinnedChats.Contains(chatId)) {
                Hub.NavbarUI.SelectGroup(chatId.GetNavbarGroupId(), false);
                return;
            }
        }

        if (placeId is not null) {
            var place = await Places.Get(Session, placeId, default).ConfigureAwait(true); // Continue on blazor context.
            var navbarGroupId = place != null ? placeId.GetNavbarGroupId() : NavbarGroupIds.Chats;
            NavbarUI.SelectGroup(navbarGroupId, false);
            return;
        }

        var selectedPlaceId = SelectedPlaceId.Value;
        if (chatId.Kind == ChatKind.Peer
            && selectedPlaceId is not null
            && NavbarUI.SelectedGroupId == selectedPlaceId.GetNavbarGroupId()) {
            var placeChatListSettings = ChatListUI.GetPlaceChatListSettings(selectedPlaceId);
            // When a peer chat is "selected" via URL, we should retain the selected place
            // nav group if we're on the "People" tab (or no tab is selected) and the peer is a member of this place
            var chatListSettings = await placeChatListSettings.Get().ConfigureAwait(false);
            if (chatListSettings.GetFilter() == ChatListFilter.People || chatListSettings.GetFilter() == ChatListFilter.None) {
                var chats = await ChatListUI.ListMembersOnly(selectedPlaceId, default).ConfigureAwait(false);
                if (chats.ContainsKey(chatId))
                    return; // Keep a selected group
            }
        }

        NavbarUI.SelectGroup(NavbarGroupIds.Chats, false);
    }

    private async ValueTask<ChatId?> FixSelectedChatId(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        chatId = await FixChatId(chatId, cancellationToken).ConfigureAwait(false);
        return chatId ?? Constants.Chat.AnnouncementsChatId;
    }

    // Not compute method!
    private static Trimmed<int> ComputeUnreadCount(ChatId chatId, ChatNews? chatNews, long readEntryLid)
    {
        // A negative lid is ChatPosition.None - nothing was ever stored, so the chat was never
        // opened and counting all of it as unread would flood the badge on first sight. A stored 0
        // is a real position: joining a chat that was empty at the time seeds exactly that, and
        // entry lids start at 1, so every message that arrives afterwards is genuinely unread.
        // Peer chats are exempt from the "never opened" rule - they have no join step to seed a
        // position, so a missing one just means this side of the conversation hasn't started.
        var unreadCount = 0;
        var hasReadPosition = readEntryLid >= 0 || chatId.Kind == ChatKind.Peer;
        if (chatNews is not null && hasReadPosition) {
            var lastId = chatNews.TextEntryLidRange.End - 1;
            unreadCount = (int)(lastId - Math.Max(0, readEntryLid)).Clamp(0, ChatInfo.MaxUnreadCount);
        }
        return new Trimmed<int>(unreadCount, ChatInfo.MaxUnreadCount);
    }

    private async Task<SyncedState<ReadPosition>> CreateReadPositionState(ChatId chatId, CancellationToken cancellationToken)
    {
        // Commander use here is intended: this "action" shouldn't be counted as user action
        var writeDebouncer = Debouncer.New<ICommand>(
            TimeSpan.FromSeconds(1),
            command => Commander.Run(command, CancellationToken.None));

        ChatSwitchTracer.Mark("ChatUI.CreateReadPositionState: Chats.Get -> in", chatId);
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        ChatSwitchTracer.Mark("ChatUI.CreateReadPositionState: Chats.Get <- out");
        var hasSingleAuthor = chat?.HasSingleAuthor == true;
        return StateFactory.NewCustomSynced<ReadPosition>(
            new (
                // Reader
                async ct => {
                    if (hasSingleAuthor) // Notes chat should always appear as fully read
                        return ReadPosition.NewFullyRead(chatId);

                    ChatSwitchTracer.Mark("ChatUI.ReadPosition.Read: ChatPositions.GetOwn -> in (SYNCHRONIZED)");
                    using var _ = ComputedSynchronizer.Default.Activate();
                    var (entryLid, origin) = await ChatPositions.GetOwn(Session, chatId, ChatPositionKind.Read, ct).ConfigureAwait(false);
                    ChatSwitchTracer.Mark("ChatUI.ReadPosition.Read: ChatPositions.GetOwn <- out", $"lid={entryLid}");
                    return new ReadPosition(chatId, entryLid, origin);
                },
                // Writer
                (position, ct) => {
                    if (ReferenceEquals(position, null) || hasSingleAuthor)
                        return Task.CompletedTask;

                    if (position.ChatId != chatId) {
                        Log.LogWarning(
                            $"{nameof(CreateReadPositionState)}.Write: expected ChatId={{ChatId}}, but received {{ActualChatId}}",
                            chatId,
                            position.ChatId);
                        return Task.CompletedTask;
                    }

                    var command = new ChatPositions_Set {
                        Session = Session,
                        ChatId = chatId,
                        Kind = ChatPositionKind.Read,
                        Position = new ChatPosition(position.EntryLid, position.Origin),
                    };
                    writeDebouncer.Throttle(command);

                    var cReadEntryLid = Computed.GetExisting(() => GetReadEntryLid(chatId, default));
                    // Conditions:
                    // - No computed -> nothing to invalidate
                    // - No value (error) -> invalidate
                    // - Value < current -> invalidate
                    if (cReadEntryLid?.IsConsistent() == true && (!cReadEntryLid.IsValue(out var entryLid) || entryLid < position.EntryLid))
                        cReadEntryLid.Invalidate();

                    return Task.CompletedTask;
                }) {
                InitialValue = ReadPosition.NewInitial(chatId),
                UpdateDelayer = _readStateUpdateDelayer,
                Category = StateCategories.Get(GetType(), nameof(ChatPositions), "[*]"),
            }
        );
    }

    private Task<MutableState<ReadPosition>> CreateViewPositionState(ChatId chatId, CancellationToken cancellationToken)
        => Task.FromResult(StateFactory.NewMutable(new ReadPosition(chatId, 0)));

    private void NavbarUIOnSelectedGroupChanged(object? sender, NavbarGroupChangedEventArgs e)
    {
        _selectedNavbarGroupId.Value = e.Id;
        var placeId = (PlaceId?)null;
        var isChatOrPlace = NavbarUI.SelectedGroupId == NavbarGroupIds.Chats
            || NavbarUI.IsPlaceSelected(out placeId);
        if (NavbarUI.IsPinnedChatSelected(out var pinnedChatId)) {
            isChatOrPlace = true;
            placeId = (pinnedChatId as PlaceChatId)?.PlaceId;
        }
        if (!isChatOrPlace)
            return;

        SelectPlaceInternal(placeId);
        if (!e.IsUserAction)
            return;

        if (pinnedChatId is not null)
            _ = NavigateToPinnedChat();
        else
            _ = SelectLastUsedChat();

        return;

        async Task NavigateToPinnedChat()
        {
            try {
                var mustReplace = History.LocalUrl.IsChat();
                await History.NavigateTo(Links.Chat(pinnedChatId), mustReplace).ConfigureAwait(true);
                PanelsUI.HidePanels();
            }
            catch (Exception ex) {
                Log.LogError(ex, "NavigateToPinnedChat failed");
            }
        }

        async Task SelectLastUsedChat(CancellationToken cancellationToken = default) {
            try {
                var lastSelectedChatId = await GetLastUsedChatId(cancellationToken).ConfigureAwait(true);
                DebugLog?.LogDebug(
                    "SelectLastUsedChat: PlaceId: {PlaceId} -> ChatId: {ChatId}",
                    placeId, lastSelectedChatId);

                // Navigation is the only writer of SelectedChatId, so restoring the selection means
                // navigating. On a narrow screen that would normally swap the chat list for the chat -
                // but the list is what the user just asked for, so the panels stay put there.
                if (lastSelectedChatId is null)
                    return;

                var link = Links.Chat(lastSelectedChatId);
                var panelsUI = Hub.PanelsUI;
                if (!panelsUI.IsWide())
                    panelsUI.KeepPanelsOn(link);

                var mustReplace = History.LocalUrl.IsChat();
                await History.NavigateTo(link, mustReplace).ConfigureAwait(false);
            }
            catch (Exception ex) {
                Log.LogError(ex, "SelectLastUsedChat failed");
            }
        }

        async Task<ChatId?> GetLastUsedChatId(CancellationToken cancellationToken)
        {
            var selectedChatIds = SelectedChatIds.Value;
            if (!selectedChatIds.TryGetValue(placeId?.Value ?? "", out var lastSelectedChatId)) {
                var contactIds = await Contacts.ListIds(Session, placeId, cancellationToken).ConfigureAwait(false);
                if (contactIds.Length > 0)
                    lastSelectedChatId = contactIds[0].ChatId;
            }
            Chat.Chat? readChat = null;
            if (lastSelectedChatId is not null)
                readChat = await Chats.Get(Session, lastSelectedChatId, cancellationToken)
                    .ConfigureAwait(false);
            if (readChat == null)
                lastSelectedChatId = placeId is not null
                    ? null
                    : Constants.Chat.AnnouncementsChatId;
            return lastSelectedChatId;
        }
    }

    private async Task DeleteOrLeaveChatInternal(Chat.Chat chat, bool isDelete, Modal modal)
    {
        if (!isDelete) {
            var isOwner = chat.Rules.IsOwner();
            if (isOwner) {
                var authorId = chat.Rules.Author?.Id;
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
            ? (ICommand)new Chats_Change {
                Session = Session,
                ChatId = chat.Id,
                ExpectedVersion = null,
                Change = Change.Remove<ChatDiff>(),
            }
            : new Authors_Leave { Session = Session, ChatId = chat.Id };
        var result = await UICommander.Run(command).ConfigureAwait(true); // Continue on Blazor context
        if (result.HasError)
            return;

        modal.Close();
        // If a chat was selected and we no longer can see a chat, navigate to another visible chat
        if (isSelectedChat && !(chat.IsPublic && !isDelete))
            _ = NavigateToVisibleChat((chat.Id as PlaceChatId)?.PlaceId).SuppressExceptions();
    }

    private async Task DeleteOrLeavePlaceInternal(PlaceId placeId, bool isDelete, Func<Task> onBeforeExecuteCommand, Modal modal)
    {
        var isSelectedPlace = placeId.Equals(SelectedPlaceId.Value)
            || (NavbarUI.IsPlaceSelected(out var selectedPlaceId) && placeId == selectedPlaceId);
        modal.Close();
        await onBeforeExecuteCommand().ConfigureAwait(true);
        var command = isDelete
            ? (ICommand)new Places_Change {
                Session = Session,
                PlaceId = placeId,
                ExpectedVersion = null,
                Change = Change.Remove<PlaceDiff>(),
            }
            : new Places_Leave { Session = Session, PlaceId = placeId };
        var result = await UICommander.Run(command).ConfigureAwait(true);
        if (result.HasError)
            return;
        if (isSelectedPlace)
            NavbarUI.SelectGroup(NavbarGroupIds.Chats, true);
    }

    private async Task ArchiveChatInternal(ChatId chatId)
    {
        var archiveCommand = new Chats_Change {
            Session = Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff {
                IsArchived = true
            }),
        };
        await UICommander.Call(archiveCommand).ConfigureAwait(true);
    }

    private async Task NavigateToVisibleChat(PlaceId? preferredPlaceId)
    {
        var chatIdToNavigate = (ChatId?)null;
        if (preferredPlaceId is not null)
            chatIdToNavigate = await GetFirstChatId(preferredPlaceId).ConfigureAwait(true);
        if (chatIdToNavigate is null)
            chatIdToNavigate = await GetFirstChatId(null).ConfigureAwait(true);
        await History.NavigateTo(Links.Chat(chatIdToNavigate ?? Constants.Chat.AnnouncementsChatId)).ConfigureAwait(true);
        return;

        async Task<ChatId?> GetFirstChatId(PlaceId? placeId)
        {
            var chatListSettings = new ChatListSettings { FilterId = ChatListFilter.None.Id }; // TODO(DF): better use stored sorting settings for the place.
            var chats = await ChatListUI.List(placeId, chatListSettings, default).ConfigureAwait(false);
            return chats.Count > 0 ? chats[0].Id : null;
        }
    }

    public async Task RestoreNavbarSelectedGroup()
    {
        await _selectedNavbarGroupId.WhenRead.ConfigureAwait(false);
        var groupId = _selectedNavbarGroupId.Value;
        NavbarUI.InitSelectedGroup(groupId);
        if (NavbarUI.IsPlaceSelected(out var placeId))
            SelectPlaceInternal(placeId);
    }
}
