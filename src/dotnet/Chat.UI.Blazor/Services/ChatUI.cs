using ActualChat.Kvas;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI
{
    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private IChats Chats { get; }
    private IChatReadPositions ChatReadPositions { get; }
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private Session Session { get; }

    public IMutableState<Symbol> ActiveChatId { get; }
    public IMutableState<Symbol> RecordingChatId { get; }
    public IMutableState<ImmutableDictionary<string, Moment>> PinnedChatIds { get; }
    public IMutableState<ImmutableArray<Symbol>> ListeningChatIds { get; }
    public IMutableState<bool> MustPlayPinnedChats { get; }
    public IMutableState<bool> MustPlayPinnedContactChats { get; }
    public IMutableState<bool> IsPlaying { get; }
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
        ActiveChatId = StateFactory.NewMutable<Symbol>();
        RecordingChatId = StateFactory.NewMutable<Symbol>();
        PinnedChatIds = StateFactory.NewKvasSynced<ImmutableDictionary<string, Moment>>(
            new(accountSettings, nameof(PinnedChatIds)) {
                InitialValue = ImmutableDictionary<string, Moment>.Empty,
                Corrector = FixPinnedChatIds,
            });
        ListeningChatIds = StateFactory.NewMutable(ImmutableArray<Symbol>.Empty);
        MustPlayPinnedChats = StateFactory.NewMutable<bool>();
        MustPlayPinnedContactChats = StateFactory.NewMutable<bool>();
        IsPlaying = StateFactory.NewMutable<bool>();
        LinkedChatEntry = StateFactory.NewMutable<ChatEntryLink?>();
        HighlightedChatEntryId = StateFactory.NewMutable<long>();

        _lastReadEntryIds = new SharedResourcePool<Symbol, IPersistentState<long>>(RestoreLastReadEntryId);
        var stateSync = Services.GetRequiredService<ChatUIStateSync>();
        stateSync.Start();
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackState?> GetRealtimeChatPlaybackState(CancellationToken cancellationToken)
    {
        var isPlaying = await IsPlaying.Use(cancellationToken).ConfigureAwait(false);
        if (!isPlaying)
            return null;

        var chatIds = ImmutableHashSet<Symbol>.Empty;
        var activeChatId = await ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
        if (!activeChatId.IsEmpty)
            chatIds = chatIds.Add(activeChatId);

        var mustPlayPinnedChats = await MustPlayPinnedChats.Use(cancellationToken).ConfigureAwait(false);
        var mustPlayPinnedContactChats = await MustPlayPinnedContactChats.Use(cancellationToken).ConfigureAwait(false);
        if (mustPlayPinnedChats || mustPlayPinnedContactChats) {
            var pinnedChatIds = await PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
            foreach (var (chatId, _) in pinnedChatIds) {
                var chatType = new ParsedChatId(chatId).Kind.ToChatType();
                var mustPlay = chatType switch {
                    ChatType.Group => mustPlayPinnedChats,
                    ChatType.Peer => mustPlayPinnedContactChats,
                    _ => false,
                };
                if (mustPlay)
                    chatIds = chatIds.Add(chatId);
            }
        }

        return chatIds.Count == 0 ? null : new RealtimeChatPlaybackState(chatIds);
    }

    [ComputeMethod]
    public virtual async Task<bool> MustKeepAwake(CancellationToken cancellationToken)
    {
        var isPlaying = await IsPlaying.Use(cancellationToken).ConfigureAwait(false);
        if (isPlaying)
            return true;

        var recordingChatId = await RecordingChatId.Use(cancellationToken).ConfigureAwait(false);
        return !recordingChatId.IsEmpty;
    }

    public void SetPinState(Symbol chatId, bool mustPin)
        => PinnedChatIds.Set(pinnedChatIdsResult => {
            var pinnedChatIds = pinnedChatIdsResult.Value;
            var isPinned = pinnedChatIds.ContainsKey(chatId);
            if (isPinned == mustPin)
                return pinnedChatIds;
            return mustPin
                ? pinnedChatIds.Add(chatId, Clocks.SystemClock.Now)
                : pinnedChatIds.Remove(chatId);
        });

    public void TogglePinState(Symbol chatId)
        => PinnedChatIds.Set(pinnedChatIdsResult => {
            var pinnedChatIds = pinnedChatIdsResult.Value;
            var mustPin = !pinnedChatIds.ContainsKey(chatId);
            return mustPin
                ? pinnedChatIds.Add(chatId, Clocks.SystemClock.Now)
                : pinnedChatIds.Remove(chatId);
        });

    public async Task<ImmutableArray<Symbol>> AddToListeningChatIds(string chatId, CancellationToken cancellationToken)
    {
        var listeningChatIds = await ListeningChatIds.Use(cancellationToken).ConfigureAwait(false);
        var contains = listeningChatIds.Contains(chatId);
        var count = listeningChatIds.Length;

        if (count >= 4)
            listeningChatIds = contains ? listeningChatIds.Remove(chatId) : listeningChatIds.RemoveAt(0);
        else
            listeningChatIds = contains ? listeningChatIds.Remove(chatId) : listeningChatIds;
        listeningChatIds = listeningChatIds.Add(chatId);
        return listeningChatIds;
    }

    public void ShowChatAuthorDialog(string authorId)
        => ModalUI.Show(new ChatAuthorDialog.Model(authorId));

    public void ShowDeleteMessageRequest(ChatMessageModel model)
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
