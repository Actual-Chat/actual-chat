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

    public static async ValueTask<ChatEntry?> GetEntry(
        this IChatsBackend chatsBackend,
        ChatEntryId entryId,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
    {
        if (entryId.IsNone)
            return null;

        var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(entryId.LocalId);
        var cTile = await Computed.Capture(() => chatsBackend.GetTile(
                entryId.ChatId,
                entryId.Kind,
                idTile.Range,
                false,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var tile = cTile.Value;
        var entry = tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
        if (entry == null)
            return entry;

        // Tile doesn't contain the entry yet (prob. due to invalidation delays), so we're going to wait for it
        cTile = await cTile
            .When(ct => ct.Entries.Any(e => e.LocalId == entryId.LocalId), cancellationToken)
            .WaitAsync(waitTimeout, cancellationToken)
            .ConfigureAwait(false);

        tile = cTile.Value;
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

    public static async ValueTask<IReadOnlyList<ChatEntry>> GetEntries(
        this IChatsBackend chatsBackend,
        IEnumerable<ChatEntryId> entryIds,
        bool includeRemoved = false,
        CancellationToken cancellationToken = default)
    {
        var (chatId, entryKind) = (ChatId.None, default(ChatEntryKind?));
        var (minId, maxId) = (long.MaxValue, long.MinValue);
        var localIds = new HashSet<long>();
        foreach (var entryId in entryIds) {
            if (chatId == ChatId.None || entryKind is null) {
                chatId = entryId.ChatId;
                entryKind = entryId.Kind;
            }
            else {
                if (chatId != entryId.ChatId) {
                    throw new InvalidOperationException("All entries must belong to the same chat.");
                }
                if (entryKind != entryId.Kind) {
                    throw new InvalidOperationException("All entries must be of the same kind.");
                }
            }

            var localId = entryId.LocalId;
            localIds.Add(localId);

            minId = Math.Min(minId, localId);
            maxId = Math.Max(maxId, localId);
        }
        if (maxId < minId || entryKind is null)
            return [];

        var idTiles = Constants.Chat.ServerIdTileStack.FirstLayer.GetCoveringTiles(new Range<long>(minId, maxId + 1));
        var entries = new List<ChatEntry>(localIds.Count);
        foreach (var idTile in idTiles) {
            var tile = await chatsBackend.GetTile(chatId,
                    entryKind.Value,
                    idTile.Range,
                    includeRemoved,
                    cancellationToken)
                .ConfigureAwait(false);
            entries.AddRange(tile.Entries.Where(e => localIds.Contains(e.LocalId)));
        }
        return entries;
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
        long maxVersion,
        ChatId lastId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var chats = await chatsBackend.ListChanged(minVersion, maxVersion, lastId, batchSize, cancellationToken)
                .ConfigureAwait(false);
            if (chats.Count == 0)
                yield break;

            yield return chats;

            var last = chats[^1];
            lastId = last.Id;
            minVersion = last.Version;
        }
    }
}
