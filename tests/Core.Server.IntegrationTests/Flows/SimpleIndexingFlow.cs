using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SimpleIndexingFlow : IndexingFlowBase<long>
{
    protected override async Task<BatchIndexingResult<long>> ProcessBatch(long cursor, CancellationToken cancellationToken)
    {
        var context = Host.Services.GetRequiredService<IndexingFlowTestContext>();
        await Task.Delay(1, cancellationToken);
        return context.Next(Id.Arguments);
    }

    protected override async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var context = Host.Services.GetRequiredService<IndexingFlowTestContext>();
        var transition = await base.OnIndex(cancellationToken);
        context.OnTransition(Id.Arguments, transition);

        return transition;
    }
}
