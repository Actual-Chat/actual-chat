using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ConversationSplitMasterFlow
    : IndexingMasterFlowBase<ConversationSplitFlow, Chat, ChatId>, IMasterFlow
{
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Host.Services.GetRequiredService<IChatsBackend>();

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
            await StartOrResetFor(item, cancellationToken).ConfigureAwait(false);
    }
}
