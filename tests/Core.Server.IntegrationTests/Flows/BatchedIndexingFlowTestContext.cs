using ActualChat.Flows;
using ActualLab.Versioning;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class BatchedIndexingFlowTestContext<TItem, TId>(MomentClockSet clocks)
    : IndexingFlowContextBase<IReadOnlyList<TItem>>(clocks)
    where TItem : IHasVersion<long>, IHasId<TId>
    where TId : ISymbolIdentifier
{
    private TItem? _last;

    public IReadOnlyList<TItem> Next(IndexingFlowCursor<TId>? cursor, string id)
    {
        var batchQueue = Batches[id];
        if (!batchQueue.TryPeek(out var batch))
            return [];

        var lastUpdatedVersion = cursor?.LastUpdatedVersion ?? 0;
        if (batch.Count == 0)
            return batchQueue.Dequeue();

        if (batch[0].Version < lastUpdatedVersion)
            return [];

        if (_last != null && cursor != null && !Equals(_last.Id, cursor.LastUpdatedId))
            return [];

        _last = batch[^1];
        return batchQueue.Dequeue();
    }

    public override IReadOnlyList<TItem> Next(Symbol id)
        => Batches[id].TryDequeue(out var batch) ? batch : [];

    protected override int GetCount(IReadOnlyList<TItem> batch)
        => batch.Count;
}
