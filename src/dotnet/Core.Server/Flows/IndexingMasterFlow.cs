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
            await ScheduleResume(item, cancellationToken).ConfigureAwait(false);
    }

    protected override ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        Console.Log("Tail reached, suspending");
        return default;
    }

    protected Task ScheduleResume(TItem item, CancellationToken cancellationToken)
        => ScheduleResume(item, mustReset: false, cancellationToken);
    protected Task ScheduleResume(TItem item, bool mustReset, CancellationToken cancellationToken)
    {
        var resumeEvent = Hub.NewResumeEvent<TIndexingFlow>(item.Id.Value).WithReset(mustReset);
        return resumeEvent.Schedule(cancellationToken);
    }
}
