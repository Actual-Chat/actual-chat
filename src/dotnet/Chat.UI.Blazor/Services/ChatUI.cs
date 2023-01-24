using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI : WorkerBase
{
    private readonly SharedResourcePool<Symbol, ISyncedState<long?>> _readStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly IMutableState<RelatedChatEntry?> _relatedChatEntry;
    private readonly IMutableState<ChatEntryId> _highlightedEntryId;
    private readonly object _lock = new();

    private ChatPlayers? _chatPlayers;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
    private Session Session { get; }
    private IUserPresences UserPresences { get; }
    private IChats Chats { get; }
    private IContacts Contacts { get; }
    private IReadPositions ReadPositions { get; }
    private IMentions Mentions { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private AccountSettings AccountSettings { get; }
    private LanguageUI LanguageUI { get; }
    private InteractiveUI InteractiveUI { get; }
    private KeepAwakeUI KeepAwakeUI { get; }
    private TuneUI TuneUI { get; }
    private ModalUI ModalUI { get; }
    private ChatListUI ChatListUI { get; }
    private UICommander UICommander { get; }
    private UIEventHub UIEventHub { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;
    private ILogger Log { get; }

    public IState<RelatedChatEntry?> RelatedChatEntry => _relatedChatEntry;
    public IState<ChatEntryId> HighlightedEntryId => _highlightedEntryId;

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        StateFactory = services.StateFactory();
        ChatMarkupHubFactory = services.KeyedFactory<IChatMarkupHub, ChatId>();

        Session = services.GetRequiredService<Session>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        Chats = services.GetRequiredService<IChats>();
        Contacts = services.GetRequiredService<IContacts>();
        ReadPositions = services.GetRequiredService<IReadPositions>();
        Mentions = services.GetRequiredService<IMentions>();
        AccountSettings = services.AccountSettings();
        LanguageUI = services.GetRequiredService<LanguageUI>();
        InteractiveUI = services.GetRequiredService<InteractiveUI>();
        KeepAwakeUI = services.GetRequiredService<KeepAwakeUI>();
        TuneUI = services.GetRequiredService<TuneUI>();
        ModalUI = services.GetRequiredService<ModalUI>();
        ChatListUI = services.GetRequiredService<ChatListUI>();
        UICommander = services.UICommander();
        UIEventHub = services.UIEventHub();

        _relatedChatEntry = StateFactory.NewMutable<RelatedChatEntry?>();
        _highlightedEntryId = StateFactory.NewMutable<ChatEntryId>();

        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readStates = new SharedResourcePool<Symbol, ISyncedState<long?>>(CreateReadState);
        _stopRecordingAt = services.StateFactory().NewMutable<Moment?>();
        Start();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> List(ChatListOrder order, CancellationToken cancellationToken = default)
    {
        Log.LogDebug("List, Order = {Order}", order.ToString());
        var chats = await ListUnordered(cancellationToken).ConfigureAwait(false);
        var preOrderedChats = chats.Values
            .OrderByDescending(c => c.Contact.IsPinned)
            .ThenByDescending(c => c.HasUnreadMentions);
        var orderedChats = order switch {
            ChatListOrder.ByLastEventTime => preOrderedChats
                .ThenByDescending(c => c.News.LastTextEntry?.Version ?? 0),
            ChatListOrder.ByOwnUpdateTime => preOrderedChats
                .ThenByDescending(c => c.Contact.TouchedAt),
            ChatListOrder.ByUnreadCount => preOrderedChats
                .ThenByDescending(c => c.UnreadCount.Value)
                .ThenByDescending(c => c.News.LastTextEntry?.Version),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
        var result = orderedChats.ToList();
        return result;
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyDictionary<ChatId, ChatInfo>> ListUnordered(CancellationToken cancellationToken = default)
    {
        Log.LogDebug("ListUnordered");
        var contactIds = await Contacts.ListIds(Session, cancellationToken).ConfigureAwait(false);
        var result = await contactIds
            .Select(contactId => Get(contactId.ChatId, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
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
        var readEntryIdTask = GetReadEntryId(chatId, cancellationToken);
        var userSettingsTask = AccountSettings.GetUserChatSettings(chatId, cancellationToken);

        var news = await chatNewsTask.ConfigureAwait(false);
        var userSettings = await userSettingsTask.ConfigureAwait(false);
        var lastMention = await lastMentionTask.ConfigureAwait(false);
        var readEntryId = await readEntryIdTask.ConfigureAwait(false);

        var unreadCount = 0;
        if (readEntryId is { } vReadEntryId) { // Otherwise the chat wasn't ever opened
            var lastId = news.TextEntryIdRange.End - 1;
            unreadCount = (int)(lastId - vReadEntryId).Clamp(0, ChatInfo.MaxUnreadCount);
        }

        var hasUnreadMentions = false;
        if (userSettings.NotificationMode is not ChatNotificationMode.Muted) {
            var lastMentionEntryId = lastMention?.EntryId.LocalId ?? 0;
            hasUnreadMentions = lastMentionEntryId > readEntryId;
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
            ReadEntryId = readEntryId,
            UnreadCount = new Trimmed<int>(unreadCount, ChatInfo.MaxUnreadCount),
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

        var isSelected = await ChatListUI.IsSelected(chatId).ConfigureAwait(false);
        var mediaState = await GetMediaState(chatId).ConfigureAwait(false);
        return new(chat, mediaState) {
            IsSelected = isSelected,
        };
    }

    [ComputeMethod] // Synced
    public virtual Task<ChatMediaState> GetMediaState(ChatId chatId)
    {
        if (chatId.IsNone)
            return Task.FromResult(ChatMediaState.None);

        var activeChats = ChatListUI.ActiveChats.Value;
        activeChats.TryGetValue(chatId, out var activeChat);
        var isListening = activeChat.IsListening;
        var isRecording = activeChat.IsRecording;
        var isPlayingHistorical = ChatPlayers.PlaybackState.Value is HistoricalPlaybackState hps && hps.ChatId == chatId;
        var result = new ChatMediaState(chatId, isListening, isPlayingHistorical, isRecording);
        return Task.FromResult(result);
    }

    [ComputeMethod] // Synced
    public virtual Task<ImmutableHashSet<ChatId>> GetListeningChatIds()
        => Task.FromResult(ChatListUI.ActiveChats.Value.Where(c => c.IsListening).Select(c => c.ChatId).ToImmutableHashSet());

    [ComputeMethod]
    public virtual async Task<RealtimePlaybackState?> GetExpectedRealtimePlaybackState()
    {
        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count == 0 ? null : new RealtimePlaybackState(listeningChatIds);
    }

    // SetXxx & Add/RemoveXxx

    public ValueTask AddActiveChat(ChatId chatId)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return ChatListUI.UpdateActiveChats(activeChats => activeChats.Add(new ActiveChat(chatId, false, false, Now)));
    }

    public ValueTask RemoveActiveChat(ChatId chatId)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return ChatListUI.UpdateActiveChats(activeChats => activeChats.Remove(chatId));
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

        var command = new IContacts.ChangeCommand(Session, contact.Id, contact.Version, new Change<Contact>() {
            Update = contact with { IsPinned = mustPin },
        });
        _ = TuneUI.Play("pin-unpin-chat");
        await UICommander.Run(command).ConfigureAwait(false);
    }

    public ValueTask SetListeningState(ChatId chatId, bool mustListen)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return ChatListUI.UpdateActiveChats(activeChats => {
            var oldActiveChats = activeChats;
            if (activeChats.TryGetValue(chatId, out var chat) && chat.IsListening != mustListen) {
                activeChats = activeChats.Remove(chat);
                chat = chat with { IsListening = mustListen };
                activeChats = activeChats.Add(chat);
            }
            else if (mustListen)
                activeChats = activeChats.Add(new ActiveChat(chatId, true, false, Now));
            if (oldActiveChats != activeChats)
                UICommander.RunNothing();

            return activeChats;
        });
    }

    // Helpers

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

    public async ValueTask<SyncedStateLease<long?>> LeaseReadState(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _readStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        var result = new SyncedStateLease<long?>(lease);
        await result.WhenFirstTimeRead;
        return result;
    }

    // Private methods

    // TODO: Make it non-nullable?
    private async ValueTask<long?> GetReadEntryId(ChatId chatId, CancellationToken cancellationToken)
    {
        using var readEntryState = await LeaseReadState(chatId, cancellationToken).ConfigureAwait(false);
        return await readEntryState.Use(cancellationToken).ConfigureAwait(false);
    }

    private Task<ISyncedState<long?>> CreateReadState(Symbol chatId, CancellationToken cancellationToken)
    {
        var pChatId = new ChatId(chatId, ParseOrNone.Option);
        return Task.FromResult(StateFactory.NewCustomSynced<long?>(
            new (
                // Reader
                async ct => {
                    if (pChatId.IsNone)
                        return null;

                    return await ReadPositions.GetOwn(Session, pChatId, ct).ConfigureAwait(false);
                },
                // Writer
                async (readEntryId, ct) => {
                    if (pChatId.IsNone || readEntryId is not { } entryId)
                        return;

                    var command = new IReadPositions.SetCommand(Session, pChatId, entryId);
                    await UICommander.Run(command, ct);
                }) { UpdateDelayer = _readStateUpdateDelayer }
        ));
    }


}
