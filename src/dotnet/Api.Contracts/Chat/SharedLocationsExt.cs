namespace ActualChat.Chat;

public static class SharedLocationsExt
{
    public static async Task<bool> IsSharing(
        this ISharedLocations sharedLocations,
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var authors = sharedLocations.GetServices().GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return false;

        var liveLocations = await sharedLocations.List(session, chatId, cancellationToken).ConfigureAwait(false);
        return liveLocations.Any(x => x.AuthorId == author.Id);
    }
}
