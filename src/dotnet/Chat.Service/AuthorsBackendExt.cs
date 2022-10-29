namespace ActualChat.Chat;

public static class AuthorsBackendExt
{
    internal static async Task<Symbol> GetUserId(
        this IAuthorsBackend authorsBackend,
        Symbol chatId,
        ParsedPrincipalId principalId,
        CancellationToken cancellationToken)
    {
        switch (principalId.Kind) {
        case PrincipalKind.User:
            return principalId.UserId;
        case PrincipalKind.Author:
            var authorId = principalId.AuthorId;
            if (chatId != authorId.ChatId.Id)
                return Symbol.Empty;
            var author = await authorsBackend.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
            return author?.UserId ?? Symbol.Empty;
        default:
            return Symbol.Empty;
        }
    }
}
