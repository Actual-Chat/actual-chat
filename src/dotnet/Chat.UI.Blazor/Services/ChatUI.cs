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
    private readonly SharedResourcePool<Symbol, ISyncedState<ReadPosition>> _readPositionStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly IStoredState<ChatId> _selectedChatId;
    private readonly IMutableState<ChatEntryId> _highlightedEntryId;
    private readonly object _lock = new();

    private IUserPresences? _userPresences;
    private IAccounts? _accounts;
    private IChats? _chats;
    private IContacts? _contacts;
    private IChatPositions? _chatPositions;
    private IMentions? _mentions;
    private AccountSettings? _accountSettings;
    private KeepAwakeUI? _keepAwakeUI;
    private TuneUI? _tuneUI;
    private ActiveChatsUI? _activeChatsUI;
    private ChatAudioUI? _chatAudioUI;
    private ChatEditorUI? _chatEditorUI;
    private SelectionUI? _selectionUI;
    private UICommander? _uiCommander;
    private UIEventHub? _uiEventHub;
    private ICommander? _commander;
    private KeyedFactory<IChatMarkupHub, ChatId>? _chatMarkupHubFactory;

    private Session Session { get; }
    private IStateFactory StateFactory { get; }
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory
        => _chatMarkupHubFactory ??= Services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();

    private IUserPresences UserPresences => _userPresences ??= Services.GetRequiredService<IUserPresences>();
    private IAccounts Accounts => _accounts ??= Services.GetRequiredService<IAccounts>();
    private IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    private IContacts Contacts => _contacts ??= Services.GetRequiredService<IContacts>();
    private IChatPositions ChatPositions => _chatPositions ??= Services.GetRequiredService<IChatPositions>();
    private IMentions Mentions => _mentions ??= Services.GetRequiredService<IMentions>();
    private AccountSettings AccountSettings => _accountSettings ??= Services.GetRequiredService<AccountSettings>();
    private KeepAwakeUI KeepAwakeUI => _keepAwakeUI ??= Services.GetRequiredService<KeepAwakeUI>();
    private TuneUI TuneUI => _tuneUI ??= Services.GetRequiredService<TuneUI>();
    private ActiveChatsUI ActiveChatsUI => _activeChatsUI ??= Services.GetRequiredService<ActiveChatsUI>();
    private ChatAudioUI ChatAudioUI => _chatAudioUI ??= Services.GetRequiredService<ChatAudioUI>();
    private ChatEditorUI ChatEditorUI => _chatEditorUI ??= Services.GetRequiredService<ChatEditorUI>();
    private SelectionUI SelectionUI => _selectionUI ??= Services.GetRequiredService<SelectionUI>();
    private UICommander UICommander => _uiCommander ??= Services.UICommander();
    private UIEventHub UIEventHub => _uiEventHub ??= Services.UIEventHub();
    private ICommander Commander => _commander ??= Services.Commander();

    private ILogger Log { get; }
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    public IServiceProvider Services { get; }
    public IState<ChatId> SelectedChatId => _selectedChatId;
    public IState<ChatEntryId> HighlightedEntryId => _highlightedEntryId;
    public Task WhenLoaded => _selectedChatId.WhenRead;

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        Session = services.Session();
        StateFactory = services.StateFactory();

        var type = GetType();
        _selectedChatId = StateFactory.NewKvasStored<ChatId>(new(services.LocalSettings(), nameof(SelectedChatId)) {
            Corrector = FixSelectedChatId,
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

    [ComputeMethod]
    public virtual async Task<Trimmed<int>> GetUnreadCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatInfo = await Get(chatId, cancellationToken).ConfigureAwait(false);
        return chatInfo?.UnreadCount ?? new();
    }

    [ComputeMethod]
    public virtual async Task<bool> IsEmpty(ChatId chatId, CancellationToken cancellationToken)
    {
        var idRange = await Chats.GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken).ConfigureAwait(false);
        if (idRange.End - idRange.Start > 100) {
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
        lock (_lock) {
            if (_selectedChatId.Value == chatId)
                return false;

            _selectedChatId.Value = chatId;
        }
        // The rest is done by InvalidateSelectedChatDependencies
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
            _ = UICommander.RunNothing();
    }

    public async ValueTask<SyncedStateLease<ReadPosition>> LeaseReadPositionState(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _readPositionStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        var result = new SyncedStateLease<ReadPosition>(lease);
        await result.WhenFirstTimeRead.ConfigureAwait(false);
        return result;
    }

    // Private methods

    private async ValueTask<ChatId> FixSelectedChatId(ChatId chatId, CancellationToken cancellationToken = default)
    {
        chatId = await FixChatId(chatId, cancellationToken).ConfigureAwait(false);
        return chatId.IsNone ? Constants.Chat.AnnouncementsChatId : chatId;
    }

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
}
