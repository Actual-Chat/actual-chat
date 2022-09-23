using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatUI
{
    public const int ActiveChatsLimit = 4;

    private ChatPlayers? _chatPlayers;
    private readonly SharedResourcePool<Symbol, ISyncedState<long?>> _lastReadEntryStates;
    private readonly ISyncedState<ImmutableDictionary<string, Moment>> _pinnedChatIds;
    private readonly IStoredState<Symbol> _recordingChatId;
    private readonly IStoredState<ImmutableList<Symbol>> _listeningChatIds;
    private readonly ISyncedState<ImmutableList<(Symbol ChatId, Moment Recency)>> _activeChatIds;
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IChats Chats { get; }
    private IChatReadPositions ChatReadPositions { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private UICommander UICommander { get; }

    private Moment Now => Clocks.SystemClock.Now;

    public IStoredState<Symbol> ActiveChatId { get; }

    public IState<ImmutableDictionary<string, Moment>> PinnedChatIds => _pinnedChatIds;
    public IState<Symbol> RecordingChatId => _recordingChatId;
    public IState<ImmutableList<Symbol>> ListeningChatIds => _listeningChatIds;
    public IState<ImmutableList<(Symbol ChatId, Moment Recency)>> ActiveChatIds => _activeChatIds;
    public IMutableState<LinkedChatEntry?> LinkedChatEntry { get; }
    public IMutableState<long> HighlightedChatEntryId { get; }

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        StateFactory = services.StateFactory();
        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        ChatReadPositions = services.GetRequiredService<IChatReadPositions>();
        ModalUI = services.GetRequiredService<ModalUI>();
        Clocks = services.Clocks();
        UICommander = services.UICommander();

        var localSettings = services.LocalSettings();
        var accountSettings = services.AccountSettings().WithPrefix(nameof(ChatUI));
        ActiveChatId = StateFactory.NewKvasStored<Symbol>(new(localSettings, nameof(ActiveChatId)));
        _pinnedChatIds = StateFactory.NewKvasSynced<ImmutableDictionary<string, Moment>>(
            new(accountSettings, nameof(PinnedChatIds)) {
                InitialValue = ImmutableDictionary<string, Moment>.Empty,
                Corrector = FixPinnedChatIds,
            });
        _activeChatIds = StateFactory.NewKvasSynced<ImmutableList<(Symbol, Moment)>>(
            new (accountSettings, nameof(ActiveChatIds)) {
                InitialValue = ImmutableList<(Symbol, Moment)>.Empty,
                Corrector = FixActiveChatIds,
                Serializer = KvasSerializers<ImmutableList<(Symbol, Moment)>>.ValueTupleSerializer
            });
        _recordingChatId = StateFactory.NewKvasStored<Symbol>(new(localSettings, nameof(RecordingChatId)) {
            Corrector = FixRecordingChatId,
        });
        _listeningChatIds = StateFactory.NewKvasStored<ImmutableList<Symbol>>(
            new(localSettings, nameof(ListeningChatIds)) {
                InitialValue = ImmutableList<Symbol>.Empty,
                Corrector = FixListeningChatIds,
            });
        Task.WhenAll(_listeningChatIds.WhenRead, _recordingChatId.WhenRead)
            .ContinueWith(_ => InitActiveChatIds(), TaskScheduler.Default);
        LinkedChatEntry = StateFactory.NewMutable<LinkedChatEntry?>();
        HighlightedChatEntryId = StateFactory.NewMutable<long>();

        _lastReadEntryStates = new SharedResourcePool<Symbol, ISyncedState<long?>>(CreateLastReadEntryState);
        var stateSync = Services.GetRequiredService<ChatUIStateSync>();
        stateSync.Start();
    }

    [ComputeMethod]
    public virtual Task<bool> IsPinned(string chatId) => Task.FromResult(PinnedChatIds.Value.ContainsKey(chatId));
    [ComputeMethod]
    public virtual Task<bool> IsListening(string chatId) => Task.FromResult(ListeningChatIds.Value.Contains(chatId));
    [ComputeMethod]
    public virtual Task<bool> IsRecording(string chatId) => Task.FromResult(RecordingChatId.Value == chatId);
    [ComputeMethod]
    public virtual Task<bool> IsActive(string chatId) => Task.FromResult(ActiveChatId.Value == chatId);

    [ComputeMethod]
    public virtual async Task<SingleChatPlaybackState> GetPlaybackState(Symbol chatId, CancellationToken cancellationToken)
    {
        var isListening = await IsListening(chatId).ConfigureAwait(false);
        var chatPlaybackState = await ChatPlayers.ChatPlaybackState.Use(cancellationToken).ConfigureAwait(false);
        var isPlayingHistorical = chatPlaybackState is HistoricalChatPlaybackState x && x.ChatId == chatId;
        return new SingleChatPlaybackState(chatId, isListening, isPlayingHistorical);
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackState?> GetRealtimePlaybackState(CancellationToken cancellationToken)
    {
        var listeningChatIds = await ListeningChatIds.Use(cancellationToken).ConfigureAwait(false);
        return !listeningChatIds.Any() ? null : new RealtimeChatPlaybackState(listeningChatIds.ToImmutableHashSet());
    }

    [ComputeMethod]
    public virtual async Task<bool> MustKeepAwake(CancellationToken cancellationToken)
    {
        var recordingChatId = await RecordingChatId.Use(cancellationToken).ConfigureAwait(false);
        if (!recordingChatId.IsEmpty)
            return true;

        var listeningChatIds = await ListeningChatIds.Use(cancellationToken).ConfigureAwait(false);
        return listeningChatIds.Any();
    }

    public async Task SetPinState(Symbol chatId, bool mustPin)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        _pinnedChatIds.Value = mustPin
            ? _pinnedChatIds.Value.Add(chatId, Now)
            : _pinnedChatIds.Value.Remove(chatId);
    }

    public async Task SetListeningState(Symbol chatId, bool mustListen)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        var listeningChatIds = _listeningChatIds.Value;
        var activeChatIds = _activeChatIds.Value;

        if (!mustListen) {
            listeningChatIds = listeningChatIds.Remove(chatId);
        }
        else if (!listeningChatIds.Contains(chatId)) {
            var recordingChatId = RecordingChatId.Value;
            var chatIdToEliminate = await GetListeningChatIdToEliminate(listeningChatIds, chatId, recordingChatId)
                .ConfigureAwait(false);

            if (!chatIdToEliminate.IsEmpty) {
                listeningChatIds = listeningChatIds.Replace(chatIdToEliminate, chatId);
                var itemToReplace = activeChatIds.Find(c => c.ChatId == chatIdToEliminate);
                activeChatIds = activeChatIds.Replace(itemToReplace!, (chatId, Now));
            }
            else {
                listeningChatIds = listeningChatIds.Add(chatId);
                if (activeChatIds.All(c => c.ChatId != chatId))
                    activeChatIds = activeChatIds.Add((chatId, Now));
            }
        }
        _listeningChatIds.Value = listeningChatIds;
        PruneAndSetActiveChatIds(activeChatIds);
    }

    public async Task SetRecordingState(Symbol chatId)
    {
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        var activeChatIds = _activeChatIds.Value;
        if (!chatId.IsEmpty && !_listeningChatIds.Value.Contains(chatId)) {
            var listenChatIdToEliminate = await GetListeningChatIdToEliminate(_listeningChatIds.Value, Symbol.Empty, chatId)
                .ConfigureAwait(false);
            if (!listenChatIdToEliminate.IsEmpty) {
                await SetListeningState(listenChatIdToEliminate, false).ConfigureAwait(false);
                var itemToReplace = activeChatIds.Find(c => c.ChatId == listenChatIdToEliminate);
                activeChatIds = activeChatIds.Replace(itemToReplace!, (chatId, Now));
            }
        }
        _recordingChatId.Value = chatId;

        if (!chatId.IsEmpty && activeChatIds.All(c => c.ChatId != chatId))
            activeChatIds = activeChatIds.Add((chatId, Now));
        PruneAndSetActiveChatIds(activeChatIds);
    }

    public async Task AddActiveChat(Symbol chatId)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        using var _ = await _asyncLock.Lock().ConfigureAwait(false);

        var activeChatIds = _activeChatIds.Value;
        if (activeChatIds.All(c => c.ChatId != chatId))
            activeChatIds = activeChatIds.Add((chatId, Now));
        PruneAndSetActiveChatIds(activeChatIds);
    }

    public async Task RemoveActiveChat(Symbol chatId)
    {
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        if (RecordingChatId.Value == chatId)
            await SetRecordingState(Symbol.Empty).ConfigureAwait(false);
        await SetListeningState(chatId, false).ConfigureAwait(false);

        _activeChatIds.Value = _activeChatIds.Value.RemoveAll(c => c.ChatId == chatId);
    }

    public void ShowChatAuthorModal(string authorId)
        => ModalUI.Show(new ChatAuthorModal.Model(authorId));

    public void ShowDeleteMessageModal(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));

    public async ValueTask<SyncedStateLease<long?>> LeaseLastReadEntryState(
        Symbol chatId,
        CancellationToken cancellationToken)
    {
        var lease = await _lastReadEntryStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        var result = new SyncedStateLease<long?>(lease);
        await result.WhenFirstTimeRead;
        return result;
    }

    private Task<ISyncedState<long?>> CreateLastReadEntryState(Symbol chatId, CancellationToken cancellationToken)
        => Task.FromResult(StateFactory.NewCustomSynced<long?>(
            new(
                // Reader
                async ct => await ChatReadPositions.Get(Session, chatId, ct).ConfigureAwait(false),
                // Writer
                async (lastReadEntryId, ct) => {
                    if (lastReadEntryId is not { } entryId)
                        return;

                    var command = new IChatReadPositions.SetReadPositionCommand(Session, chatId, entryId);
                    await UICommander.Run(command, ct);
                })
            ));

    // Private methods

    private async ValueTask<ImmutableDictionary<string, Moment>> FixPinnedChatIds(
        ImmutableDictionary<string, Moment> pinnedChatIds,
        CancellationToken cancellationToken)
    {
        if (pinnedChatIds.Count < 32)
            return pinnedChatIds;
        var oldChatBoundary = Now - TimeSpan.FromDays(365);
        var rules = await pinnedChatIds
            .Where(kv => kv.Value < oldChatBoundary)
            .Select(kv => Chats.GetRules(Session, kv.Key, default))
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

    private async ValueTask<ImmutableList<Symbol>> FixListeningChatIds(
        ImmutableList<Symbol> listeningChatIds,
        CancellationToken cancellationToken)
    {
        if (listeningChatIds.Count == 0)
            return listeningChatIds;

        await _activeChatIds.WhenFirstTimeRead.ConfigureAwait(false);
        var activeChatIds = _activeChatIds.Value.Select(c => c.ChatId).ToHashSet();
        listeningChatIds = listeningChatIds.RemoveAll(c => !activeChatIds.Contains(c));

        var rules = await listeningChatIds
            .Select(chatId => Chats.GetRules(Session, chatId, cancellationToken))
            .Collect()
            .ConfigureAwait(false);

        foreach (var r in rules) {
            if (r.CanRead())
                continue;
            listeningChatIds = listeningChatIds.RemoveAll(chatId => chatId == r.ChatId);
        }
        return listeningChatIds;
    }

    private async ValueTask<Symbol> FixRecordingChatId(
        Symbol recordingChatId,
        CancellationToken cancellationToken)
    {
        if (recordingChatId.IsEmpty)
            return Symbol.Empty;

        await _activeChatIds.WhenFirstTimeRead.ConfigureAwait(false);
        var activeChatIds = _activeChatIds.Value;
        if (activeChatIds.All(c => c.ChatId != recordingChatId))
            return Symbol.Empty;

        var rules = await Chats.GetRules(Session, recordingChatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanWrite())
            return Symbol.Empty;
        return recordingChatId;
    }

    private async ValueTask<ImmutableList<(Symbol, Moment)>> FixActiveChatIds(
        ImmutableList<(Symbol ChatId, Moment Recency)> activeChatIds,
        CancellationToken cancellationToken)
    {
        activeChatIds = activeChatIds.Where(c => !c.ChatId.IsEmpty).ToImmutableList();
        var rules = await activeChatIds
            .Select(kv => Chats.GetRules(Session, kv.ChatId, default))
            .Collect()
            .ConfigureAwait(false);

        var result = activeChatIds;
        foreach (var r in rules) {
            if (r.CanRead())
                continue;
            result = result.RemoveAll(c => c.ChatId == r.ChatId);
        }
        return result;
    }

    private async Task<Symbol> GetListeningChatIdToEliminate(ImmutableList<Symbol> listeningChatIds, Symbol listenChatId, Symbol recordingChatId)
    {
        // When active chats panel limit exceeded, we look for a less important chat to eliminate it from the list.
        var limit = ActiveChatsLimit;
        if (!listenChatId.IsEmpty) {
            if (!recordingChatId.IsEmpty && !listeningChatIds.Contains(recordingChatId) && listenChatId != recordingChatId)
                limit--; // reserve one slot for recording chat
        }

        if (listeningChatIds.Count < limit)
            return Symbol.Empty;

        var candidatesToEliminate = listeningChatIds;
        if (!recordingChatId.IsEmpty)
            candidatesToEliminate = candidatesToEliminate.Remove(recordingChatId);
        var unpinnedChats = candidatesToEliminate.Where(c => !IsPinnedChat(c)).ToArray();
        var chatIdToEliminate = await GetChatToEliminate(unpinnedChats).ConfigureAwait(false);
        if (chatIdToEliminate.IsEmpty) {
            var pinnedChats = candidatesToEliminate.Where(IsPinnedChat).ToArray();
            chatIdToEliminate = await GetChatToEliminate(pinnedChats).ConfigureAwait(false);
        }

        return chatIdToEliminate;

        bool IsPinnedChat(Symbol c)
            => PinnedChatIds.Value.Keys.Contains(c.Value, StringComparer.OrdinalIgnoreCase);

        async Task<Symbol> GetChatToEliminate(ICollection<Symbol> chatIds)
        {
            if (chatIds.Count == 0)
                return Symbol.Empty;
            if (chatIds.Count == 1)
                return chatIds.First();
            var priority = await GetListeningChatPriorities(chatIds).ConfigureAwait(false);
            return chatIds.MinBy(c => priority[c]);
        }
    }

    private async Task<Dictionary<Symbol, int>> GetListeningChatPriorities(ICollection<Symbol> chatIds)
    {
        var timestamps = await chatIds
                .Select(GetLastEntryTimestamp)
                .Collect()
                .ConfigureAwait(false);

        var orderedChatIds = chatIds.Zip(timestamps)
            .OrderByDescending(c => c.Second)
            .Select((c, i) => new { ChatId = c.First, Moment = c.Second, Index = i })
            .ToList();

        var priority = orderedChatIds.ToDictionary(c => c.ChatId, c => c.Index);
        return priority;
    }

    private async Task<Moment> GetLastEntryTimestamp(Symbol chatId)
    {
        var chatIdRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Audio, CancellationToken.None);
        var chatEntryReader = Chats.NewEntryReader(Session, chatId, ChatEntryType.Audio);
        var lastEditableEntry = await chatEntryReader.GetLast(chatIdRange, _ => true, CancellationToken.None);
        if (lastEditableEntry == null)
            return Moment.EpochStart;
        return lastEditableEntry.BeginsAt;
    }

    private async Task InitActiveChatIds()
    {
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        var activeChatIds = ImmutableList<(Symbol ChatId, Moment Recency)>.Empty;
        var now = Now;
        foreach (var chatId in _listeningChatIds.Value)
            activeChatIds = activeChatIds.Add((chatId, now));
        var recordingChatId = _recordingChatId.Value;
        if (!recordingChatId.IsEmpty && activeChatIds.All(c => c.ChatId != recordingChatId))
            activeChatIds = activeChatIds.Add((recordingChatId, now));
        _activeChatIds.Value = activeChatIds;
    }

    private void PruneAndSetActiveChatIds(ImmutableList<(Symbol ChatId, Moment Recency)> activeChatIds)
    {
        if (activeChatIds.Count > ActiveChatsLimit) {
            var retainIds = _listeningChatIds.Value;
            var recordingChatId = _recordingChatId.Value;
            if (!recordingChatId.IsEmpty && !retainIds.Contains(recordingChatId))
                retainIds = retainIds.Add(recordingChatId);
            while (activeChatIds.Count > ActiveChatsLimit) {
                var itemToEliminate = activeChatIds
                    .Where(c => !retainIds.Contains(c.ChatId))
                    .MinBy(c => c.Recency);
                activeChatIds = activeChatIds.Remove(itemToEliminate!);
            }
        }
        if (_activeChatIds.Value != activeChatIds)
            _activeChatIds.Value = activeChatIds;
    }
}
