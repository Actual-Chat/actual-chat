using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

/// <summary>
/// Convenience base class for batched entity indexing flows.
/// Subclasses implement <see cref="GetBatch"/> and <see cref="ProcessBatch"/> to define the indexing logic.
/// Handles cursor management automatically using (Id, Version) pairs.
/// </summary>
/// <typeparam name="TItem">Entity type that has Id and Version.</typeparam>
/// <typeparam name="TId">Id type (must be a StringIdentifier).</typeparam>
public abstract class NewBatchIndexingFlow<TItem, TId> : NewIndexingFlow<IndexingFlowCursor<TId>>
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : StringIdentifier
{
    // ═══════════════════════════════════════════════════════════════════
    // Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Number of items per batch. GetBatch should return at most this many items.</summary>
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int BatchSize { get; } = 100;

    // ═══════════════════════════════════════════════════════════════════
    // Abstract Methods - Subclasses Must Implement
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetch next batch of items starting from cursor.
    /// Should return items with (Id &gt; cursor.LastUpdatedId) OR (Version &gt; cursor.LastUpdatedVersion).
    /// Items should be ordered by (Version, Id) to ensure consistent pagination.
    /// </summary>
    /// <param name="cursor">Current cursor position (null = start from beginning).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of items to process (empty if nothing to index).</returns>
    protected abstract Task<IReadOnlyList<TItem>> GetBatch(
        IndexingFlowCursor<TId>? cursor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Process a batch of items (save to search index, transform, etc).
    /// </summary>
    /// <param name="batch">Items to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task ProcessBatch(
        IReadOnlyList<TItem> batch,
        CancellationToken cancellationToken);

    // ═══════════════════════════════════════════════════════════════════
    // Core Implementation
    // ═══════════════════════════════════════════════════════════════════

    protected sealed override async Task<IndexingBatch<IndexingFlowCursor<TId>>> ProcessNextBatch(
        IndexingFlowCursor<TId>? cursor,
        CancellationToken cancellationToken)
    {
        var items = await GetBatch(cursor, cancellationToken).ConfigureAwait(false);

        if (items.Count == 0)
            return new(IsEmpty: true, IsTailReached: true, NextCursor: cursor);

        await ProcessBatch(items, cancellationToken).ConfigureAwait(false);

        var last = items[^1];
        var nextCursor = new IndexingFlowCursor<TId>(last.Id, last.Version);
        var isTailReached = items.Count < BatchSize;

        return new(IsEmpty: false, IsTailReached: isTailReached, NextCursor: nextCursor);
    }
}
