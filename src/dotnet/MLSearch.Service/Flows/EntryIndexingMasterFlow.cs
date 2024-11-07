using ActualChat.Chat;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

public sealed class EntryIndexingMasterFlow
    : IndexingMasterFlowBase<EntryIndexingFlow, Chat.Chat, ChatId>
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public long MaxVersion { get; private set; }

    protected override Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        // only created before now + 10sec. New chats are handled from events
        MaxVersion = (Clocks.CoarseCpuClock.Now + TimeSpan.FromSeconds(10)).EpochOffset.Ticks;
        return base.OnReset(cancellationToken);
    }

    protected override async Task<IReadOnlyList<Chat.Chat>> GetBatch(IndexMasterFlowCursor<ChatId>? cursor, CancellationToken cancellationToken)
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
