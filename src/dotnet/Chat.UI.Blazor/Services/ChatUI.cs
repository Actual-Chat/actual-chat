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
    public IMutableState<bool> IsPlayingActive { get; }
    public IMutableState<bool> IsPlayingPinned { get; }

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        ModalUI = services.GetRequiredService<ModalUI>();

        var stateFactory = services.StateFactory();
        ActiveChatId = stateFactory.NewMutable<Symbol>();
        RecordingChatId = stateFactory.NewMutable<Symbol>();
        PinnedChatIds = stateFactory.NewMutable(ImmutableHashSet<Symbol>.Empty);
        IsPlayingActive = stateFactory.NewMutable<bool>();
        IsPlayingPinned = stateFactory.NewMutable<bool>();

        var stateSync = Services.GetRequiredService<ChatUIStateSync>();
        stateSync.Start();
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackState?> GetRealtimeChatPlaybackState(CancellationToken cancellationToken)
    {
        var chatIds = ImmutableHashSet<Symbol>.Empty;

        var isPlayingActive = await IsPlayingActive.Use(cancellationToken).ConfigureAwait(false);
        var isPlayingPinned = await IsPlayingPinned.Use(cancellationToken).ConfigureAwait(false);

        if (isPlayingActive) {
            var activeChatId = await ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
            if (!activeChatId.IsEmpty)
                chatIds = chatIds.Add(activeChatId);

        }
        if (isPlayingPinned) {
            var pinnedChatIds = await PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(pinnedChatIds);
        }

        return chatIds.Count == 0 ? null : new RealtimeChatPlaybackState(chatIds);
    }

    public void ShowChatAuthorDialog(string authorId)
        => ModalUI.Show(new ChatAuthorDialog.Model(authorId));

    public void ShowDeleteMessageRequest(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));
}
