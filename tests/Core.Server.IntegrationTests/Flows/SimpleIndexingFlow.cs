using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SimpleIndexingFlow : IndexingFlow<long>
{
    private IndexingFlowTestContext Context => field ??= Services.GetRequiredService<IndexingFlowTestContext>();

    protected override async ValueTask<BatchIndexingResult<long>> Process(long cursor, CancellationToken cancellationToken)
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
