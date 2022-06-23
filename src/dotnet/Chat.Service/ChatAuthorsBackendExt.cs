namespace ActualChat.Chat;

public static class ChatAuthorsBackendExt
{
    internal static async Task<Symbol> GetUserId(
        this IChatAuthorsBackend chatAuthorsBackend,
        ParsedChatPrincipalId chatPrincipalId,
        CancellationToken cancellationToken)
    {
        if (!chatPrincipalId.IsValid)
            return Symbol.Empty;
        if (chatPrincipalId.Kind == ChatPrincipalKind.User)
            return chatPrincipalId.UserId;

        var chatAuthorId = chatPrincipalId.AuthorId;
        var chatId = chatAuthorId.ChatId;
        var chatAuthor = await chatAuthorsBackend
            .Get(chatId, chatAuthorId, false, cancellationToken)
            .ConfigureAwait(false);
        return chatAuthor?.UserId ?? Symbol.Empty;
    }
}
