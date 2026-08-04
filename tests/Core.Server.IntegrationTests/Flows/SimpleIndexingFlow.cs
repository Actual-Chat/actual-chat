using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MessagePackObject(true)]
public partial class SimpleIndexingFlow : IndexingFlow<long>
{
    private IndexingFlowTestContext Context => field ??= Services.GetRequiredService<IndexingFlowTestContext>();

    protected override ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
        => new(FlowReadiness.Ready);

    protected override async ValueTask<BatchIndexingResult<long>> Run(long cursor, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var batch = Context.Next(Id.Arguments);
        if (batch is null)
            batch = new BatchIndexingResult<long> {
                Cursor = cursor,
                IsTailReached = false,
                HasProcessedAnyItems = true,
            };
        else
            Context.OnProcessed(Id.Arguments, batch);
        return batch;
    }
}
