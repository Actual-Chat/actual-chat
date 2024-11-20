namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class BatchedIndexingFlowTestContext<TItem>(MomentClockSet clocks) : IndexingFlowContextBase<IReadOnlyList<TItem>>(clocks)
{
    public override IReadOnlyList<TItem> Next(Symbol id)
        => _batches[id].TryDequeue(out var batch) ? batch : [];

    protected override int GetCount(IReadOnlyList<TItem> batch)
        => batch.Count;
}
