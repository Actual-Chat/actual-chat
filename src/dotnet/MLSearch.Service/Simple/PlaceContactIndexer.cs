using ActualChat.Chat;

namespace ActualChat.Search;

public sealed class PlaceContactIndexer(IServiceProvider services) : ContactIndexer(services)
{
    private IPlacesBackend PlacesBackend { get; } = services.GetRequiredService<IPlacesBackend>();

    protected override async Task Sync(CancellationToken cancellationToken)
    {
        var hasChanges = await SyncPlaceChanges(cancellationToken).ConfigureAwait(false);
        if (hasChanges) {
            var cmd = new SearchBackend_Refresh(refreshPlaces: hasChanges);
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> SyncPlaceChanges(CancellationToken cancellationToken)
    {
        var state = await ContactIndexStatesBackend.GetForPlaces(cancellationToken).ConfigureAwait(false);
        var batches = PlacesBackend
            .BatchChanged(
                state.LastUpdatedVersion,
                MaxVersion,
                state.LastUpdatedPlaceId,
                SyncBatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        var hasChanges = false;
        await foreach (var places in batches) {
            var first = places[0];
            var last = places[^1];
            Log.LogDebug(
                "Indexing {BatchSize} places [(v={FirstVersion}, #{FirstId})..(v={LastVersion}, #{LastId})]",
                places.Count,
                first.Version,
                first.Id,
                last.Version,
                last.Id);
            NeedsSync.Reset();
            var updates = places.Select(x => x.ToIndexedPlaceContact())
                .ToApiArray();
            var indexCmd = new SearchBackend_PlaceContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken).ConfigureAwait(false);

            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
            hasChanges |= updates.Count > 0;
        }
        return hasChanges;
    }
}
