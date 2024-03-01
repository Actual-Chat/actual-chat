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

    public static async IAsyncEnumerable<ApiArray<Chat>> Batch(
        this IChatsBackend chatsBackend,
        Moment minCreatedAt,
        ChatId lastChatId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var chats = await chatsBackend.List(minCreatedAt, lastChatId, batchSize, cancellationToken)
                .ConfigureAwait(false);
            if (chats.Count == 0)
                yield break;

            yield return chats;

            var last = chats[^1];
            lastChatId = last.Id;
            minCreatedAt = last.CreatedAt;
        }
    }

    public static async IAsyncEnumerable<ApiArray<Chat>> BatchChanged(
        this IChatsBackend chatsBackend,
        long minVersion,
        ApiSet<ChatId> lastIdsWithSameVersion,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var chats = await chatsBackend.ListChanged(minVersion, lastIdsWithSameVersion, batchSize, cancellationToken)
                .ConfigureAwait(false);
            if (chats.Count == 0)
                yield break;

            yield return chats;

            var last = chats[^1];
            lastIdsWithSameVersion = chats.Reverse()
                .TakeWhile(x => x.Version == last.Version)
                .Select(x => x.Id)
                .ToApiSet();
            minVersion = last.Version;
        }
    }

    public static async Task<Place?> GetPlace(
        this IChatsBackend chatsBackend,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        placeId.Require();

        var placeRootChat = await chatsBackend.Get(placeId.ToRootChatId(), cancellationToken).ConfigureAwait(false);
        return placeRootChat?.ToPlace();
    }
}
