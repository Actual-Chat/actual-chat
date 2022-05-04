using Blazored.Modal;
using Blazored.Modal.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatUI
{
    private IServiceProvider Services { get; }
    private IModalService ModalService { get; }

    public IMutableState<Symbol> ActiveChatId { get; }
    public IMutableState<Symbol> RecordingChatId { get; }
    public IMutableState<ImmutableHashSet<Symbol>> PinnedChatIds { get; }

    public ChatUI(IServiceProvider services)
    {
        Services = services;
        ModalService = services.GetRequiredService<IModalService>();

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
    {
        var modalParameters = new ModalParameters();
        modalParameters.Add(nameof(ChatAuthorCard.AuthorId), authorId);
        ModalService.Show<ChatAuthorCard>(null, modalParameters, CustomModalOptions.DialogHeaderless);
    }

    public void ShowDeleteMessageRequest(ChatMessageModel model)
    {
        var modalParameters = new ModalParameters();
        modalParameters.Add(nameof(DeleteMessageModal.Model), model);
        ModalService.Show<DeleteMessageModal>("Delete Message", modalParameters, CustomModalOptions.Dialog);
    }
}
