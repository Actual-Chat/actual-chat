namespace ActualChat.Chat;

public static class AuthorsBackendExt
{
    public static Task? TrackedTask = null;

    public static async Task<AuthorFull> EnsureJoined(
        this IAuthorsBackend authorsBackend,
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var author = await authorsBackend.GetByUserId(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (author is { HasLeft: false })
            return author;

        var command = new IAuthorsBackend.UpsertCommand(chatId, default, userId, null, new AuthorDiff());
        var commander = authorsBackend.GetCommander();
        var context = await commander.Run(command, true, cancellationToken).ConfigureAwait(false);
        var typedContext = (CommandContext<AuthorFull>) context;
        author = await typedContext.ResultTask.ConfigureAwait(false);
        return author;
    }
}
