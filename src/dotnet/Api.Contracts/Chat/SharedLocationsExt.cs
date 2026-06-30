namespace ActualChat.Chat;

public static class SharedLocationsExt
{
    extension(ISharedLocations sharedLocations)
    {
        public async Task<bool> IsOwnSharing(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
        {
            var authors = sharedLocations.GetServices().GetRequiredService<IAuthors>();
            var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
            if (author == null)
                return false;

            var liveLocations = await sharedLocations.ListLive(session, chatId, cancellationToken).ConfigureAwait(false);
            return liveLocations.Any(x => x.AuthorId == author.Id);
        }

        public async Task<bool> IsAnyoneSharing(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
        {
            var liveLocations = await sharedLocations.ListLive(session, chatId, cancellationToken).ConfigureAwait(false);
            return !liveLocations.IsEmpty;
        }
    }
}
