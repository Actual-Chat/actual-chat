using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class PlaceContactIndexingFlow : BatchedIndexingFlowBase<Place, PlaceId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override TimeSpan RecheckInterval => Host.Services.GetRequiredService<MLSearchSettings>().IndexingRecheckInterval;
    private Task WhenReady => Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override async Task<IReadOnlyList<Place>> GetBatch(
        IndexingFlowCursor<PlaceId>? cursor,
        CancellationToken cancellationToken)
    {
        var placesBackend = Host.Services.GetRequiredService<IPlacesBackend>();
        var settings = Host.Services.GetRequiredService<MLSearchSettings>();
        var maxVersion = (Clocks.CoarseCpuClock.Now - settings.IndexingDelay).EpochOffset.Ticks;
        cursor ??= new (PlaceId.None, 0);
        var batch = await placesBackend.ListChanged(
                cursor.LastUpdatedVersion,
                maxVersion,
                cursor.LastUpdatedId,
                BatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        Log.LogDebug("`{Id}`.GetBatch: retrieved {Count} items with maxVersion={MaxVersion}, cursor={Cursor}", Id, batch.Count, maxVersion, cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<Place> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var indexedDocuments = Host.Services.GetRequiredService<IndexedDocuments>();

        var updated = batch.Select(x => x.ToIndexedPlaceContact()).ToApiArray();
        await indexedDocuments.UpdatePlaceContacts(updated, [], cancellationToken).ConfigureAwait(false);
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
