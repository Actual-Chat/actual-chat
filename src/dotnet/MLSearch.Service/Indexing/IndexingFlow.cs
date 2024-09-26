using ActualChat.Chat;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualLab.Fusion.EntityFramework;
using MemoryPack;

namespace ActualChat.MLSearch.Indexing;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

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

internal sealed class ChatEntryIndexingFlow(
    MomentClock clock,
    IChatsBackend chats,
    ICursorStates<ChatEntryIndexingFlow.IndexAllChatsCursor> cursors,
    ILogger<ChatEntryIndexingFlow> log
): Flow
{
    public TimeSpan? AllChatsIndexingIdleInterval { get; init; } = TimeSpan.FromMinutes(1);

    public enum IndexChatResult {
        IndexedToTheEnd,
        HasMoreEntriesToIndex
    }

    public const string IndexAllChatsCursorKey = $"{nameof(ChatEntryIndexingFlow)}.{nameof(IndexAllChatsCursor)}";
    public static string IndexChatCursorKey(ChatId chatId) 
    =>  $"{nameof(ChatEntryIndexingFlow)}.{nameof(IndexAllChatsCursor)}";
    public record IndexAllChatsCursor(ChatId LastIndexedChatId, long LastVersion);
    public record IndexChatCursor(long LastVersion);

    [EventHandler]
    public async Task OnChatEntryChanged(TextEntryChangedEvent e, CancellationToken cancellationToken)
        => TriggerChatEntriesIndexing(e.Entry.ChatId);

    private void TriggerChatEntriesIndexing(ChatId chatId) {
        var flowEvent = new TriggerChatEntryIndexingFlowEvent(
            this.Host.Registry.NewId<ChatEntryIndexingFlow>(nameof(IndexChat)), 
            chatId
        );
        CommandContext.GetCurrent().Operation.AddEvent(flowEvent);
    }

    protected async override Task<FlowTransition> OnReset(CancellationToken cancellationToken) {
        // Entry point.
        if (this.Id.Arguments.IsNullOrEmpty()) {
            return StoreAndResume(nameof(IndexAllChats));
        } else {
            return StoreAndResume(nameof(IndexChat));
        }
    }

    private async Task<FlowTransition> IndexAllChats(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        const int BatchSize = 100;
        var cursor = await cursors.LoadAsync(
            IndexAllChatsCursorKey, 
            cancellationToken
        ).ConfigureAwait(false) ?? new (ChatId.None, 0);
        ApiArray<Chat.Chat> batch = await chats.ListChanged(
            new ChangedChatsQuery {
                LastId = cursor.LastIndexedChatId,
                MinVersion = cursor.LastVersion,
                Limit = BatchSize,
            },
            cancellationToken
        ).ConfigureAwait(false);
        if (batch.IsEmpty) {
            // check if we want to finish this task
            // or we will repeat it forever
            if (AllChatsIndexingIdleInterval.HasValue) {
                var idleInterval = AllChatsIndexingIdleInterval.Value;
                return WaitForTimer(
                    nameof(IndexAllChats),
                    clock.Now + idleInterval
                );
            } else {
                return End();
            }
        }
        var last = batch[0];
        foreach (var chat in batch) {
            TriggerChatEntriesIndexing(chat.Id);
            last = chat;
        }
        await cursors.SaveAsync(
            IndexAllChatsCursorKey,
            new IndexAllChatsCursor(last.Id, last.Version),
            cancellationToken
        ).ConfigureAwait(false);
        return this.StoreAndResume(nameof(IndexAllChats));
    }

    private async Task<IndexChatResult> IndexChat(ChatId chatId, CancellationToken cancellationToken) {
        
        return IndexChatResult.IndexedToTheEnd;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "<Pending>")]
    public async Task<FlowTransition> IndexChat(CancellationToken cancellationToken) {
        var chatId = this.Event.As<TriggerChatEntryIndexingFlowEvent>()!.ChatId;
        var result = await IndexChat(chatId, cancellationToken).ConfigureAwait(false);
        return result switch {
            IndexChatResult.HasMoreEntriesToIndex => StoreAndResume(nameof(IndexChat)),
            IndexChatResult.IndexedToTheEnd => End(),
            _ => throw new ArgumentOutOfRangeException(nameof(IndexChatResult))
        };
    }
    
}
