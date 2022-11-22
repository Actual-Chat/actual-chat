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
    private ContactUI? _contactUI;
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
    private ContactUI ContactUI => _contactUI ??= Services.GetRequiredService<ContactUI>();
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private UICommander UICommander { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IStoredState<ImmutableHashSet<ActiveChat>> ActiveChats { get; }
    public IMutableState<LinkedChatEntry?> LinkedChatEntry { get; }
    public IMutableState<ChatEntryId> HighlightedChatEntryId { get; }

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
        ActiveChats = StateFactory.NewKvasStored<ImmutableHashSet<ActiveChat>>(
            new (localSettings, nameof(ActiveChats)) {
                InitialValue = ImmutableHashSet<ActiveChat>.Empty,
                Corrector = FixActiveChats,
            });
        LinkedChatEntry = StateFactory.NewMutable<LinkedChatEntry?>();
        HighlightedChatEntryId = StateFactory.NewMutable<ChatEntryId>();

        // Read entry states from other windows / devices are delayed by 1s
        _readStateUpdateDelayer = FixedDelayer.Get(1);
        _readStates = new SharedResourcePool<Symbol, ISyncedState<long?>>(CreateReadState);
        var uiStateSync = services.GetRequiredService<UIStateSync>();
        uiStateSync.Start();
    }

    [ComputeMethod]
    public virtual async Task<ChatState?> GetState(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty)
            return null;

        var accountTask = Accounts.GetOwn(Session, cancellationToken);
        var chatTask = Chats.Get(Session, chatId, cancellationToken);
        var chatSummaryTask = Chats.GetSummary(Session, chatId, cancellationToken);
        var lastMentionTask = Mentions.GetLastOwn(Session, chatId, cancellationToken);
        var readEntryIdTask = GetReadEntryId(chatId, cancellationToken);

        var isSelectedTask = IsSelected(chatId);
        var isPinnedTask = IsPinned(chatId);
        var isListeningTask = IsListening(chatId);
        var isRecordingTask = IsRecording(chatId);

        var account = await accountTask.ConfigureAwait(false);
        if (account == null)
            return null;

        var contactId = new ContactId(account.Id, chatId, ParseOptions.Skip);
        var contact = await Contacts.Get(Session, contactId, cancellationToken).ConfigureAwait(false);

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
        => Task.FromResult(ContactUI.SelectedContactId.Value.ChatId.Id == chatId);

    [ComputeMethod]
    public virtual Task<bool> IsPinned(ChatId chatId)
        => Task.FromResult(ContactUI.PinnedContacts.Value.Any(c => c.Id.ChatId == chatId));

    [ComputeMethod]
    public virtual Task<bool> IsListening(ChatId chatId)
        => Task.FromResult(ActiveChats.Value.TryGetValue(chatId, out var c) && c.IsListening);

    [ComputeMethod]
    public virtual Task<bool> IsRecording(ChatId chatId)
        => Task.FromResult(ActiveChats.Value.TryGetValue(chatId, out var c) && c.IsRecording);

    [ComputeMethod]
    public virtual Task<ChatId> GetRecordingChatId()
        => Task.FromResult(ActiveChats.Value.FirstOrDefault(c => c.IsRecording).Id);

    [ComputeMethod]
    public virtual Task<ImmutableHashSet<ChatId>> GetListeningChatIds()
        => Task.FromResult(ActiveChats.Value.Where(c => c.IsListening).Select(c => c.Id).ToImmutableHashSet());

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
        if (!recordingChatId.IsEmpty)
            return true;

        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count > 0;
    }

    // SetXxx & Add/RemoveXxx

    public ValueTask SetListeningState(ChatId chatId, bool mustListen)
    {
        if (chatId.IsEmpty)
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
            if (oldChat.Id == chatId)
                return activeChats;
            if (!oldChat.Id.IsEmpty)
                activeChats = activeChats.AddOrUpdate(oldChat with {
                    IsRecording = false,
                    Recency = Now,
                });
            if (!chatId.IsEmpty) {
                var newChat = new ActiveChat(chatId, true, true, Now);
                activeChats = activeChats.AddOrUpdate(newChat);
            }
            return activeChats;
        });

    public ValueTask AddActiveChat(ChatId chatId)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdateActiveChats(activeChats => activeChats.Add(new ActiveChat(chatId, false, false, Now)));
    }

    public ValueTask RemoveActiveChat(ChatId chatId)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdateActiveChats(activeChats => activeChats.Remove(chatId));
    }

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

    // Private methods

    // TODO: Make it non-nullable?
    private async ValueTask<long?> GetReadEntryId(ChatId chatId, CancellationToken cancellationToken)
    {
        using var readEntryState = await LeaseReadState(chatId, cancellationToken).ConfigureAwait(false);
        return await readEntryState.Use(cancellationToken).ConfigureAwait(false);
    }

    private Task<ISyncedState<long?>> CreateReadState(Symbol chatId, CancellationToken cancellationToken)
    {
        var pChatId = new ChatId(chatId, ParseOptions.Skip);
        return Task.FromResult(StateFactory.NewCustomSynced<long?>(
            new (
                // Reader
                async ct => await ReadPositions.GetOwn(Session, pChatId, ct).ConfigureAwait(false),
                // Writer
                async (readEntryId, ct) => {
                    if (readEntryId is not { } entryId)
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
                var rules = await Chats.GetRules(Session, chat.Id, default).ConfigureAwait(false);
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

            var chatIdRange = await Chats.GetIdRange(Session, chat.Id, ChatEntryKind.Audio, ct);
            var chatEntryReader = Chats.NewEntryReader(Session, chat.Id, ChatEntryKind.Audio);
            var lastEntry = await chatEntryReader.GetLast(chatIdRange, ct);
            if (lastEntry == null)
                return chat.Recency;
            return lastEntry.IsStreaming
                ? Clocks.CpuClock.Now
                : Moment.Max(chat.Recency, lastEntry.EndsAt ?? lastEntry.BeginsAt);
        }
    }
}
