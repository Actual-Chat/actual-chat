using Blazored.Modal;
using Blazored.Modal.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUI
{
    private readonly IModalService _modalService;

    public ChatUI(IModalService modalService)
        => _modalService = modalService;

    public void ShowChatAuthorCard(string authorId)
    {
        var modalParameters = new ModalParameters();
        var options = new ModalOptions() {
            HideCloseButton = true,
            HideHeader = true,
            Animation = ModalAnimation.FadeIn(0.3),
        };
        modalParameters.Add(nameof(ChatAuthorCard.AuthorId), authorId);
        _modalService.Show<ChatAuthorCard>(null, modalParameters, options);
    }

    public void ShowDeleteMessageRequest(ChatEntry entry)
    {
        var modalParameters = new ModalParameters();
        var options = new ModalOptions() {
            HideCloseButton = true,
            HideHeader = true,
            Animation = ModalAnimation.FadeIn(0.3),
        };
        modalParameters.Add(nameof(DeleteMessageModal.Entry), entry);
        _modalService.Show<DeleteMessageModal>(null, modalParameters, options);
    }
}
