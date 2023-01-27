using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Contacts;
using ActualChat.IO;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI : WorkerBase
{
    private readonly SharedResourcePool<Symbol, ISyncedState<ChatPosition>> _readPositionStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly IMutableState<ChatId> _selectedChatId;
    private readonly IMutableState<RelatedChatEntry?> _relatedChatEntry;
    private readonly IMutableState<ChatEntryId> _highlightedEntryId;
    private readonly object _lock = new();

    private IStateFactory StateFactory { get; }
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
    private Session Session { get; }
    private IUserPresences UserPresences { get; }
    private IChats Chats { get; }
    private IContacts Contacts { get; }
    private IChatPositions ChatPositions { get; }
    private IMentions Mentions { get; }
    private AccountSettings AccountSettings { get; }
    private KeepAwakeUI KeepAwakeUI { get; }
    private TuneUI TuneUI { get; }
    private ModalUI ModalUI { get; }
    private ActiveChatsUI ActiveChatsUI { get; }
    private AudioUI AudioUI { get; }
    private UICommander UICommander { get; }
    private UIEventHub UIEventHub { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }
    public IStoredState<ChatListSettings> ListSettings { get; }
    public IState<ChatId> SelectedChatId => _selectedChatId;
    public IState<RelatedChatEntry?> RelatedChatEntry => _relatedChatEntry;
    public IState<ChatEntryId> HighlightedEntryId => _highlightedEntryId;

    public ChatUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        StateFactory = services.StateFactory();
        ChatMarkupHubFactory = services.KeyedFactory<IChatMarkupHub, ChatId>();

        Session = services.GetRequiredService<Session>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        Chats = services.GetRequiredService<IChats>();
        Contacts = services.GetRequiredService<IContacts>();
        ChatPositions = services.GetRequiredService<IChatPositions>();
        Mentions = services.GetRequiredService<IMentions>();
        AccountSettings = services.AccountSettings();
        KeepAwakeUI = services.GetRequiredService<KeepAwakeUI>();
        TuneUI = services.GetRequiredService<TuneUI>();
        ModalUI = services.GetRequiredService<ModalUI>();
        ActiveChatsUI = services.GetRequiredService<ActiveChatsUI>();
        AudioUI = services.GetRequiredService<AudioUI>();
        UICommander = services.UICommander();
        UIEventHub = services.UIEventHub();
        Commander = services.Commander();

        _selectedChatId = StateFactory.NewMutable<ChatId>();
        _relatedChatEntry = StateFactory.NewMutable<RelatedChatEntry?>();
        _highlightedEntryId = StateFactory.NewMutable<ChatEntryId>();

        ListSettings = StateFactory.NewKvasStored<ChatListSettings>(
            new (AccountSettings, nameof(ListSettings)) {
                InitialValue = new(),
            });

        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readPositionStates = new SharedResourcePool<Symbol, ISyncedState<ChatPosition>>(CreateReadPositionState);
        Start();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> List(CancellationToken cancellationToken = default)
    {
        var settings = await ListSettings.Use(cancellationToken).ConfigureAwait(false);
        Log.LogDebug("List: {Settings}", settings);
        var chats = await ListUnordered(cancellationToken).ConfigureAwait(false);
        var filterId = settings.Filter.Id;
        var filteredChats = filterId switch {
            _ when filterId == ChatListFilter.Personal.Id => chats.Values.Where(c => c.Chat.Kind == ChatKind.Peer).ToList(),
            _ => chats.Values.ToList(),
        };
        var preOrderedChats = filteredChats
            .OrderByDescending(c => c.Contact.IsPinned)
            .ThenByDescending(c => c.HasUnreadMentions);
        var orderedChats = settings.Order switch {
            ChatListOrder.ByLastEventTime => preOrderedChats
                .ThenByDescending(c => c.News.LastTextEntry?.Version ?? 0),
            ChatListOrder.ByOwnUpdateTime => preOrderedChats
                .ThenByDescending(c => c.Contact.TouchedAt),
            ChatListOrder.ByUnreadCount => preOrderedChats
                .ThenByDescending(c => c.UnreadCount.Value)
                .ThenByDescending(c => c.News.LastTextEntry?.Version),
            ChatListOrder.ByAlphabet => filteredChats
                .OrderByDescending(c => c.Contact.IsPinned)
                .ThenBy(c => c.Chat.Title),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
        var result = orderedChats.ToList();
        return result;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnordered(CancellationToken cancellationToken = default)
    {
        Log.LogDebug("ListUnordered");
        var contactIds = await Contacts.ListIds(Session, cancellationToken).ConfigureAwait(false);
        var result = await Task.WhenAll(
            contactIds.Select(contactId => Get(contactId.ChatId, cancellationToken))
            ).ConfigureAwait(false);
        return result.SkipNullItems().ToDictionary(c => c.Id);
    }

    [ComputeMethod]
    public virtual async Task<ChatInfo?> Get(ChatId chatId, CancellationToken cancellationToken = default)
    {
        Log.LogDebug("Get: {ChatId}", chatId.Value);
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
            var chatMarkupHub = ChatMarkupHubFactory[chatId];
            var markup = await chatMarkupHub.GetMarkup(lastTextEntry, MarkupConsumer.ChatListItemText, cancellationToken);
            lastTextEntryText = markup.ToReadableText(MarkupConsumer.ChatListItemText);
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
            var state = await GetState(chatId, false).ConfigureAwait(false);
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
        var audioState = await AudioUI.GetState(chatId).ConfigureAwait(false);
        return new(chat, audioState) {
            IsSelected = isSelected,
        };
    }

    [ComputeMethod] // Manually & automatically invalidated
    public virtual async ValueTask<long> GetReadEntryLid(ChatId chatId, CancellationToken cancellationToken)
    {
        // NOTE(AY): This method uses LeaseReadPositionState in a bit tricky way:
        // on one hand, it can't depend on it, coz it disposes the lease, which means
        // computed it maintains might end up being never updated.
        // On another hand, it makes sense to read the most up-to-date read position,
        // so it returns max(leased read position, fetched read position).

        var fetchedReadPosition = await ChatPositions
            .GetOwn(Session, chatId, ChatPositionKind.Read, cancellationToken)
            .ConfigureAwait(false);

        using var readPositionState = await LeaseReadPositionState(chatId, cancellationToken).ConfigureAwait(false);
        var readPosition = readPositionState.Value;
        return readPosition.EntryLid > fetchedReadPosition.EntryLid
            ? readPosition.EntryLid
            : fetchedReadPosition.EntryLid;
    }

    [ComputeMethod] // Synced
    public virtual Task<bool> IsSelected(ChatId chatId)
        => Task.FromResult(!chatId.IsNone && SelectedChatId.Value == chatId);

    // Not compute method!
    public async ValueTask<Trimmed<int>> GetUnreadCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatNews = await Chats.GetNews(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chatNews.IsNone)
            return new Trimmed<int>(0, ChatInfo.MaxUnreadCount);

        var readEntryLid = await GetReadEntryLid(chatId, cancellationToken).ConfigureAwait(false);
        return ComputeUnreadCount(chatNews, readEntryLid);
    }

    // Not compute method!
    public Trimmed<int> ComputeUnreadCount(ChatNews chatNews, long readEntryLid)
    {
        var unreadCount = 0;
        if (readEntryLid > 0 && !chatNews.IsNone) {
            // Otherwise the chat wasn't ever opened
            var lastId = chatNews.TextEntryIdRange.End - 1;
            unreadCount = (int)(lastId - readEntryLid).Clamp(0, ChatInfo.MaxUnreadCount);
        }
        return new Trimmed<int>(unreadCount, ChatInfo.MaxUnreadCount);
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

        var command = new IContacts.ChangeCommand(Session, contact.Id, contact.Version, new Change<Contact>() {
            Update = contact with { IsPinned = mustPin },
        });
        _ = TuneUI.Play("pin-unpin-chat");
        await UICommander.Run(command).ConfigureAwait(false);
    }

    // Helpers

    public void SelectChat(ChatId chatId)
    {
        lock (_lock) {
            if (_selectedChatId.Value == chatId)
                return;

            _selectedChatId.Value = chatId;
        }
        _ = TuneUI.Play("select-chat");
        _ = UIEventHub.Publish<SelectedChatChangedEvent>(CancellationToken.None);
        UICommander.RunNothing();
    }

    public void ShowRelatedEntry(RelatedEntryKind kind, ChatEntryId entryId, bool focusOnEditor, bool updateUI = true)
    {
        var relatedChatEntry = new RelatedChatEntry(kind, entryId);
        lock (_lock) {
            if (_relatedChatEntry.Value == relatedChatEntry)
                return;

            _relatedChatEntry.Value = relatedChatEntry;
        }
        if (focusOnEditor)
            _ = UIEventHub.Publish<FocusChatMessageEditorEvent>();
        if (updateUI)
            UICommander.RunNothing();

        var tuneName = kind switch {
            RelatedEntryKind.Reply => "reply-message",
            RelatedEntryKind.Edit => "edit-message",
            _ => "",
        };
        if (!tuneName.IsNullOrEmpty())
            TuneUI.Play(tuneName);
    }

    public void HideRelatedEntry(bool updateUI = true)
    {
        lock (_lock) {
            if (_relatedChatEntry.Value == null)
                return;

            _relatedChatEntry.Value = null;
        }
        if (updateUI)
            UICommander.RunNothing();
        TuneUI.Play("cancel");
    }

    public void HighlightEntry(ChatEntryId entryId, bool navigate, bool updateUI = true)
    {
        lock (_lock) {
            if (_highlightedEntryId.Value == entryId)
                return;

            _highlightedEntryId.Value = entryId;
        }
        if (navigate)
            _ = UIEventHub.Publish(new NavigateToChatEntryEvent(entryId));
        if (updateUI)
            UICommander.RunNothing();
    }

    public void ShowDeleteMessageModal(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));

    public async ValueTask<SyncedStateLease<ChatPosition>> LeaseReadPositionState(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _readPositionStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        var result = new SyncedStateLease<ChatPosition>(lease);
        await result.WhenFirstTimeRead;
        return result;
    }

    // Private methods

    private Task<ISyncedState<ChatPosition>> CreateReadPositionState(Symbol chatId, CancellationToken cancellationToken)
    {
        var pChatId = new ChatId(chatId, ParseOrNone.Option);

        // Commander use here is intended: this "action" shouldn't be counted as user action
        var writeDebouncer = new Debouncer<ICommand>(
            TimeSpan.FromSeconds(1),
            command => Commander.Run(command, CancellationToken.None));

        return Task.FromResult(StateFactory.NewCustomSynced<ChatPosition>(
            new (
                // Reader
                async ct => {
                    if (pChatId.IsNone)
                        return new ChatPosition();

                    return await ChatPositions.GetOwn(Session, pChatId, ChatPositionKind.Read, ct).ConfigureAwait(false);
                },
                // Writer
                (position, ct) => {
                    if (pChatId.IsNone || position == null!)
                        return Task.CompletedTask;

                    var command = new IChatPositions.SetCommand(Session, pChatId, ChatPositionKind.Read, position);
                    writeDebouncer.Throttle(command);

                    var cReadEntryLid = Computed.GetExisting(() => GetReadEntryLid(pChatId, default));
                    // Conditions:
                    // - No computed -> nothing to invalidate
                    // - No value (error) -> invalidate
                    // - Value < current -> invalidate
                    if (cReadEntryLid != null && (!cReadEntryLid.IsValue(out var entryLid) || entryLid < position.EntryLid))
                        cReadEntryLid.Invalidate();

                    return Task.CompletedTask;
                }) {
                InitialValue = new ChatPosition(),
                UpdateDelayer = _readStateUpdateDelayer,
            }
        ));
    }
}
