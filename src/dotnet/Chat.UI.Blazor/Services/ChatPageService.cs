using ActualChat.Chat.UI.Blazor.Pages;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPageService
{
    private readonly IAuth _auth;
    private readonly IChatServiceFrontend _chats;

    public ChatPageService(IChatServiceFrontend chats, IAuth auth)
    {
        _chats = chats;
        _auth = auth;
    }

    [ComputeMethod]
    public virtual async Task<ChatPageModel> GetChatPageModel(
        Session session,
        string chatId,
        CancellationToken cancellationToken)
    {
        var user = await _auth.GetSessionUser(session, cancellationToken).ConfigureAwait(false);
        var chat = await _chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return new () { IsUnavailable = true };

        if (!user.IsAuthenticated && !chat.IsPublic)
            return new () { MustLogin = true };

        return new () { Chat = chat };
    }
}
