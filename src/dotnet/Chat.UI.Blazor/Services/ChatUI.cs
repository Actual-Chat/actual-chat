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

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        ModalUI = services.GetRequiredService<ModalUI>();

        var stateFactory = services.StateFactory();
        ActiveChatId = stateFactory.NewMutable<Symbol>();
        RecordingChatId = stateFactory.NewMutable<Symbol>();
        PinnedChatIds = stateFactory.NewMutable(ImmutableHashSet<Symbol>.Empty);

        var stateSync = Services.GetRequiredService<ChatUIStateSync>();
        stateSync.Start();
    }

    [ComputeMethod]
    public virtual async Task<RealtimeChatPlaybackState> GetRealtimeChatPlaybackState(
        bool mustPlayPinned, CancellationToken cancellationToken)
    {
        var activeChatId = await ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
        var chatIds = ImmutableHashSet<Symbol>.Empty;
        chatIds = activeChatId.IsEmpty ? chatIds : chatIds.Add(activeChatId);
        if (mustPlayPinned) {
            var pinnedChatIds = await PinnedChatIds.Use(cancellationToken).ConfigureAwait(false);
            chatIds = chatIds.Union(pinnedChatIds);
        }
        return new RealtimeChatPlaybackState(chatIds, mustPlayPinned);
    }

    public void ShowChatAuthorCard(string authorId)
        => ModalUI.Show(new ChatAuthorCard.Model(authorId));

    public void ShowDeleteMessageRequest(ChatMessageModel model)
        => ModalUI.Show(new DeleteMessageModal.Model(model));
}
