using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// Master flow that walks all chats and spawns the per-chat content/media indexing flows.
// Its first run backfills the whole chat history; new chats are handled by direct flow resumes.

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class ChatContentIndexingMasterFlow
    : IndexingMasterFlow<ChatEntryContentIndexingFlow, Chat, ChatId>, IMasterFlow
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();

    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public long MaxVersion { get; set; }

    // Spawn child flows for every chat within a single Run instead of relying on chained resumes.
    protected override int Quota => int.MaxValue;

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        MaxVersion = ResumedAt.ToVersion(-TimeSpan.FromSeconds(10));
        return default;
    }

    protected override async Task<IReadOnlyList<Chat>> GetBatch(
        IndexingFlowCursor<ChatId>? cursor,
        CancellationToken cancellationToken)
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
        foreach (var chat in batch) {
            await ScheduleResume(chat, cancellationToken).ConfigureAwait(false);
            await Hub.NewResumeEvent<ChatMediaIndexingFlow>(chat.Id.Value)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
