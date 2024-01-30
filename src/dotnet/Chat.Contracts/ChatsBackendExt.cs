namespace ActualChat.Chat;

public static class ChatsBackendExt
{
    public static async ValueTask<ChatEntry?> GetEntry(
        this IChatsBackend chatsBackend,
        ChatEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId.IsNone)
            return null;

        var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(entryId.LocalId);
        var tile = await chatsBackend.GetTile(entryId.ChatId,
                entryId.Kind,
                idTile.Range,
                false,
                cancellationToken)
            .ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
    }

    public static async ValueTask<ChatEntry?> GetRemovedEntry(
        this IChatsBackend chatsBackend,
        ChatEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId.IsNone)
            return null;

        var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(entryId.LocalId);
        var tile = await chatsBackend.GetTile(entryId.ChatId,
                entryId.Kind,
                idTile.Range,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
    }

    public static async IAsyncEnumerable<ApiArray<Chat>> Batches(
        this IChatsBackend chatsBackend,
        Moment minCreatedAt,
        ChatId lastChatId,
        int limit,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var chats = await chatsBackend.List(minCreatedAt, lastChatId, limit, cancellationToken)
                .ConfigureAwait(false);
            if (chats.Count == 0)
                yield break;

            yield return chats;

            var lastChat = chats[^1];
            lastChatId = lastChat.Id;
            minCreatedAt = lastChat.CreatedAt;
        }
    }

    public static async Task<Place?> GetPlace(this IChatsBackend chatsBackend, PlaceId placeId, CancellationToken cancellationToken)
    {
        placeId.Require();

        var placeRootChat = await chatsBackend.Get(placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
        return placeRootChat?.ToPlace();
    }
}
