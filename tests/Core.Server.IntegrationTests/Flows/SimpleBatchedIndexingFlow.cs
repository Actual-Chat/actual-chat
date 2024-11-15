using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SimpleBatchedIndexingFlow : BatchedIndexingFlowBase<SimpleItem, ChatId>
{
    protected override int BatchSize => 3;
    protected override int Quota => 6;

    [IgnoreDataMember, MemoryPackIgnore]
    private BatchedIndexingFlowTestContext<SimpleItem> Context => Host.Services.GetRequiredService<BatchedIndexingFlowTestContext<SimpleItem>>();

    protected override async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var transition = await base.OnIndex(cancellationToken);
        Context.OnTransition(Id.Arguments, transition);

        return transition;
    }

    protected override async Task<IReadOnlyList<SimpleItem>> GetBatch(
        IndexingFlowCursor<ChatId>? cursor,
        CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return Context.Next(Id.Arguments);
    }

    protected override async Task ProcessBatch(IReadOnlyList<SimpleItem> batch, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        Context.OnProcessed(Id.Arguments, batch);
    }

    protected override Task<bool> OnTailReached(CancellationToken cancellationToken)
        => Context.OnTailReached(Id.Arguments);
}
