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
        var sw = Stopwatch.StartNew();
        var batches = ListBatches(cursor, cancellationToken).ConfigureAwait(false);
        var batchCount = 0;
        var totalCount = 0;
        await foreach (var (batch, newCursor) in batches) {
            var batchSw = Stopwatch.StartNew();
            await ProcessBatch(batch, cancellationToken).ConfigureAwait(false);
            var first = batch[0];
            var last = batch[^1];
            DebugLog?.LogDebug(
                "`{Id}`.Process: processed batch: {Count} items, first=(#{FirstId},v{FirstIdVersion}), last=(#{LastId},v{LastIdVersion})). {Elapsed}",
                Id,
                batch.Count,
                first.Id,
                first.Version,
                last.Id,
                last.Version,
                batchSw.Elapsed);
            cursor = newCursor;
            batchCount++;
            totalCount += batch.Count;
        }

        Log.LogInformation("`{Id}`.Process: Completed {TotalCount} items in {BatchCount} batches. {Elapsed}. New cursor: {Cursor}",
            Id,
            totalCount,
            batchCount,
            cursor,
            sw.Elapsed);
        return new (false, totalCount < Quota, cursor, totalCount);
    }

    protected abstract Task<IReadOnlyList<TItem>> GetBatch(IndexingFlowCursor<TId>? cursor, CancellationToken cancellationToken);
    protected abstract Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken);

    // Private methods

    private async IAsyncEnumerable<(IReadOnlyList<TItem> Batch, IndexingFlowCursor<TId> NewCursor)> ListBatches(
        IndexingFlowCursor<TId>? cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalCount = 0;
        do {
            var batch = await GetBatch(cursor, cancellationToken).ConfigureAwait(false);
            if (batch.Count > BatchSize)
                Log.LogWarning("`{Id}`.ListBatches: GetBatch returned batch with size({Size}) > BatchSize({BatchSize})",
                    Id,
                    batch.Count,
                    BatchSize);
            totalCount += batch.Count;
            if (batch.Count == 0)
                yield break;

            var last = batch[^1];
            cursor = new (last.Id, last.Version);
            yield return (batch, cursor);

            if (batch.Count < BatchSize || totalCount >= Quota)
                yield break;
        } while (totalCount < Quota);
    }
}

