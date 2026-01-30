using ActualChat.Chat;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntryIndexingMasterFlow
    : IndexingMasterFlow<EntryIndexingFlow, Chat.Chat, ChatId>, IMasterFlow
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();

    [DataMember(Order = 5), MemoryPackOrder(5)]
    public long MaxVersion { get; private set; }

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
