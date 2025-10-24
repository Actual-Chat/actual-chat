using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class SimpleBatchedIndexingFlow : BatchedIndexingFlowBase<SimpleItem, ChatId>
{
    public const int BatchSizeOverride = 3;
    public const int QuotaOverride = 6;
    public static readonly TimeSpan RecheckIntervalOverride = TimeSpan.FromSeconds(1.5);
    protected override int BatchSize => BatchSizeOverride;
    protected override int Quota => QuotaOverride;
    protected override int CurrentFlowSetVersion => 1;
    protected override TimeSpan RecheckInterval => RecheckIntervalOverride;
    protected override TimeSpan TimerRescheduleThreshold => TimeSpan.FromSeconds(0.5);

    [IgnoreDataMember, MemoryPackIgnore]
    private BatchedIndexingFlowTestContext<SimpleItem, ChatId> Context
        => Host.Services.GetRequiredService<BatchedIndexingFlowTestContext<SimpleItem, ChatId>>();

    protected override async Task<LegacyFlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var transition = await base.OnIndex(cancellationToken);
        if (transition != default)
            Context.OnTransition(Id.Arguments, transition);
        return transition;
    }

    protected override async Task<IReadOnlyList<SimpleItem>> GetBatch(
        IndexingFlowCursor<ChatId>? cursor,
        CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return Context.Next(cursor, Id.Arguments);
    }

    protected override async Task ProcessBatch(IReadOnlyList<SimpleItem> batch, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        Context.OnProcessed(Id.Arguments, batch);
    }

    protected override async Task<IndexingFlowTransitionKind> HandleTail(
        bool hasProcessedAnyItems,
        CancellationToken cancellationToken)
    {
        var result = await base.HandleTail(hasProcessedAnyItems, cancellationToken);
        return await Context.HandleTail(Id.Arguments, hasProcessedAnyItems) ?? result;
    }
}
