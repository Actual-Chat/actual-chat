using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class PlaceIndexingFlow : BatchedIndexingFlowBase<Place, PlaceId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override TimeSpan RecheckInterval => Settings.IndexingTailRecheckInterval;

    private Task WhenReady => field ??= Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;
    private IPlacesBackend PlacesBackend => field ??= Host.Services.GetRequiredService<IPlacesBackend>();
    private IndexedDocuments IndexedDocuments => field ??= Host.Services.GetRequiredService<IndexedDocuments>();
    private MLSearchSettings Settings => field ??= Host.Services.GetRequiredService<MLSearchSettings>();

    protected override async Task<IReadOnlyList<Place>> GetBatch(
        IndexingFlowCursor<PlaceId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Clocks.GetMaxVersion(Settings.ChangedEntityIndexingDelay);
        cursor ??= new (null, 0);
        var batch = await PlacesBackend.ListChanged(
                cursor.LastUpdatedVersion,
                maxVersion,
                cursor.LastUpdatedId,
                BatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        DebugLog?.LogDebug(
            "`{Id}`.GetBatch: retrieved {Count} items with maxVersion={MaxVersion}, cursor={Cursor}",
            Id, batch.Length, maxVersion, cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<Place> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var updated = batch.Select(x => x.ToIndexedPlaceContact()).ToArray();
        await IndexedDocuments.SavePlaces(updated, [], cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IndexingFlowTransitionKind> HandleTail(
        bool hasProcessedAnyItems,
        CancellationToken cancellationToken)
    {
        var transition = await base.HandleTail(hasProcessedAnyItems, cancellationToken).ConfigureAwait(false);
        if (hasProcessedAnyItems) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting place index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshPlaces: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return transition;
    }
}
