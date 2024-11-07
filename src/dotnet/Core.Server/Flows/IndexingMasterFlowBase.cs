using ActualLab.Versioning;

namespace ActualChat.Flows;

public abstract class IndexingMasterFlowBase<TIndexingFlow, TItem, TId>
    : BatchedIndexingFlowBase<TItem, TId>
    where TIndexingFlow : Flow
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : ISymbolIdentifier
{
    protected virtual string BuildArguments(TItem item)
        => item.Id.Value;

    protected override async Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken)
    {
        foreach (var item in batch)
            await Host.Flows.GetOrStart<TIndexingFlow>(BuildArguments(item), cancellationToken).ConfigureAwait(false);
    }

    protected override Task<bool> OnTailReached(CancellationToken cancellationToken)
        => ActualLab.Async.TaskExt.FalseTask; // end if tail is reached
}
