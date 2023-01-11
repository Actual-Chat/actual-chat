using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualChat.Users.UI.Blazor.Services;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI : WorkerBase
{
    public const int MaxUnreadChatCount = 100;
    public const int MaxActiveChatCount = 3;

    private readonly SharedResourcePool<Symbol, ISyncedState<long?>> _readStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly IStoredState<ChatId> _selectedChatId;
    private readonly IMutableState<RelatedChatEntry?> _relatedChatEntry;
    private readonly IMutableState<ChatEntryId> _highlightedEntryId;
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);
    private readonly object _lock = new();

    private ChatPlayers? _chatPlayers;
    private AudioRecorder? _audioRecorder;
    private AudioSettings? _audioSettings;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private IUserPresences UserPresences { get; }
    private IChats Chats { get; }
    private IContacts Contacts { get; }
    private IReadPositions ReadPositions { get; }
    private IMentions Mentions { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    private AudioSettings AudioSettings => _audioSettings ??= Services.GetRequiredService<AudioSettings>();
    private AccountSettings AccountSettings { get; }
    private LocalSettings LocalSettings { get; }
    private AccountUI AccountUI { get; }
    private LanguageUI LanguageUI { get; }
    private InteractiveUI InteractiveUI { get; }
    private KeepAwakeUI KeepAwakeUI { get; }
    private ModalUI ModalUI { get; }
    private UICommander UICommander { get; }
    private UIEventHub UIEventHub { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;
    private ILogger Log { get; }

    public IStoredState<ImmutableHashSet<ActiveChat>> ActiveChats { get; }
    public IState<ChatId> SelectedChatId => _selectedChatId;
    public IState<RelatedChatEntry?> RelatedChatEntry => _relatedChatEntry;
    public IState<ChatEntryId> HighlightedEntryId => _highlightedEntryId;
    public Task WhenLoaded => _selectedChatId.WhenRead;

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        StateFactory = services.StateFactory();
        ChatMarkupHubFactory = services.KeyedFactory<IChatMarkupHub, ChatId>();

        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        Chats = services.GetRequiredService<IChats>();
        Contacts = services.GetRequiredService<IContacts>();
        ReadPositions = services.GetRequiredService<IReadPositions>();
        Mentions = services.GetRequiredService<IMentions>();
        AccountSettings = services.AccountSettings();
        LocalSettings = services.LocalSettings();
        AccountUI = services.GetRequiredService<AccountUI>();
        LanguageUI = services.GetRequiredService<LanguageUI>();
        InteractiveUI = services.GetRequiredService<InteractiveUI>();
        KeepAwakeUI = services.GetRequiredService<KeepAwakeUI>();
        ModalUI = services.GetRequiredService<ModalUI>();
        UICommander = services.UICommander();
        UIEventHub = services.UIEventHub();

        _selectedChatId = StateFactory.NewKvasStored<ChatId>(new(LocalSettings, nameof(SelectedChatId)));
        _relatedChatEntry = StateFactory.NewMutable<RelatedChatEntry?>();
        _highlightedEntryId = StateFactory.NewMutable<ChatEntryId>();

        ActiveChats = StateFactory.NewKvasStored<ImmutableHashSet<ActiveChat>>(
            new (LocalSettings, nameof(ActiveChats)) {
                InitialValue = ImmutableHashSet<ActiveChat>.Empty,
                Corrector = FixActiveChats,
            });

        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readStates = new SharedResourcePool<Symbol, ISyncedState<long?>>(CreateReadState);
        Start();
    }

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> List(ChatListOrder order, CancellationToken cancellationToken = default)
    {
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

        var isSelected = await IsSelected(chatId).ConfigureAwait(false);
        var mediaState = await GetMediaState(chatId).ConfigureAwait(false);
        return new(chat, mediaState) {
            IsSelected = isSelected,
        };
    }

    [ComputeMethod] // Synced
    public virtual Task<bool> IsSelected(ChatId chatId)
        => Task.FromResult(!chatId.IsNone && SelectedChatId.Value == chatId);

    [ComputeMethod] // Synced
    public virtual Task<ChatMediaState> GetMediaState(ChatId chatId)
    {
        if (chatId.IsNone)
            return Task.FromResult(ChatMediaState.None);

        var activeChats = ActiveChats.Value;
        activeChats.TryGetValue(chatId, out var activeChat);
        var isListening = activeChat.IsListening;
        var isRecording = activeChat.IsRecording;
        var isPlayingHistorical = ChatPlayers.PlaybackState.Value is HistoricalPlaybackState hps && hps.ChatId == chatId;
        var result = new ChatMediaState(chatId, isListening, isPlayingHistorical, isRecording);
        return Task.FromResult(result);
    }

    [ComputeMethod] // Synced
    public virtual Task<ChatId> GetRecordingChatId()
        => Task.FromResult(ActiveChats.Value.FirstOrDefault(c => c.IsRecording).ChatId);

    [ComputeMethod] // Synced
    public virtual Task<ImmutableHashSet<ChatId>> GetListeningChatIds()
        => Task.FromResult(ActiveChats.Value.Where(c => c.IsListening).Select(c => c.ChatId).ToImmutableHashSet());

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

        return UpdateActiveChats(activeChats => activeChats.Add(new ActiveChat(chatId, false, false, Now)));
    }

    public ValueTask RemoveActiveChat(ChatId chatId)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return UpdateActiveChats(activeChats => activeChats.Remove(chatId));
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
        await UICommander.Run(command).ConfigureAwait(false);
    }

    public ValueTask SetListeningState(ChatId chatId, bool mustListen)
    {
        if (chatId.IsNone)
            return ValueTask.CompletedTask;

        return UpdateActiveChats(activeChats => {
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

    public ValueTask SetRecordingChatId(ChatId chatId)
        => UpdateActiveChats(activeChats => {
            var oldChat = activeChats.FirstOrDefault(c => c.IsRecording);
            if (oldChat.ChatId == chatId)
                return activeChats;

            if (!oldChat.ChatId.IsNone)
                activeChats = activeChats.AddOrUpdate(oldChat with {
                    IsRecording = false,
                    Recency = Now,
                });
            if (!chatId.IsNone) {
                var newChat = new ActiveChat(chatId, true, true, Now);
                activeChats = activeChats.AddOrUpdate(newChat);
            }
            UICommander.RunNothing();
            return activeChats;
        });

    // Helpers

    public void SelectChat(ChatId chatId)
    {
        lock (_lock) {
            if (_selectedChatId.Value == chatId)
                return;

            _selectedChatId.Value = chatId;
        }
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

    private async ValueTask UpdateActiveChats(
        Func<ImmutableHashSet<ActiveChat>, ImmutableHashSet<ActiveChat>> updater,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var originalValue = ActiveChats.Value;
        var updatedValue = updater.Invoke(originalValue);
        if (ReferenceEquals(originalValue, updatedValue))
            return;

        updatedValue = await FixActiveChats(updatedValue, cancellationToken).ConfigureAwait(false);
        ActiveChats.Value = updatedValue;
    }

    private async ValueTask<ImmutableHashSet<ActiveChat>> FixActiveChats(
        ImmutableHashSet<ActiveChat> activeChats,
        CancellationToken cancellationToken = default)
    {
        if (activeChats.Count == 0)
            return activeChats;

        // Removing chats that violate access rules + enforce "just 1 recording chat" rule
        var recordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
        var chatRules = await activeChats
            .Select(async chat => {
                var rules = await Chats.GetRules(Session, chat.ChatId, default).ConfigureAwait(false);
                return (Chat: chat, Rules: rules);
            })
            .Collect()
            .ConfigureAwait(false);
        foreach (var (c, rules) in chatRules) {
            // There must be just 1 recording chat
            var chat = c;
            if (c.IsRecording && c != recordingChat) {
                chat = chat with { IsRecording = false };
                activeChats = activeChats.AddOrUpdate(chat);
            }

            // And it must be accessible
            if (!rules.CanRead() || (chat.IsRecording && !rules.CanRead()))
                activeChats = activeChats.Remove(chat);
        }

        // There must be no more than MaxActiveChatCount active chats
        if (activeChats.Count <= MaxActiveChatCount)
            return activeChats;

        var activeChatsWithEffectiveRecency = await activeChats
            .Select(async chat => {
                var effectiveRecency = await GetEffectiveRecency(chat, cancellationToken);
                return (Chat: chat, EffectiveRecency: effectiveRecency);
            })
            .Collect()
            .ConfigureAwait(false);
        var remainingChats = (
            from x in activeChatsWithEffectiveRecency
            orderby x.Chat.IsRecording descending, x.EffectiveRecency descending
            select x.Chat
            ).Take(MaxActiveChatCount)
            .ToImmutableHashSet();
        return remainingChats;

        async ValueTask<Moment> GetEffectiveRecency(ActiveChat chat, CancellationToken ct)
        {
            if (chat.IsRecording)
                return Clocks.CpuClock.Now;
            if (!chat.IsListening)
                return chat.Recency;

            var chatIdRange = await Chats.GetIdRange(Session, chat.ChatId, ChatEntryKind.Audio, ct);
            var chatEntryReader = Chats.NewEntryReader(Session, chat.ChatId, ChatEntryKind.Audio);
            var lastEntry = await chatEntryReader.GetLast(chatIdRange, ct);
            if (lastEntry == null)
                return chat.Recency;
            return lastEntry.IsStreaming
                ? Clocks.CpuClock.Now
                : Moment.Max(chat.Recency, lastEntry.EndsAt ?? lastEntry.BeginsAt);
        }
    }
}
