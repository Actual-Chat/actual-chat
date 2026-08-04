using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MessagePackObject(true)]
public partial class SimpleBatchedIndexingFlow : BatchedIndexingFlow<SimpleItem, ChatId>
{
    public const int BatchSizeOverride = 3;
    public const int QuotaOverride = 6;
    protected override int BatchSize => BatchSizeOverride;
    protected override int Quota => QuotaOverride;

    [IgnoreDataMember]
    private BatchedIndexingFlowTestContext<SimpleItem, ChatId> Context
        => Services.GetRequiredService<BatchedIndexingFlowTestContext<SimpleItem, ChatId>>();

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
}
