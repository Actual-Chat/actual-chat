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
            await Reset(item, cancellationToken).ConfigureAwait(false);
    }

    protected Task Reset(TItem item, CancellationToken cancellationToken)
        => Host.Flows.Reset<TIndexingFlow>(item.Id.Value, null, GetType().Name, cancellationToken);

    protected override Task<IndexingFlowTransitionKind> HandleTail(
        bool hasProcessedAnyItems,
        CancellationToken cancellationToken)
    {
        // Pause indexing until the version is bumped
        FlowSetVersion = CurrentFlowSetVersion;
        return Task.FromResult(IndexingFlowTransitionKind.Suspend);
    }
}
