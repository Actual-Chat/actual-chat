using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatUI
{
    private ChatPlayers? _chatPlayers;
    private readonly SharedResourcePool<Symbol, ISyncedState<long>> _lastReadEntryStates;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IChats Chats { get; }
    private IChatReadPositions ChatReadPositions { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private UICommander UICommander { get; }

    public IStoredState<Symbol> ActiveChatId { get; }
    public ISyncedState<ImmutableDictionary<string, Moment>> PinnedChatIds { get; }
    public IStoredState<Symbol> RecordingChatId { get; }
    public IStoredState<ImmutableList<Symbol>> ListeningChatIds { get; }
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
        PinnedChatIds = StateFactory.NewKvasSynced<ImmutableDictionary<string, Moment>>(
            new(accountSettings, nameof(PinnedChatIds)) {
                InitialValue = ImmutableDictionary<string, Moment>.Empty,
                Corrector = FixPinnedChatIds,
            });
        RecordingChatId = StateFactory.NewKvasStored<Symbol>(new(localSettings, nameof(RecordingChatId)));
        ListeningChatIds = StateFactory.NewKvasStored<ImmutableList<Symbol>>(
            new(localSettings, nameof(ListeningChatIds)) {
                InitialValue = ImmutableList<Symbol>.Empty,
                Corrector = FixListeningChatIds,
            });
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

    public void SetPinState(Symbol chatId, bool mustPin)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        PinnedChatIds.Set(pinnedChatIdsResult => {
            var pinnedChatIds = pinnedChatIdsResult.Value;
            return mustPin
                ? pinnedChatIds.Add(chatId, Clocks.SystemClock.Now)
                : pinnedChatIds.Remove(chatId);
        });
    }

    public void SetListeningState(Symbol chatId, bool mustListen)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        ListeningChatIds.Set(rListeningChatIds => {
            var listeningChatIds = rListeningChatIds.ValueOrDefault ?? ImmutableList<Symbol>.Empty;
            if (!mustListen)
                return listeningChatIds.Remove(chatId);
            if (listeningChatIds.Contains(chatId))
                return listeningChatIds;

            if (listeningChatIds.Count >= 4)
                listeningChatIds = listeningChatIds.RemoveAt(0);
            return listeningChatIds.Add(chatId);
        });
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
        var oldChatBoundary = Clocks.SystemClock.Now - TimeSpan.FromDays(365);
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
}
