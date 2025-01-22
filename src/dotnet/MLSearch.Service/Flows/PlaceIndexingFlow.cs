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
    protected override TimeSpan RecheckInterval => Host.Services.GetRequiredService<MLSearchSettings>().IndexingTailRecheckInterval;
    [field: AllowNull, MaybeNull]
    private Task WhenReady => field ??= Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;
    [field: AllowNull, MaybeNull]
    private IPlacesBackend PlacesBackend => field ??= Host.Services.GetRequiredService<IPlacesBackend>();
    [field: AllowNull, MaybeNull]
    private IndexedDocuments IndexedDocuments => field ??= Host.Services.GetRequiredService<IndexedDocuments>();
    [field: AllowNull, MaybeNull]
    private MLSearchSettings Settings => field ??= Host.Services.GetRequiredService<MLSearchSettings>();

    protected override async Task<IReadOnlyList<Place>> GetBatch(
        IndexingFlowCursor<PlaceId>? cursor,
        CancellationToken cancellationToken)
    {
        var maxVersion = Clocks.GetMaxVersion(Settings.ChangedEntityIndexingDelay);
        cursor ??= new (PlaceId.None, 0);
        var batch = await PlacesBackend.ListChanged(
                cursor.LastUpdatedVersion,
                maxVersion,
                cursor.LastUpdatedId,
                BatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        DebugLog?.LogDebug("`{Id}`.GetBatch: retrieved {Count} items with maxVersion={MaxVersion}, cursor={Cursor}", Id, batch.Count, maxVersion, cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<Place> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);

        var updated = batch.Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        await IndexedDocuments.SavePlaces(updated, [], cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IndexingFlowTransitionKind> HandleTail(int processedCount, CancellationToken cancellationToken)
    {
        var transition = await base.HandleTail(processedCount, cancellationToken).ConfigureAwait(false);
        if (processedCount > 0) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting place index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshPlaces: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return transition;
    }
}
