using ActualChat.Chat;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntryIndexingMasterFlow
    : IndexingMasterFlowBase<EntryIndexingFlow, Chat.Chat, ChatId>, IMasterFlow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public long MaxVersion { get; private set; }

    protected override int CurrentFlowSetVersion => 1;
    protected override async Task<bool> OnBeforeFirstIndexAfterReset(CancellationToken cancellationToken)
    {
        var mustContinue = await base.OnBeforeFirstIndexAfterReset(cancellationToken).ConfigureAwait(false);
        if (mustContinue)
            // only created before now + 10sec. New chats are handled from events
            // Note: intentionally set negative number
            MaxVersion = Clocks.GetMaxVersion(TimeSpan.FromSeconds(-10));

        return mustContinue;
    }

    protected override async Task<IReadOnlyList<Chat.Chat>> GetBatch(IndexingFlowCursor<ChatId>? cursor, CancellationToken cancellationToken)
    {
        var chatsBackend = Host.Services.GetRequiredService<IChatsBackend>();
        cursor ??= new (ChatId.None, 0);
        return await chatsBackend.ListChanged(new ChangedChatsQuery {
                    MinVersion = cursor.LastUpdatedVersion,
                    MaxVersion = MaxVersion,
                    LastId = cursor.LastUpdatedId,
                    Limit = BatchSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
