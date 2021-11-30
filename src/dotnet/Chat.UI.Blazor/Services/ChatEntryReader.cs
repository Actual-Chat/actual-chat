using Stl.Fusion.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatEntryReader
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private readonly IChats _chats;
    private readonly MomentClockSet _clocks;

    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; }
    public TimeSpan InvalidationWaitTimeout { get; init; } = TimeSpan.FromMilliseconds(50);

    public ChatEntryReader(IChats chats)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        var services = ((IComputeService)chats).GetServiceProvider();
        _chats = chats;
        _clocks = services.Clocks();
    }

    public async IAsyncEnumerable<ChatEntry> GetAllAfter(
        long minEntryId,
        bool waitForNewEntries,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var idRangeComputed = await Computed
            .Capture(ct => _chats.GetIdRange(Session, ChatId, ct), cancellationToken)
            .ConfigureAwait(false);
        var lastId = minEntryId - 1;
        while (true) {
            var idRange = idRangeComputed.Value;
            var lastIdTile = idTilesLayer0.GetTile(idRange.End);

            // 1. Let's yield whatever is available till the very end
            for (var idTile = idTilesLayer0.GetTile(lastId + 1);
                 idTile.Start <= lastIdTile.Start;
                 idTile = idTile.Next())
            {
                var chatTile = await _chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
                foreach (var entry in chatTile.Entries) {
                    if (entry.Id <= lastId)
                        continue;
                    lastId = entry.Id;
                    yield return entry; // Note that this "yield" can take arbitrary long time
                }
            }

            // 2. Let's check if it's really the very end
            idRangeComputed = await idRangeComputed.Update(cancellationToken).ConfigureAwait(false);
            if (idRangeComputed.Value == idRange) {
                if (!waitForNewEntries)
                    break; // It is the end & we won't wait for new entries

                await idRangeComputed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                idRangeComputed = await idRangeComputed.Update(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<long> GetNextEntryId(Moment minBeginsAt, CancellationToken cancellationToken)
    {
        // Let's bisect (minId, maxId) range to find the right entry
        var (minId, maxId) = await _chats.GetIdRange(Session, ChatId, cancellationToken).ConfigureAwait(false);
        while (minId < maxId) {
            var entryId = minId + ((maxId - minId) >> 1);
            var entry = await Get(entryId, maxId, cancellationToken).ConfigureAwait(false);
            if (entry == null)
                maxId = entryId;
            else {
                if (minBeginsAt == entry.BeginsAt) {
                    var prevEntry = await Get(entryId - 1, maxId, cancellationToken).ConfigureAwait(false);
                    if (prevEntry != null && minBeginsAt == prevEntry.BeginsAt)
                        return entryId - 1;

                    return entryId;
                }

                if (minBeginsAt > entry.BeginsAt && !entry.IsStreaming)
                    minId = entryId + 1;
                else
                    maxId = entryId;
            }
        }
        return minId;
    }

    public async Task<ChatEntry?> Get(long entryId, CancellationToken cancellationToken)
    {
        var idTile = IdTileStack.FirstLayer.GetTile(entryId);
        var chatTile = await _chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
        return chatTile.Entries.SingleOrDefault(e => e.Id == entryId);
    }

    public async Task<ChatEntry?> Get(long minEntryId, long maxEntryId, CancellationToken cancellationToken)
    {
        var idTilesLayer0 = IdTileStack.FirstLayer;
        var lastIdTile = idTilesLayer0.GetTile(maxEntryId);
        while (true) {
            var idTile = idTilesLayer0.GetTile(minEntryId);
            var chatTile = await _chats.GetTile(Session, ChatId, idTile.Range, cancellationToken).ConfigureAwait(false);
            if (chatTile.IsEmpty) {
                if (idTile.Start >= lastIdTile.Start) // We're at the very last tile
                    return null;

                minEntryId = idTile.End + 1;
                continue;
            }
            foreach (var entry in chatTile.Entries)
                if (entry.Id >= minEntryId && entry.Id <= maxEntryId)
                    return entry;

            minEntryId = idTile.End + 1;
        }
    }
}
