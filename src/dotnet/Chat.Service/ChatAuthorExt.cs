namespace ActualChat.Chat;

public static class ChatAuthorExt
{
    internal static async Task<string?> GetUserIdFromPrincipalId(
        this IChatAuthorsBackend chatAuthorsBackend,
        string chatPrincipalId,
        CancellationToken cancellationToken)
    {
        if (!ChatAuthor.TryParseId(chatPrincipalId, out var chatId, out _))
            return chatPrincipalId;
        var chatAuthor = await chatAuthorsBackend.Get(chatId, chatPrincipalId, false, cancellationToken)
            .ConfigureAwait(false);
        if (chatAuthor == null || chatAuthor.UserId.IsEmpty)
            return null;
        return chatAuthor.UserId;
    }
}
