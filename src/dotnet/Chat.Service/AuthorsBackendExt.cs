namespace ActualChat.Chat;

public static class AuthorsBackendExt
{
    internal static async Task<Symbol> GetUserId(
        this IChatAuthorsBackend chatAuthorsBackend,
        Symbol chatId,
        ParsedChatPrincipalId chatPrincipalId,
        CancellationToken cancellationToken)
    {
        switch (chatPrincipalId.Kind) {
        case ChatPrincipalKind.User:
            return chatPrincipalId.UserId;
        case ChatPrincipalKind.Author:
            var authorId = chatPrincipalId.AuthorId;
            if (chatId != authorId.ChatId.Id)
                return Symbol.Empty;
            var chatAuthor = await chatAuthorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
            return chatAuthor?.UserId ?? Symbol.Empty;
        default:
            return Symbol.Empty;
        }
    }
}
