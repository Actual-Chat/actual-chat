using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatUI
{
    public const int MaxActiveChatCount = 3;

    private ChatPlayers? _chatPlayers;
    private readonly SharedResourcePool<Symbol, ISyncedState<long?>> _readStates;
    private readonly IUpdateDelayer _lastReadEntryStatesUpdateDelayer;
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IChats Chats { get; }
    private IReadPositions ReadPositions { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private UICommander UICommander { get; }

    private Moment Now => Clocks.SystemClock.Now;

    public IStoredState<ContactId> SelectedContact { get; }
    public ISyncedState<ImmutableHashSet<PinnedContact>> PinnedContacts { get; }
    public IStoredState<ImmutableHashSet<ActiveContact>> ActiveContacts { get; }
    public IMutableState<LinkedChatEntry?> LinkedChatEntry { get; }
    public IMutableState<ChatEntryId> HighlightedChatEntryId { get; }

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        StateFactory = services.StateFactory();
        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        ReadPositions = services.GetRequiredService<IReadPositions>();
        ModalUI = services.GetRequiredService<ModalUI>();
        Clocks = services.Clocks();
        UICommander = services.UICommander();

        var localSettings = services.LocalSettings();
        var accountSettings = services.AccountSettings().WithPrefix(nameof(ChatUI));
        SelectedContact = StateFactory.NewKvasStored<ContactId>(new(localSettings, nameof(SelectedContact)));
        PinnedContacts = StateFactory.NewKvasSynced<ImmutableHashSet<PinnedContact>>(
            new(accountSettings, nameof(PinnedContacts)) {
                InitialValue = ImmutableHashSet<PinnedContact>.Empty,
                Corrector = FixPinnedContacts,
            });
        ActiveContacts = StateFactory.NewKvasStored<ImmutableHashSet<ActiveContact>>(
            new (localSettings, nameof(ActiveContacts)) {
                InitialValue = ImmutableHashSet<ActiveContact>.Empty,
                Corrector = FixActiveContacts,
            });
        LinkedChatEntry = StateFactory.NewMutable<LinkedChatEntry?>();
        HighlightedChatEntryId = StateFactory.NewMutable<ChatEntryId>();

        // Read entry states from other windows / devices aren't delayed
        _lastReadEntryStatesUpdateDelayer = FixedDelayer.Instant;
        _readStates = new SharedResourcePool<Symbol, ISyncedState<long?>>(CreateReadState);
        var stateSync = services.GetRequiredService<ChatUIStateSync>();
        stateSync.Start();
    }

    [ComputeMethod]
    public virtual Task<bool> IsSelected(string contactId)
        => Task.FromResult(SelectedContact.Value == contactId);

    [ComputeMethod]
    public virtual Task<bool> IsPinned(string contactId)
        => Task.FromResult(PinnedContacts.Value.Contains(new ContactId(contactId)));

    [ComputeMethod]
    public virtual Task<bool> IsListening(string contactId)
        => Task.FromResult(ActiveContacts.Value.TryGetValue(new ContactId(contactId), out var chat) && chat.IsListening);

    [ComputeMethod]
    public virtual Task<bool> IsRecording(string contactId)
        => Task.FromResult(ActiveContacts.Value.TryGetValue(new ContactId(contactId), out var chat) && chat.IsRecording);

    [ComputeMethod]
    public virtual Task<ChatId> RecordingChatId()
    {
        var c = ActiveContacts.Value.FirstOrDefault(c => c.IsRecording);
        return Task.FromResult(c.IsRecording ? c.Id.GetChatId() : default);
    }

    [ComputeMethod]
    public virtual Task<ImmutableHashSet<ChatId>> ListeningChatIds()
        => Task.FromResult(ActiveContacts.Value.Where(c => c.IsListening).Select(c => c.Id.GetChatId()).ToImmutableHashSet());

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
        var listeningChatIds = await ListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count == 0 ? null : new RealtimeChatPlaybackState(listeningChatIds);
    }

    [ComputeMethod]
    public virtual async Task<bool> MustKeepAwake()
    {
        var recordingChatId = await RecordingChatId().ConfigureAwait(false);
        if (!recordingChatId.IsEmpty)
            return true;

        var listeningChatIds = await ListeningChatIds().ConfigureAwait(false);
        return listeningChatIds.Count > 0;
    }

    public ValueTask SetPinState(Symbol chatId, bool mustPin)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdatePinnedChats(
            pinnedChats => mustPin
                ? pinnedChats.Add(new PinnedContact(chatId, Now))
                : pinnedChats.Remove(chatId)
            );
    }

    public ValueTask SetListeningState(Symbol chatId, bool mustListen)
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
                activeChats = activeChats.Add(new ActiveContact(chatId, true, false, Now));
            return activeChats;
        });
    }

    public ValueTask SetRecordingChatId(Symbol recordingChatId)
        => UpdateActiveChats(activeChats => {
            var oldRecordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
            if (oldRecordingChat.ChatId == recordingChatId)
                return activeChats;
            if (!oldRecordingChat.ChatId.IsEmpty)
                activeChats = activeChats.AddOrUpdate(oldRecordingChat with { IsRecording = false, Recency = Now});
            if (!recordingChatId.IsEmpty) {
                var newRecordingChat = new ActiveContact(recordingChatId, true, true, Now);
                activeChats = activeChats.AddOrUpdate(newRecordingChat);
            }
            return activeChats;
        });

    public ValueTask AddActiveChat(Symbol chatId)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdateActiveChats(activeChats => activeChats.Add(new ActiveContact(chatId, false, false, Now)));
    }

    public ValueTask RemoveActiveChat(Symbol chatId)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        return UpdateActiveChats(activeChats => activeChats.Remove(chatId));
    }

    public void ShowAuthorModal(string authorId)
    {
        var parsedAuthorId = new AuthorId(authorId);
        ModalUI.Show(new AuthorModal.Model(parsedAuthorId.ChatId, parsedAuthorId.Id));
    }

    public void ShowDeleteMessageModal(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));

    public async ValueTask<SyncedStateLease<long?>> LeaseReadState(
        Symbol chatId,
        CancellationToken cancellationToken)
    {
        var lease = await _readStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        var result = new SyncedStateLease<long?>(lease);
        await result.WhenFirstTimeRead;
        return result;
    }

    private Task<ISyncedState<long?>> CreateReadState(Symbol chatId, CancellationToken cancellationToken)
        => Task.FromResult(StateFactory.NewCustomSynced<long?>(
            new(
                // Reader
                async ct => await ReadPositions.GetOwn(Session, chatId, ct).ConfigureAwait(false),
                // Writer
                async (lastReadEntryId, ct) => {
                    if (lastReadEntryId is not { } entryId)
                        return;

                    var command = new IReadPositions.SetCommand(Session, chatId, entryId);
                    await UICommander.Run(command, ct);
                }) { UpdateDelayer = _lastReadEntryStatesUpdateDelayer }
            ));

    // Private methods

    private async ValueTask UpdatePinnedChats(
        Func<ImmutableHashSet<PinnedContact>, ImmutableHashSet<PinnedContact>> updater,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var originalValue = PinnedContacts.Value;
        var updatedValue = updater.Invoke(originalValue);
        if (ReferenceEquals(originalValue, updatedValue))
            return;

        updatedValue = await FixPinnedContacts(updatedValue, cancellationToken).ConfigureAwait(false);
        PinnedContacts.Value = updatedValue;
    }

    private async ValueTask UpdateActiveChats(
        Func<ImmutableHashSet<ActiveContact>, ImmutableHashSet<ActiveContact>> updater,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var originalValue = ActiveContacts.Value;
        var updatedValue = updater.Invoke(originalValue);
        if (ReferenceEquals(originalValue, updatedValue))
            return;

        updatedValue = await FixActiveContacts(updatedValue, cancellationToken).ConfigureAwait(false);
        ActiveContacts.Value = updatedValue;
    }

    private async ValueTask<ImmutableHashSet<PinnedContact>> FixPinnedContacts(
        ImmutableHashSet<PinnedContact> pinnedChatIds,
        CancellationToken cancellationToken = default)
    {
        if (pinnedChatIds.Count < 32)
            return pinnedChatIds;

        var oldChatBoundary = Now - TimeSpan.FromDays(365);
        var rules = await pinnedChatIds
            .Where(c => c.Recency < oldChatBoundary)
            .Select(c => Chats.GetRules(Session, c.ChatId, default))
            .Collect()
            .ConfigureAwait(false);

        var result = pinnedChatIds;
        foreach (var r in rules) {
            if (r.CanRead())
                continue;
            result = result.Remove(r.ChatId);
        }
        return result;
    }

    private async ValueTask<ImmutableHashSet<ActiveContact>> FixActiveContacts(
        ImmutableHashSet<ActiveContact> activeChats,
        CancellationToken cancellationToken = default)
    {
        if (activeChats.Count == 0)
            return activeChats;

        // Removing chats that violate access rules + enforce "just 1 recording chat" rule
        var recordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
        var rules = await activeChats
            .Select(c => Chats.GetRules(Session, c.ChatId, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        foreach (var r in rules) {
            if (!activeChats.TryGetValue(r.ChatId, out var chat))
                continue; // Weird, but ok

            // There must be just 1 recording chat
            if (chat.IsRecording && chat != recordingChat) {
                chat = chat with { IsRecording = false };
                activeChats = activeChats.AddOrUpdate(chat);
            }

            // And it must be accessible
            if (!r.CanRead() || (chat.IsRecording && !r.CanRead()))
                activeChats = activeChats.Remove(chat.ChatId);
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

        async ValueTask<Moment> GetEffectiveRecency(ActiveContact chat, CancellationToken ct)
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
