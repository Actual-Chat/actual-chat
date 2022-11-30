using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatUI
{
    public const int MaxUnreadChatCount = 100;
    public const int MaxActiveChatCount = 3;

    private ChatPlayers? _chatPlayers;
    private readonly SharedResourcePool<Symbol, ISyncedState<long?>> _readStates;
    private readonly IUpdateDelayer _readStateUpdateDelayer;
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private IChats Chats { get; }
    private IContacts Contacts { get; }
    private IReadPositions ReadPositions { get; }
    private IMentions Mentions { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private UICommander UICommander { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IStoredState<ImmutableHashSet<ActiveChat>> ActiveChats { get; }
    public ISyncedState<ImmutableHashSet<PinnedChat>> PinnedChats { get; }
    public IStoredState<ChatId> SelectedChatId { get; }
    public IMutableState<LinkedChatEntry?> LinkedChatEntry { get; }
    public IMutableState<long> HighlightedChatEntryId { get; }
    public IMutableState<Range<long>> VisibleIdRange { get; }

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        StateFactory = services.StateFactory();
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        Chats = services.GetRequiredService<IChats>();
        Contacts = services.GetRequiredService<IContacts>();
        ReadPositions = services.GetRequiredService<IReadPositions>();
        Mentions = services.GetRequiredService<IMentions>();
        ModalUI = services.GetRequiredService<ModalUI>();
        Clocks = services.Clocks();
        UICommander = services.UICommander();

        var localSettings = services.LocalSettings();
        var accountSettings = services.AccountSettings().WithPrefix(nameof(ChatUI));
        SelectedChatId = StateFactory.NewKvasStored<ChatId>(new(localSettings, nameof(SelectedChatId)));
        ActiveChats = StateFactory.NewKvasStored<ImmutableHashSet<ActiveChat>>(
            new (localSettings, nameof(ActiveChats)) {
                InitialValue = ImmutableHashSet<ActiveChat>.Empty,
                Corrector = FixActiveChats,
            });
        PinnedChats = StateFactory.NewKvasSynced<ImmutableHashSet<PinnedChat>>(
            new(accountSettings, nameof(PinnedChats)) {
                InitialValue = ImmutableHashSet<PinnedChat>.Empty,
                Corrector = FixPinnedChats,
            });
        LinkedChatEntry = StateFactory.NewMutable<LinkedChatEntry?>();
        HighlightedChatEntryId = StateFactory.NewMutable<long>();
        VisibleIdRange = StateFactory.NewMutable<Range<long>>();

        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readStates = new SharedResourcePool<Symbol, ISyncedState<long?>>(CreateReadState);
        var uiStateSync = services.GetRequiredService<UIStateSync>();
        uiStateSync.Start();
    }

    [ComputeMethod]
    public virtual async Task<ImmutableList<ChatState>> ListChatStates(CancellationToken cancellationToken)
    {
        var result = await ListChatStatesExcludingSelected(cancellationToken).ConfigureAwait(false);
        if (result.Any(c => c.IsSelected))
            return result;

        var selectedChatState = await GetSelectedChatState(cancellationToken).ConfigureAwait(false);
        if (selectedChatState == null)
            return result;

        result = result.Insert(0, selectedChatState);
        return result;
    }

    [ComputeMethod]
    public virtual async Task<ChatState?> GetSelectedChatState(CancellationToken cancellationToken)
    {
        var selectedChatId = await SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        var result = await GetChatState(selectedChatId, cancellationToken).ConfigureAwait(false);
        return result;
    }

    [ComputeMethod]
    public virtual async Task<ChatState?> GetChatState(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            return null;

        var ownAccountTask = Accounts.GetOwn(Session, cancellationToken);
        var chatTask = Chats.Get(Session, chatId, cancellationToken);
        var chatSummaryTask = Chats.GetSummary(Session, chatId, cancellationToken);
        var lastMentionTask = Mentions.GetLastOwn(Session, chatId, cancellationToken);
        var readEntryIdTask = GetReadEntryId(chatId, cancellationToken);

        var isSelectedTask = IsSelected(chatId);
        var isPinnedTask = IsPinned(chatId);
        var isListeningTask = IsListening(chatId);
        var isRecordingTask = IsRecording(chatId);

        var ownAccount = await ownAccountTask.ConfigureAwait(false);
        var contactId = new ContactId(ownAccount.Id, chatId, ParseOrNone.Option);
        var contact = contactId.IsNone ? null
            : await Contacts.Get(Session, contactId, cancellationToken).ConfigureAwait(false);

        var chat = await chatTask.ConfigureAwait(false);
        var chatSummary = await chatSummaryTask.ConfigureAwait(false);
        if (chat == null || chatSummary == null)
            return null;

        var result = new ChatState() {
            Chat = chat,
            Summary = chatSummary,
            Contact = contact,
            LastMention = await lastMentionTask.ConfigureAwait(false),
            ReadEntryId = await readEntryIdTask.ConfigureAwait(false),
            IsSelected = await isSelectedTask.ConfigureAwait(false),
            IsPinned = await isPinnedTask.ConfigureAwait(false),
            IsListening = await isListeningTask.ConfigureAwait(false),
            IsRecording = await isRecordingTask.ConfigureAwait(false),
        };
        return result;
    }

    [ComputeMethod]
    public virtual Task<bool> IsSelected(ChatId chatId)
        => Task.FromResult(SelectedChatId.Value == chatId);

    [ComputeMethod]
    public virtual Task<bool> IsPinned(ChatId chatId)
        => Task.FromResult(PinnedChats.Value.Contains(chatId));

    [ComputeMethod]
    public virtual Task<bool> IsListening(ChatId chatId)
        => Task.FromResult(ActiveChats.Value.TryGetValue(chatId, out var c) && c.IsListening);

    [ComputeMethod]
    public virtual Task<bool> IsRecording(ChatId chatId)
        => Task.FromResult(ActiveChats.Value.TryGetValue(chatId, out var c) && c.IsRecording);

    [ComputeMethod]
    public virtual Task<ChatId> GetRecordingChatId()
        => Task.FromResult(ActiveChats.Value.FirstOrDefault(c => c.IsRecording).ChatId);

    [ComputeMethod]
    public virtual Task<ImmutableHashSet<ChatId>> GetListeningChatIds()
        => Task.FromResult(ActiveChats.Value.Where(c => c.IsListening).Select(c => c.ChatId).ToImmutableHashSet());

    [ComputeMethod]
    public virtual async Task<SingleChatPlaybackState> GetPlaybackState(ChatId chatId, CancellationToken cancellationToken)
    {
        var isListeningTask = IsListening(chatId);
        var chatPlaybackStateTask = ChatPlayers.ChatPlaybackState.Use(cancellationToken);
        var isListening = await isListeningTask.ConfigureAwait(false);
        var chatPlaybackState = await chatPlaybackStateTask.ConfigureAwait(false);
        var isPlayingHistorical = chatPlaybackState is HistoricalChatPlaybackState x && x.ChatId == chatId;
        return new SingleChatPlaybackState(chatId, isListening, isPlayingHistorical);
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackState?> GetRealtimePlaybackState()
    {
        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count == 0 ? null : new RealtimeChatPlaybackState(listeningChatIds);
    }

    [ComputeMethod]
    public virtual async Task<bool> MustKeepAwake()
    {
        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        if (!recordingChatId.IsNone)
            return true;

        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count > 0;
    }

    // SetXxx & Add/RemoveXxx

    public ValueTask AddActiveChat(ChatId chatId)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdateActiveChats(activeChats => activeChats.Add(new ActiveChat(chatId, false, false, Now)));
    }

    public ValueTask RemoveActiveChat(ChatId chatId)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdateActiveChats(activeChats => activeChats.Remove(chatId));
    }

    public ValueTask Pin(ChatId chatId) => SetPinState(chatId, true);
    public ValueTask Unpin(ChatId chatId) => SetPinState(chatId, false);
    public ValueTask SetPinState(ChatId chatId, bool mustPin)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdatePinnedChats(
            pinnedChats => mustPin
                ? pinnedChats.Add(new PinnedChat(chatId, Now))
                : pinnedChats.Remove(chatId)
            );
    }

    public ValueTask SetListeningState(ChatId chatId, bool mustListen)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdateActiveChats(activeChats => {
            if (activeChats.TryGetValue(chatId, out var chat)) {
                activeChats = activeChats.Remove(chat);
                chat = chat with { IsListening = mustListen };
                activeChats = activeChats.Add(chat);
            }
            else if (mustListen)
                activeChats = activeChats.Add(new ActiveChat(chatId, true, false, Now));
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
            return activeChats;
        });

    // Helpers

    public void ShowAuthorModal(AuthorId authorId)
        => ModalUI.Show(new AuthorModal.Model(authorId));

    public void ShowDeleteMessageModal(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));

    public async ValueTask<SyncedStateLease<long?>> LeaseReadState(ChatId chatId, CancellationToken cancellationToken)
    {
        var lease = await _readStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        var result = new SyncedStateLease<long?>(lease);
        await result.WhenFirstTimeRead;
        return result;
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ImmutableList<ChatState>> ListChatStatesExcludingSelected(CancellationToken cancellationToken)
    {
        var contactIds = await Contacts.ListIds(Session, cancellationToken).ConfigureAwait(false);
        var chats = await contactIds
            .Select(contactId => GetChatState(contactId.ChatId, cancellationToken))
            .Collect()
            .ConfigureAwait(false);

        var result = chats
            .SkipNullItems()
            .OrderByDescending(c => c.HasMentions).ThenByDescending(c => c.Contact?.TouchedAt ?? Moment.MaxValue)
            .ToImmutableList();
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

    private async ValueTask UpdatePinnedChats(
        Func<ImmutableHashSet<PinnedChat>, ImmutableHashSet<PinnedChat>> updater,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var originalValue = PinnedChats.Value;
        var updatedValue = updater.Invoke(originalValue);
        if (ReferenceEquals(originalValue, updatedValue))
            return;

        updatedValue = await FixPinnedChats(updatedValue, cancellationToken).ConfigureAwait(false);
        PinnedChats.Value = updatedValue;
    }

    private async ValueTask<ImmutableHashSet<PinnedChat>> FixPinnedChats(
        ImmutableHashSet<PinnedChat> pinnedChats,
        CancellationToken cancellationToken = default)
    {
        if (pinnedChats.Count < 32)
            return pinnedChats;

        var oldBoundary = Now - TimeSpan.FromDays(365);
        var chats = await pinnedChats
            .Where(c => c.Recency < oldBoundary)
            .Select(async c => {
                var chat = await Chats.Get(Session, c.ChatId, default).ConfigureAwait(false);
                return (PinnedChat: c, Chat: chat);
            })
            .Collect()
            .ConfigureAwait(false);

        var result = pinnedChats;
        foreach (var (c, chat) in chats) {
            if (chat != null)
                continue;
            result = result.Remove(c);
        }
        return result;
    }
}
