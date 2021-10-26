using ActualChat.Mathematics.Internal;
using Stl.Fusion.Interception;

namespace ActualChat.Chat;

public class ChatEntryReader
{
    private IChatService Chats { get; }
    private MomentClockSet Clocks { get; }

    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; } = default;
    public TimeSpan InvalidationWaitTimeout { get; init; } = TimeSpan.FromMilliseconds(50);
    public ChatEntryReader(IChatService chats)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        var services = ((IComputeService) chats).GetServiceProvider();
        Chats = chats;
        Clocks = services.Clocks();
    }

    public async Task<ChatEntry?> TryGet(long entryId, CancellationToken cancellationToken)
    {
        var tile = ChatConstants.IdTiles.GetMinCoveringTile(entryId);
        var entries = await Chats.GetEntries(Session, ChatId, tile, cancellationToken).ConfigureAwait(false);
        return entries.SingleOrDefault(e => e.Id == entryId);
    }

    public async IAsyncEnumerable<ChatEntry> ReadTail(
        long afterEntryId,
        bool waitForNewEntries,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var idRangeComputed = await Computed
            .Capture(ct => Chats.GetIdRange(Session, ChatId, ct), cancellationToken)
            .ConfigureAwait(false);
        var lastReadEntryId = afterEntryId;
        while (true) {
            var tile = ChatConstants.IdTiles.GetMinCoveringTile(lastReadEntryId + 1);
            var entriesComputed = await Computed
                .Capture(ct => Chats.GetEntries(Session, ChatId, tile, ct), cancellationToken)
                .ConfigureAwait(false);
            var entries = entriesComputed.Value;

            if (entries.Length == 0) {
                // Update is ~ free when the computed is consistent
                idRangeComputed = await idRangeComputed.Update(cancellationToken).ConfigureAwait(false);
                var maxEntryId = idRangeComputed.Value.End;
                var lastTile = ChatConstants.IdTiles.GetMinCoveringTile(maxEntryId);
                if (tile.Start < lastTile.Start) {
                    // An empty tile, though it's not the one that includes the very last chat entry
                    lastReadEntryId = tile.End;
                    continue;
                }
                // We're either at the very last tile or maybe even on the next one
                if (!waitForNewEntries)
                    yield break; // And since there are 0 entries...

                // Ok, we're waiting for new entries.
                // 1. This should happen for sure if we already read the last entry:
                if (lastReadEntryId == maxEntryId)
                    await idRangeComputed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                // 2. And this should also happen unless _somehow_ the inserted entry landed
                //    on the next tile (i.e. it was either N-times-rollback scenario or something similar).
                //    In this case we want to probably adjust the tile by re-entering this block
                //    after timeout.
                await entriesComputed.WhenInvalidated(cancellationToken)
                    .WithTimeout(Clocks.CpuClock, InvalidationWaitTimeout, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            // Ok, we have some entries to yield back!
            foreach (var entry in entries) {
                if (!entriesComputed.IsConsistent())
                    break; // This ensures we'll re-read entries
                if (entry.Id > lastReadEntryId)
                    yield return entry; // Note that this "yield" can take arbitrary long time
                lastReadEntryId = entry.Id;
            }
        }
    }
}
