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
        // Return found author if exists in the db and hasn't left
        if (author is { HasLeft: false } and not { Version: 0 } )
            return author;

        var command = new AuthorsBackend_Upsert(chatId, default, userId, null, new AuthorDiff());
        var commander = authorsBackend.GetCommander();
        var context = await commander.Run(command, true, cancellationToken).ConfigureAwait(false);
        var typedContext = (CommandContext<AuthorFull>) context;
        author = await typedContext.ResultTask.ConfigureAwait(false);
        return author;
    }
}
