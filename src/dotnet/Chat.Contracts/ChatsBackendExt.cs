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

    public static async ValueTask<IReadOnlyList<ChatEntry>> ListEntries(
        this IChatsBackend chatsBackend,
        IEnumerable<ChatEntryId> entryIds,
        bool includeRemoved = false,
        CancellationToken cancellationToken = default)
    {
        ChatId? chatId = null;
        var (minId, maxId) = (long.MaxValue, long.MinValue);
        var localIds = new HashSet<long>();
        foreach (var entryId in entryIds) {
            if (chatId is null) {
                chatId = entryId.ChatId;
            }
            else {
                if (chatId != entryId.ChatId) {
                    throw new InvalidOperationException("All entries must belong to the same chat.");
                }
            }

            var localId = entryId.LocalId;
            localIds.Add(localId);

            minId = Math.Min(minId, localId);
            maxId = Math.Max(maxId, localId);
        }
        if (maxId < minId || chatId is null)
            return [];

        var idTiles = Constants.Chat.EntryIdTiles.GetCoveringTiles(new Range<long>(minId, maxId + 1));
        var entries = new List<ChatEntry>(localIds.Count);
        foreach (var idTile in idTiles) {
            var tile = await chatsBackend.GetTile(chatId!,
                    idTile.Range,
                    includeRemoved,
                    cancellationToken)
                .ConfigureAwait(false);
            entries.AddRange(tile.Entries.Where(e => localIds.Contains(e.LocalId)));
        }
        return entries;
    }

    public static async Task<IReadOnlyList<ChatEntry>> ListEntries(
        this IChatsBackend chatsBackend,
        ChatId chatId,
        Range<long> lidRange,
        bool includeRemoved = false,
        CancellationToken cancellationToken = default)
    {
        var idTiles = Constants.Chat.EntryIdTiles.GetCoveringTiles(lidRange);
        var tiles = await idTiles.Select(t => chatsBackend.GetTile(
                chatId,
                t.Range,
                includeRemoved,
                cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        return tiles.SelectMany(t => t.Entries).ToList();
    }

    public static async Task<IReadOnlyList<ChatEntry>> ListEntries(
        this IChatsBackend chatsBackend,
        ChatId chatId,
        Moment minBeginsAt,
        CancellationToken cancellationToken = default)
    {
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
        var entryIdTiles = Constants.Chat.EntryIdTiles;
        var result = new List<ChatEntry>();
        for (var idTile = entryIdTiles.GetTile(idRange.End - 1); idTile.End > idRange.Start; idTile = idTile.Prev()) {
            var tile = await chatsBackend
                .GetTile(chatId, idTile.Range, true, cancellationToken)
                .ConfigureAwait(false);
            var tileEntries = tile.Entries;
            if (tileEntries.Length == 0)
                continue;

            for (var i = tileEntries.Length - 1; i >= 0; i--) {
                var entry = tile.Entries[i];
                if (entry.BeginsAt >= minBeginsAt)
                    result.Add(entry);
            }

            if (tile.BeginsAtRange.End <= cutoff)
                break;
        }

        // We visit tiles high→low and walk each tile's entries high→low, so `result` is
        // already in strictly descending LocalId order — just reverse instead of sorting.
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
        var idTiles = Constants.Chat.EntryIdTiles.GetCoveringTiles(lidRange);
        foreach (var idTile in idTiles) {
            var tile = await chatsBackend.GetTile(
                chatId,
                idTile.Range,
                includeRemoved,
                cancellationToken).ConfigureAwait(false);
            foreach (var chatEntry in tile.Entries)
                yield return chatEntry;
        }
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
