namespace ActualChat.Chat;

public sealed class ChatEntryReader(
    IChats chats,
    Session session,
    ChatId chatId)
{
    public TileLayer<long> IdTileLayer { get; } = Constants.Chat.EntryIdTileLayer;
    public IChats Chats { get; } = chats;
    public Session Session { get; init; } = session;
    public ChatId ChatId { get; init; } = chatId;
    public TimeSpan MaxBeginsAtDisorder { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxEntryCountDisorder { get; init; } = 1000;

    public async ValueTask<ChatEntry?> Get(long id, CancellationToken cancellationToken)
    {
        if (id < 0)
            return null;

        var idTile = IdTileLayer.GetTile(id);
        var tile = await Chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.LocalId == id);
    }

    public async ValueTask<ChatEntry?> GetFirst(Range<long> lidRange, CancellationToken cancellationToken)
    {
        var (minId, maxIdExclusive) = lidRange;
        while (minId < maxIdExclusive) {
            var idTile = IdTileLayer.GetTile(minId);
            var tile = await Chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries) {
                if (entry.LocalId >= maxIdExclusive)
                    break;
                if (entry.LocalId >= minId)
                    return entry;
            }
            minId = idTile.End;
        }
        return null;
    }

    public async ValueTask<ChatEntry?> GetFirst(Range<long> lidRange, Func<ChatEntry, bool> filter, int filterLimit, CancellationToken cancellationToken)
    {
        var (minId, maxIdExclusive) = lidRange;
        while (minId < maxIdExclusive) {
            var idTile = IdTileLayer.GetTile(minId);
            var tile = await Chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries) {
                if (entry.LocalId >= maxIdExclusive)
                    break;
                if (entry.LocalId >= minId) {
                    if (filter(entry))
                        return entry;
                    if (--filterLimit < 0)
                        break;
                }
            }
            minId = idTile.End;
        }
        return null;
    }

    public async ValueTask<ChatEntry?> GetLast(Range<long> lidRange, CancellationToken cancellationToken)
    {
        var (minId, maxIdExclusive) = lidRange;
        while (minId < maxIdExclusive) {
            var idTile = IdTileLayer.GetTile(maxIdExclusive - 1);
            var tile = await Chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
            for (var i = tile.Entries.Length - 1; i >= 0; i--) {
                var entry = tile.Entries[i];
                if (entry.LocalId < minId)
                    break;
                if (entry.LocalId < maxIdExclusive)
                    return entry;
            }
            maxIdExclusive = idTile.Start;
        }
        return null;
    }

    public async ValueTask<ChatEntry?> GetLast(Range<long> lidRange, Func<ChatEntry, bool> filter, int filterLimit, CancellationToken cancellationToken)
    {
        var (minId, maxIdExclusive) = lidRange;
        var skippedCount = 0;
        while (minId < maxIdExclusive) {
            var idTile = IdTileLayer.GetTile(maxIdExclusive - 1);
            var tile = await Chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
            for (var i = tile.Entries.Length - 1; i >= 0; i--) {
                var entry = tile.Entries[i];
                if (entry.LocalId < minId)
                    break;
                if (entry.LocalId < maxIdExclusive) {
                    if (filter(entry))
                        return entry;

                    if (++skippedCount >= filterLimit)
                        break;
                }
            }
            maxIdExclusive = idTile.Start;
        }
        return null;
    }

    public async Task<ChatEntry?> GetWhen(
        long id,
        Func<ChatEntry?, bool> predicate,
        CancellationToken cancellationToken)
    {
        var idTile = IdTileLayer.GetTile(id);
        var cTile = await Computed
            .Capture(() => Chats.GetTile(Session, ChatId, idTile.Range, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        cTile = await cTile.When(
                t => predicate(t.Entries.FirstOrDefault(e => e.LocalId == id)),
                cancellationToken
            ).ConfigureAwait(false);
        return cTile.Value.Entries.FirstOrDefault(e => e.LocalId == id);
    }

    public async IAsyncEnumerable<ChatEntry> Read(
        Range<long> lidRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var tile in ReadTiles(lidRange, cancellationToken).ConfigureAwait(false)) {
            foreach (var entry in tile.Entries) {
                if (entry.LocalId < lidRange.Start)
                    continue;
                if (entry.LocalId >= lidRange.End)
                    yield break;
                yield return entry;
            }
        }
    }

    public async IAsyncEnumerable<ChatEntry> ReadReverse(
        Range<long> lidRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var tile in ReadTilesReverse(lidRange, cancellationToken).ConfigureAwait(false)) {
            for (var i = tile.Entries.Length - 1; i >= 0; i--) {
                var entry = tile.Entries[i];
                if (entry.LocalId >= lidRange.End)
                    continue;
                if (entry.LocalId < lidRange.Start)
                    yield break;
                yield return entry;
            }
        }
    }

    // This method never returns empty tiles
    public async IAsyncEnumerable<ChatTile> ReadTiles(
        Range<long> lidRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (lidRange.Size() <= 0)
            yield break;

        for (var idTile = IdTileLayer.GetTile(lidRange.Start);
             idTile.Start < lidRange.End;
             idTile = idTile.Next())
        {
            var tile = await GetTile(idTile.Range, cancellationToken).ConfigureAwait(false);
            // tile can be empty, i.e. when all entries are removed
            if (!tile.IsEmpty)
                yield return tile;
        }
    }

    // This method never returns empty tiles
    public async IAsyncEnumerable<ChatTile> ReadTilesReverse(
        Range<long> lidRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (lidRange.Size() <= 0)
            yield break;

        for (var idTile = IdTileLayer.GetTile(lidRange.End - 1);
             idTile.End > lidRange.Start;
             idTile = idTile.Prev())
        {
            var tile = await GetTile(idTile.Range, cancellationToken).ConfigureAwait(false);
            // tile can be empty, i.e. when all entries are removed
            if (!tile.IsEmpty)
                yield return tile;
        }
    }

    public async IAsyncEnumerable<ChatEntry> Observe(
        long minId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            var tile = await ObserveTileWithSuccessiveEntries(minId, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries) {
                if (entry.LocalId < minId)
                    continue;
                yield return entry;
            }
            minId = tile.Entries[^1].LocalId + 1; // Entries are always sorted by Id
        }
        // ReSharper disable once IteratorNeverReturns
    }

    // This method never returns an empty tile
    public async Task<ChatTile> ObserveTileWithSuccessiveEntries(long minEntryId, CancellationToken cancellationToken)
    {
        var idTile = IdTileLayer.GetTile(minEntryId);
        var cTileTask = CaptureTile(idTile.Range, cancellationToken);
        var cIdRangeTask = CaptureLidRange(cancellationToken);
        var cTile = await cTileTask.ConfigureAwait(false);
        var cIdRange = await cIdRangeTask.ConfigureAwait(false);

        while (true) {
            if (!(cTile.IsConsistent() && cIdRange.IsConsistent())) {
                var updateTileTask = cTile.Update(cancellationToken);
                var updateRangeTask = cIdRange.Update(cancellationToken);
                cTile = await updateTileTask.ConfigureAwait(false);
                cIdRange = await updateRangeTask.ConfigureAwait(false);
            }

            var tile = cTile.Value;
            foreach (var e in tile.Entries) // In fact, .Any, just w/ less allocations
                if (e.LocalId >= minEntryId)
                    return tile;

            var idRange = cIdRange.Value;
            if (idRange.IsEmptyOrNegative)
                // Empty chat (no entries of EntryKind) -> let's wait for the new ones
                goto waitForInvalidation;

            if (idTile.Range.End <= idRange.Start) {
                // The tile lies before the very first tile
                // -> move to the very first tile
                idTile = IdTileLayer.GetTile(idTile.Range.Start);
                cTile = await CaptureTile(idTile.Range, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (idTile.Range.End < idRange.End) {
                // There are tiles after this tile, so candidate entries on this tile are removed
                // -> move to the next tile
                idTile = idTile.Next();
                cTile = await CaptureTile(idTile.Range, cancellationToken).ConfigureAwait(false);
                continue;
            }

            waitForInvalidation:

            // It's presumably the last tile, so we have to wait for some.
            // The wait is scoped so the loser's handler doesn't stay on its computed - the winner
            // is usually cTile, while cIdRange survives the iteration and would collect one per entry.
            using var waitCts = cancellationToken.CreateLinkedTokenSource();
            await Task.WhenAny(
                cTile.WhenInvalidated(waitCts.Token),
                cIdRange.WhenInvalidated(waitCts.Token)
                ).ConfigureAwait(false);
            waitCts.CancelAndDisposeSilently();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public async Task<ChatEntry?> FindByMinBeginsAt(
        Moment minBeginsAt,
        Range<long> lidRange,
        CancellationToken cancellationToken)
    {
        var entry = await FindByMinBeginsAtPrecise(minBeginsAt - MaxBeginsAtDisorder, lidRange, cancellationToken)
            .ConfigureAwait(false);
        if (entry == null)
            return null;
        return await GetFirst((entry.LocalId, lidRange.End), e => e.BeginsAt >= minBeginsAt, MaxEntryCountDisorder, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private Task<Range<long>> GetLidRange(CancellationToken cancellationToken)
        => Chats.GetIdRange(Session, ChatId, cancellationToken);

    private Task<ChatTile> GetTile(Range<long> lidRange, CancellationToken cancellationToken)
        => Chats.GetTile(Session, ChatId, lidRange, cancellationToken);

    private ValueTask<Computed<Range<long>>> CaptureLidRange(CancellationToken cancellationToken)
        => Computed.Capture(() => GetLidRange(cancellationToken), cancellationToken);

    private ValueTask<Computed<ChatTile>> CaptureTile(Range<long> idRange, CancellationToken cancellationToken)
        => Computed.Capture(() => GetTile(idRange, cancellationToken), cancellationToken);

    private async Task<ChatEntry?> FindByMinBeginsAtPrecise(
        Moment beginsAt,
        Range<long> lidRange,
        CancellationToken cancellationToken)
    {
        var (minId, maxId) = lidRange.MoveEnd(-1);
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
        entry = await GetFirst((minId, lidRange.End), e => e.BeginsAt >= beginsAt, MaxEntryCountDisorder, cancellationToken)
            .ConfigureAwait(false);
        return entry;
    }
}
