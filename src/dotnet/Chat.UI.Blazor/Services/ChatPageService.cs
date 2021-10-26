using ActualChat.Chat.UI.Blazor.Pages;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPageService
{
    private readonly IChatServiceFacade _chats;
    private readonly IAuthService _auth;

    public ChatPageService(IChatServiceFacade chats, IAuthService auth)
    {
        _chats = chats;
        _auth = auth;
    }

    [ComputeMethod]
    public virtual async Task<ChatPageModel> GetChatPageModel(Session session, string chatId, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var chat = await _chats.TryGet(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new ChatPageModel() { IsUnavailable = true };
        if (!user.IsAuthenticated && !chat.IsPublic)
            return new ChatPageModel() { MustLogin = true };
        return new ChatPageModel() { Chat = chat };
    }
}
