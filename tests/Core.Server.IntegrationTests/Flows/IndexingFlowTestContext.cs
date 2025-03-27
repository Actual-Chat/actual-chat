using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class IndexingFlowTestContext(MomentClockSet clocks) : IndexingFlowContextBase<BatchIndexingResult<long>>(clocks)
{
    public override BatchIndexingResult<long> Next(Symbol id)
        => Batches[id].Dequeue();

    protected override bool HasProcessedAnyItems(BatchIndexingResult<long> batch)
        => batch.HasProcessedAnyItems;
}
