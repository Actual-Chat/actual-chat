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
            foreach (var entry in tile.Value.Entries) {
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
        var lastId = minId - 1;
        var idRange = await Chats.GetIdRange(Session, ChatId, EntryType, cancellationToken).ConfigureAwait(false);
        idRange = (Math.Min(minId, idRange.End - 1), Math.Max(minId + 1, idRange.End));

        var thisTileComputed = null as IComputed<ChatTile>;
        await foreach (var tileComputed in ReadAllTiles(idRange, cancellationToken).ConfigureAwait(false)) {
            thisTileComputed = tileComputed;
            foreach (var entry in thisTileComputed.Value.Entries) {
                if (entry.Id <= lastId)
                    continue;

                yield return entry;
                lastId = entry.Id;
            }
        }
        if (thisTileComputed == null)
            throw new InvalidOperationException("Internal error: thisTileComputed == null!");

        while (true) {
            var newTileTask = WaitForNewTile(lastId, cancellationToken);
            var thisTileInvalidated = thisTileComputed.WhenInvalidated(cancellationToken);
            await Task.WhenAny(thisTileInvalidated, newTileTask).ConfigureAwait(false);
            if (thisTileInvalidated.IsCompleted) {
                await thisTileInvalidated.ConfigureAwait(false);
                thisTileComputed = await thisTileComputed.Update(cancellationToken).ConfigureAwait(false);
                foreach (var entry in thisTileComputed.Value.Entries) {
                    if (entry.Id <= lastId)
                        continue;

                    yield return entry;

                    lastId = entry.Id;
                }
            }
            if (newTileTask.IsCompleted) {
                thisTileComputed = await newTileTask.ConfigureAwait(false);
                foreach (var entry in thisTileComputed.Value.Entries) {
                    if (entry.Id <= lastId)
                        continue;

                    yield return entry;

                    lastId = entry.Id;
                }
            }
        }
    }

    public async IAsyncEnumerable<IComputed<ChatTile>> ReadAllTiles(
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
            var idTileRange = idTile.Range;
            var tileComputed = await Computed
                .Capture(ct => Chats.GetTile(Session, ChatId, EntryType, idTileRange, ct), cancellationToken)
                .ConfigureAwait(false);
            yield return tileComputed;
        }
    }

    public async Task<IComputed<ChatTile>> WaitForNewTile(long minId, CancellationToken cancellationToken)
    {
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var tile = idTilesLayer0.GetTile(minId);
        var nextTile = tile.Next();
        var nextTileComputed = await Computed
            .Capture(ct => Chats.GetTile(Session, ChatId, EntryType, nextTile.Range, ct), cancellationToken)
            .ConfigureAwait(false);
        while (nextTileComputed.Value.IsEmpty) {
            await nextTileComputed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
            nextTileComputed = await nextTileComputed.Update(cancellationToken).ConfigureAwait(false);
        }
        return nextTileComputed;
    }

    public async IAsyncEnumerable<IComputed<ChatTile>> ReadNewTiles(
        long minId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true) {
            var newTile = await WaitForNewTile(minId, cancellationToken).ConfigureAwait(false);
            yield return newTile;

            minId = newTile.Value.Entries[0].Id;
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

    private static async IAsyncEnumerable<IComputed<ChatTile>> InvalidateAndUpdate(
        IAsyncEnumerable<IComputed<ChatTile>> tiles,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completedSlots = new Stack<int>();
        var taskBuffer = ArrayBuffer<ValueTask<(IComputed<ChatTile>?,bool)>>.Lease(false);
        try {
            // adding artificial completed tasks
            for (int i = 0; i < taskBuffer.Capacity - 1; i++)
                taskBuffer.Add(ValueTask.FromResult((null as IComputed<ChatTile>, false)));

            var tilesEnumerator = tiles.GetAsyncEnumerator(cancellationToken);
            taskBuffer.Add(WrapMoveNext(tilesEnumerator, cancellationToken));
            var whenAnyValue = TaskExt.WhenAny(taskBuffer.Buffer);
            while (taskBuffer.Capacity > completedSlots.Count) {
                // cancellationToken.ThrowIfCancellationRequested();

                // tricky awaiter that returns an index of first completed task
                var index = await whenAnyValue;
                completedSlots.Push(index);

                // value tasks should be awaited once
                var (tileComputed, isEnum) = await taskBuffer[index].ConfigureAwait(false);
                if (isEnum) {
                    if (tileComputed == null) {
                        // tiles enumerable has been enumerated to the end
                    }
                    else {
                        var invalidateWrap = WrapInvalidateAndUpdate(tileComputed, cancellationToken);
                        var moveNextWrap = WrapMoveNext(tilesEnumerator, cancellationToken);

                        if (completedSlots.Count >= 2) {
                            whenAnyValue.Replace(completedSlots.Pop(), invalidateWrap);
                            whenAnyValue.Replace(completedSlots.Pop(), moveNextWrap);
                        }
                        else {
                            var newCapacity = taskBuffer.Capacity * 2;
                            var newTaskBuffer =
                                ArrayBuffer<ValueTask<(IComputed<ChatTile>?, bool)>>.Lease(false, newCapacity);
                            var oldTaskBuffer = taskBuffer;
                            try {
                                taskBuffer.Buffer.CopyTo(newTaskBuffer.Buffer, 0);
                                newTaskBuffer.Count = taskBuffer.Count;
                                newTaskBuffer.Add(invalidateWrap);
                                newTaskBuffer.Add(moveNextWrap);
                                // adding artificial completed tasks
                                for (int i = newTaskBuffer.Count; i < newTaskBuffer.Capacity; i++)
                                    newTaskBuffer.Add(ValueTask.FromResult((null as IComputed<ChatTile>, false)));

                                taskBuffer = newTaskBuffer;
                                whenAnyValue.Replace(taskBuffer.Buffer);
                            }
                            finally {
                                oldTaskBuffer.Release();
                            }
                        }
                    }
                }
                else {
                    if (tileComputed == null) {
                        // artificial completed task, do nothing
                    }
                    else
                        yield return tileComputed;
                }
            }
        }
        finally {
            taskBuffer.Release();
        }
    }

    private static async ValueTask<(IComputed<ChatTile>?, bool)> WrapInvalidateAndUpdate(
        IComputed<ChatTile> tileComputed,
        CancellationToken cancellationToken)
    {
        var updatedComputed = await InvalidateAndUpdate(tileComputed, cancellationToken).ConfigureAwait(false);
        return (updatedComputed, false);
    }

    private static async ValueTask<(IComputed<ChatTile>?, bool)> WrapMoveNext(
        IAsyncEnumerator<IComputed<ChatTile>> tilesEnumerator,
        CancellationToken cancellationToken)
    {
        var hasNext = await tilesEnumerator.MoveNextAsync(cancellationToken).ConfigureAwait(false);
        return hasNext
            ? (tilesEnumerator.Current, true)
            : (null, true);
    }

    private static async ValueTask<IComputed<ChatTile>> InvalidateAndUpdate(
        IComputed<ChatTile> tileComputed,
        CancellationToken cancellationToken)
    {
        await tileComputed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        return await tileComputed.Update(cancellationToken).ConfigureAwait(false);
    }

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
