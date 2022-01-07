namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatEntryReader
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private IChats Chats { get; }

    public Session Session { get; init; } = Session.Null;
    public Symbol ChatId { get; init; }
    public ChatEntryType EntryType { get; init; }
    public TimeSpan MaxBeginsAtDisorder { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ExtraChatTileInvalidationWaitTimeout { get; init; } = TimeSpan.FromMilliseconds(50);

    public ChatEntryReader(IChats chats)
        => Chats = chats;

    public async Task<ChatEntry?> Get(long id, CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(id);
        var tile = await Chats.GetTile(Session, ChatId, EntryType, idTile.Range, cancellationToken).ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.Id == id);
    }

    public async Task<ChatEntry?> GetFirst(Range<long> idRange, CancellationToken cancellationToken)
    {
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var (minId, maxIdExclusive) = idRange;
        while (minId < maxIdExclusive) {
            var idTile = idTilesLayer0.GetTile(minId);
            var tile = await Chats.GetTile(Session, ChatId, EntryType, idTile.Range, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries) {
                if (entry.Id >= maxIdExclusive)
                    return null;
                if (entry.Id >= minId)
                    return entry;
            }
            minId = idTile.End;
        }
        return null;
    }

    public async Task<ChatEntry?> GetFirst(Range<long> idRange, Func<ChatEntry, bool> filter, CancellationToken cancellationToken)
    {
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var (minId, maxIdExclusive) = idRange;
        while (minId < maxIdExclusive) {
            var idTile = idTilesLayer0.GetTile(minId);
            var tile = await Chats.GetTile(Session, ChatId, EntryType, idTile.Range, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries) {
                if (entry.Id >= maxIdExclusive)
                    return null;
                if (entry.Id >= minId && filter(entry))
                    return entry;
            }
            minId = idTile.End;
        }
        return null;
    }

    public async IAsyncEnumerable<ChatEntry> ReadAll(
        Range<long> idRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var tile in ReadAllTiles(idRange, cancellationToken).ConfigureAwait(false)) {
            foreach (var entry in tile.Entries) {
                if (entry.Id < idRange.Start)
                    continue;
                if (entry.Id >= idRange.End)
                    yield break;
                yield return entry;
            }
        }
    }

    public async IAsyncEnumerable<ChatEntry> ReadAllWaitingForNew(
        long minId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var lastId = minId - 1;
        var idRange = await Chats.GetIdRange(Session, ChatId, EntryType, cancellationToken).ConfigureAwait(false);
        idRange = (minId, Math.Max(minId + 1, idRange.End));

        ChatTile? thisTile = null;
        await foreach (var tile in ReadAllTiles(idRange, cancellationToken).ConfigureAwait(false)) {
            thisTile = tile;
            foreach (var entry in tile.Entries) {
                if (entry.Id <= lastId)
                    continue;
                yield return entry;
                lastId = entry.Id;
            }
        }
        if (thisTile == null)
            throw new InvalidOperationException("Internal error: tile == null!");

        while (true) {
            var thisTileIdRange = thisTile.IdTileRange;
            var nextTileIdRange = thisTile.IdTileRange.Move(idTilesLayer0.TileSize);
            if (lastId >= thisTile.IdTileRange.End - 1) {
                // We anyway have to move to the next tile
                thisTile = await Chats.GetTile(Session, ChatId, EntryType, nextTileIdRange, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            foreach (var entry in thisTile.Entries) {
                if (entry.Id <= lastId)
                    continue;
                yield return entry;
                lastId = entry.Id;
            }

            var thisTileComputed = await Computed
                .Capture(ct => Chats.GetTile(Session, ChatId, EntryType, thisTileIdRange, ct), cancellationToken)
                .ConfigureAwait(false);
            if (!ReferenceEquals(thisTileComputed.Value, thisTile)) {
                // We've got a new version of thisTile
                thisTile = thisTileComputed.Value;
                continue;
            }

            // It's still the same tile, so we need to wait for either its invalidation or the next tile
            var thisTileInvalidatedTask = thisTileComputed.WhenInvalidated(cancellationToken);
            var nextTileComputed = await Computed
                .Capture(ct => Chats.GetTile(Session, ChatId, EntryType, nextTileIdRange, ct), cancellationToken)
                .ConfigureAwait(false);
            var nextTileInvalidatedTask = nextTileComputed.Value.IsEmpty
                ? nextTileComputed.WhenInvalidated(cancellationToken)
                : Task.CompletedTask; //
            await Task.WhenAny(thisTileInvalidatedTask, nextTileInvalidatedTask).ConfigureAwait(false);

            if (thisTileComputed.IsConsistent()) {
                // nextTileComputed is either invalidated (i.e. likely non-empty) or non-empty here,
                // but thisTileComputed is still the same.
                // Let's give it a bit of extra time - maybe it's a rapid sequence
                // of updates that invalidated both tiles, and we just need to wait
                // a bit more to see the lastTileComputed invalidation.
                await thisTileInvalidatedTask
                    .WithTimeout(ExtraChatTileInvalidationWaitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!thisTileComputed.IsConsistent()) {
                // thisTile was invalidated - let's update it
                thisTileComputed = await thisTileComputed.Update(cancellationToken).ConfigureAwait(false);
                thisTile = thisTileComputed.Value;
                continue;
            }

            // We gave thisTileComputed every chance to get invalidated, but it didn't happen,
            // and we know the next tile might be available, so...
            if (nextTileComputed.Value.IsEmpty) {
                // This means it was invalidated (see nextTileInvalidatedTask = ...), so let's update it first
                nextTileComputed = await nextTileComputed.Update(cancellationToken).ConfigureAwait(false);
            }
            if (!nextTileComputed.Value.IsEmpty) {
                // Next tile is ready, so we're switching to it
                thisTile = nextTileComputed.Value;
            }
            // nextTile is still empty, so let's continue watching thisTile/nextTile pair
        }
        // ReSharper disable once IteratorNeverReturns
    }

    public async IAsyncEnumerable<ChatTile> ReadAllTiles(
        Range<long> idRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (idRange.Size() <= 0)
            yield break;

        var idTilesLayer0 = IdTileStack.FirstLayer;
        for (var idTile = idTilesLayer0.GetTile(idRange.Start);
             idTile.Start < idRange.End;
             idTile = idTile.Next())
        {
            var tile = await Chats.GetTile(Session, ChatId, EntryType, idTile.Range, cancellationToken).ConfigureAwait(false);
            yield return tile;
        }
    }

    public async Task<ChatEntry?> FindByMinBeginsAt(
        Moment minBeginsAt,
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        var entry = await FindByMinBeginsAtPrecise(minBeginsAt - MaxBeginsAtDisorder, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (entry == null)
            return null;
        return await GetFirst((entry.Id, idRange.End), e => e.BeginsAt >= minBeginsAt, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task<ChatEntry?> FindByMinBeginsAtPrecise(
        Moment beginsAt,
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        var (minId, maxId) = idRange.ToInclusive();
        ChatEntry? entry;
        while (minId < maxId) {
            var midId = minId + ((maxId - minId) >> 1);
            entry = await GetFirst((midId, maxId + 1), cancellationToken).ConfigureAwait(false);
            if (entry == null) {
                // No entries in [midId, maxId] range
                maxId = midId - 1;
                continue;
            }
            if (beginsAt <= entry.BeginsAt)
                maxId = midId - 1;
            else
                minId = midId + 1;
        }
        entry = await GetFirst((minId, idRange.End), e => e.BeginsAt >= beginsAt, cancellationToken)
            .ConfigureAwait(false);
        return entry;
    }
}
