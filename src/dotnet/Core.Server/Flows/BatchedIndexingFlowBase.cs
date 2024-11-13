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
        var anyHandled = false;
        await foreach (var batch in batches) {
            await ProcessBatch(batch, cancellationToken).ConfigureAwait(false);
            var last = batch[^1];
            cursor = new (last.Id, last.Version);
            anyHandled = true;
            if (batch.Count < BatchSize)
                return new (false, true, cursor);
        }

        return new (false, !anyHandled, cursor);
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

