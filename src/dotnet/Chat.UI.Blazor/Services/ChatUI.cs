using Blazored.Modal;
using Blazored.Modal.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUI
{
    private readonly IModalService _modalService;

    public ChatUI(IModalService modalService)
        => _modalService = modalService;

    public void ShowChatAuthorCard(string chatId, string chatAuthorId)
    {
        var modalParameters = new ModalParameters();
        modalParameters.Add(nameof(ChatAuthorCard.ChatId), chatId);
        modalParameters.Add(nameof(ChatAuthorCard.ChatAuthorId), chatAuthorId);
        _modalService.Show<ChatAuthorCard>(null, modalParameters);
    }

    public void ShowChatAuthorCard(string chatAuthorId)
    {
        if (!ChatAuthor.TryGetChatId(chatAuthorId, out var chatId))
            throw new ArgumentOutOfRangeException(nameof(chatAuthorId));
        ShowChatAuthorCard(chatId, chatAuthorId);
    }
}
