namespace ActualChat.Chat;

/// <summary>
/// Extension methods for <see cref="IChatsBackend"/>.
/// </summary>
public static class ChatsBackendExt
{
    public static async ValueTask<ChatEntry?> GetEntry(
        this IChatsBackend chatsBackend,
        ChatEntryId? entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId is null)
            return null;

        var idTile = Constants.Chat.EntryIdTiles.GetTile(entryId.LocalId);
        var tile = await chatsBackend.GetTile(entryId.ChatId,
                idTile.Range,
                false,
                cancellationToken)
            .ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
    }

    public static async ValueTask<ChatEntry?> GetEntry(
        this IChatsBackend chatsBackend,
        ChatEntryId? entryId,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
    {
        if (entryId is null)
            return null;

        var idTile = Constants.Chat.EntryIdTiles.GetTile(entryId.LocalId);
        var cTile = await Computed.Capture(() => chatsBackend.GetTile(
                entryId.ChatId,
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
        ChatEntryId? entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId is null)
            return null;

        var idTile = Constants.Chat.EntryIdTiles.GetTile(entryId.LocalId);
        var tile = await chatsBackend.GetTile(entryId.ChatId,
                idTile.Range,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
    }

    public static async Task<ChatEntry?[]> ListEntries(
        this IChatsBackend chatsBackend,
        IEnumerable<ChatEntryId> entryIds,
        bool includeRemoved = false,
        CancellationToken cancellationToken = default)
    {
        // Returns the entries in the order of entryIdSequence, null for the missing ones;
        // each tile they fall into is fetched once
        var entryIdList = entryIds as IReadOnlyList<ChatEntryId> ?? entryIds.ToList();
        if (entryIdList.Count == 0)
            return [];

        var chatId = entryIdList[0].ChatId;
        foreach (var entryId in entryIdList)
            if (entryId.ChatId != chatId)
                throw new InvalidOperationException("All entries must belong to the same chat.");

        var idTiles = Constants.Chat.EntryIdTiles.GetCoveringTiles(entryIdList.Select(x => x.LocalId));
        var tiles = await idTiles
            .Select(idTile => chatsBackend.GetTile(chatId, idTile.Range, includeRemoved, cancellationToken))
            .Collect(ApiConstants.Concurrency.Low, cancellationToken)
            .ConfigureAwait(false);

        var result = new ChatEntry?[entryIdList.Count];
        for (var i = 0; i < entryIdList.Count; i++) {
            var localId = entryIdList[i].LocalId;
            var tile = tiles[IndexOfTile(Constants.Chat.EntryIdTiles.GetTile(localId).Start)];
            foreach (var entry in tile.Entries) {
                if (entry.LocalId != localId)
                    continue;

                result[i] = entry;
                break;
            }
        }
        return result;

        // idTiles are ordered by Start
        int IndexOfTile(long tileStart) {
            var (min, max) = (0, idTiles.Length - 1);
            while (min < max) {
                var mid = (min + max) >> 1;
                if (idTiles[mid].Start < tileStart)
                    min = mid + 1;
                else
                    max = mid;
            }
            return min;
        }
    }

    public static async Task<IReadOnlyList<ChatEntry>> ListEntries(
        this IChatsBackend chatsBackend,
        ChatId chatId,
        Range<long> lidRange,
        bool includeRemoved = false,
        CancellationToken cancellationToken = default)
    {
        var idTiles = Constants.Chat.EntryIdTiles.GetCoveringTiles(lidRange);
        var tiles = await idTiles
            .Select(idTile => chatsBackend.GetTile(chatId, idTile.Range, includeRemoved, cancellationToken))
            .Collect(ApiConstants.Concurrency.Low, cancellationToken)
            .ConfigureAwait(false);
        return tiles.SelectMany(t => t.Entries).ToList();
    }

    public static Task<IReadOnlyList<ChatEntry>> ListEntries(
        this IChatsBackend chatsBackend,
        ChatId chatId,
        Moment minBeginsAt,
        CancellationToken cancellationToken = default)
        => ListEntries(chatsBackend, chatId, minBeginsAt, int.MaxValue, cancellationToken);

    public static async Task<IReadOnlyList<ChatEntry>> ListEntries(
        this IChatsBackend chatsBackend,
        ChatId chatId,
        Moment minBeginsAt,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        // maxCount keeps the newest entries: the walk is newest-first, so it stops once it has that many.
        // We don't want callers of this method to be dependent on whatever it fetches
        using var _ = Computed.BeginIsolation();

        var idRange = await chatsBackend.GetLidRange(chatId, true, cancellationToken).ConfigureAwait(false);
        if (idRange.Size() <= 0)
            return [];

        // BeginsAt is roughly monotone in LocalId but not strictly — concurrent authors can cause
        // a few seconds of disorder, so we stop only once a tile's whole BeginsAt range is
        // comfortably before `from`.
        var maxBeginsAtDisorder = TimeSpan.FromSeconds(15);
        var cutoff = minBeginsAt - maxBeginsAtDisorder;
        // Tiles are fetched a batch at a time, so the walk overshoots by at most a batch minus one tile
        const int tileBatchSize = 4;
        var entryIdTiles = Constants.Chat.EntryIdTiles;
        var result = new List<ChatEntry>();
        var idTiles = new List<Tile<long>>(tileBatchSize);
        var nextIdTile = entryIdTiles.GetTile(idRange.End - 1);
        var isDone = false;
        while (!isDone && nextIdTile.End > idRange.Start) {
            idTiles.Clear();
            for (; idTiles.Count < tileBatchSize && nextIdTile.End > idRange.Start; nextIdTile = nextIdTile.Prev())
                idTiles.Add(nextIdTile);
            var tiles = await idTiles
                .Select(idTile => chatsBackend.GetTile(chatId, idTile.Range, true, cancellationToken))
                .Collect(ApiConstants.Concurrency.Low, cancellationToken)
                .ConfigureAwait(false);

            foreach (var tile in tiles) {
                var tileEntries = tile.Entries;
                if (tileEntries.Length == 0)
                    continue;

                for (var i = tileEntries.Length - 1; i >= 0; i--) {
                    var entry = tileEntries[i];
                    if (entry.BeginsAt >= minBeginsAt)
                        result.Add(entry);
                }

                if (tile.BeginsAtRange.End <= cutoff || result.Count >= maxCount) {
                    isDone = true;
                    break;
                }
            }
        }

        // We visit tiles high→low and walk each tile's entries high→low, so `result` is
        // already in strictly descending LocalId order — just reverse instead of sorting.
        if (result.Count > maxCount)
            result.RemoveRange(maxCount, result.Count - maxCount);
        result.Reverse();
        return result.ToArray();
    }

    public static async IAsyncEnumerable<ChatEntry> ReadEntries(
        this IChatsBackend chatsBackend,
        ChatId chatId,
        Range<long> lidRange,
        bool includeRemoved = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The next tile is fetched while the caller consumes the current one
        var idTiles = Constants.Chat.EntryIdTiles.GetCoveringTiles(lidRange);
        var nextTileTask = idTiles.Length == 0 ? null : GetTile(0);
        for (var i = 0; nextTileTask is not null; i++) {
            var tile = await nextTileTask.ConfigureAwait(false);
            nextTileTask = i + 1 < idTiles.Length ? GetTile(i + 1) : null;
            foreach (var chatEntry in tile.Entries)
                yield return chatEntry;
        }

        Task<ChatTile> GetTile(int index)
            => chatsBackend.GetTile(chatId, idTiles[index].Range, includeRemoved, cancellationToken);
    }

    public static async IAsyncEnumerable<Chat[]> Batch(
        this IChatsBackend chatsBackend,
        Moment minCreatedAt,
        ChatId? lastChatId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var chats = await chatsBackend.List(minCreatedAt, lastChatId, batchSize, cancellationToken)
                .ConfigureAwait(false);
            if (chats.Length == 0)
                yield break;

            yield return chats;

            var last = chats[^1];
            lastChatId = last.Id;
            minCreatedAt = last.CreatedAt;
        }
    }

    public static async IAsyncEnumerable<Chat[]> BatchChangedGroups(
        this IChatsBackend chatsBackend,
        long minVersion,
        long maxVersion,
        ChatId? lastChatId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var query = new ChangedChatsQuery {
                LastId = lastChatId,
                Limit = batchSize,
                MinVersion = minVersion,
                MaxVersion = maxVersion,
                ExcludePeerChats = true,
                ExcludePlaceRootChats = true,
            };
            var chats = await chatsBackend.ListChanged(query, cancellationToken).ConfigureAwait(false);
            if (chats.Length == 0)
                yield break;

            yield return chats;

            var last = chats[^1];
            lastChatId = last.Id;
            minVersion = last.Version;
        }
    }
}
