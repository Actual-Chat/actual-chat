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
    private readonly SharedResourcePool<Symbol, ISyncedState<long>> _lastReadEntryStates;
    private readonly ISyncedState<ImmutableDictionary<string, Moment>> _pinnedChatIds;
    private readonly IStoredState<Symbol> _recordingChatId;
    private readonly IStoredState<ImmutableList<Symbol>> _listeningChatIds;
    private readonly IMutableState<ImmutableList<(Symbol, Moment)>> _activeChatIds;
    private readonly AsyncLock _asyncLock = new AsyncLock(ReentryMode.CheckedPass);

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
    public IState<ImmutableList<(Symbol, Moment)>> ActiveChatIds => _activeChatIds;
    public IMutableState<ChatEntryLink?> LinkedChatEntry { get; }
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

        var localSettings = services.GetRequiredService<LocalSettings>().WithPrefix(nameof(ChatUI));
        var accountSettings = services.GetRequiredService<AccountSettings>().WithPrefix(nameof(ChatUI));
        ActiveChatId = StateFactory.NewKvasStored<Symbol>(new(localSettings, nameof(ActiveChatId)));
        _pinnedChatIds = StateFactory.NewKvasSynced<ImmutableDictionary<string, Moment>>(
            new(accountSettings, nameof(PinnedChatIds)) {
                InitialValue = ImmutableDictionary<string, Moment>.Empty,
                Corrector = FixPinnedChatIds,
            });
        _recordingChatId = StateFactory.NewKvasStored<Symbol>(new(localSettings, nameof(RecordingChatId)));
        _listeningChatIds = StateFactory.NewKvasStored<ImmutableList<Symbol>>(
            new(localSettings, nameof(ListeningChatIds)) {
                InitialValue = ImmutableList<Symbol>.Empty,
                Corrector = FixListeningChatIds,
            });
        _activeChatIds = StateFactory.NewMutable(ImmutableList<(Symbol, Moment)>.Empty);
        Task.WhenAll(_listeningChatIds.WhenRead, _recordingChatId.WhenRead)
            .ContinueWith(_ => InitializeRecentChatIds(), TaskScheduler.Default);
        LinkedChatEntry = StateFactory.NewMutable<ChatEntryLink?>();
        HighlightedChatEntryId = StateFactory.NewMutable<long>();

        _lastReadEntryStates = new SharedResourcePool<Symbol, ISyncedState<long>>(CreateLastReadEntryState);
        var stateSync = Services.GetRequiredService<ChatUIStateSync>();
        stateSync.Start();
    }

    [ComputeMethod]
    public virtual async Task<bool> IsPinned(Symbol chatId, CancellationToken cancellationToken)
    {
        var pinnedChatIds = await PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
        return pinnedChatIds.ContainsKey(chatId);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsListening(Symbol chatId, CancellationToken cancellationToken)
    {
        var listeningChatIds = await ListeningChatIds.Use(cancellationToken).ConfigureAwait(false);
        return listeningChatIds.Contains(chatId);
    }

    [ComputeMethod]
    public virtual async Task<SingleChatPlaybackState> GetSingleChatPlaybackState(Symbol chatId, CancellationToken cancellationToken)
    {
        var listeningChatIds = await ListeningChatIds.Use(cancellationToken).ConfigureAwait(false);
        var isListening = listeningChatIds.Contains(chatId);
        var chatPlaybackState = await ChatPlayers.ChatPlaybackState.Use(cancellationToken).ConfigureAwait(false);
        var isPlayingHistorical = chatPlaybackState is HistoricalChatPlaybackState;
        return new SingleChatPlaybackState(chatId, isListening, isPlayingHistorical);
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackState?> GetRealtimeChatPlaybackState(CancellationToken cancellationToken)
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
                var itemToReplace = activeChatIds.Find(c => c.Item1 == chatIdToEliminate);
                activeChatIds = activeChatIds.Replace(itemToReplace, (chatId, Now));

            }
            else {
                listeningChatIds = listeningChatIds.Add(chatId);
                if (activeChatIds.All(c => c.Item1 != chatId))
                    activeChatIds = activeChatIds.Add((chatId, Now));
            }
        }
        _listeningChatIds.Value = listeningChatIds;
        if (activeChatIds != _activeChatIds.Value)
            _activeChatIds.Value = activeChatIds;

        CleanupActiveChatIds();
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
                var itemToReplace = activeChatIds.Find(c => c.Item1 == listenChatIdToEliminate);
                activeChatIds = activeChatIds.Replace(itemToReplace, (chatId, Now));
            }
        }
        _recordingChatId.Value = chatId;

        if (!chatId.IsEmpty && activeChatIds.All(c => c.Item1 != chatId))
            activeChatIds = activeChatIds.Add((chatId, Now));
        if (_activeChatIds.Value != activeChatIds)
            _activeChatIds.Value = activeChatIds;

        CleanupActiveChatIds();
    }

    public async Task RemoveActiveChat(Symbol chatId)
    {
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        if (RecordingChatId.Value == chatId)
            await SetRecordingState(Symbol.Empty).ConfigureAwait(false);
        await SetListeningState(chatId, false).ConfigureAwait(false);

        _activeChatIds.Value = _activeChatIds.Value.RemoveAll(c => c.Item1 == chatId);
    }

    public void ShowChatAuthorModal(string authorId)
        => ModalUI.Show(new ChatAuthorModal.Model(authorId));

    public void ShowDeleteMessageModal(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));

    public async Task<SyncedStateLease<long>> LeaseLastReadEntryState(
        Symbol chatId,
        CancellationToken cancellationToken)
    {
        var lease = await _lastReadEntryStates.Rent(chatId, cancellationToken).ConfigureAwait(false);
        return new SyncedStateLease<long>(lease);
    }

    private Task<ISyncedState<long>> CreateLastReadEntryState(Symbol chatId, CancellationToken cancellationToken)
        => Task.FromResult(StateFactory.NewCustomSynced<long>(
            new(
                // Reader
                async ct => await ChatReadPositions.Get(Session, chatId, ct).ConfigureAwait(false) ?? 0,
                // Writer
                async (lastReadEntryId, ct) => {
                    var command = new IChatReadPositions.SetReadPositionCommand(Session, chatId, lastReadEntryId);
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
        var rules = await listeningChatIds
            .Select(chatId => Chats.GetRules(Session, chatId, default))
            .Collect()
            .ConfigureAwait(false);

        foreach (var r in rules) {
            if (r.CanRead())
                continue;
            listeningChatIds = listeningChatIds.RemoveAll(chatId => chatId == r.ChatId);
        }
        return listeningChatIds;
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

        var candidatesToEliminate = listeningChatIds;
        if (!recordingChatId.IsEmpty)
            candidatesToEliminate = candidatesToEliminate.Remove(recordingChatId);
        var unpinnedChats = candidatesToEliminate.Where(c => !IsPinnedChat(c)).ToArray();
        var chatIdToEliminate = await GetChatToEliminate(unpinnedChats).ConfigureAwait(false);
        if (chatIdToEliminate.IsEmpty) {
            var pinnedChats = candidatesToEliminate.Where(c => IsPinnedChat(c)).ToArray();
            chatIdToEliminate = await GetChatToEliminate(pinnedChats).ConfigureAwait(false);
        }

        return chatIdToEliminate;
    }

    private async Task<Dictionary<Symbol, int>> GetListeningChatPriorities(ICollection<Symbol> chatIds)
    {
        var timestamps = await chatIds
                .Select(chatId => GetLastEntryTimestamp(chatId))
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
        var lastEditableEntry = await chatEntryReader.GetLast(chatIdRange, x => true, CancellationToken.None);
        if (lastEditableEntry == null)
            return Moment.EpochStart;
        return lastEditableEntry.BeginsAt;
    }

    private async Task InitializeRecentChatIds()
    {
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        var activeChatIds = ImmutableList<(Symbol, Moment)>.Empty;
        var now = Now;
        foreach (var chatId in _listeningChatIds.Value)
            activeChatIds = activeChatIds.Add((chatId, now));
        var recordingChatId = _recordingChatId.Value;
        if (!recordingChatId.IsEmpty && activeChatIds.All(c => c.Item1 != recordingChatId))
            activeChatIds = activeChatIds.Add((recordingChatId, now));
        _activeChatIds.Value = activeChatIds;
    }

    private void CleanupActiveChatIds()
    {
        var activeChatIds = _activeChatIds.Value;
        if (activeChatIds.Count <= ActiveChatsLimit)
            return;
        var retainIds = _listeningChatIds.Value;
        var recordingChatId = _recordingChatId.Value;
        if (!recordingChatId.IsEmpty && !retainIds.Contains(recordingChatId))
            retainIds = retainIds.Add(recordingChatId);
        while (activeChatIds.Count > ActiveChatsLimit) {
            var itemToEliminate = activeChatIds
                .Where(c => !retainIds.Contains(c.Item1))
                .MinBy(c => c.Item2);
            activeChatIds = activeChatIds.Remove(itemToEliminate);
        }
        if (_activeChatIds.Value != activeChatIds)
            _activeChatIds.Value = activeChatIds;
    }
}
