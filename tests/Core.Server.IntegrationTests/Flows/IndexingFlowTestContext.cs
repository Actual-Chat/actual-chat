using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class IndexingFlowTestContext(MomentClockSet clocks) : IndexingFlowContextBase<BatchIndexingResult<long>>(clocks)
{
    protected override int GetCount(BatchIndexingResult<long> batch)
        => batch.Count;
}
