using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class EntryIndexingFlow : BatchedIndexingFlow<ChatEntry, ChatEntryId>
{
    private MLSearchSettings Settings => field ??= Services.GetRequiredService<MLSearchSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    private IndexedDocuments IndexedDocuments => field ??= Services.GetRequiredService<IndexedDocuments>();
    private Task WhenOpenSearchReady => field ??= Services.GetRequiredService<OpenSearchConfigurator>().WhenReady;

    [IgnoreDataMember, MemoryPackIgnore]
    private ChatId ChatId { get; set; } = null!;

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        ChatId = ChatId.Parse(Id.Arguments);
        await WhenOpenSearchReady.ConfigureAwait(false);
        var readiness = await PrepareOnce().ConfigureAwait(false);
        if (readiness.IsSuspended) {
            // Let's wait a bit for chat to appear & retry
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            readiness = await PrepareOnce().ConfigureAwait(false);
        }
        return readiness;

        async ValueTask<FlowReadiness> PrepareOnce() {
            var chat = await ChatsBackend.Get(ChatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                return $"Chat #{ChatId} doesn't exist";

            Place? place = null;
            if (chat.Id is PlaceChatId placeChatId) {
                place = await PlacesBackend.Get(placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
                if (place is null)
                    return $"Place #{placeChatId.PlaceId} doesn't exist";
            }

            var indexedChat = chat.ToIndexedChat(place);
            await IndexedDocuments.SaveChats([indexedChat], cancellationToken).ConfigureAwait(false);
            return FlowReadiness.Ready;
        }
    }

    protected override async Task<IReadOnlyList<ChatEntry>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Hub.SystemNow.ToVersion(-Settings.ChangedEntityIndexingDelay);
        cursor ??= new(TextEntryId.New(ChatId, 0), 0);
        var batch = await ChatsBackend.ListChangedEntries(new ChangedEntriesQuery {
                    ChatId = ChatId,
                    LastLocalId = cursor.LastUpdatedId?.LocalId ?? 0,
                    MinVersion = cursor.LastUpdatedVersion,
                    MaxVersion = maxVersion,
                    Limit = BatchSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<ChatEntry> batch, CancellationToken cancellationToken)
    {
        await WhenOpenSearchReady.ConfigureAwait(false);

        var updated = batch
            .Where(x => x is { IsRemoved: false, IsSystemEntry: false })
            .Select(x => x.ToIndexedEntry())
            .ToList();
        var removed = batch
            .Where(x => x is { IsRemoved: true, IsSystemEntry: false })
            .Select(x => x.Id.ToTextEntryId())
            .ToList();
        await IndexedDocuments.SaveEntries(updated, removed, cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        await base.TailReached(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Console.Log("Requesting entry index refresh");
            await Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshEntries: true), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
