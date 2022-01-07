namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatEntryReader
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;
    private IChats Chats { get; }

    public Session Session { get; init; } = Session.Null;
    public Symbol ChatId { get; init; }
    public ChatEntryType EntryType { get; init; }
    public TimeSpan InvalidationWaitTimeout { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan MaxBeginsAtDisorder { get; init; } = TimeSpan.FromSeconds(15);

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
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var lastId = idRange.Start - 1;
        for (var idTile = idTilesLayer0.GetTile(lastId + 1);
             idTile.Start < idRange.End;
             idTile = idTile.Next())
        {
            var tile = await Chats.GetTile(Session, ChatId, EntryType, idTile.Range, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries) {
                if (entry.Id <= lastId)
                    continue;
                if (entry.Id >= idRange.End)
                    yield break;
                yield return entry;
                lastId = entry.Id;
            }
        }
    }

    public async IAsyncEnumerable<ChatEntry> ReadAllWaitingForNew(
        long minId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var idRangeComputed = await Computed
            .Capture(ct => Chats.GetIdRange(Session, ChatId, EntryType, ct), cancellationToken)
            .ConfigureAwait(false);
        var lastId = minId - 1;
        while (true) {
            var idRange = idRangeComputed.Value;
            var entries = ReadAll((lastId + 1, idRange.End), cancellationToken);
            await foreach (var entry in entries.ConfigureAwait(false)) {
                if (entry.Id <= lastId)
                    continue;
                yield return entry;
                lastId = entry.Id;
            }
            await idRangeComputed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            idRangeComputed = await idRangeComputed.Update(cancellationToken).ConfigureAwait(false);
        }
        // ReSharper disable once IteratorNeverReturns
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
        entry = await GetFirst((minId, idRange.End), cancellationToken).ConfigureAwait(false);
        if (entry == null || entry.BeginsAt < beginsAt)
            return null;
        return entry;
    }
}
