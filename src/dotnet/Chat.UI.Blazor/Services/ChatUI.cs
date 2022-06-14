using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class ChatUI
{
    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private IChatReadPositions ChatReadPositions { get; }
    private ModalUI ModalUI { get; }
    private Session Session { get; }

    public IMutableState<Symbol> ActiveChatId { get; }
    public IMutableState<Symbol> RecordingChatId { get; }
    public IMutableState<ImmutableHashSet<Symbol>> PinnedChatIds { get; }
    public IMutableState<bool> MustPlayPinnedChats { get; }
    public IMutableState<bool> MustPlayPinnedContactChats { get; }
    public IMutableState<bool> IsPlaying { get; }
    public IMutableState<ChatEntry?> RepliedChatEntry { get; }

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        ModalUI = services.GetRequiredService<ModalUI>();
        Session = services.GetRequiredService<Session>();

        StateFactory = services.StateFactory();
        ChatReadPositions = services.GetRequiredService<IChatReadPositions>();
        ActiveChatId = StateFactory.NewMutable<Symbol>();
        RecordingChatId = StateFactory.NewMutable<Symbol>();
        PinnedChatIds = StateFactory.NewMutable(ImmutableHashSet<Symbol>.Empty);
        MustPlayPinnedChats = StateFactory.NewMutable<bool>();
        MustPlayPinnedContactChats = StateFactory.NewMutable<bool>();
        IsPlaying = StateFactory.NewMutable<bool>();
        RepliedChatEntry = StateFactory.NewMutable<ChatEntry?>();

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
            foreach (var chatId in pinnedChatIds) {
                var chatType = ChatId.GetChatType(chatId);
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

    public void ShowChatAuthorDialog(string authorId)
        => ModalUI.Show(new ChatAuthorDialog.Model(authorId));

    public void ShowDeleteMessageRequest(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));
}
