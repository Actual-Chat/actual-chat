using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI
{
    private ChatPlayers? _chatPlayers;

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private IChats Chats { get; }
    private IChatReadPositions ChatReadPositions { get; }
    private ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private Session Session { get; }

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
        Chats = services.GetRequiredService<IChats>();
        ChatReadPositions = services.GetRequiredService<IChatReadPositions>();
        ModalUI = services.GetRequiredService<ModalUI>();
        Clocks = services.Clocks();
        Session = services.GetRequiredService<Session>();

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
            });
        LinkedChatEntry = StateFactory.NewMutable<ChatEntryLink?>();
        HighlightedChatEntryId = StateFactory.NewMutable<long>();

        _lastReadEntryIds = new SharedResourcePool<Symbol, IPersistentState<long>>(RestoreLastReadEntryId);
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
}
