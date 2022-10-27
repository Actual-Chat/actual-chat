namespace ActualChat.Chat;

public static class AuthorsBackendExt
{
    internal static async Task<Symbol> GetUserId(
        this IChatAuthorsBackend chatAuthorsBackend,
        ChatPrincipalId chatPrincipalId,
        CancellationToken cancellationToken)
    {
        if (chatPrincipalId.Kind == ChatPrincipalKind.User)
            return chatPrincipalId.UserId;

        var authorId = chatPrincipalId.AuthorId;
        var parsedAuthorId = new ParsedAuthorId(authorId);
        if (!parsedAuthorId.IsValid)
            return default;

        var chatId = parsedAuthorId.ChatId;
        var chatAuthor = await chatAuthorsBackend
            .Get(chatId, authorId, false, cancellationToken)
            .ConfigureAwait(false);
        return chatAuthor?.UserId ?? Symbol.Empty;
    }
}
