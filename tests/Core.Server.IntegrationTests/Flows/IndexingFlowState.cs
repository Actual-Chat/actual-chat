using ActualChat.Chat;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Rpc;
using MemoryPack;
using OpenSearch.Client;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

/*
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record TriggerChatEntryIndexingFlowEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] ChatId ChatId
) : IFlowControlEvent
{
    public Symbol GetNextStep(Flow flow)
        => FlowId.Arguments;
}

public sealed partial record NewChatCreatedFlowEvent (
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] ChatId ChatId
): IFlowControlEvent
{
    public Symbol GetNextStep(Flow flow)
        => "Dispatch";
}
*/

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
internal sealed partial class IndexingFlowState : Flow
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public IndexAllChatsCursor? Cursor { get; set; }

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        Cursor = new IndexAllChatsCursor(ChatId.None, 0);
        return StoreAndResume(nameof(OnDispatch));
    }

    private async Task<FlowTransition> OnDispatch(CancellationToken cancellationToken)
    {
        /*
        if (this.Event.Is<NewChatCreatedFlowEvent>(out var e)) {
            var parameters = e.ChatId;
            //await this.Host.Flows.GetOrStart<IndexingFlowState>(parameters, cancellationToken).ConfigureAwait(false);
            return StoreAndResume(nameof(Dispatch));
        }
        */
        Cursor = new IndexAllChatsCursor(ChatId.None, 1);
        return WaitForEvent(nameof(OnDispatch), InfiniteHardResumeAt);
    }

    private async Task<FlowTransition> IndexNextBatch(CancellationToken cancellationToken)
    {
        var indexator = this.Host.Services.GetRequiredService<ChatEntryIndexingFlow>();
        var next = await indexator.IndexAllChats(
            Cursor ?? new (ChatId.None, 0),
            async (ChatId chatId, CancellationToken cancellationToken) => {
                Symbol parameters = chatId;
                //await this.Host.Flows.GetOrStart<IndexingFlowState>(parameters, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken
        ).ConfigureAwait(false);
        if (next != null) {
            Cursor = next;
            return StoreAndResume(nameof(OnDispatch));
        } else {
            return End();
            //return this.WaitForEvent(nameof(Dispatch), InfiniteHardResumeAt);
        }
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record IndexAllChatsCursor(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ChatId LastIndexedChatId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long LastIndexedChatVersion
);

internal sealed class ChatEntryIndexingFlow(
    //MomentClock clock,
    //IChatsBackend chats,
    //ILogger<ChatEntryIndexingFlow> log
) : IBackendService
{
    public TimeSpan? AllChatsIndexingIdleInterval { get; init; } = TimeSpan.FromMinutes(1);

    public enum IndexChatResult {
        IndexedToTheEnd,
        HasMoreEntriesToIndex
    }

    public record IndexChatCursor(long LastVersion);

    [EventHandler]
    public async Task OnChatEntryChanged(TextEntryChangedEvent e, CancellationToken cancellationToken)
        => TriggerChatEntriesIndexing(e.Entry.ChatId);

    private void TriggerChatEntriesIndexing(ChatId chatId) {
        /*
        var flowEvent = new TriggerChatEntryIndexingFlowEvent(
            this.Host.Registry.NewId<ChatEntryIndexingFlow>(nameof(IndexChat)),

        );
        CommandContext.GetCurrent().Operation.AddEvent(flowEvent);
        */
    }

    public async Task<IndexAllChatsCursor?> IndexAllChats(
        IndexAllChatsCursor cursor,
        Func<ChatId, CancellationToken, Task> startChatIndexingFlow,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        // Count to 10 and exit.
        if (cursor.LastIndexedChatVersion < 10) {
            return new IndexAllChatsCursor(cursor.LastIndexedChatId, cursor.LastIndexedChatVersion + 1);
        } else {
            return null;
        }
        /*
        const int BatchSize = 100;
        ApiArray<Chat.Chat> batch = await chats.ListChanged(
            new ChangedChatsQuery {
                LastId = cursor.LastIndexedChatId,
                MinVersion = cursor.LastIndexedChatVersion,
                Limit = BatchSize,
            },
            cancellationToken
        ).ConfigureAwait(false);
        if (batch.IsEmpty) {
            return null;
        }
        var last = batch[0];
        foreach (var chat in batch) {
            await startChatIndexingFlow(chat.Id, cancellationToken).ConfigureAwait(false);
            last = chat;
        }
        return new IndexAllChatsCursor(last.Id, last.Version);
        */
    }

    private async Task<IndexChatResult> IndexChat(ChatId chatId, CancellationToken cancellationToken) {

        return IndexChatResult.IndexedToTheEnd;
    }


}
