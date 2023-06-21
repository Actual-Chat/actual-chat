namespace ActualChat.Chat;

public static class AuthorsExt
{
    public static async Task<Author> EnsureJoined(
        this IAuthors authors,
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is { HasLeft: false })
            return author;

        var command = new Authors_Join(session, chatId);
        author = await authors.GetCommander().Call(command, true, cancellationToken).ConfigureAwait(false);
        return author;
    }
}
