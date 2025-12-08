using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class GroupIndexingFlow : BatchedIndexingFlowBase<Chat.Chat, ChatId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override TimeSpan RecheckInterval => Settings.IndexingTailRecheckInterval;

    private IndexedDocuments IndexedDocuments => field ??= Host.Services.GetRequiredService<IndexedDocuments>();
    private MLSearchSettings Settings => field ??= Host.Services.GetRequiredService<MLSearchSettings>();
    private Task WhenReady => field ??= Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override async Task<IReadOnlyList<Chat.Chat>> GetBatch(
        IndexingFlowCursor<ChatId>? cursor,
        CancellationToken cancellationToken)
    {
        var chatsBackend = Host.Services.GetRequiredService<IChatsBackend>();
        var maxVersion = Clocks.GetMaxVersion(Settings.ChangedEntityIndexingDelay);
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
        Log.LogDebug(
            "`{Id}`.GetBatch: retrieved {Count} items with maxVersion={MaxVersion}, cursor={Cursor}",
            Id, batch.Length, maxVersion, cursor);
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

    protected override async Task<IndexingFlowTransitionKind> HandleTail(
        bool hasProcessedAnyItems,
        CancellationToken cancellationToken)
    {
        var transition = await base.HandleTail(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting group index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshGroups: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return transition;
    }

    private async Task<Dictionary<PlaceId, Place>> GetPlaceMap(IReadOnlyList<Chat.Chat> chats, CancellationToken cancellationToken) {
        var placesBackend = Host.Services.GetRequiredService<IPlacesBackend>();
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
