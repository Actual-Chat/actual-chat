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
            Class = "custom-modal-class",
        };
        modalParameters.Add(nameof(ChatAuthorCard.AuthorId), authorId);
        _modalService.Show<ChatAuthorCard>(null, modalParameters, options);
    }

    public void ShowDeleteMessageRequest(ChatEntry entry, ChatAuthor requestAuthor)
    {
        var modalParameters = new ModalParameters();
        var options = new ModalOptions() {
            HideCloseButton = true,
            Animation = ModalAnimation.FadeIn(0.3),
            Class = "custom-modal-class",
        };

        modalParameters.Add(nameof(DeleteMessageModal.Entry), entry);
        modalParameters.Add(nameof(DeleteMessageModal.RequestAuthor), requestAuthor);
        _modalService.Show<DeleteMessageModal>("Delete Message", modalParameters, options);
    }
}
