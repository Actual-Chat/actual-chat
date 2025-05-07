using ActualLab.Versioning;

namespace ActualChat.Flows;

public abstract class IndexingMasterFlowBase<TIndexingFlow, TItem, TId>
    : BatchedIndexingFlowBase<TItem, TId>
    where TIndexingFlow : Flow
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : StringIdentifier
{
    protected override async Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken)
    {
        foreach (var item in batch)
            await Host.Flows
                .StartOrReset<TIndexingFlow>(BuildArguments(item), null, "IndexingMasterFlow", cancellationToken)
                .ConfigureAwait(false);
    }

    protected virtual string BuildArguments(TItem item)
        => item.Id.Value;

    protected override Task<IndexingFlowTransitionKind> HandleTail(
        bool hasProcessedAnyItems,
        CancellationToken cancellationToken)
    {
        // Pause indexing until the version is bumped
        FlowSetVersion = CurrentFlowSetVersion;
        return Task.FromResult(IndexingFlowTransitionKind.Suspend);
    }
}
