using ActualLab.Versioning;

namespace ActualChat.Flows;

// Base class for master flows that spawn child indexing flows.
// Processes batches of items by resetting/starting a child flow for each item.
public abstract class IndexingMasterFlow<TIndexingFlow, TItem, TId> : BatchedIndexingFlow<TItem, TId>
    where TIndexingFlow : Flow
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : StringIdentifier
{
    protected override async Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken)
    {
        foreach (var item in batch)
            await ScheduleReset(item, cancellationToken).ConfigureAwait(false);
    }

    protected override ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        Console.Log("Tail reached, suspending");
        return default;
    }

    protected Task ScheduleReset(TItem item, CancellationToken cancellationToken)
        => Hub.NewResumeEvent<TIndexingFlow>(item.Id.Value).WithReset().Schedule(cancellationToken);
}
