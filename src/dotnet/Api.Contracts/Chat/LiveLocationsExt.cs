namespace ActualChat.Chat;

public static class LiveLocationsExt
{
    public static async Task<bool> IsSharing(
        this ILiveLocations liveLocations,
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var authors = liveLocations.GetServices().GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return false;

        var location = await liveLocations.Get(session, chatId, author.Id, cancellationToken).ConfigureAwait(false);
        return location != null;
    }
}
