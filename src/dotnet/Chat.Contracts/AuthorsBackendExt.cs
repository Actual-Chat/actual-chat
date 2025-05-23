namespace ActualChat.Chat;

public static class AuthorsBackendExt
{
    public static async Task<AuthorFull> EnsureJoined(
        this IAuthorsBackend authorsBackend,
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        if (chatId is PlaceChatId)
            throw StandardError.NotSupported("EnsureJoined method should not be used for place chats.");

        var author = await authorsBackend.GetByUserId(chatId, userId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
        // Return found author if exists in the db and hasn't left
        if (author is { HasLeft: false } and not { Version: 0 } )
            return author;

        var command = new AuthorsBackend_Upsert(chatId, null, userId, null, new AuthorDiff());
        var commander = authorsBackend.GetCommander();
        author = await commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return author;
    }

    public static Task<UserId[]> ListPlaceUserIds(
        this IAuthorsBackend authorsBackend,
        PlaceId placeId,
        CancellationToken cancellationToken)
        => authorsBackend.ListUserIds(placeId.RootChatId, cancellationToken);
}
