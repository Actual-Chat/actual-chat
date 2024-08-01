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

    public static Task<AuthorFull?> Get(
        this IAuthorsBackend authorsBackend,
        ChatId chatId,
        PrincipalId principalId,
        CancellationToken cancellationToken)
        => Get(authorsBackend,
            chatId,
            principalId,
            AuthorsBackend_GetAuthorOption.Full,
            cancellationToken);

    public static Task<AuthorFull?> Get(
        this IAuthorsBackend authorsBackend,
        ChatId chatId,
        PrincipalId principalId,
        AuthorsBackend_GetAuthorOption option,
        CancellationToken cancellationToken)
    {
        if (principalId.IsUser(out var userId))
            return authorsBackend.GetByUserId(chatId, userId, AuthorsBackend_GetAuthorOption.Full, cancellationToken);

        if(principalId.IsAuthor(out var authorId))
            return authorsBackend.Get(chatId, authorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken);

        throw StandardError.Internal("Can't remap principal id");
    }
}
