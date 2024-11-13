using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class IndexingMasterFlowBase<TIndexingFlow, TItem, TId>
    : BatchedIndexingFlowBase<TItem, TId>
    where TIndexingFlow : Flow
    where TItem : class, IHasId<TId>, IHasVersion<long>
    where TId : ISymbolIdentifier
{
    [DataMember(Order = 200), MemoryPackOrder(200)]
    public long FlowSetVersion { get; protected set; }
    [IgnoreDataMember, MemoryPackIgnore]
    protected abstract int CurrentFlowSetVersion { get; }

    protected override Task<bool> OnBeforeFirstIndexAfterReset(CancellationToken cancellationToken)
        => FlowSetVersion >= CurrentFlowSetVersion
            ? ActualLab.Async.TaskExt.FalseTask // Already indexed
            : base.OnBeforeFirstIndexAfterReset(cancellationToken); // start indexing

    protected override async Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken)
    {
        foreach (var item in batch)
            await Host.Flows.GetOrStart<TIndexingFlow>(BuildArguments(item), cancellationToken).ConfigureAwait(false);
    }

    protected virtual string BuildArguments(TItem item)
        => item.Id.Value;

    protected override Task<bool> OnTailReached(CancellationToken cancellationToken)
    {
        // stop indexing until version is bumped
        FlowSetVersion = CurrentFlowSetVersion;
        return ActualLab.Async.TaskExt.FalseTask;
    }
}
