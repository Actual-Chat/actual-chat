namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatEntryReader
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    public IChats Chats { get; }
    public Session Session { get; init; } = Session.Null;
    public Symbol ChatId { get; init; }
    public ChatEntryType EntryType { get; init; }
    public TimeSpan MaxBeginsAtDisorder { get; init; } = TimeSpan.FromSeconds(15);

    public ChatEntryReader(IChats chats, Session session, Symbol chatId, ChatEntryType entryType)
    {
        Chats = chats;
        Session = session;
        ChatId = chatId;
        EntryType = entryType;
    }

    public async Task<ChatEntry?> Get(long id, CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(id);
        var tile = await Chats.GetTile(Session, ChatId, EntryType, idTile.Range, cancellationToken).ConfigureAwait(false);
        return tile.Entries.SingleOrDefault(e => e.Id == id);
    }

    public async Task<TextEntryAttachment> GetFirstAttachment(long entryId, CancellationToken cancellationToken)
    {
        var all = await Chats.GetTextEntryAttachments(Session, ChatId, entryId, cancellationToken).ConfigureAwait(false);
        return all.First();
    }

    public Task<ImmutableArray<TextEntryAttachment>> GetAttachments(long entryId, CancellationToken cancellationToken)
        => Chats.GetTextEntryAttachments(Session, ChatId, entryId, cancellationToken);

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

    public async Task<ChatEntry?> GetLast(Range<long> idRange, Func<ChatEntry, bool> filter, CancellationToken cancellationToken)
    {
        var (minId, maxId) = idRange;

        await foreach (var tile in ReadTilesReverse(idRange, cancellationToken).ConfigureAwait(false)) {
            foreach (var entry in tile.Entries.Reverse()) {
                if (entry.Id < minId)
                    return null;
                if (entry.Id <= maxId && filter(entry))
                    return entry;
            }
        }

        return null;
    }

    public async Task<ChatEntry?> GetWhen(
        long id,
        Func<ChatEntry?, bool> predicate,
        CancellationToken cancellationToken)
    {
        var idTile = Constants.Chat.IdTileStack.FirstLayer.GetTile(id);
        var cTile = await Computed
            .Capture(() => Chats.GetTile(Session, ChatId, EntryType, idTile.Range, cancellationToken))
            .ConfigureAwait(false);

        cTile = await cTile.When(
                t => predicate(t.Entries.FirstOrDefault(e => e.Id == id)),
                cancellationToken
            ).ConfigureAwait(false);
        return cTile.Value.Entries.FirstOrDefault(e => e.Id == id);
    }

    public async IAsyncEnumerable<ChatEntry> Read(
        Range<long> idRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var tile in ReadTiles(idRange, cancellationToken).ConfigureAwait(false)) {
            foreach (var entry in tile.Entries) {
                if (entry.Id < idRange.Start)
                    continue;
                if (entry.Id >= idRange.End)
                    yield break;
                yield return entry;
            }
        }
    }

    public async IAsyncEnumerable<ChatEntry> ReadReverse(
        Range<long> idRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var tile in ReadTilesReverse(idRange, cancellationToken).ConfigureAwait(false)) {
            foreach (var entry in tile.Entries.Reverse()) {
                if (entry.Id >= idRange.End)
                    continue;
                if (entry.Id < idRange.Start)
                    yield break;
                yield return entry;
            }
        }
    }

    // This method never returns empty tiles
    public async IAsyncEnumerable<ChatTile> ReadTiles(
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
            var tile = await GetTile(idTile.Range, cancellationToken).ConfigureAwait(false);
            // tile can be empty, i.e. when all entries are removed
            if (tile.IsEmpty)
                continue;
            yield return tile;
        }
    }

    // This method never returns empty tiles
    public async IAsyncEnumerable<ChatTile> ReadTilesReverse(
        Range<long> idRange,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (idRange.Size() <= 0)
            yield break;

        var idTilesLastLayer = IdTileStack.LastLayer;
        for (var idTile = idTilesLastLayer.GetTile(idRange.End);
             idTile.End > idRange.Start;
             idTile = idTile.Prev())
        {
            var tile = await GetTile(idTile.Range, cancellationToken).ConfigureAwait(false);
            // tile can be empty, i.e. when all entries are removed
            if (tile.IsEmpty)
                continue;
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
                if (entry.Id < minId)
                    continue;
                yield return entry;
            }
            minId = tile.Entries[^1].Id + 1; // Entries are always sorted by Id
        }
        // ReSharper disable once IteratorNeverReturns
    }

    // This method never returns an empty tile
    public async Task<ChatTile> ObserveTileWithSuccessiveEntries(long minEntryId, CancellationToken cancellationToken)
    {
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var idTile = idTilesLayer0.GetTile(minEntryId);

        var cTileTask = CaptureTile(idTile.Range, cancellationToken);
        var cIdRangeTask = CaptureIdRange(cancellationToken);
        await Task.WhenAll(cTileTask.AsTask(), cIdRangeTask.AsTask()).ConfigureAwait(false);
#pragma warning disable MA0004
        var cTile = await cTileTask;
        var cIdRange = await cIdRangeTask;
#pragma warning restore MA0004
        while (true) {
            if (!(cTile.IsConsistent() && cIdRange.IsConsistent()))
                (cTile, cIdRange) = await ComputedExt.Update(cTile, cIdRange, cancellationToken).ConfigureAwait(false);

            var tile = cTile.Value;
            foreach (var e in tile.Entries) // In fact, .Any, just w/ less allocations
                if (e.Id >= minEntryId)
                    return tile;

            var idRange = cIdRange.Value;
            if (idRange.IsEmpty)
                // Empty chat (no entries of EntryType) -> let's wait for the new ones
                goto waitForInvalidation;

            if (idTile.Range.End <= idRange.Start) {
                // The tile lies before the very first tile
                // -> move to the very first tile
                idTile = idTilesLayer0.GetTile(idTile.Range.Start);
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

            // It's presumably the last tile, so we have to wait for some
            var completedTask = await Task.WhenAny(
                cTile.WhenInvalidated(cancellationToken),
                cIdRange.WhenInvalidated(cancellationToken)
                ).ConfigureAwait(false);
#pragma warning disable MA0004
            await completedTask; // Will throw an exception on cancellation
#pragma warning restore MA0004
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

    private Task<Range<long>> GetIdRange(CancellationToken cancellationToken)
        => Chats.GetIdRange(Session, ChatId, EntryType, cancellationToken);

    private Task<ChatTile> GetTile(Range<long> idRange, CancellationToken cancellationToken)
        => Chats.GetTile(Session, ChatId, EntryType, idRange, cancellationToken);

    private ValueTask<Computed<Range<long>>> CaptureIdRange(CancellationToken cancellationToken)
        => Computed.Capture(() => GetIdRange(cancellationToken));

    private ValueTask<Computed<ChatTile>> CaptureTile(Range<long> idRange, CancellationToken cancellationToken)
        => Computed.Capture(() => GetTile(idRange, cancellationToken));

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
