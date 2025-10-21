using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

public sealed class IndexingFlowTestContext(MomentClockSet clocks) : IndexingFlowContextBase<BatchIndexingResult<long>>(clocks)
{
    public override BatchIndexingResult<long>? Next(string id)
        => Batches[id].TryDequeue(out var result)
            ? result
            : null;

    protected override bool HasProcessedAnyItems(BatchIndexingResult<long> batch)
        => batch.HasProcessedAnyItems;
}
