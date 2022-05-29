using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatUI
{
    private IServiceProvider Services { get; }
    private ModalUI ModalUI { get; }

    public IMutableState<Symbol> ActiveChatId { get; }
    public IMutableState<Symbol> RecordingChatId { get; }
    public IMutableState<ImmutableHashSet<Symbol>> PinnedChatIds { get; }
    public IMutableState<bool> MustPlayPinnedChats { get; }
    public IMutableState<bool> MustPlayPinnedContactChats { get; }
    public IMutableState<bool> IsPlaying { get; }

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        ModalUI = services.GetRequiredService<ModalUI>();

        var stateFactory = services.StateFactory();
        ActiveChatId = stateFactory.NewMutable<Symbol>();
        RecordingChatId = stateFactory.NewMutable<Symbol>();
        PinnedChatIds = stateFactory.NewMutable(ImmutableHashSet<Symbol>.Empty);
        MustPlayPinnedChats = stateFactory.NewMutable<bool>();
        MustPlayPinnedContactChats = stateFactory.NewMutable<bool>();
        IsPlaying = stateFactory.NewMutable<bool>();

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

    public void ShowChatAuthorDialog(string authorId)
        => ModalUI.Show(new ChatAuthorDialog.Model(authorId));

    public void ShowDeleteMessageRequest(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));
}
