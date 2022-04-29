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
        modalParameters.Add(nameof(ChatAuthorCard.AuthorId), authorId);
        _modalService.Show<ChatAuthorCard>(null, modalParameters, CustomModalOptions.DialogHeaderless);
    }

    public void ShowDeleteMessageRequest(ChatMessageModel model)
    {
        var modalParameters = new ModalParameters();
        modalParameters.Add(nameof(DeleteMessageModal.Model), model);
        _modalService.Show<DeleteMessageModal>("Delete Message", modalParameters, CustomModalOptions.Dialog);
    }
}
