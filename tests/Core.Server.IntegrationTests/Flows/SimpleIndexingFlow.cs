using System.Diagnostics.CodeAnalysis;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SimpleIndexingFlow : IndexingFlowBase<long>
{
    public static readonly TimeSpan RecheckIntervalOverride = TimeSpan.FromSeconds(1.5);
    protected override int CurrentFlowSetVersion => Context.GetCurrentFlowSetVersionOverride(Id.Arguments) ?? 1;
    protected override TimeSpan RecheckInterval => RecheckIntervalOverride;
    protected override TimeSpan TimerRescheduleThreshold => TimeSpan.FromSeconds(0.5);
    [field: AllowNull, MaybeNull]
    private IndexingFlowTestContext Context => field ??= Host.Services.GetRequiredService<IndexingFlowTestContext>();

    protected override async Task<BatchIndexingResult<long>> Process(long cursor, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var batch = Context.Next(Id.Arguments);
        if (batch is null)
            batch = new (false, true, cursor, false);
        else
            Context.OnProcessed(Id.Arguments, batch);
        return batch;
    }

    protected override async Task<LegacyFlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var transition = await base.OnIndex(cancellationToken);
        if (transition != default)
            Context.OnTransition(Id.Arguments, transition);
        return transition;
    }
}
