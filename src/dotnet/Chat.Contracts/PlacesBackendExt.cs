namespace ActualChat.Chat;

public static class PlacesBackendExt
{
    public static async IAsyncEnumerable<ApiArray<Place>> BatchChanged(
        this IPlacesBackend placesBackend,
        long minVersion,
        long maxVersion,
        PlaceId lastId,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            var places = await placesBackend.ListChanged(
                    minVersion,
                    maxVersion,
                    lastId,
                    batchSize,
                    cancellationToken)
                .ConfigureAwait(false);
            if (places.Count == 0)
                yield break;

            yield return places;

            var last = places[^1];
            lastId = last.Id;
            minVersion = last.Version;
        }
    }
}
