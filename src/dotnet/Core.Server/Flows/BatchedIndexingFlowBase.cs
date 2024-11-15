using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

public abstract class BatchedIndexingFlowBase<TItem, TId> : IndexingFlowBase<IndexingFlowCursor<TId>>
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : ISymbolIdentifier
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int BatchSize => 100;
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int Quota => 1000;

    protected override async Task<BatchIndexingResult<IndexingFlowCursor<TId>>> Process(
        IndexingFlowCursor<TId>? cursor,
        CancellationToken cancellationToken)
    {
        var batches = ListBatches(cancellationToken).ConfigureAwait(false);
        var batchCount = 0;
        var totalCount = 0;
        await foreach (var batch in batches) {
            await ProcessBatch(batch, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("`{Id}`.Processed batch of {Count}", Id, batch.Count);
            var last = batch[^1];
            cursor = new (last.Id, last.Version);
            batchCount++;
            totalCount += batch.Count;
            if (batch.Count < BatchSize)
                break;
        }

        Log.LogInformation("`{Id}`.Process: Completed {TotalCount} items in {BatchCount} batches. New cursor: {Cursor}",
            Id,
            totalCount,
            batchCount,
            cursor);
        return new (false, totalCount < BatchSize, cursor);
    }

    protected abstract Task<IReadOnlyList<TItem>> GetBatch(IndexingFlowCursor<TId>? cursor, CancellationToken cancellationToken);
    protected abstract Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken);

    // Private methods

    private async IAsyncEnumerable<IReadOnlyList<TItem>> ListBatches([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalCount = 0;
        do {
            var batch = await GetBatch(Cursor, cancellationToken).ConfigureAwait(false);
            totalCount += batch.Count;
            if (batch.Count == 0)
                yield break;

            yield return batch;

            if (batch.Count < BatchSize || totalCount >= Quota)
                yield break;
        } while (totalCount < Quota);
    }
}

