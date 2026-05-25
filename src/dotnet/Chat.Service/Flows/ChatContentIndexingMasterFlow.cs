using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

// Master flow that walks all chats and spawns the per-chat content/media indexing flows.
// Its first run backfills the whole chat history; new chats are handled by direct flow resumes.

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class ChatContentIndexingMasterFlow
    : BatchedIndexingFlow<Chat, ChatId>, IMasterFlow
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public long MaxVersion { get; set; }

    protected override int BatchSize => 200;
    protected override int Quota => 2000;

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
        var startedAt = CpuTimestamp.Now;
        var first = batch[0];
        var last = batch[^1];
        Log.LogInformation(
            "[ChatContentIndexingMasterFlow] ProcessBatch --> MaxVersion={MaxVersion}, First={FirstChatId} v{FirstVersion}, Last={LastChatId} v{LastVersion}",
            MaxVersion, first.Id, first.Version, last.Id, last.Version);

        foreach (var chat in batch) {
            await Hub.NewResumeEvent<ChatEntryContentIndexingFlow>(chat.Id.Value)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
            await Hub.NewResumeEvent<ChatMediaIndexingFlow>(chat.Id.Value)
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
        }

        Log.LogInformation(
            "[ChatContentIndexingMasterFlow] ProcessBatch <-- Count={Count}, Duration={Duration}",
            batch.Count, startedAt.Elapsed.ToShortString());
    }
}
