using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class GroupIndexingFlow : BatchedIndexingFlow<Chat.Chat, ChatId>, IMasterFlow
{
    private IndexedDocuments IndexedDocuments => field ??= Services.GetRequiredService<IndexedDocuments>();
    private MLSearchSettings Settings => field ??= Services.GetRequiredService<MLSearchSettings>();
    private Task WhenReady => field ??= Services.GetRequiredService<OpenSearchConfigurator>().WhenReady;

    protected override async Task<IReadOnlyList<Chat.Chat>> GetBatch(
        IndexingFlowCursor<ChatId>? cursor,
        CancellationToken cancellationToken)
    {
        var chatsBackend = Services.GetRequiredService<IChatsBackend>();
        var maxVersion = ResumedAt.ToVersion(-Settings.ChangedEntityIndexingDelay);
        cursor ??= new(null, 0);
        var query = new ChangedChatsQuery() {
            LastId = cursor.LastUpdatedId,
            Limit = BatchSize,
            MinVersion = cursor.LastUpdatedVersion,
            MaxVersion = maxVersion,
            ExcludePeerChats = true,
            ExcludePlaceRootChats = true,
        };
        var batch = await chatsBackend.ListChanged(query, cancellationToken).ConfigureAwait(false);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<Chat.Chat> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var placeMap = await GetPlaceMap(batch, cancellationToken).ConfigureAwait(false);
        var updated = batch
            .Select(c => {
                var place = c.Id is PlaceChatId placeChatId
                    ? placeMap.GetValueOrDefault(placeChatId.PlaceId)
                    : null;
                return c.ToIndexedGroup(place);
            }).ToArray();
        await IndexedDocuments.SaveGroups(updated, [], cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        await base.TailReached(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Console.Log("Requesting group index refresh");
            await Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshGroups: true), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<PlaceId, Place>> GetPlaceMap(IReadOnlyList<Chat.Chat> chats, CancellationToken cancellationToken) {
        var placesBackend = Services.GetRequiredService<IPlacesBackend>();
        var places = await chats
            .Select(c => (c.Id as PlaceChatId)?.PlaceId)
            .SkipNullItems()
            .Distinct()
            .Select(placeId => placesBackend.Get(placeId, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return places.SkipNullItems().ToDictionary(x => x.Id);
    }
}
