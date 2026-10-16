namespace ActualChat.Chat;

/// <summary>
/// Extension methods for <see cref="IAuthorsBackend"/>.
/// </summary>
public static class AuthorsBackendExt
{
    extension(IAuthorsBackend authorsBackend)
    {
        public async Task<AuthorFull> EnsureJoined(
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

        public async Task<AuthorFull> Admit(
            ChatId chatId,
            UserId userId,
            MembershipChange change,
            CancellationToken cancellationToken)
        {
            // Unlike EnsureJoined, works for Place chats too: a non-root one needs the Place membership first
            var author = await authorsBackend
                .GetByUserId(chatId, userId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author is { HasLeft: false })
                return author;

            var command = new AuthorsBackend_Upsert(
                chatId, author?.Id, author is null ? userId : null, author?.Version,
                new AuthorDiff { HasLeft = false }) {
                Change = change,
            };
            var commander = authorsBackend.GetCommander();
            return await commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        }

        public Task<UserId[]> ListPlaceUserIds(
            PlaceId placeId,
            CancellationToken cancellationToken)
            => authorsBackend.ListUserIds(placeId.RootChatId, cancellationToken);

        public async Task<UserId[]> ListUserIds(
            ChatId chatId,
            IEnumerable<AuthorId> authorIds,
            RequestedAuthorKind authorKind,
            CancellationToken cancellationToken)
        {
            var authors = await authorIds
                .Select(authorId => authorsBackend.Get(chatId, authorId, authorKind, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            return [.. authors.SkipNullItems().Select(a => a.UserId)];
        }
    }
}
