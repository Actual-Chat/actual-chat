using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Contacts;
using ActualChat.IO;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI : WorkerBase, IHasServices, IComputeService, INotifyInitialized
{
    private readonly SharedResourcePool<Symbol, ISyncedState<ChatPosition>> _readPositionStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly IStoredState<ChatId> _selectedChatId;
    private readonly IMutableState<ChatEntryId> _highlightedEntryId;
    private readonly object _lock = new();

    private Session Session { get; }
    private IUserPresences UserPresences { get; }
    private IAccounts Accounts { get; }
    private IChats Chats { get; }
    private IContacts Contacts { get; }
    private IChatPositions ChatPositions { get; }
    private IMentions Mentions { get; }
    private AccountSettings AccountSettings { get; }
    private History History { get; }
    private KeepAwakeUI KeepAwakeUI { get; }
    private TuneUI TuneUI { get; }
    private ModalUI ModalUI { get; }
    private ActiveChatsUI ActiveChatsUI { get; }
    private ChatListUI ChatListUI { get; }
    private ChatAudioUI ChatAudioUI { get; }
    private ChatEditorUI ChatEditorUI { get; }
    private UICommander UICommander { get; }
    private UIEventHub UIEventHub { get; }
    private ICommander Commander { get; }
    private IStateFactory StateFactory { get; }
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }

    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    public IServiceProvider Services { get; }
    public IStoredState<ChatListSettings> ListSettings { get; }
    public IState<ChatId> SelectedChatId => _selectedChatId;
    public IState<ChatEntryId> HighlightedEntryId => _highlightedEntryId;
    public Task WhenLoaded => _selectedChatId.WhenRead;

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        Session = services.GetRequiredService<Session>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        Accounts = services.GetRequiredService<IAccounts>();
        Chats = services.GetRequiredService<IChats>();
        Contacts = services.GetRequiredService<IContacts>();
        ChatPositions = services.GetRequiredService<IChatPositions>();
        Mentions = services.GetRequiredService<IMentions>();
        AccountSettings = services.AccountSettings();
        History = services.GetRequiredService<History>();
        KeepAwakeUI = services.GetRequiredService<KeepAwakeUI>();
        TuneUI = services.GetRequiredService<TuneUI>();
        ModalUI = services.GetRequiredService<ModalUI>();
        ActiveChatsUI = services.GetRequiredService<ActiveChatsUI>();
        ChatListUI = services.GetRequiredService<ChatListUI>();
        ChatAudioUI = services.GetRequiredService<ChatAudioUI>();
        ChatEditorUI = services.GetRequiredService<ChatEditorUI>();
        UIEventHub = services.UIEventHub();
        UICommander = services.UICommander();
        Commander = services.Commander();
        StateFactory = services.StateFactory();
        ChatMarkupHubFactory = services.KeyedFactory<IChatMarkupHub, ChatId>();

        var type = GetType();
        _selectedChatId = StateFactory.NewKvasStored<ChatId>(new (services.LocalSettings(), nameof(SelectedChatId)) {
            Corrector = FixChatId,
        });
        _highlightedEntryId = StateFactory.NewMutable(
            ChatEntryId.None,
            StateCategories.Get(type, nameof(HighlightedEntryId)));

        ListSettings = StateFactory.NewKvasStored<ChatListSettings>(
            new (AccountSettings, nameof(ListSettings)) {
                InitialValue = new(),
                Category = StateCategories.Get(type, nameof(ListSettings)),
            });

        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readPositionStates = new SharedResourcePool<Symbol, ISyncedState<ChatPosition>>(CreateReadPositionState);
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod(MinCacheDuration = 300, InvalidationDelay = 0.6)]
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

    [ComputeMethod]
    public virtual async Task<Trimmed<int>> GetUnreadCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatInfo = await Get(chatId, cancellationToken).ConfigureAwait(false);
        return chatInfo?.UnreadCount ?? new();
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
        lock (_lock) {
            if (_selectedChatId.Value == chatId)
                return false;

            _selectedChatId.Value = chatId;
        }

        _ = ChatEditorUI.RestoreRelatedEntry(chatId).ConfigureAwait(false);
        _ = UIEventHub.Publish<SelectedChatChangedEvent>(CancellationToken.None);
        _ = UICommander.RunNothing();
        return true;
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

    public Task ShowDeleteMessageModal(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));

    public async ValueTask<SyncedStateLease<ChatPosition>> LeaseReadPositionState(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _readPositionStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        var result = new SyncedStateLease<ChatPosition>(lease);
        await result.WhenFirstTimeRead;
        return result;
    }

    // Private methods

    // Not compute method!
    private Trimmed<int> ComputeUnreadCount(ChatNews chatNews, long readEntryLid)
    {
        var unreadCount = 0;
        if (readEntryLid > 0 && !chatNews.IsNone) {
            // Otherwise the chat wasn't ever opened
            var lastId = chatNews.TextEntryIdRange.End - 1;
            unreadCount = (int)(lastId - readEntryLid).Clamp(0, ChatInfo.MaxUnreadCount);
        }
        return new Trimmed<int>(unreadCount, ChatInfo.MaxUnreadCount);
    }

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
                    if (cReadEntryLid?.IsConsistent() == true && (!cReadEntryLid.IsValue(out var entryLid) || entryLid < position.EntryLid))
                        cReadEntryLid.Invalidate();

                    return Task.CompletedTask;
                }) {
                InitialValue = new ChatPosition(),
                UpdateDelayer = _readStateUpdateDelayer,
                Category = StateCategories.Get(GetType(), nameof(ChatPositions), "[*]"),
            }
        ));
    }
}
