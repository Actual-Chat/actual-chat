using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class PlaceIndexingFlow : BatchedIndexingFlow<Place, PlaceId>, IMasterFlow
{
    private Task WhenReady => field ??= Services.GetRequiredService<OpenSearchConfigurator>().WhenReady;
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    private IndexedDocuments IndexedDocuments => field ??= Services.GetRequiredService<IndexedDocuments>();
    private MLSearchSettings Settings => field ??= Services.GetRequiredService<MLSearchSettings>();

    protected override async Task<IReadOnlyList<Place>> GetBatch(
        IndexingFlowCursor<PlaceId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Hub.SystemNow.ToVersion(-Settings.ChangedEntityIndexingDelay);
        cursor ??= new (null, 0);
        var batch = await PlacesBackend.ListChanged(
                cursor.LastUpdatedVersion,
                maxVersion,
                cursor.LastUpdatedId,
                BatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<Place> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var updated = batch.Select(x => x.ToIndexedPlaceContact()).ToArray();
        await IndexedDocuments.SavePlaces(updated, [], cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask TailReached(bool hasProcessedAnyItems, CancellationToken cancellationToken)
    {
        await base.TailReached(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Console.Log("Requesting place index refresh");
            await Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshPlaces: true), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
