using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class IndexingMasterFlowBase<TIndexingFlow, TItem, TId>
    : IndexingFlowBase<IndexMasterFlowCursor<TId>>
    where TIndexingFlow : Flow
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : ISymbolIdentifier
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int BatchSize => 1000;

    protected abstract Task<IReadOnlyList<TItem>> GetBatch(IndexMasterFlowCursor<TId>? cursor, CancellationToken cancellationToken);

    protected virtual string BuildArguments(TItem item)
        => item.Id.Value;

    protected override async Task<BatchIndexingResult<IndexMasterFlowCursor<TId>>> ProcessBatch(
        IndexMasterFlowCursor<TId>? cursor,
        CancellationToken cancellationToken)
    {
        var batch = await GetBatch(Cursor, cancellationToken).ConfigureAwait(false);
        foreach (var item in batch)
            await Host.Flows.GetOrStart<TIndexingFlow>(BuildArguments(item), cancellationToken).ConfigureAwait(false);
        var last = batch[^1];
        return new (batch.Count < BatchSize, batch.Count < BatchSize, new (last.Id, last.Version));
    }

    protected virtual Task<bool> OnBeforeTargetFlowStart(TItem item, CancellationToken cancellationToken)
        => ActualLab.Async.TaskExt.TrueTask;
}
