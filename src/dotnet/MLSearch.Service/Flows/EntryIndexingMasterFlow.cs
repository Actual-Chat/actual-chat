using ActualChat.Flows;

namespace ActualChat.MLSearch.Flows;

[Flow(DataVersion = 2)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class EntryIndexingMasterFlow
    : IndexingMasterFlow<EntryIndexingFlow, Chat.Chat, ChatId>, IMasterFlow
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();

    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public long MaxVersion { get; set; }

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        // TODO(AY): Check why we don't want to go too far to past w/ Frol
        MaxVersion = ResumedAt.ToVersion(TimeSpan.FromSeconds(-10));
        return base.Init(cancellationToken);
    }

    protected override async Task<IReadOnlyList<Chat.Chat>> GetBatch(IndexingFlowCursor<ChatId>? cursor, CancellationToken cancellationToken)
    {
        cursor ??= new(null, 0);
        var query = new ChangedChatsQuery {
            LastId = cursor.LastUpdatedId,
            Limit = BatchSize,
            MinVersion = cursor.LastUpdatedVersion,
            MaxVersion = MaxVersion,
        };
        return await ChatsBackend.ListChanged(query, cancellationToken).ConfigureAwait(false);
    }
}
