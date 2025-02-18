using ActualChat.Flows;
using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Chat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ChatMasterFlow: BatchedIndexingFlowBase<Chat, ChatId>, IMasterFlow
{
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Host.Services.GetRequiredService<IChatsBackend>();

    [field: AllowNull, MaybeNull]
    private HostInfo HostInfo => field ??= Host.Services.GetRequiredService<HostInfo>();

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

        if (HostInfo.BaseUrlKind != BaseUrlKind.Local)
            return mustContinue;

        // TODO(AK): Start child flow for development purposes - SHOULD BE FIXED FOR GENERAL USE OF FLOWS!!!!!
        var chat = await ChatsBackend.Get(new ChatId("052w3sgrad", ParseOrNone.Option), cancellationToken)
            .ConfigureAwait(false);
        if (chat != null)
            await Host.Flows
                .StartOrReset<ConversationSplitFlow>(chat.Id, null, "ChatMasterFlow", cancellationToken)
                .ConfigureAwait(false);

        return mustContinue;
    }

    protected override async Task<IReadOnlyList<Chat>> GetBatch(IndexingFlowCursor<ChatId>? cursor, CancellationToken cancellationToken)
    {
        cursor ??= new (ChatId.None, 0);
        return await ChatsBackend.ListChanged(new ChangedChatsQuery {
                    MinVersion = cursor.LastUpdatedVersion,
                    MaxVersion = MaxVersion,
                    LastId = cursor.LastUpdatedId,
                    Limit = BatchSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async Task ProcessBatch(IReadOnlyList<Chat> batch, CancellationToken cancellationToken)
    {
        foreach (var item in batch)
            await Host.Flows
                .StartOrReset<ConversationSplitFlow>(item.Id, null, "ChatMasterFlow", cancellationToken)
                .ConfigureAwait(false);
    }

    protected override Task<IndexingFlowTransitionKind> HandleTail(int processedCount, CancellationToken cancellationToken)
    {
        // stop indexing until version is bumped
        FlowSetVersion = CurrentFlowSetVersion;
        return Task.FromResult(IndexingFlowTransitionKind.Suspend);
    }
}
