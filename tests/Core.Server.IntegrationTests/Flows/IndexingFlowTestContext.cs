using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class IndexingFlowTestContext(MomentClockSet clocks) : IndexingFlowContextBase<BatchIndexingResult<long>>(clocks)
{
    public override BatchIndexingResult<long> Next(Symbol id)
        => _batches[id].Dequeue();

    protected override int GetCount(BatchIndexingResult<long> batch)
        => batch.ProcessedCount;
}
