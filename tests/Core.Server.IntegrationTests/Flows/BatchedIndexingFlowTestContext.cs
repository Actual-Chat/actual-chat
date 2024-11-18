namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class BatchedIndexingFlowTestContext<TItem>(MomentClockSet clocks) : IndexingFlowContextBase<IReadOnlyList<TItem>>(clocks)
{
    protected override int GetCount(IReadOnlyList<TItem> batch)
        => batch.Count;

    protected override IReadOnlyList<TItem> Default()
        => [];
}
