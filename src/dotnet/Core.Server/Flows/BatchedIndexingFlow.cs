using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

// Base class for flows that perform batched indexing of versioned items.
// Provides a higher-level abstraction over IndexingFlowBase
// with built-in batch enumeration and quota management.
public abstract class BatchedIndexingFlow<TItem, TId> : IndexingFlow<IndexingFlowCursor<TId>>
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : StringIdentifier
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int BatchSize => 100;
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int Quota => 1000;

    // Overridable methods

    protected abstract Task<IReadOnlyList<TItem>> GetBatch(IndexingFlowCursor<TId>? cursor, CancellationToken cancellationToken);
    protected abstract Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken);

    // Implementation

    protected override async ValueTask<BatchIndexingResult<IndexingFlowCursor<TId>>> Process(
        IndexingFlowCursor<TId>? cursor,
        CancellationToken cancellationToken)
    {
        var startedAt = CpuTimestamp.Now;
        var batchCount = 0;
        var totalCount = 0;

        await foreach (var (batch, newCursor) in EnumerateBatches(cursor, cancellationToken).ConfigureAwait(false)) {
            var batchStartedAt = CpuTimestamp.Now;
            await ProcessBatch(batch, cancellationToken).ConfigureAwait(false);

            var first = batch[0];
            var last = batch[^1];
            Console.Log($"Processed batch of {batch.Count} items, first=(#{first.Id}, v{first.Version}), last=(#{last.Id}, v{last.Version}) | {batchStartedAt.Elapsed.ToShortString()}");

            cursor = newCursor;
            batchCount++;
            totalCount += batch.Count;
        }
        Console.Log($"Processed {totalCount} items in {batchCount} batches -> {cursor} | {startedAt.Elapsed.ToShortString()}");

        return new BatchIndexingResult<IndexingFlowCursor<TId>> {
            Cursor = cursor,
            HasProcessedAnyItems = totalCount > 0,
            IsTailReached = totalCount < Quota,
        };
    }

    // Private methods

    private async IAsyncEnumerable<(IReadOnlyList<TItem> Batch, IndexingFlowCursor<TId> NewCursor)> EnumerateBatches(
        IndexingFlowCursor<TId>? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalCount = 0;
        while (totalCount < Quota) {
            var batch = await GetBatch(cursor, cancellationToken).ConfigureAwait(false);

            if (batch.Count > BatchSize)
                Console.LogWarning($"GetBatch returned {batch.Count} items (> BatchSize={BatchSize})");

            if (batch.Count == 0)
                yield break;

            var last = batch[^1];
            cursor = new IndexingFlowCursor<TId>(last.Id, last.Version);
            totalCount += batch.Count;

            yield return (batch, cursor);

            if (batch.Count < BatchSize)
                yield break;
        }
    }
}
