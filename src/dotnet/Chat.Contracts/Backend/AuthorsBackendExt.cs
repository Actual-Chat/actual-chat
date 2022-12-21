namespace ActualChat.Chat;

public static class AuthorsBackendExt
{
    public static async Task<AuthorFull> EnsureJoined(
        this IAuthorsBackend authorsBackend,
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var author = await authorsBackend.GetByUserId(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (author is { HasLeft: false })
            return author;

        var command = new IAuthorsBackend.UpsertCommand(chatId, default, userId, default, new AuthorDiff());
        author = await authorsBackend.GetCommander().Call(command, true, cancellationToken).ConfigureAwait(false);
        return author;
    }
}
