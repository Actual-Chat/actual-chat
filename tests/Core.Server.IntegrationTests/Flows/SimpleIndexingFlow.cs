using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SimpleIndexingFlow : IndexingFlowBase<long>
{
    protected override int CurrentFlowSetVersion => 1;
    public static readonly TimeSpan RecheckIntervalOverride = TimeSpan.FromSeconds(1.5);

    protected override TimeSpan RecheckInterval => RecheckIntervalOverride;
    protected override TimeSpan TimerRescheduleThreshold => TimeSpan.FromSeconds(0.5);
    [IgnoreDataMember, MemoryPackIgnore]
    private IndexingFlowTestContext Context => Host.Services.GetRequiredService<IndexingFlowTestContext>();


    protected override async Task<BatchIndexingResult<long>> Process(long cursor, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        var batch = Context.Next(Id.Arguments);
        Context.OnProcessed(Id.Arguments, batch);
        return batch;
    }

    protected override async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var transition = await base.OnIndex(cancellationToken);
        Context.OnTransition(Id.Arguments, transition);

        return transition;
    }
}
