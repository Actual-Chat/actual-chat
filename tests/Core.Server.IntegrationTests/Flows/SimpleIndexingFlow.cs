using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SimpleIndexingFlow : IndexingFlowBase<long>
{
    [IgnoreDataMember, MemoryPackIgnore]
    private IndexingFlowTestContext Context => Host.Services.GetRequiredService<IndexingFlowTestContext>();

    protected override async Task<BatchIndexingResult<long>> Process(long cursor, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return Context.Next(Id.Arguments);
    }

    protected override async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var transition = await base.OnIndex(cancellationToken);
        Context.OnTransition(Id.Arguments, transition);

        return transition;
    }
}
