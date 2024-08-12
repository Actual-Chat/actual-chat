namespace ActualChat.Chat;

public static class AuthorsBackendExt
{
    public static async Task<AuthorFull> EnsureJoined(
        this IAuthorsBackend authorsBackend,
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsPlaceChat)
            throw StandardError.NotSupported("EnsureJoined method should not be used for place chats.");
        var author = await authorsBackend.GetByUserId(chatId, userId, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
        // Return found author if exists in the db and hasn't left
        if (author is { HasLeft: false } and not { Version: 0 } )
            return author;

        var command = new AuthorsBackend_Upsert(chatId, default, userId, null, new AuthorDiff());
        var commander = authorsBackend.GetCommander();
        author = await commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return author;
    }

    public static Task<ApiArray<UserId>> ListPlaceUserIds(this IAuthorsBackend authorsBackend, PlaceId placeId, CancellationToken cancellationToken)
        => authorsBackend.ListUserIds(placeId.ToRootChatId(), cancellationToken);

    public static async IAsyncEnumerable<ApiArray<AuthorFull>> BatchChangedPlaceAuthors(
        this IAuthorsBackend authorsBackend,
        long minVersion,
        long maxVersion,
        AuthorId lastId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var authors = await authorsBackend.ListChangedPlaceAuthors(minVersion,
                    maxVersion,
                    lastId,
                    batchSize,
                    cancellationToken)
                .ConfigureAwait(false);
            if (authors.Count == 0)
                yield break;

            yield return authors;

            var last = authors[^1];
            lastId = last.Id;
            minVersion = last.Version;
        }
    }
}
