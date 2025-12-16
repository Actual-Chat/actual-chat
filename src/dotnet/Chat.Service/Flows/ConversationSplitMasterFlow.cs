using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ConversationSplitMasterFlow
    : IndexingMasterFlow<ConversationSplitFlow, Chat, ChatId>, IMasterFlow
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();

    [DataMember(Order = 5), MemoryPackOrder(5)]
    public long MaxVersion { get; private set; }

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        // TODO(AY): Check why we don't want to go too far to past w/ AK
        MaxVersion = ResumedAt.ToVersion(-TimeSpan.FromSeconds(10));
        return default;
    }

    protected override async Task<IReadOnlyList<Chat>> GetBatch(IndexingFlowCursor<ChatId>? cursor, CancellationToken cancellationToken)
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

    protected override async Task ProcessBatch(IReadOnlyList<Chat> batch, CancellationToken cancellationToken)
    {
        foreach (var item in batch.Where(x => x.IsSummarized ?? false))
            await ScheduleReset(item, cancellationToken).ConfigureAwait(false);
    }
}
