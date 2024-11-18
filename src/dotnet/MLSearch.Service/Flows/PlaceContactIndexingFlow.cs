using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class PlaceContactIndexingFlow : BatchedIndexingFlowBase<Place, PlaceId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    private Task WhenReady => Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override async Task<IReadOnlyList<Place>> GetBatch(
        IndexingFlowCursor<PlaceId>? cursor,
        CancellationToken cancellationToken)
    {
        var placesBackend = Host.Services.GetRequiredService<IPlacesBackend>();
        cursor ??= new (PlaceId.None, 0);
        return await placesBackend.ListChanged(
                cursor.LastUpdatedVersion,
                long.MaxValue,
                cursor.LastUpdatedId,
                BatchSize,
                cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async Task ProcessBatch(IReadOnlyList<Place> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var indexedDocuments = Host.Services.GetRequiredService<IndexedDocuments>();

        var updated = batch.Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        await indexedDocuments.UpdatePlaceContacts(updated, [], cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<bool> OnTailReached(int processCount, CancellationToken cancellationToken)
    {
        if (processCount > 0) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting entry index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshPlaces: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return true;
    }
}
